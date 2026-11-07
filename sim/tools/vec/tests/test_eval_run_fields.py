"""`sts2vec eval` game summaries carry the run state at the end (``sts2vec.runstats``, 2026-11-05): hp / deck /
relics / gold / potions / killer / boss_entry / room and the fights list, ``death.room_type`` filled (it was always
null), consistent with each other and with the protocol fields; the report still renders."""

from __future__ import annotations

import json

from sts2vec.evaluate import evaluate
from sts2vec.runstats import FIELDS, FIGHT_KEYS

SS = {"name": "t3", "ascension": 0, "content": "ported_only",
      "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"},
                {"seed": "E1A0DE01", "character": "DEFECT"}]}
COMBAT_ROOMS = ("Monster", "Elite", "Boss")


def test_summaries_carry_run_state_and_fights(tmp_path, full):
    for policy in ("random", "heuristic"):
        out = tmp_path / policy
        rep = evaluate(SS, policy, out, threads=2, log=lambda _: None)
        assert rep["headline"]["replayable"] == 3
        for p in sorted((out / "games").glob("*.summary.json")):
            s = json.loads(p.read_text())
            assert set(FIELDS) | {"fights"} <= set(s), sorted(set(FIELDS) - set(s))
            assert s["hp"] == s["final_hp"] and s["deck"] >= 10 and 0 <= s["upgraded"] <= s["deck"]
            assert s["room"] is not None and s["relics"] >= 1 and s["gold"] >= 0
            fights = s["fights"]
            assert fights and all(set(f) == set(FIGHT_KEYS) for f in fights)
            assert [f["floor"] for f in fights] == sorted(f["floor"] for f in fights)
            assert all(f["result"] == "won" and f["hp_after"] is not None for f in fights[:-1])
            assert all(f["room"] in COMBAT_ROOMS and f["enemies"] for f in fights)
            assert len(s["boss_entry"]) == sum(f["room"] == "Boss" for f in fights)
            if s["result"] == "loss":
                d = s["death"]
                assert d["room_type"] == s["room"] and s["hp"] == 0
                if s["killer"] is not None:
                    assert [s["killer"]] == d["enemies"] and s["room"] in COMBAT_ROOMS
                    assert fights[-1]["result"] == "lost" and fights[-1]["encounter"] == s["killer"]
                    assert fights[-1]["floor"] == s["floor"] and fights[-1]["hp_after"] == 0
            else:
                assert s["killer"] is None
        assert (out / "report.md").read_text().startswith("# Eval report")
