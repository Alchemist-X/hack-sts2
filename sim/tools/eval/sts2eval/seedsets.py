"""Seed sets: fixed lists of (seed, character) at one ascension and content mode."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

SEEDSET_DIR = Path(__file__).resolve().parent.parent / "seedsets"
CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")
_SEED_RE = re.compile(r"^[A-Z0-9]{1,10}$")


class SeedSetError(ValueError):
    pass


def checksum(data: dict) -> str:
    """sha256 of the canonical seed-set content: {"ascension","content","games":[{"character","seed"}]} as
    compact JSON with sorted keys, games in file order. Names, ids and versions are not part of it."""
    canon = {"ascension": data["ascension"], "content": data["content"],
             "games": [{"character": g["character"], "seed": g["seed"]} for g in data["games"]]}
    return hashlib.sha256(json.dumps(canon, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def frozen_by_checksum() -> dict[str, dict]:
    """Committed seed sets that carry a `sha256` (the frozen ones), by checksum."""
    out = {}
    for p in sorted(SEEDSET_DIR.glob("*.json")):
        try:
            with open(p, encoding="utf-8") as f:
                d = json.load(f)
        except (OSError, ValueError):
            continue
        if d.get("sha256"):
            out[d["sha256"]] = d
    return out


def identity(data: dict | None) -> dict:
    """The seed-set part of a run's identity (protocol v0.2): id, version, sha256 of the content and whether it
    matches a frozen committed set. Works on the seed set embedded in an older config.json too."""
    none = {"seedset_id": None, "seedset_version": None, "seedset_sha256": None, "seedset_frozen": False}
    if not data or not data.get("games"):
        return none
    try:
        sha = checksum(data)
    except (KeyError, TypeError):
        return none
    frozen = frozen_by_checksum().get(sha)
    return {"seedset_id": (frozen or {}).get("id") or data.get("id") or data.get("name"),
            "seedset_version": (frozen or {}).get("version") or data.get("version"),
            "seedset_sha256": sha, "seedset_frozen": frozen is not None}


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
    if data.get("sha256") is not None and data["sha256"] != checksum(data):
        raise SeedSetError(f"{origin}: sha256 {data['sha256']} does not match the content ({checksum(data)}); "
                           "a frozen seed set must not change (publish a new id/version instead)")
    return data


def load(name_or_path: str) -> dict:
    """A committed seed set by name (e.g. 'a0-s10') or id (e.g. 'std-a0-v1'), or a path to a JSON file."""
    p = Path(name_or_path)
    if not p.suffix and not p.exists():
        p = SEEDSET_DIR / f"{name_or_path}.json"
        if not p.exists():
            for q in sorted(SEEDSET_DIR.glob("*.json")):
                try:
                    with open(q, encoding="utf-8") as f:
                        if json.load(f).get("id") == name_or_path:
                            p = q
                            break
                except (OSError, ValueError):
                    continue
    if not p.exists():
        avail = ", ".join(sorted(x.stem for x in SEEDSET_DIR.glob("*.json")))
        raise SeedSetError(f"seed set {name_or_path!r} not found (committed: {avail})")
    with open(p, encoding="utf-8") as f:
        return validate(json.load(f), str(p))


def game_key(game: dict) -> str:
    return f"{game['character']}-{game['seed']}"
