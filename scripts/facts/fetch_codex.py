#!/usr/bin/env python3
"""Fetch the Spire Codex public API (https://spire-codex.com, stable channel).

Spire Codex is a community DB auto-extracted from the *latest* stable game
build, so it can be ahead of the pinned harness version; it is used as a
secondary cross-check only. Output: artifacts/facts-cache/v<version>/codex/*.json

Usage: python3 scripts/facts/fetch_codex.py --version 0.107.1 [--refresh]
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from common import Http, cache_dir, today, write_json  # noqa: E402

BASE = "https://spire-codex.com/api"
ENDPOINTS = ["cards", "relics", "potions", "monsters", "encounters", "events", "powers",
             "keywords", "enchantments", "afflictions", "acts", "ascensions", "intents"]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.107.1")
    ap.add_argument("--refresh", action="store_true")
    ap.add_argument("--interval", type=float, default=1.0)
    args = ap.parse_args()
    out = cache_dir(args.version) / "codex"
    http = Http(args.interval)
    meta_path = out / "meta.json"
    if args.refresh or not meta_path.exists():
        meta = {"fetched": today()}
        for extra in ("mod/latest", "stats"):
            try:
                meta[extra] = http.get_json(f"{BASE}/{extra}")
            except RuntimeError as error:
                meta[extra] = {"error": str(error)}
        write_json(meta_path, meta)
    for name in ENDPOINTS:
        path = out / f"{name}.json"
        if path.exists() and not args.refresh:
            continue
        rows = http.get_json(f"{BASE}/{name}", {"lang": "eng"})
        write_json(path, {"source": f"{BASE}/{name}?lang=eng", "fetched": today(), "rows": rows})
        print(name, len(rows) if isinstance(rows, list) else type(rows).__name__)


if __name__ == "__main__":
    main()
