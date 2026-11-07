#!/usr/bin/env python3
"""Bundle reviewed dashboard documents without opening their evidence references."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import sys

from project_live import read_json

SECTIONS = {
    "history": "history/experiments.json",
    "architecture": "architecture-performance/architecture.json",
    "performance": "architecture-performance/performance.json",
    "budget": "budget/breakdown.json",
}
MAX_SECTION = 768 * 1024
MAX_ARCHIVE = 2 * 1024 * 1024
ARTIFACT_MARKER = "/artifacts/modal-research/2026-11-04/"
LOCAL_PATH = re.compile(r"(?:^|[\s`'\"=(])/(?:Users|data|tmp|opt|var|home|private|workspace|etc|root|mnt|srv|run|Volumes|proc|dev|Library|System)(?:/|\b)|[A-Za-z]:\\")
SECRET_TEXT = re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----|\bBearer\s+[A-Za-z0-9._~-]{12,}|\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}", re.I)
SECRET_KEYS = {"secret", "credential", "credentials", "token", "access_token", "api_key",
               "private_key", "authorization", "raw_log", "raw_logs", "env_vars", "environment_variables"}


def short_ref(value):
    if ARTIFACT_MARKER in value:
        relative = value.split(ARTIFACT_MARKER, 1)[1]
        if re.fullmatch(r"[A-Za-z0-9_./-]{1,220}", relative) and ".." not in relative.split("/"):
            return relative
    if re.fullmatch(r"/[^\n\r]{1,700}/[A-Za-z0-9_.-]{1,128}", value):
        return Path(value).name
    raise ValueError("reference must use a short artifact name")


def curate(value, key="", depth=0):
    if depth > 16:
        raise ValueError("curated section nesting exceeds bound")
    if isinstance(value, dict):
        result = {}
        for field, item in value.items():
            if (not re.fullmatch(r"[A-Za-z0-9_./: ()+-]{1,160}", field)
                    or field.lower() in SECRET_KEYS or LOCAL_PATH.search(field)):
                raise ValueError("unexpected or sensitive curated field")
            result[field] = curate(item, field, depth + 1)
        return result
    if isinstance(value, list):
        if len(value) > 5000:
            raise ValueError("curated list exceeds bound")
        return [curate(item, key, depth + 1) for item in value]
    if isinstance(value, str):
        if len(value) > 2048 or SECRET_TEXT.search(value) or "\0" in value:
            raise ValueError("unsafe curated string")
        if value.startswith(("/", "~/")) or LOCAL_PATH.search(value):
            if key not in {"path", "file", "source_path", "artifact_path", "evidence_path", "ref", "source_ref", "relative_path", "original_live_path"}:
                raise ValueError(f"local path in curated field {key}")
            value = short_ref(value)
        return value
    if isinstance(value, float) and not math.isfinite(value):
        raise ValueError("non-finite curated number")
    if type(value) is int and abs(value) > 2**53 - 1:
        raise ValueError("curated integer outside JavaScript safe range")
    if value is not None and type(value) not in (int, float, bool):
        raise ValueError("unsupported curated data")
    return value


def canonical(value):
    return json.dumps(value, ensure_ascii=False, allow_nan=False, sort_keys=True, separators=(",", ":"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--pins", type=Path, required=True, help="exact reviewed section SHA-256s")
    parser.add_argument("--live", type=Path, required=True, help="already projected observer summary")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    pins = read_json(args.pins, 4096)
    if set(pins) != set(SECTIONS) or any(not re.fullmatch(r"[a-f0-9]{64}", h) for h in pins.values()):
        raise ValueError("expected exactly four reviewed SHA-256 pins")
    sections = {}
    for section, source in SECTIONS.items():
        document = read_json(args.data_dir / source, MAX_SECTION, pins[section])
        if not isinstance(document, (dict, list)):
            raise ValueError(f"invalid curated {section}")
        sections[section] = curate(document)
    live = read_json(args.live, 16 * 1024)
    archive = {
        "archive_version": 1,
        "archive_sha256": hashlib.sha256(canonical(sections).encode()).hexdigest(),
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "initial_live": curate(live),
        **sections,
    }
    # JS object literal is safe for data except the magic __proto__ key. Parse JSON data
    # explicitly so authored content is never interpreted as source or executable HTML.
    source = "export const RESEARCH_ARCHIVE = JSON.parse(" + json.dumps(canonical(archive)) + ");\n"
    if len(source.encode()) > MAX_ARCHIVE:
        raise ValueError("self-contained archive exceeds bound")
    temporary = args.output.with_suffix(".tmp")
    with temporary.open("x", encoding="utf8") as output:
        output.write(source)
    temporary.replace(args.output)
    print(f"archive_sha256={archive['archive_sha256']} bytes={len(source.encode())}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, OverflowError) as error:
        print(f"archive_assembly_failed={type(error).__name__}: {error}", file=sys.stderr)
        raise SystemExit(1) from None
