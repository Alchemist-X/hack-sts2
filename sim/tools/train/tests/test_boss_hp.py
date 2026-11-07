"""Boss-HP fraction (``sts2train.bosshp``) and ``--boss-hp-bonus B`` (wave 20, ``objective.EpisodeRewards``): the
encounter table matches the sources; the fraction of a single boss, a multi-form boss (TEST_SUBJECT), a boss with a
minion (QUEEN), the A10 double boss (per-act ``(k + f) / n``), the earned baseline of a mid-fight start and the fallback
for an encounter missing from the table, all on synthetic observations; the measurement uses the last decision
observation, never the auto-reset successor's; bonus off = rewards / records unchanged, bonus on = B x frac at the
terminal step only (in the return, never in progress); metrics, episodes.jsonl, config / CLI / resume defaults and the
trainer's wiring (``--overlap off`` and ``double``) on the real library."""

from __future__ import annotations

import json
import math

import numpy as np
import pytest
from sts2vec.schema import Schema

from sts2train import bosshp
from sts2train.bosshp import BossHpTracker, build_table, dump_table, load_table
from sts2train.objective import EpisodeRewards, boss_hp_stats

AUX = ["total_floor", "act_index", "act_floor", "act_rooms", "is_combat"]
LE = ["win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id"]
GI = ["phase", "ascension", "act_index", "total_floor", "map_row", "map_col", "boss_id", "second_boss_id", "is_combat"]
EF = ["present", "uid", "monster_id", "hp", "max_hp", "block", "flank", "behind", "intent_n"]
ENC = ["", "?", "AEONGLASS_BOSS", "QUEEN_BOSS", "TEST_SUBJECT_BOSS", "KAISER_CRAB_BOSS", "MYSTERY_BOSS",
       "VANTOM_BOSS"]
MON = ["", "?", "AEONGLASS", "QUEEN", "TORCH_HEAD_AMALGAM", "TEST_SUBJECT", "CRUSHER", "ROCKET", "BLOB", "VANTOM"]
PT = ["Unassigned", "Unknown", "Monster", "Elite", "Boss", "RestSite"]
ROW, COL = 3, 1
B = 0.3


def _schema(routes: bool = True) -> Schema:
    bufs = [{"name": "global_i", "dtype": "int32", "shape": [len(GI)], "fields": GI},
            {"name": "enemies", "dtype": "int32", "shape": [6, len(EF)], "fields": EF},
            {"name": "map_nodes", "dtype": "int32", "shape": [4, 3, 6],
             "fields": ["point_type", "visited", "current", "legal_next", "child_mask", "quest"]},
            {"name": "aux", "dtype": "float32", "shape": [len(AUX)], "fields": AUX},
            {"name": "last_episode", "dtype": "int32", "shape": [len(LE)], "fields": LE}]
    if routes:
        bufs.insert(3, {"name": "map_routes", "dtype": "int32", "shape": [4, 3, 5],
                        "fields": ["elites_min", "elites_max", "rests", "shops", "boss_dist"]})
    raw = {"encoder_version": "test", "buffers": bufs, "caps": {},
           "vocab": {"encounters": ENC, "monsters": MON, "enums": {"MapPointType": PT}}}
    return Schema.from_json(json.dumps(raw))


def S(floor=48, act0=2, combat=1, node="Boss", second=False, boss="AEONGLASS_BOSS", boss2="QUEEN_BOSS", asc=10,
      enemies=()):
    """One slot's observation: act / floor, the current map node's type, which boss (``second`` -> boss_dist -1), the
    act's boss ids and the present enemy rows ((uid, monster, hp, max_hp), ...)."""
    return dict(floor=floor, act0=act0, combat=combat, node=node, second=second, boss=boss, boss2=boss2, asc=asc,
                enemies=list(enemies))


def OUT(floor=10, act0=0, **kw):        # an out-of-combat observation (map / rewards / a fresh run)
    return S(floor=floor, act0=act0, combat=0, node="Monster", **kw)


