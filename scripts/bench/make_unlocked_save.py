#!/usr/bin/env python3
"""Turn a Slay the Spire 2 progress.save into a fully-unlocked one (bench baseline v2).

Research/benchmark use only. The edit mirrors the game's own developer console
command ``unlock all`` (``UnlockConsoleCmd`` in sts2.dll) and adds the few extra
fields a fully-progressed profile has, so menus, seeds/custom mode and every
character are available without timeline reveals or first-time popups:

* every timeline epoch -> ``revealed`` (incl. CUSTOM_AND_SEEDS_EPOCH, DAILY_RUN_EPOCH,
  all character epochs, so all 5 characters are selectable);
* discovered cards / relics / potions / events / acts = ``ModelDb.All*`` of the game;
* ``enemy_stats`` for every monster referenced by an act encounter (``unlock all``
  adds Ironclad wins=1 when a monster has no fight stats; same rule here);
* ``encounter_stats`` keys for every encounter (empty fight stats) so the
  standard-mode "first boss you have not seen" discovery-order override is a no-op;
* every character ``max_ascension`` = 10 (and multiplayer max 10) while
  ``preferred_ascension`` stays 0, so a default lobby is still Ascension 0;
* every FTUE/popup id referenced by the game marked completed;
* ``total_unlocks`` = length of the agnostic epoch unlock order;
* ``pending_character_unlock`` = ``NONE.NONE``.

Every ID list is derived from the game itself: the script decompiles
``$STS2_GAME_DATA_DIR/sts2.dll`` with ILSpy (cached under
``artifacts/knowledge/decompiled-sts2-<sha12>``, git-ignored) and parses ModelDb,
the character/pool/act/encounter models and the timeline epochs. Nothing is
hand-typed. progress.save is plain JSON (schema 21) without a checksum
or signature; the game falls back to ``progress.save.backup`` when the primary is
unreadable, so both are written identically.

Never commit save files or game assets.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MAX_ASCENSION = 10  # AscensionManager clamps at 10 (UnlockConsoleCmd uses 10)
FIXED_OBTAIN_DATE = 1793000000  # deterministic timestamp for newly revealed epochs


# --------------------------------------------------------------------------- ids

def slugify(name: str) -> str:
    """Python port of MegaCrit StringHelper.Slugify (CamelCase -> UPPER_SNAKE)."""
    s = re.sub(r"(?<=[A-Za-z0-9])([A-Z])", r"_\1", name.strip()).upper()
    s = re.sub(r"\s+", "_", s)
    return re.sub(r"[^A-Z0-9_]", "", s)


def _read(root: Path, ns: str, cls: str) -> str:
    p = root / ns / f"{cls}.cs"
    if not p.is_file():
        raise SystemExit(f"missing decompiled source: {p}")
    return p.read_text(encoding="utf-8")


def _block(text: str, marker: str) -> str:
    """Return a member's body: an `=> expr;` expression or a balanced `{ ... }` block."""
    i = text.find(marker)
    if i < 0:
        raise SystemExit(f"marker not found: {marker!r}")
    j = i + len(marker)
    arrow, brace = text.find("=>", j), text.find("{", j)
    if arrow >= 0 and (brace < 0 or arrow < brace) and ";" not in text[j:arrow] and "\n\t{" not in text[j:arrow]:
        depth = 0
        for k in range(arrow, len(text)):
            c = text[k]
            depth += c in "({[" 
            depth -= c in ")}]"
            if c == ";" and depth == 0:
                return text[arrow:k]
        return text[arrow:]
    depth = 0
    for k in range(brace, len(text)):
        depth += text[k] == "{"
        depth -= text[k] == "}"
        if depth == 0:
            return text[brace:k + 1]
    return text[brace:]


def _refs(text: str, kind: str) -> list[str]:
    out: list[str] = []
    for m in re.finditer(rf"\b{kind}<(\w+)>\(\)", text):
        if m.group(1) not in out:
            out.append(m.group(1))
    return out


def _pool_items(root: Path, ns: str, pool: str, method: str, kind: str) -> list[str]:
    body = _block(_read(root, ns, pool), method)
    items = _refs(body, kind)
    # e.g. IroncladPotionPool.GenerateAllPotions() => Ironclad4Epoch.Potions
    for cls, member in re.findall(r"\b(\w+Epoch)\.(\w+)\b", body):
        src = _read(root, "MegaCrit.Sts2.Core.Timeline.Epochs", cls)
        items += [x for x in _refs(_block(src, f" {member}"), kind) if x not in items]
    return items


