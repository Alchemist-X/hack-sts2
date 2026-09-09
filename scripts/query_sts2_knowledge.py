#!/usr/bin/env python3
"""Search versioned STS2 facts and explicitly selected character priors."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any


def normalized(value: str) -> str:
    return re.sub(r"[^a-z0-9\u3400-\u9fff]", "", value.lower())


def searchable_text(record: dict[str, Any]) -> str:
    pieces = [
        str(record.get("id", "")),
        str(record.get("model_id", "")),
        str(record.get("title", "")),
        str(record.get("summary", "")),
        json.dumps(record.get("apis", []), ensure_ascii=False),
        json.dumps(record.get("related_models", []), ensure_ascii=False),
        json.dumps(record.get("invariants", []), ensure_ascii=False),
    ]
    for localized in record.get("localized", {}).values():
        if isinstance(localized, dict):
            pieces.extend(str(value) for value in localized.values())
    runtime = record.get("runtime", {})
    if isinstance(runtime, dict):
        pieces.extend(
            str(runtime.get(key, "")) for key in ("id", "name", "description")
        )
        for variant in ("base", "upgraded"):
            pieces.append(json.dumps(runtime.get(variant, {}), ensure_ascii=False))
    return " ".join(pieces)


def score(query: str, record: dict[str, Any]) -> int:
    needle = normalized(query)
    identifiers = {
        normalized(str(record.get("id", ""))),
        normalized(str(record.get("model_id", ""))),
    }
    if needle in identifiers:
        return 1000
    text = normalized(searchable_text(record))
    if needle and needle in text:
        return 500
    return 0


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("query")
    parser.add_argument(
        "--root", type=Path, default=Path("knowledge/source/v0.107.1")
    )
    parser.add_argument(
        "--kind",
        choices=(
            "cards",
            "relics",
            "potions",
            "enemies",
            "keywords",
            "mechanics",
        ),
    )
    parser.add_argument("--limit", type=int, default=10)
    parser.add_argument("--character", default="ironclad")
    args = parser.parse_args()

    kinds = [args.kind] if args.kind else [
        "cards",
        "relics",
        "potions",
        "enemies",
        "keywords",
        "mechanics",
    ]
    matches: list[tuple[int, str, dict[str, Any]]] = []
    for kind in kinds:
        path = args.root / f"{kind}.jsonl"
        if not path.is_file():
            continue
        for record in read_jsonl(path):
            rank = score(args.query, record)
            if rank:
                matches.append((rank, kind, record))

    # A model lookup should also surface a verified cross-model mechanic that
    # explicitly names it. This is how a query for Bone Shards or Bound
    # Phylactery exposes the otherwise easy-to-miss Osty death/revival contract.
    matched_ids = {
        str(record.get("model_id") or record.get("id"))
        for _, kind, record in matches
        if kind != "mechanics"
    }
    mechanics_path = args.root / "mechanics.jsonl"
    if args.kind is None and matched_ids and mechanics_path.is_file():
        existing_mechanics = {
            str(record.get("id"))
            for _, kind, record in matches
            if kind == "mechanics"
        }
        for record in read_jsonl(mechanics_path):
            related = {
                str(value) for value in record.get("related_models", [])
            }
            if matched_ids & related and str(record.get("id")) not in existing_mechanics:
                matches.append((250, "mechanics", record))
    matches.sort(
        key=lambda item: (-item[0], item[1], str(item[2].get("id", "")))
    )

    prior_path = (
        args.root.parents[1]
        / "priors" / args.root.name / args.character / "cards.json"
    )
    priors: list[dict[str, Any]] = []
    if prior_path.is_file():
        prior_data = json.loads(prior_path.read_text(encoding="utf-8"))
        priors = [
            prior
            for prior in prior_data.get("cards", [])
            if normalized(args.query) == normalized(str(prior.get("id", "")))
        ]

    output = {
        "query": args.query,
        "facts": [
            {"kind": kind, **record}
            for _, kind, record in matches[: max(1, args.limit)]
        ],
        "community_priors": priors,
        "community_memory_files": [str(p.resolve()) for p in prior_path.parent.glob("*.md")],
    }
    print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