def _obs(slots, routes: bool = True) -> dict:
    n = len(slots)
    gi = np.zeros((n, len(GI)), np.int32)
    en = np.zeros((n, 6, len(EF)), np.int32)
    mn = np.zeros((n, 4, 3, 6), np.int32)
    mr = np.zeros((n, 4, 3, 5), np.int32)
    aux = np.zeros((n, len(AUX)), np.float32)
    for i, s in enumerate(slots):
        g = dict(ascension=s["asc"], act_index=s["act0"], total_floor=s["floor"], map_row=ROW, map_col=COL,
                 boss_id=ENC.index(s["boss"]) if s["boss"] else 0,
                 second_boss_id=ENC.index(s["boss2"]) if s["boss2"] else 0, is_combat=s["combat"])
        for k, v in g.items():
            gi[i, GI.index(k)] = v
        mn[i, ROW, COL, 0] = PT.index(s["node"])
        mr[i, ROW, COL, 4] = -1 if s["second"] else 0
        for j, (uid, mon, hp, mx) in enumerate(s["enemies"]):
            en[i, j, :5] = (1, uid, MON.index(mon), hp, mx)
        start = {0: 0, 1: 17, 2: 33}[s["act0"]]
        aux[i] = (s["floor"], s["act0"], s["floor"] - start, 16, s["combat"])
    o = {"global_i": gi, "enemies": en, "map_nodes": mn, "aux": aux}
    if routes:
        o["map_routes"] = mr
    return o


def _le(win=0, floor=48, act0=2, end_kind=1):
    r = np.zeros(len(LE), np.int32)
    r[LE.index("win")], r[LE.index("floor")], r[LE.index("act")] = win, floor, act0
    r[LE.index("steps")], r[LE.index("end_kind")] = 5, end_kind
    return r


def _track(seq, *, min_act=2, routes=True):
    """Feed one slot's decision observations to a tracker and return (R after each, end())."""
    t = BossHpTracker(_schema(routes), 1, min_act=min_act)
    assert t.ok
    rs = []
    for s in seq:
        t.observe(_obs([s], routes))
        f = t.fights[0]
        rs.append(None if f is None or not t.in_fight[0] else f.r_last)
    return rs, t.end(0)


# ------------------------------------------------------------------------------------------------ table
def test_table_regenerates_from_the_sources():
    built = build_table()
    assert built == load_table()
    assert dump_table(built) == bosshp.TABLE_PATH.read_text()          # the committed file is the generator's output
    enc = built["encounters"]
    assert "MOCK_BOSS_ENCOUNTER" not in enc and len(enc) == 12
    pools = {k: v["pool"]["tough"] for k, v in enc.items()}
    assert pools["AEONGLASS_BOSS"] == 535 and pools["QUEEN_BOSS"] == 419 and pools["TEST_SUBJECT_BOSS"] == 636
    assert pools["KAISER_CRAB_BOSS"] == 219 + 209 and pools["THE_KIN_BOSS"] == 199
    assert enc["TEST_SUBJECT_BOSS"]["units"] == [{"monster": "TEST_SUBJECT",
                                                 "forms": {"base": [100, 200, 300], "tough": [111, 212, 313]}}]
    assert enc["QUEEN_BOSS"]["minions"] == ["TORCH_HEAD_AMALGAM"] and enc["THE_KIN_BOSS"]["minions"] == ["KIN_FOLLOWER"]
    assert {v["act"] for k, v in enc.items() if k in ("AEONGLASS_BOSS", "QUEEN_BOSS", "TEST_SUBJECT_BOSS")} == {3}
    assert built["tough_enemies_ascension"] == 8


# ------------------------------------------------------------------------------------------------ one fight
def test_single_boss_fraction_from_the_fight_entry():
    seq = [OUT(47, 2), S(enemies=[(7, "AEONGLASS", 535, 535)]), S(enemies=[(7, "AEONGLASS", 300, 535)]),
           S(enemies=[(7, "AEONGLASS", 124, 535)])]
    rs, m = _track(seq)
    assert rs == [None, 535, 300, 124]
    assert m["encounter"] == "AEONGLASS_BOSS" and m["table"] and m["act"] == 3 and not m["second"]
    assert m["fight_frac"] == pytest.approx(411 / 535)
    assert m["bosses"] == 2 and m["first_killed"] == 0 and m["frac"] == pytest.approx(411 / 535 / 2)   # A10 act 3
    _, m1 = _track([S(boss2=None, enemies=[(7, "AEONGLASS", 535, 535)]), S(boss2=None, enemies=[(7, "AEONGLASS", 124,
                                                                                                 535)])])
    assert m1["bosses"] == 1 and m1["frac"] == pytest.approx(411 / 535)            # no second boss: F = f
    _, m2 = _track([S(asc=5, boss2=None, enemies=[(7, "AEONGLASS", 512, 512)]),
                    S(asc=5, boss2=None, enemies=[(7, "AEONGLASS", 112, 512)])])
    assert m2["pool"] == 512 and m2["frac"] == pytest.approx(400 / 512)             # below ToughEnemies: base HP