def derive(root: Path) -> dict:
    M = "MegaCrit.Sts2.Core.Models"
    modeldb = _read(root, M, "ModelDb")

    characters = _refs(_block(modeldb, "public static IEnumerable<CharacterModel> AllCharacters =>"), "Character")
    acts = _refs(_block(modeldb, "public static IEnumerable<ActModel> Acts"), "Act")
    shared_card_pools = _refs(_block(modeldb, "public static IEnumerable<CardPoolModel> AllSharedCardPools =>"), "CardPool")
    shared_relic_pools = _refs(_block(modeldb, "private static IEnumerable<RelicPoolModel> AllSharedRelicPools =>"), "RelicPool")
    shared_potion_pools = _refs(_block(modeldb, "private static IEnumerable<PotionPoolModel> AllSharedPotionPools =>"), "PotionPool")
    shared_events = _refs(_block(modeldb, "public static IEnumerable<EventModel> AllSharedEvents =>"), "Event")

    cards: list[str] = []
    relics: list[str] = []
    potions: list[str] = []
    card_pools, relic_pools, potion_pools = [], [], []
    starting_cards, starting_relics = [], []
    for ch in characters:
        src = _read(root, f"{M}.Characters", ch)
        card_pools += _refs(_block(src, "public override CardPoolModel CardPool"), "CardPool")
        relic_pools += _refs(_block(src, "public override RelicPoolModel RelicPool"), "RelicPool")
        potion_pools += _refs(_block(src, "public override PotionPoolModel PotionPool"), "PotionPool")
        starting_cards += _refs(_block(src, "public override IEnumerable<CardModel> StartingDeck"), "Card")
        starting_relics += _refs(_block(src, "public override IReadOnlyList<RelicModel> StartingRelics"), "Relic")
    card_pools += shared_card_pools
    relic_pools += shared_relic_pools
    potion_pools += shared_potion_pools

    def add(dst: list[str], items: list[str]) -> None:
        for x in items:
            if x not in dst:
                dst.append(x)

    # ModelDb.AllCards = pools' cards + starting decks; AllRelics = pools + starting relics.
    for p in dict.fromkeys(card_pools):
        add(cards, _pool_items(root, f"{M}.CardPools", p, "GenerateAllCards()", "Card"))
    add(cards, starting_cards)
    for p in dict.fromkeys(relic_pools):
        add(relics, _pool_items(root, f"{M}.RelicPools", p, "GenerateAllRelics()", "Relic"))
    add(relics, starting_relics)
    for p in dict.fromkeys(potion_pools):
        add(potions, _pool_items(root, f"{M}.PotionPools", p, "GenerateAllPotions()", "Potion"))

    events: list[str] = []
    encounters: list[str] = []
    for a in acts:
        src = _read(root, f"{M}.Acts", a)
        add(events, _refs(_block(src, "public override IEnumerable<EventModel> AllEvents"), "Event"))
        add(encounters, _refs(_block(src, "GenerateAllEncounters()"), "Encounter"))
    add(events, shared_events)

    monsters: list[str] = []
    for e in encounters:
        src = _read(root, f"{M}.Encounters", e)
        # AllPossibleMonsters is often computed from private fields, so take every
        # monster the encounter class references (a superset; all real MonsterModels).
        add(monsters, _refs(src, "Monster"))

    # Epochs: EpochModel.EpochIds lists GetId<XEpoch>(); each class overrides Id.
    tl = "MegaCrit.Sts2.Core.Timeline"
    epoch_model = _read(root, tl, "EpochModel")
    epoch_classes = _refs(_block(epoch_model, "EpochIds"), "GetId")
    epochs = []
    for cls in epoch_classes:
        src = _read(root, f"{tl}.Epochs", cls)
        m = re.search(r'override string Id => "([A-Z0-9_]+)"', src)
        if not m:
            raise SystemExit(f"no Id in epoch {cls}")
        epochs.append(m.group(1))

    savemgr = _read(root, "MegaCrit.Sts2.Core.Saves", "SaveManager")
    agnostic = _refs(_block(savemgr, "_agnosticEpochUnlockOrder ="), "GetId")

    ftues: list[str] = []
    for p in sorted(root.rglob("*.cs")):
        t = p.read_text(encoding="utf-8", errors="replace")
        for m in re.finditer(r'(?:MarkFtueAsComplete|SeenFtue|SeenPopup|MarkPopupAsSeen)\("([a-z0-9_]+)"\)', t):
            if m.group(1) not in ftues:
                ftues.append(m.group(1))
    ftues.sort()

    return {
        "characters": [f"CHARACTER.{slugify(x)}" for x in characters],
        "cards": [f"CARD.{slugify(x)}" for x in cards],
        "relics": [f"RELIC.{slugify(x)}" for x in relics],
        "potions": [f"POTION.{slugify(x)}" for x in potions],
        "events": [f"EVENT.{slugify(x)}" for x in events],
        "acts": [f"ACT.{slugify(x)}" for x in acts],
        "encounters": [f"ENCOUNTER.{slugify(x)}" for x in encounters],
        "monsters": [f"MONSTER.{slugify(x)}" for x in monsters],
        "epochs": epochs,
        "agnostic_unlock_count": len(agnostic),
        "ftues": ftues,
        "card_pools": list(dict.fromkeys(card_pools)),
        "relic_pools": list(dict.fromkeys(relic_pools)),
        "potion_pools": list(dict.fromkeys(potion_pools)),
    }


