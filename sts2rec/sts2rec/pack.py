"""Gzip-pack recorded sessions (offline counterpart of the recorder's
gzip-at-complete).

Same safety semantics as the C# SessionCompressor: each plain stream is
compressed to <stream>.jsonl.gz, decompressed again and byte-compared against
the original, and the plain file is deleted ONLY after its round-trip
verifies. manifest.json and native/ always stay plain. The manifest's
"compression" field is set to "gz" so `sts2rec sessions` can mark the session.

Incomplete sessions (live, or crash-terminated) are refused unless forced:
packing a file another process is appending to would corrupt the recording.
"""

from __future__ import annotations

import gzip
import json
from dataclasses import dataclass
from pathlib import Path

from .errors import PackError
from .session import (
    COMPRESSED_SUFFIX,
    MANIFEST_NAME,
    STREAM_NAMES,
    load_manifest,
    stream_path,
)

_CHUNK_SIZE = 1 << 16


@dataclass(frozen=True, slots=True)
class PackResult:
    session_dir: str
    packed: tuple[str, ...]  # stream names compressed by this call
    already_packed: tuple[str, ...]  # streams that were .gz before this call
    bytes_before: int  # plain bytes of the packed streams
    bytes_after: int  # compressed bytes of the packed streams


def _compress_file(plain: Path, compressed: Path) -> None:
    with plain.open("rb") as source, gzip.open(compressed, "wb") as target:
        while chunk := source.read(_CHUNK_SIZE):
            target.write(chunk)


def _round_trip_matches(plain: Path, compressed: Path) -> bool:
    with plain.open("rb") as expected, gzip.open(compressed, "rb") as actual:
        while True:
            chunk_a = expected.read(_CHUNK_SIZE)
            chunk_b = actual.read(_CHUNK_SIZE)
            if chunk_a != chunk_b:
                return False
            if not chunk_a:
                return True


def _mark_manifest_compressed(session_dir: Path) -> None:
    """Set manifest 'compression': 'gz' (read-modify-write, other keys kept)."""
    path = session_dir / MANIFEST_NAME
    raw = json.loads(path.read_text(encoding="utf-8"))
    updated = {**raw, "compression": "gz"}
    temp = path.with_name(path.name + ".pack.tmp")
    temp.write_text(json.dumps(updated, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def pack_session(session_dir: Path | str, *, force: bool = False) -> PackResult:
    """Compress a session's plain streams in place (verify-then-delete).

    Raises PackError when the session is incomplete (unless force=True), when
    the manifest is unreadable, or when any stream fails to compress/verify —
    in the failure case every plain file is left untouched and partial .gz
    files created by this call are removed.
    """
    session_dir = Path(session_dir)
    try:
        manifest = load_manifest(session_dir)
    except Exception as error:
        raise PackError(f"{session_dir}: cannot read manifest: {error}") from error
    if manifest.incomplete and not force:
        raise PackError(
            f"{session_dir}: session is incomplete (live or crash-terminated); "
            "pass --force to pack it anyway"
        )

    to_pack: list[tuple[str, Path, Path]] = []
    already: list[str] = []
    for stream in STREAM_NAMES:
        plain = stream_path(session_dir, stream)
        compressed = plain.with_name(plain.name + COMPRESSED_SUFFIX)
        if compressed.is_file() and not plain.is_file():
            already.append(stream)
            continue
        if not plain.is_file():
            continue  # neither file: tolerated, validate reports it
        to_pack.append((stream, plain, compressed))

    created: list[Path] = []
    try:
        for _stream, plain, compressed in to_pack:
            created.append(compressed)
            _compress_file(plain, compressed)
            if not _round_trip_matches(plain, compressed):
                raise PackError(f"round-trip verification failed for {compressed}")
    except (OSError, PackError) as error:
        for compressed in created:
            try:
                compressed.unlink(missing_ok=True)
            except OSError:
                pass  # cleanup must never mask the original failure
        if isinstance(error, PackError):
            raise
        raise PackError(f"{session_dir}: pack failed: {error}") from error

    # All verified: only now delete the plain originals.
    bytes_before = 0
    bytes_after = 0
    for _stream, plain, compressed in to_pack:
        bytes_before += plain.stat().st_size
        bytes_after += compressed.stat().st_size
        plain.unlink()

    if to_pack or already:
        try:
            _mark_manifest_compressed(session_dir)
        except OSError as error:
            raise PackError(
                f"{session_dir}: streams packed but manifest update failed: {error}"
            ) from error

    return PackResult(
        session_dir=str(session_dir),
        packed=tuple(stream for stream, _p, _c in to_pack),
        already_packed=tuple(already),
        bytes_before=bytes_before,
        bytes_after=bytes_after,
    )