def test_multi_form_test_subject_counts_dead_forms_in_full():
    TS = "TEST_SUBJECT_BOSS"
    seq = [S(boss=TS, enemies=[(3, "TEST_SUBJECT", 111, 111)]), S(boss=TS, enemies=[(3, "TEST_SUBJECT", 23, 111)]),
           S(boss=TS, enemies=[]),                                          # form 1 dead, waiting to revive (row absent)
           S(boss=TS, enemies=[(3, "TEST_SUBJECT", 212, 212)]), S(boss=TS, enemies=[(3, "TEST_SUBJECT", 140, 212)]),
           S(boss=TS, enemies=[]),
           S(boss=TS, enemies=[(3, "TEST_SUBJECT", 313, 313)]), S(boss=TS, enemies=[(3, "TEST_SUBJECT", 287, 313)])]
    rs, m = _track(seq)
    assert rs == [636, 23 + 525, 525, 525, 140 + 313, 313, 313, 287]
    assert m["fight_frac"] == pytest.approx((636 - 287) / 636) and m["frac"] == pytest.approx((636 - 287) / 636 / 2)
    # a death right after the first form fell (row absent): both later forms remain
    _, m2 = _track(seq[:3])
    assert m2["fight_frac"] == pytest.approx(111 / 636)


def test_minions_are_left_out_and_the_double_boss_is_per_act():
    """A10 act 3: QUEEN (the Amalgam is a minion: its HP never counts) killed at floor 48, then the second boss at floor
    49 (boss_dist -1): F = (1 + f2) / 2; a save start at the second boss gets k = 0."""
    Q, AE = "QUEEN_BOSS", "AEONGLASS_BOSS"
    kw = dict(boss=Q, boss2=AE)
    first = [S(**kw, enemies=[(1, "TORCH_HEAD_AMALGAM", 211, 211), (2, "QUEEN", 419, 419)]),
             S(**kw, enemies=[(1, "TORCH_HEAD_AMALGAM", 50, 211), (2, "QUEEN", 373, 419)])]
    rs, m = _track(first)
    assert rs == [419, 373] and m["encounter"] == Q and m["fight_frac"] == pytest.approx(46 / 419)
    assert m["frac"] == pytest.approx(46 / 419 / 2)
    second = [S(floor=49, second=True, **kw, enemies=[(9, "AEONGLASS", 535, 535)]),
              S(floor=49, second=True, **kw, enemies=[(9, "AEONGLASS", 400, 535)])]
    seq = first + [OUT(48, 2, **kw)] + second                    # the boss's Rewards screen between the fights
    rs, m = _track(seq)
    assert rs[-2:] == [535, 400] and m["encounter"] == AE and m["second"] and m["first_killed"] == 1
    assert m["fight_frac"] == pytest.approx(135 / 535) and m["frac"] == pytest.approx((1 + 135 / 535) / 2)
    _, m0 = _track(second)                                       # started at the second boss: the first is not earned
    assert m0["first_killed"] == 0 and m0["frac"] == pytest.approx(135 / 535 / 2)
    # without map_routes the second fight is told apart by a first-boss fight earlier in the act
    _, mr = _track(seq, routes=False)
    assert mr["second"] and mr["encounter"] == AE and mr["frac"] == pytest.approx((1 + 135 / 535) / 2)


def test_earned_baseline_is_the_first_observed_decision():
    _, m = _track([S(enemies=[(7, "AEONGLASS", 300, 535)]), S(enemies=[(7, "AEONGLASS", 124, 535)])])
    assert m["hp_first"] == 300 and m["fight_frac"] == pytest.approx(176 / 535)
    _, m = _track([S(enemies=[(7, "AEONGLASS", 300, 535)])])                    # died at the first decision: 0
    assert m["fight_frac"] == 0.0 and m["frac"] == 0.0