# ------------------------------------------------------------------------- edit

def _char_stats(cid: str) -> dict:
    return {"badges": [], "best_win_streak": 0, "current_streak": 0, "fastest_win_time": -1,
            "id": cid, "max_ascension": 0, "playtime": 0, "preferred_ascension": 0,
            "total_losses": 0, "total_wins": 0}


def unlock(progress: dict, ids: dict) -> dict:
    p = json.loads(json.dumps(progress))
    if p.get("schema_version") != 21:
        raise SystemExit(f"unexpected progress schema_version {p.get('schema_version')} (expected 21)")

    by_id = {e["id"]: e for e in p.get("epochs", [])}
    for eid in ids["epochs"]:
        e = by_id.get(eid)
        if e is None:
            e = {"id": eid, "obtain_date": FIXED_OBTAIN_DATE, "state": "revealed"}
            p["epochs"].append(e)
            by_id[eid] = e
        if e["state"] != "revealed":
            e["state"] = "revealed"
            if not e.get("obtain_date"):
                e["obtain_date"] = FIXED_OBTAIN_DATE

    for key, src in (("discovered_cards", "cards"), ("discovered_relics", "relics"),
                     ("discovered_potions", "potions"), ("discovered_events", "events"),
                     ("discovered_acts", "acts")):
        cur = p.setdefault(key, [])
        cur.extend(x for x in ids[src] if x not in cur)

    seen_enemies = {e["enemy_id"]: e for e in p.setdefault("enemy_stats", [])}
    for mid in ids["monsters"]:
        e = seen_enemies.get(mid)
        if e is None:
            e = {"enemy_id": mid, "fight_stats": []}
            p["enemy_stats"].append(e)
        if not e["fight_stats"]:  # same rule as UnlockConsoleCmd
            e["fight_stats"].append({"character": "CHARACTER.IRONCLAD", "losses": 0, "wins": 1})

    seen_enc = {e["encounter_id"] for e in p.setdefault("encounter_stats", [])}
    for enc in ids["encounters"]:
        if enc not in seen_enc:
            p["encounter_stats"].append({"encounter_id": enc, "fight_stats": []})

    stats = {c["id"]: c for c in p.setdefault("character_stats", [])}
    for cid in ids["characters"]:
        c = stats.get(cid)
        if c is None:
            c = _char_stats(cid)
            p["character_stats"].append(c)
        c["max_ascension"] = MAX_ASCENSION
        c["preferred_ascension"] = 0
    p["max_multiplayer_ascension"] = MAX_ASCENSION
    p["preferred_multiplayer_ascension"] = 0

    ftue = p.setdefault("ftue_completed", [])
    ftue.extend(x for x in ids["ftues"] if x not in ftue)

    p["total_unlocks"] = max(int(p.get("total_unlocks", 0)), ids["agnostic_unlock_count"])
    p["pending_character_unlock"] = "NONE.NONE"
    return p


