"""sts2rec command-line interface.

Subcommands: sessions, validate, canonical, watch, archive, report, pack.
Exit codes: 0 = ok, 1 = validation/operation errors, 2 = usage errors.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

from . import __version__
from .archive import archive_native
from .audit import audit_lineage
from .canonical import build_canonical
from .errors import Sts2RecError
from .pack import pack_session
from .paths import profile_saves_dirs, recorder_output_root, sessions_root, user_data_dir
from .report import build_report
from .review import review_lineage, write_review
from .session import COMPRESSED_SUFFIX, STREAM_NAMES, Manifest, load_manifest, stream_path
from .validate import validate_session
from .watch import watch_session

EXIT_OK = 0
EXIT_ERRORS = 1
EXIT_USAGE = 2


def _result_label(manifest: Manifest) -> str:
    if manifest.result is None:
        return "in-progress" if manifest.incomplete else "no-result"
    if manifest.result.abandoned:
        return "abandoned"
    return "win" if manifest.result.win else "loss"


def _format_table(rows: list[tuple[str, ...]], header: tuple[str, ...]) -> str:
    table = [header, *rows]
    widths = [max(len(row[col]) for row in table) for col in range(len(header))]
    lines = [
        "  ".join(cell.ljust(width) for cell, width in zip(row, widths)).rstrip()
        for row in table
    ]
    return "\n".join(lines)


def _format_size(size: int) -> str:
    value = float(size)
    for unit in ("B", "K", "M", "G"):
        if value < 1024 or unit == "G":
            return f"{int(value)}{unit}" if unit == "B" else f"{value:.1f}{unit}"
        value /= 1024
    return str(size)


def _dir_size(path: Path) -> int:
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file())


def _compression_marker(session_dir: Path, manifest: Manifest) -> str:
    if manifest.compression:
        return manifest.compression
    for stream in STREAM_NAMES:
        plain = stream_path(session_dir, stream)
        if plain.with_name(plain.name + COMPRESSED_SUFFIX).is_file():
            return "gz"
    return "-"


def _cmd_sessions(args: argparse.Namespace) -> int:
    root = Path(args.root) if args.root else sessions_root(recorder_output_root())
    if not root.is_dir():
        print(f"no sessions directory: {root}", file=sys.stderr)
        return EXIT_ERRORS
    rows: list[tuple[str, ...]] = []
    for session_dir in sorted(path for path in root.iterdir() if path.is_dir()):
        size = _format_size(_dir_size(session_dir))
        try:
            manifest = load_manifest(session_dir)
        except Sts2RecError as error:
            rows.append(
                (session_dir.name, "-", "-", f"invalid manifest: {error}", size, "-")
            )
            continue
        rows.append(
            (
                session_dir.name,
                manifest.run.seed,
                manifest.run.character,
                _result_label(manifest),
                size,
                _compression_marker(session_dir, manifest),
            )
        )
    if not rows:
        print(f"no sessions found under {root}")
        return EXIT_OK
    print(_format_table(rows, ("RUN_ID", "SEED", "CHARACTER", "RESULT", "SIZE", "COMP")))
    return EXIT_OK


def _cmd_validate(args: argparse.Namespace) -> int:
    report = validate_session(args.session_dir)
    for message in report.errors:
        print(f"ERROR   {message}")
    for message in report.warnings:
        print(f"WARNING {message}")
    counts = ", ".join(f"{k}={v}" for k, v in sorted(report.counts.items()))
    status = "OK" if report.ok else "FAILED"
    print(
        f"{status}: {report.session_dir} "
        f"({len(report.errors)} errors, {len(report.warnings)} warnings"
        + (f"; {counts}" if counts else "")
        + ")"
    )
    return EXIT_OK if report.ok else EXIT_ERRORS


def _cmd_canonical(args: argparse.Namespace) -> int:
    try:
        trajectory = build_canonical(args.session_dir)
    except Sts2RecError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_ERRORS
    rendered = json.dumps(trajectory, indent=2, sort_keys=True)
    if args.output:
        try:
            Path(args.output).write_text(rendered + "\n", encoding="utf-8")
        except OSError as error:
            print(f"error: cannot write {args.output}: {error}", file=sys.stderr)
            return EXIT_ERRORS
        print(f"wrote {len(trajectory['steps'])} steps to {args.output}")
    else:
        print(rendered)
    return EXIT_OK


def _cmd_audit_lineage(args: argparse.Namespace) -> int:
    try:
        report = audit_lineage(args.session_dir)
    except Sts2RecError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_ERRORS
    rendered = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True)
    if args.output:
        Path(args.output).write_text(rendered + "\n", encoding="utf-8")
        print(f"wrote candidate/action-space audit to {args.output}")
    else:
        print(rendered)
    return EXIT_OK


def _cmd_review_lineage(args: argparse.Namespace) -> int:
    try:
        report = review_lineage(args.session_dir)
    except Sts2RecError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_ERRORS
    write_review(report, args.output, args.csv)
    print(
        f"wrote {report['summary']['steps']} reviewed steps to {args.output} "
        f"and {args.csv}"
    )
    return EXIT_OK


def _cmd_watch(args: argparse.Namespace) -> int:
    session_dir = Path(args.session_dir)
    if not session_dir.is_dir():
        print(f"error: not a directory: {session_dir}", file=sys.stderr)
        return EXIT_ERRORS
    try:
        for item in watch_session(
            session_dir,
            poll_interval=args.interval,
            idle_timeout=args.idle_timeout,
        ):
            print(f"[{item.stream}] {json.dumps(item.record, sort_keys=True)}")
    except KeyboardInterrupt:
        pass
    return EXIT_OK


def _cmd_report(args: argparse.Namespace) -> int:
    try:
        rendered = build_report(args.session_dir)
    except Sts2RecError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_ERRORS
    print(rendered)
    if args.output:
        try:
            Path(args.output).write_text(rendered + "\n", encoding="utf-8")
        except OSError as error:
            print(f"error: cannot write {args.output}: {error}", file=sys.stderr)
            return EXIT_ERRORS
        print(f"(report written to {args.output})", file=sys.stderr)
    return EXIT_OK


def _pack_one(session_dir: Path, force: bool) -> bool:
    try:
        result = pack_session(session_dir, force=force)
    except Sts2RecError as error:
        print(f"SKIP  {session_dir.name}: {error}", file=sys.stderr)
        return False
    if result.packed:
        saved = result.bytes_before - result.bytes_after
        print(
            f"PACKED {session_dir.name}: {', '.join(result.packed)} "
            f"({_format_size(result.bytes_before)} -> "
            f"{_format_size(result.bytes_after)}, saved {_format_size(saved)})"
        )
    else:
        print(f"OK     {session_dir.name}: already packed")
    return True


def _cmd_pack(args: argparse.Namespace) -> int:
    if bool(args.session_dir) == bool(args.all):
        print("error: pass exactly one of <session_dir> or --all", file=sys.stderr)
        return EXIT_USAGE
    if args.session_dir:
        return EXIT_OK if _pack_one(Path(args.session_dir), args.force) else EXIT_ERRORS
    root = Path(args.root) if args.root else sessions_root(recorder_output_root())
    if not root.is_dir():
        print(f"no sessions directory: {root}", file=sys.stderr)
        return EXIT_ERRORS
    ok = True
    for session_dir in sorted(path for path in root.iterdir() if path.is_dir()):
        ok = _pack_one(session_dir, args.force) and ok
    return EXIT_OK if ok else EXIT_ERRORS


def _default_saves_dir() -> Path | None:
    candidates = profile_saves_dirs(user_data_dir())
    return candidates[0] if candidates else None


def _cmd_archive(args: argparse.Namespace) -> int:
    saves_dir = Path(args.saves) if args.saves else _default_saves_dir()
    if saves_dir is None:
        print(
            "error: no profile saves dir found; pass one with --saves",
            file=sys.stderr,
        )
        return EXIT_ERRORS
    try:
        result = archive_native(saves_dir, args.session_dir, overwrite=args.overwrite)
    except Sts2RecError as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_ERRORS
    for warning in result.warnings:
        print(f"WARNING {warning}")
    print(f"run_history: {result.run_history or 'MISSING'}")
    print(f"replay:      {result.replay or 'MISSING'}")
    return EXIT_OK if result.complete else EXIT_ERRORS


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sts2rec",
        description="Offline tooling for Slay the Spire 2 recorder sessions.",
    )
    parser.add_argument("--version", action="version", version=f"sts2rec {__version__}")
    subparsers = parser.add_subparsers(dest="command", required=True)

    sessions = subparsers.add_parser("sessions", help="list recorded sessions")
    sessions.add_argument(
        "root", nargs="?", help="sessions root (default: recorder output root)"
    )
    sessions.set_defaults(handler=_cmd_sessions)

    validate = subparsers.add_parser("validate", help="validate a session directory")
    validate.add_argument("session_dir")
    validate.set_defaults(handler=_cmd_validate)

    canonical = subparsers.add_parser(
        "canonical", help="emit the canonical {meta, steps[]} trajectory"
    )
    canonical.add_argument("session_dir")
    canonical.add_argument("-o", "--output", help="write JSON here instead of stdout")
    canonical.set_defaults(handler=_cmd_canonical)

    audit_lineage_parser = subparsers.add_parser(
        "audit-lineage", help="audit visible candidate/action coverage across resume parts"
    )
    audit_lineage_parser.add_argument("session_dir")
    audit_lineage_parser.add_argument("-o", "--output")
    audit_lineage_parser.set_defaults(handler=_cmd_audit_lineage)

    review_lineage_parser = subparsers.add_parser(
        "review-lineage", help="emit per-step diagnostic loss/score across resume parts"
    )
    review_lineage_parser.add_argument("session_dir")
    review_lineage_parser.add_argument("-o", "--output", required=True)
    review_lineage_parser.add_argument("--csv", required=True)
    review_lineage_parser.set_defaults(handler=_cmd_review_lineage)

    watch = subparsers.add_parser("watch", help="tail a live session directory")
    watch.add_argument("session_dir")
    watch.add_argument("--interval", type=float, default=0.5, help="poll interval (s)")
    watch.add_argument(
        "--idle-timeout",
        type=float,
        default=None,
        help="stop after this many idle seconds (default: watch forever)",
    )
    watch.set_defaults(handler=_cmd_watch)

    archive = subparsers.add_parser(
        "archive", help="copy native .run / .mcr artifacts into the session"
    )
    archive.add_argument("session_dir")
    archive.add_argument("--saves", help="profile saves dir (default: first discovered)")
    archive.add_argument(
        "--overwrite", action="store_true", help="replace existing native/ copies"
    )
    archive.set_defaults(handler=_cmd_archive)

    report = subparsers.add_parser(
        "report", help="human-readable markdown walkthrough of a session"
    )
    report.add_argument("session_dir")
    report.add_argument("-o", "--output", help="also write the markdown here")
    report.set_defaults(handler=_cmd_report)

    pack = subparsers.add_parser(
        "pack", help="gzip a finished session's streams in place (verify-then-delete)"
    )
    pack.add_argument("session_dir", nargs="?", help="one session directory")
    pack.add_argument("--all", action="store_true", help="pack every session under the root")
    pack.add_argument("--root", help="sessions root for --all (default: recorder output root)")
    pack.add_argument(
        "--force",
        action="store_true",
        help="pack even when the manifest says incomplete (NOT while the game is running)",
    )
    pack.set_defaults(handler=_cmd_pack)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return int(args.handler(args))


if __name__ == "__main__":
    raise SystemExit(main())