def test_fallback_for_an_encounter_missing_from_the_table_and_two_unit_boss():
    _, m = _track([S(boss="MYSTERY_BOSS", boss2=None, enemies=[(1, "BLOB", 100, 100), (2, "BLOB", 50, 50)]),
                   S(boss="MYSTERY_BOSS", boss2=None, enemies=[(1, "BLOB", 40, 100)])])
    assert not m["table"] and m["pool"] == 150 and m["fight_frac"] == pytest.approx(110 / 150)
    # KAISER_CRAB (act 2): both crabs are required; the dead one counts in full
    K = dict(floor=33, act0=1, boss="KAISER_CRAB_BOSS", boss2=None)
    _, m = _track([S(**K, enemies=[(1, "CRUSHER", 219, 219), (2, "ROCKET", 209, 209)]),
                   S(**K, enemies=[(2, "ROCKET", 100, 209)])])
    assert m["act"] == 2 and m["fight_frac"] == pytest.approx((428 - 100) / 428) and m["bosses"] == 1


def test_not_measured_below_min_act_or_outside_a_boss_fight():
    V = dict(floor=16, act0=0, boss="VANTOM_BOSS", boss2=None)
    _, m = _track([S(**V, enemies=[(1, "VANTOM", 183, 183)]), S(**V, enemies=[(1, "VANTOM", 83, 183)])])
    assert m is None                                                            # act 1 < min_act 2
    _, m = _track([S(**V, enemies=[(1, "VANTOM", 183, 183)]), S(**V, enemies=[(1, "VANTOM", 83, 183)])], min_act=1)
    assert m["act"] == 1 and m["frac"] == pytest.approx(100 / 183)
    _, m = _track([S(enemies=[(7, "AEONGLASS", 535, 535)]), S(node="Elite", enemies=[(1, "BLOB", 5, 50)])])
    assert m is None                                                            # the last decision was an elite fight
    _, m = _track([S(enemies=[(7, "AEONGLASS", 0, 535)]), OUT(48, 2)])          # boss Rewards screen: no fight
    assert m is None


# ------------------------------------------------------------------------------------------------ EpisodeRewards
# per slot: first observation, then per step ("obs", S) or ("end", last_episode kwargs, successor's first observation)
AE7 = lambda hp: [(7, "AEONGLASS", hp, 535)]  # noqa: E731
LINES = [
    # dies in the act-3 boss fight; the successor is a fresh run (its observation must not be used)
    (OUT(47, 2), [("obs", S(enemies=AE7(535))), ("obs", S(enemies=AE7(124))), ("end", {}, OUT(0, 0))]),
    # wins the run in a boss fight: nothing (win_bonus / --boss-bonus apply)
    (S(enemies=AE7(535)), [("obs", S(enemies=AE7(10))), ("end", {"win": 1}, OUT(0, 0)), ("obs", OUT(1, 0))]),
    # step cap (end_kind 2) in the fight: measured; the successor is an anchor start inside a boss fight (its own
    # baseline 300) that floor-stalls (end_kind 5): not measured
    (S(enemies=AE7(535)), [("end", {"end_kind": 2}, S(enemies=AE7(300))), ("obs", S(enemies=AE7(200))),
                           ("end", {"end_kind": 5}, OUT(0, 0))]),
    # dies outside a boss fight
    (OUT(40, 2), [("obs", S(node="Monster", enemies=[(1, "BLOB", 5, 50)])), ("end", {}, OUT(0, 0)),
                  ("obs", OUT(1, 0))]),
]


def _play(**kw):
    n = len(LINES)
    with_obs = kw.pop("with_obs", True)
    rw = EpisodeRewards("protocol", AUX, LE, n, ascension=10, win_bonus=0.5, **kw)
    cur = [x[0] for x in LINES]
    o = _obs(cur)
    rw.reset(o["aux"], start_entry=np.arange(n), **({"obs": o} if with_obs else {}))
    rs, eps = [], []
    for t in range(3):
        nxt, le, done = list(cur), np.zeros((n, len(LE)), np.int32), np.zeros(n, bool)
        for i, (_, steps) in enumerate(LINES):
            ev = steps[t]
            if ev[0] == "obs":
                nxt[i] = ev[1]
            else:
                s = cur[i]
                done[i], le[i], nxt[i] = True, _le(floor=s["floor"], act0=s["act0"], **ev[1]), ev[2]
        o = _obs(nxt)
        r, fin = rw.step(le[:, 0].astype(np.float32), done, le, o["aux"], start_entry=np.arange(n),
                         **({"obs": o} if with_obs else {}))
        cur = nxt
        rs.append(r.astype(np.float64))
        for e in fin:
            e["t"] = t
        eps += fin
    return np.stack(rs), eps


