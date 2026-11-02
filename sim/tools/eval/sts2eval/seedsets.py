"""Seed sets: fixed lists of (seed, character) at one ascension and content mode."""

from __future__ import annotations

import json
import re
from pathlib import Path

SEEDSET_DIR = Path(__file__).resolve().parent.parent / "seedsets"
CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")
_SEED_RE = re.compile(r"^[A-Z0-9]{1,10}$")


class SeedSetError(ValueError):
    pass


def validate(data: dict, origin: str = "<inline>") -> dict:
    for key in ("name", "ascension", "content", "games"):
        if key not in data:
            raise SeedSetError(f"{origin}: missing key {key!r}")
    if not isinstance(data["ascension"], int) or not 0 <= data["ascension"] <= 20:
        raise SeedSetError(f"{origin}: ascension must be an int in 0..20")
    if data["content"] not in ("ported_only", "all"):
        raise SeedSetError(f"{origin}: content must be 'ported_only' or 'all'")
    games = data["games"]
    if not isinstance(games, list) or not games:
        raise SeedSetError(f"{origin}: games must be a non-empty list")
    seen = set()
    for i, g in enumerate(games):
        seed, ch = g.get("seed"), g.get("character")
        if not isinstance(seed, str) or not _SEED_RE.match(seed):
            raise SeedSetError(f"{origin}: games[{i}].seed {seed!r} must be 1-10 uppercase alphanumerics")
        if ch not in CHARACTERS:
            raise SeedSetError(f"{origin}: games[{i}].character {ch!r} not in {CHARACTERS}")
        if (ch, seed) in seen:
            raise SeedSetError(f"{origin}: duplicate game {ch} {seed}")
        seen.add((ch, seed))
    return data


def load(name_or_path: str) -> dict:
    """A committed seed set by name (e.g. 'a0-s10'), or a path to a JSON file."""
    p = Path(name_or_path)
    if not p.suffix and not p.exists():
        p = SEEDSET_DIR / f"{name_or_path}.json"
    if not p.exists():
        avail = ", ".join(sorted(x.stem for x in SEEDSET_DIR.glob("*.json")))
        raise SeedSetError(f"seed set {name_or_path!r} not found (committed: {avail})")
    with open(p, encoding="utf-8") as f:
        return validate(json.load(f), str(p))


def game_key(game: dict) -> str:
    return f"{game['character']}-{game['seed']}"