def summary(before: dict, after: dict, ids: dict) -> dict:
    def n(d, k):
        return len(d.get(k, []))
    rev = lambda d: sum(1 for e in d.get("epochs", []) if e["state"] == "revealed")
    return {
        "epochs_revealed": f"{rev(before)} -> {rev(after)} / {len(ids['epochs'])}",
        "characters": [c["id"] for c in after["character_stats"]],
        "max_ascension": {c["id"]: c["max_ascension"] for c in after["character_stats"]},
        **{k: f"{n(before, k)} -> {n(after, k)}" for k in (
            "discovered_cards", "discovered_relics", "discovered_potions", "discovered_events",
            "discovered_acts", "encounter_stats", "enemy_stats", "ftue_completed")},
        "model_counts": {k: len(ids[k]) for k in ("cards", "relics", "potions", "events", "acts",
                                                   "encounters", "monsters", "epochs", "characters")},
    }


def dump(p: dict) -> bytes:
    return (json.dumps(p, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def decompile_live(data_dir: Path) -> tuple[Path, str]:
    """Decompile the live sts2.dll (ILSpy) into a cache keyed by its sha256."""
    dll = data_dir / "sts2.dll"
    digest = sha256(dll)
    out = REPO / "artifacts" / "knowledge" / f"decompiled-sts2-{digest[:12]}"
    stamp = out / ".sts2_dll_sha256"
    if stamp.is_file() and stamp.read_text().strip() == digest:
        return out, digest
    ilspy = shutil.which("ilspycmd") or str(Path.home() / ".dotnet" / "tools" / "ilspycmd")
    tmp = out.with_name(out.name + ".tmp")
    shutil.rmtree(tmp, ignore_errors=True)
    subprocess.run([ilspy, "--disable-updatecheck", "-p", "-o", str(tmp), "-r", str(data_dir), str(dll)],
                   check=True, stdout=subprocess.DEVNULL)
    (tmp / ".sts2_dll_sha256").write_text(digest + "\n")
    shutil.rmtree(out, ignore_errors=True)
    tmp.rename(out)
    return out, digest


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--saves-dir", type=Path, required=False,
                    help="profile saves dir containing progress.save (edited in place)")
    ap.add_argument("--version", default="0.107.1", help="game version recorded in the summary")
    ap.add_argument("--decompiled-root", type=Path, default=None,
                    help="use an existing ILSpy tree of the same sts2.dll instead of decompiling")
    ap.add_argument("--game-data-dir", type=Path, default=os.environ.get("STS2_GAME_DATA_DIR"))
    ap.add_argument("--dry-run", action="store_true", help="print the summary only")
    ap.add_argument("--dump-ids", type=Path, help="write the derived id lists as JSON")
    args = ap.parse_args(argv)

    if not args.game_data_dir:
        raise SystemExit("set STS2_GAME_DATA_DIR or pass --game-data-dir")
    data_dir = Path(args.game_data_dir)
    if args.decompiled_root:
        root, dll = args.decompiled_root, sha256(data_dir / "sts2.dll")
    else:
        root, dll = decompile_live(data_dir)
    if not (root / "MegaCrit.Sts2.Core.Models" / "ModelDb.cs").is_file():
        raise SystemExit(f"decompiled tree not found at {root}")
    ids = derive(root)
    if args.dump_ids:
        args.dump_ids.write_text(json.dumps(ids, indent=2) + "\n")
    if args.saves_dir is None:
        print(json.dumps({k: len(v) if isinstance(v, list) else v for k, v in ids.items()}, indent=2))
        return 0

    sd = args.saves_dir.resolve()
    real = Path.home() / ".local" / "share" / "SlayTheSpire2"
    if str(sd).startswith(str(real.resolve())) or "worker-pool" not in str(sd):
        raise SystemExit(f"refusing to edit {sd}: only worker-pool copies may be edited")
    prog = sd / "progress.save"
    before = json.loads(prog.read_text(encoding="utf-8"))
    after = unlock(before, ids)
    out = {"game_version": args.version, "sts2_dll_sha256": dll, **summary(before, after, ids)}
    if not args.dry_run:
        data = dump(after)
        prog.write_bytes(data)
        (sd / "progress.save.backup").write_bytes(data)
        out["progress_save_sha256"] = hashlib.sha256(data).hexdigest()
    print(json.dumps(out, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