def test_bonus_off_is_unchanged_and_on_pays_b_x_frac_at_the_terminal_step_only():
    r_old, e_old = _play(with_obs=False)                     # pre-wave-20 call: no schema, no obs
    r0, e0 = _play(schema=_schema())                          # diagnostic on, bonus 0
    assert np.array_equal(r_old, r0)
    keys = {"boss_death_hp_frac", "boss_death_fight_frac", "boss_death_encounter"}
    for a, b in zip(e_old, e0):
        assert {k: v for k, v in b.items() if k not in keys} == a and "boss_hp_bonus_total" not in b
    meas = {(e["slot"], e["t"]): e for e in e0 if "boss_death_hp_frac" in e}
    assert sorted(meas) == [(0, 2), (2, 0)]                   # the death and the step cap; not the win / stall / elite
    f0 = 411 / 535 / 2
    assert meas[(0, 2)]["boss_death_hp_frac"] == pytest.approx(f0)
    assert meas[(0, 2)]["boss_death_fight_frac"] == pytest.approx(411 / 535)
    assert meas[(0, 2)]["boss_death_encounter"] == "AEONGLASS_BOSS"
    assert meas[(2, 0)]["boss_death_hp_frac"] == 0.0         # capped at its first decision
    rb, eb = _play(schema=_schema(), boss_hp_bonus=B)
    expect = np.zeros_like(r0)
    expect[2, 0] = B * f0                                      # slot 0's terminal step only
    np.testing.assert_allclose(rb - r0, expect, atol=1e-6)
    for a, b in zip(e0, eb):
        for k in ("progress", "start_progress", "progress_gain", "win", "act", "floor", *keys):
            assert b.get(k) == a.get(k), k
        paid = B * a.get("boss_death_hp_frac", 0.0)
        assert b["boss_hp_bonus_total"] == pytest.approx(paid) and b["return"] == pytest.approx(a["return"] + paid)
    assert boss_hp_stats(eb) == {"boss_deaths": 2, "boss_death_hp_frac_mean": pytest.approx(f0 / 2),
                                 "boss_deaths_act2": 0, "boss_death_hp_frac_mean_act2": None,
                                 "boss_deaths_act3": 2, "boss_death_hp_frac_mean_act3": pytest.approx(f0 / 2),
                                 "boss_hp_bonus_sum": pytest.approx(B * f0, abs=1e-6)}
    assert boss_hp_stats(e0)["boss_hp_bonus_sum"] == 0.0 and boss_hp_stats([], 1)["boss_deaths_act1"] == 0
    with pytest.raises(ValueError, match="schema"):
        EpisodeRewards("protocol", AUX, LE, 1, boss_hp_bonus=B)
    with pytest.raises(ValueError, match="obs="):
        _play(schema=_schema(), boss_hp_bonus=B, with_obs=False)


def test_episode_log_writes_the_fields(tmp_path):
    from sts2train.episodes import EpisodeLog
    from sts2train.runlog import read_jsonl

    _, eb = _play(schema=_schema(), boss_hp_bonus=B)
    log = EpisodeLog(tmp_path / "episodes.jsonl")
    log.write_update(1, 1, eb)
    log.close()
    rows = read_jsonl(tmp_path / "episodes.jsonl")
    assert all("boss_hp_bonus_total" in r for r in rows)
    d = [r for r in rows if "boss_death_hp_frac" in r]
    assert len(d) == 2 and {r["boss_death_encounter"] for r in d} == {"AEONGLASS_BOSS"}
    assert sorted(r["boss_death_hp_frac"] for r in d) == [0.0, pytest.approx(411 / 535 / 2, abs=1e-6)]


