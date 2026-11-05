"""scripts/analysis/death_profile.py (stdlib death-profile tool) on small synthetic inputs: old-format eval summaries
(no run-state fields), new-format ones (wave 6: killer, boss_entry, room, deck..., fights), old / new episodes.jsonl
rows with --update-range. Asserts the band counts, conditional rates, killer ranking, exposure table and gaps line."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[4] / "scripts" / "analysis" / "death_profile.py"
_spec = importlib.util.spec_from_file_location("death_profile", SCRIPT)
dp = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dp)

R2 = {"1": 17, "2": 16}
S2 = {"1": 0, "2": 17}


def old(seed, result, act, floor, enemies=None):
    s = {"seed": seed, "ascension": 10, "result": result, "act": act, "floor": floor, "final_hp": 0, "max_hp": 70,
         "act_start_floor": S2 if act > 1 else {"1": 0}, "act_rooms": R2 if act > 1 else {"1": 17},
         "progress": 0.1 * floor / 17, "death": None}
    if result == "loss":
        s["death"] = {"floor": floor, "act": act, "room_type": None, "enemies": enemies or []}
    return s


def new(seed, result, act, floor, killer=None, room=None, boss_entry=(), fights=(), deck=20):
    s = old(seed, result, act, floor, [killer] if killer else [])
    if s["death"]:
        s["death"]["room_type"] = room
    s.update(hp=0 if result == "loss" else 40, deck=deck, upgraded=2, relics=3, gold=99, potions=1,
             killer=killer if result == "loss" else None, boss_entry=list(boss_entry), room=room, fights=list(fights))
    return s


def fight(act, floor, room, enemies, result, encounter=None):
    return {"floor": floor, "act": act, "room": room, "enemies": enemies, "encounter": encounter, "hp_before": 50,
            "max_hp_before": 70, "result": result, "hp_after": 40, "max_hp": 70}


def boss(act, floor, hp, name, max_hp=70):
    return {"act": act, "floor": floor, "hp": hp, "max_hp": max_hp, "boss": name}


def write_dir(d: Path, games: list[dict]) -> str:
    (d / "games").mkdir(parents=True)
    for i, g in enumerate(games):
        (d / "games" / f"G{i:03d}.summary.json").write_text(json.dumps(g))
    return str(d)


OLD = [old("s1", "loss", 1, 7, ["PHROG_PARASITE_ELITE"]),
       old("s2", "loss", 1, 17, ["THE_KIN_BOSS"]),
       old("s3", "loss", 1, 17, ["THE_KIN_BOSS"]),
       old("s4", "loss", 2, 21, ["CHOMPERS_NORMAL"]),          # k 4
       old("s5", "loss", 2, 25, ["DECIMILLIPEDE_ELITE"]),      # k 8
       old("s6", "loss", 2, 33, ["KAISER_CRAB_BOSS"]),         # k 16 = L
       old("s7", "capped", 2, 30),                             # trunc at k 13
       old("s8", "win", 2, 33)]

PHROG = ["PHROG_PARASITE"]
DECIM = ["DECIMILLIPEDE_SEGMENT", "DECIMILLIPEDE_SEGMENT"]
NEW = [new("n1", "loss", 1, 17, "THE_KIN_BOSS", "Boss", [boss(1, 17, 35, "THE_KIN_BOSS")],
           [fight(1, 7, "Elite", PHROG, "won"), fight(1, 17, "Boss", ["KIN"], "lost", "THE_KIN_BOSS")], deck=18),
       new("n2", "loss", 2, 25, "DECIMILLIPEDE_ELITE", "Elite", [boss(1, 17, 56, "THE_KIN_BOSS")],
           [fight(1, 7, "Elite", PHROG, "won"), fight(1, 17, "Boss", ["KIN"], "won", "THE_KIN_BOSS"),
            fight(2, 25, "Elite", DECIM, "lost", "DECIMILLIPEDE_ELITE")], deck=26),
       new("n3", "loss", 1, 9, "PHROG_PARASITE_ELITE", "Elite", [],
           [fight(1, 9, "Elite", PHROG, "lost", "PHROG_PARASITE_ELITE")], deck=14),
       new("n4", "win", 2, 33, None, "Boss", [boss(1, 17, 70, "VANTOM_BOSS")],
           [fight(1, 17, "Boss", ["VANTOM"], "won", "VANTOM_BOSS"), fight(2, 24, "Elite", DECIM, "won"),
            fight(2, 26, "Monster", ["SOME_MOB"], "won")])]


def run(specs, **kw):
    ps = dp.profile(specs, **kw)
    lines, js = dp.build(ps, 25)
    return "\n".join(lines), js


def test_old_format_bands_rates_killers_gaps(tmp_path):
    md, js = run([f"old={write_dir(tmp_path / 'old', OLD)}"])
    e = js["end"]["old"]
    assert e["games"] == 8
    assert e["deaths"] == {"A1 pre-boss": 1, "A1 boss": 2, "A2 rooms 1-5": 1, "A2 rooms 6+ pre-boss": 1,
                           "A2 boss": 1}
    assert (e["win"], e["trunc"], e["stall"], e["error"]) == (1, 1, 0, 0)
    c = js["conditional"]["old"]
    assert (c["A1 pre-boss"]["deaths"], c["A1 pre-boss"]["reached"]) == (1, 8)
    assert (c["A1 boss"]["deaths"], c["A1 boss"]["reached"]) == (2, 7)
    assert c["A2 rooms 1-5"]["reached"] == 5                  # s4..s8 (the trunc game ended at A2 k 13)
    assert c["A2 rooms 6+ pre-boss"]["reached"] == 4
    assert (c["A2 boss"]["deaths"], c["A2 boss"]["reached"]) == (1, 2)
    assert list(js["killers"]["old"].items())[0] == ("THE_KIN_BOSS", 2)
    assert js["killer_kind"]["old"] == {"ELITE": 2, "BOSS": 3, "NORMAL": 1}
    assert js["killers_by_band"]["old"]["A2 rooms 6+ pre-boss"] == {"DECIMILLIPEDE_ELITE": 1}
    assert "boss_entry" not in js and "exposure" not in js
    assert js["gaps"]["old"]["absent"] == ["hp", "deck", "upgraded", "relics", "gold", "potions", "killer",
                                           "boss_entry", "room", "fights", "death.room_type (null)"]
    assert "- data gaps old: absent: hp, deck," in md
    assert "| THE_KIN_BOSS | BOSS | 1 | 2 (25.0%) |" in md


def test_new_format_boss_hp_exposure_side_by_side(tmp_path):
    md, js = run([f"old={write_dir(tmp_path / 'old', OLD)}", f"new={write_dir(tmp_path / 'new', NEW)}"])
    assert js["end"]["new"]["deaths"]["A1 boss"] == 1 and js["end"]["new"]["win"] == 1
    assert js["gaps"]["new"] == {"absent": [], "partial": []}
    assert "- data gaps new: absent: none; partial: none" in md
    be = js["boss_entry"]["new"]
    assert be["hp_ratio"][1]["median"] == pytest.approx(0.8)               # 35/70, 56/70, 70/70
    assert be["per_boss"]["THE_KIN_BOSS"]["entered"] == 2 and be["per_boss"]["THE_KIN_BOSS"]["died"] == 1
    assert be["per_boss"]["VANTOM_BOSS"] == {"entered": 1, "died": 0, "hp_ratio_median": 1.0}
    ex = js["exposure"]["new"]
    assert ex["PHROG_PARASITE_ELITE"] == {"fought": 3, "lost": 1}   # two won fights resolved by the monster set
    assert ex["DECIMILLIPEDE_ELITE"] == {"fought": 2, "lost": 1}
    assert ex["THE_KIN_BOSS"] == {"fought": 2, "lost": 1}
    assert ex["~A2 Monster: SOME_MOB (never lost)"] == {"fought": 1, "lost": 0}
    assert "| PHROG_PARASITE_ELITE | ELITE |  | 3 / 1 (33%) |" in md
    assert js["at_death"]["new"]["A1 boss"]["deck"] == 18
    assert js["death_room"]["new"] == {"Boss": 1, "Elite": 2}
    assert "| band | old | new |" in md                       # one table, labels side by side


def test_episodes_old_and_new_rows_update_range(tmp_path):
    rows = [  # old rows (no run-state fields): floor 17 = A1 boss, floor >= 33 in act 2 ~ A2 boss
        {"update": 1, "result": "death", "act": 1, "floor": 17, "progress": 0.31},
        {"update": 1, "result": "death", "act": 2, "floor": 35, "progress": 0.6},
        {"update": 2, "result": "dropped", "act": None, "floor": None},
        {"update": 5, "result": "death", "act": 1, "floor": 3, "progress": 0.03},
        # new rows: boss_entry decides the boss room (an 18-room act 2: floor 35 is its boss, floor 33 is not)
        {"update": 10, "result": "death", "act": 2, "floor": 33, "progress": 0.6, "killer": "DECIMILLIPEDE_ELITE",
         "boss_entry": [boss(1, 17, 49, "THE_KIN_BOSS")], "room": "Elite", "hp": 0, "max_hp": 70, "deck": 25,
         "upgraded": 4, "relics": 5, "gold": 10, "potions": 0},
        {"update": 11, "result": "death", "act": 2, "floor": 35, "progress": 0.64, "killer": "KAISER_CRAB_BOSS",
         "boss_entry": [boss(1, 17, 70, "VANTOM_BOSS"), boss(2, 35, 35, "KAISER_CRAB_BOSS")], "room": "Boss",
         "hp": 0, "max_hp": 70, "deck": 30, "upgraded": 6, "relics": 6, "gold": 0, "potions": 2},
        {"update": 11, "result": "stall", "act": 1, "floor": 12, "progress": 0.2, "killer": None, "boss_entry": [],
         "room": "Shop", "hp": 20, "max_hp": 70, "deck": 15, "upgraded": 1, "relics": 2, "gold": 300, "potions": 1}]
    p = tmp_path / "episodes.jsonl"
    p.write_text("\n".join(json.dumps(r) for r in rows) + "\n{\"update\": 12, \"resu")   # torn last line
    md, js = run([f"early={p}@1-2", f"late={p}@10-11"])
    assert js["end"]["early"]["deaths"]["A1 boss"] == 1 and js["end"]["early"]["deaths"]["A2 boss"] == 1
    assert js["end"]["early"]["games"] == 2                  # dropped row skipped, update 5 outside the range
    late = js["end"]["late"]
    assert late["deaths"]["A2 rooms 6+ pre-boss"] == 1 and late["deaths"]["A2 boss"] == 1 and late["stall"] == 1
    assert js["boss_entry"]["late"]["per_boss"]["KAISER_CRAB_BOSS"] == {"entered": 1, "died": 1,
                                                                         "hp_ratio_median": 0.5}
    assert js["conditional"]["late"]["A1 boss"]["reached"] == 2   # the stall ended before the A1 boss
    assert "killer" in js["gaps"]["early"]["absent"] and "killer" not in js["gaps"]["late"]["absent"]
    assert "act_rooms (episodes: default act lengths used)" in js["gaps"]["late"]["absent"]
    _, js2 = run([f"all={p}"], update_range="5-11")
    assert js2["end"]["all"]["games"] == 4


def test_cli_main_writes_json(tmp_path, capsys):
    d = write_dir(tmp_path / "old", OLD)
    out = tmp_path / "p.json"
    assert dp.main([f"m={d}", "--json", str(out), "--top", "2"]) == 0
    text = capsys.readouterr().out
    assert text.startswith("# Death profile: m (n 8)")
    assert "| other 3 encounters |" in text
    assert json.loads(out.read_text())["end"]["m"]["games"] == 8