# ------------------------------------------------------------------------------------------------ config / CLI
def test_config_cli_and_resume_defaults():
    import argparse

    from sts2train.cli import _train_parser
    from sts2train.ppo import TrainConfig, config_dict, resolve_config

    assert TrainConfig().boss_hp_bonus == 0.0 and TrainConfig().boss_hp_min_act == 2
    cfg = resolve_config({"boss_hp_bonus": 0.3, "boss_hp_min_act": 3})
    assert config_dict(cfg)["boss_hp_bonus"] == 0.3 and config_dict(cfg)["boss_hp_min_act"] == 3
    old = {k: v for k, v in config_dict(cfg).items() if k not in ("boss_hp_bonus", "boss_hp_min_act")}
    assert resolve_config({}, base=old).boss_hp_bonus == 0.0 and resolve_config({}, base=old).boss_hp_min_act == 2
    assert resolve_config({}, base=config_dict(cfg)).boss_hp_bonus == 0.3     # a resume keeps it
    assert resolve_config({"boss_hp_bonus": 0.0}, base=config_dict(cfg)).boss_hp_bonus == 0.0
    with pytest.raises(ValueError, match="boss_hp_bonus"):
        resolve_config({"boss_hp_bonus": math.inf})
    with pytest.raises(ValueError, match="boss_hp_min_act"):
        resolve_config({"boss_hp_min_act": 4})
    p = argparse.ArgumentParser()
    _train_parser(p.add_subparsers(dest="cmd"))
    a = p.parse_args(["train", "--boss-hp-bonus", "0.3", "--boss-hp-min-act", "3"])
    assert a.boss_hp_bonus == 0.3 and a.boss_hp_min_act == 3
    assert "boss_hp_bonus" not in vars(p.parse_args(["train"]))               # SUPPRESS: the config default applies


# ------------------------------------------------------------------------------------------------ real library
def test_library_schema_has_the_columns_and_the_table_names():
    from sts2vec import PoolSpec, VecEnv

    env = VecEnv(2, threads=1, step_cap=50, pool=PoolSpec(count=2, seed_prefix="TBHP"), seed_text="TBHP")
    try:
        t = BossHpTracker(env.schema, 2)
        assert t.ok, t.missing
        lists = env.schema.vocab.lists
        tab = load_table()["encounters"]
        assert set(tab) <= set(lists["encounters"]), set(tab) - set(lists["encounters"])
        mons = {u["monster"] for v in tab.values() for u in v["units"]} | {m for v in tab.values() for m in v["minions"]}
        assert mons <= set(lists["monsters"]), mons - set(lists["monsters"])
        t.observe(env.reset())                                   # fresh runs: no boss fight
        assert not t.in_fight.any() and t.end(0) is None
    finally:
        env.close()


@pytest.mark.parametrize("overlap", ["off", "double"])
def test_trainer_passes_the_observations(tmp_path, monkeypatch, overlap):
    """train(): every EpisodeRewards gets the schema and the full observations (both overlap lanes), the metrics carry
    the boss-HP keys, episodes.jsonl the bonus field; nothing is paid without a boss-fight death."""
    from sts2train.ppo import resolve_config, train
    from sts2train.runlog import read_jsonl

    seen = {"observe": 0}
    orig = BossHpTracker.observe

    def spy(self, obs):
        seen["observe"] += 1
        assert "enemies" in obs and obs["global_i"].shape[0] == self.n
        return orig(self, obs)

    monkeypatch.setattr(BossHpTracker, "observe", spy)
    cfg = resolve_config({"envs": 8, "threads": 2, "rollout": 20, "minibatch": 80, "epochs": 1, "max_updates": 1,
                          "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2,
                          "log_every": 100, "characters": ("SILENT",), "ascension": 10, "step_cap": 8, "d": 32,
                          "s": 64, "depth": 1, "heads": 2, "ff_mult": 2, "overlap": overlap, "boss_hp_bonus": 0.25,
                          "boss_hp_min_act": 1, "out": str(tmp_path / "run")})
    train(cfg, log=lambda m: None)
    lanes = 2 if overlap == "double" else 1
    assert seen["observe"] == lanes * (1 + 20)                   # reset + every step, per lane
    row = read_jsonl(tmp_path / "run" / "metrics.jsonl")[-1]
    for k in ("boss_deaths", "boss_death_hp_frac_mean", "boss_deaths_act1", "boss_deaths_act3", "boss_hp_bonus_sum"):
        assert k in row, k
    assert row["boss_hp_bonus_sum"] == 0.0
    eps = read_jsonl(tmp_path / "run" / "episodes.jsonl")
    assert eps and all(r.get("boss_hp_bonus_total") == 0.0 for r in eps if r["result"] != "dropped")
