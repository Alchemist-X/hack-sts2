"""Run-state fields of finished episodes (``sts2vec.runstats.RunStats``; episodes.jsonl, eval summaries), 2026-11-05.

Scripted observations of the library's schema: a finished episode's fields come from the dying episode's last
observation (the one decided at before the step that ended it), not from the auto-reset successor's first observation,
which differs in every field; hp / max_hp / killer from ``last_episode``; a boss room is recorded once per entry (hp
at its first decision), the second boss of a double-boss act by ``second_boss_id``; Unknown rooms resolve to what was
seen on the floor; the fights list (won / lost / unfinished, hp after); lane offsets; hook fields are kept. Real env:
the pre-step snapshot matches the values a slot decided at under auto-reset, the current map node is the one the map
marks current, the boss-1 fixture save sits in a Boss room. Trainer: every finished row of pool (``--deck-counts``),
stream + ``--harvest`` + resume and ``--overlap double`` runs carries the fields, special rows none, no duplicates
across the resume."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest
from sts2vec import PoolSpec, VecEnv, load, random_actions, schema_of
from sts2vec.runstats import FIELDS, RunStats

FIXTURE = Path(__file__).resolve().parents[3] / "Sts2Sim.Tests" / "Fixtures" / "run_save_a8_silent_boss1.save"


@pytest.fixture(scope="module")
def schema():
    return schema_of(load())


class Obs:
    """Hand-built observations (N slots) of the library's schema."""

    def __init__(self, schema, n: int):
        self.s, self.n = schema, n
        self.G = schema.cols("global_i")
        self.D, self.P, self.M = schema.cols("deck"), schema.cols("potions"), schema.cols("map_nodes")
        self.MR, self.E, self.L = schema.cols("map_routes"), schema.cols("enemies"), schema.cols("last_episode")
        E = schema.vocab.enums
        self.pt, self.ph = E["MapPointType"], E["RunPhase"]
        self.enc = schema.vocab.encounters
        self.mon = schema.vocab.monsters

    def blank(self) -> dict:
        return {k: np.array(v, copy=True) for k, v in self.s.allocate(self.n).arrays.items()}

    def set(self, o, i, *, floor=3, act=0, hp=50, max_hp=70, gold=99, deck=(0, 1, 0), relics=2, potions=1,
            combat=False, phase="Map", point="Monster", row=2, col=3, boss=0, boss2=0, boss_dist=5, enemies=()):
        G = self.G
        gi = o["global_i"][i]
        gi[:] = 0
        gi[G.total_floor], gi[G.act_index], gi[G.hp], gi[G.max_hp], gi[G.gold] = floor, act, hp, max_hp, gold
        gi[G.deck_n], gi[G.relic_n], gi[G.is_combat] = len(deck), relics, int(combat)
        gi[G.phase] = gi[G.decision_kind] = self.ph.index_of(phase)
        gi[G.map_row], gi[G.map_col], gi[G.boss_id], gi[G.second_boss_id] = row, col, boss, boss2
        o["deck"][i] = 0
        for j, up in enumerate(deck):
            o["deck"][i, j, self.D.card_id] = 2 + j
            o["deck"][i, j, self.D.upgrade] = up
        o["potions"][i] = 0
        o["potions"][i, :potions, self.P.potion_id] = 2
        o["map_nodes"][i] = 0
        o["map_routes"][i] = 0
        if row >= 0:
            o["map_nodes"][i, row, col, self.M.point_type] = self.pt.index_of(point)
            o["map_routes"][i, row, col, self.MR.boss_dist] = boss_dist
        o["enemies"][i] = 0
        for j, m in enumerate(enemies):
            o["enemies"][i, j, self.E.present] = 1
            o["enemies"][i, j, self.E.monster_id] = m
        return o

    def le(self, *, win=0, floor=17, act=0, final_hp=0, final_max_hp=70, end_kind=1, enc=0):
        row = np.zeros((self.n, len(self.L)), dtype=np.int32)
        L = self.L
        row[:, L.win], row[:, L.floor], row[:, L.act], row[:, L.final_hp] = win, floor, act, final_hp
        row[:, L.final_max_hp], row[:, L.end_kind], row[:, L.last_encounter_id] = final_max_hp, end_kind, enc
        return row


def test_fields_come_from_the_dying_episodes_last_observation(schema):
    b = Obs(schema, 2)
    boss, boss2 = 2, 3
    rs = RunStats(schema, 2)
    o = b.blank()
    b.set(o, 1, floor=1, row=0, col=0)
    b.set(o, 0, floor=16, row=15, point="RestSite", phase="RestSite", hp=45)
    rs.observe(o)
    for hp in (40, 30, 12):         # three decisions in the act-1 boss fight: one entry, hp at the first
        b.set(o, 0, floor=17, row=16, point="Boss", combat=True, phase="Combat", hp=hp, boss=boss, boss_dist=0)
        rs.observe(o)
    # the step kills slot 0; auto-reset: its observation is now the successor's first (every field differs)
    succ = b.set(b.blank(), 0, floor=0, row=-1, hp=70, max_hp=75, gold=0, deck=(1,) * 10, relics=1, potions=0)
    b.set(succ, 1, floor=1, row=0, col=0)
    le = b.le(enc=boss)
    fin = [{"slot": 0, "win": False, "fields": {"hook_field": 7}}]
    rs.attach(fin, le)
    f = fin[0]["fields"]
    assert f == {"hp": 0, "max_hp": 70, "deck": 3, "upgraded": 1, "relics": 2, "gold": 99, "potions": 1,
                 "killer": b.enc.name(boss), "room": "Boss", "hook_field": 7,
                 "boss_entry": [{"act": 1, "floor": 17, "hp": 40, "max_hp": 70, "boss": b.enc.name(boss)}]}
    assert set(FIELDS) <= set(f)
    # the successor starts empty and is described by its own observations
    rs.observe(succ)
    fin = [{"slot": 0, "win": False}]
    rs.attach(fin, b.le(end_kind=2, final_hp=70, final_max_hp=75, enc=boss))       # trunc: no killer
    f = fin[0]["fields"]
    assert f["deck"] == 10 and f["upgraded"] == 10 and f["gold"] == 0 and f["potions"] == 0 and f["relics"] == 1
    assert f["killer"] is None and f["boss_entry"] == [] and f["room"] is None and f["hp"] == 70


def test_double_boss_unknown_rooms_and_death_outside_combat(schema):
    b = Obs(schema, 3)
    boss, boss2 = 4, 5
    rs = RunStats(schema, 3)
    o = b.blank()
    b.set(o, 0, floor=47, act=2, row=16, point="Boss", combat=True, phase="Combat", boss=boss, boss2=boss2,
          boss_dist=0)
    b.set(o, 1, floor=5, row=4, point="Unknown", combat=True, phase="Combat")
    b.set(o, 2, floor=6, row=5, point="Unknown", phase="Event")
    rs.observe(o)
    b.set(o, 0, floor=47, act=2, row=16, point="Boss", phase="Rewards", boss=boss, boss2=boss2, boss_dist=0)
    b.set(o, 1, floor=5, row=4, point="Unknown", phase="Rewards")       # after the fight: still a Monster room
    b.set(o, 2, floor=6, row=5, point="Unknown", phase="Prompt")
    rs.observe(o)
    b.set(o, 0, floor=48, act=2, row=17, point="Boss", combat=True, phase="Combat", hp=33, boss=boss, boss2=boss2,
          boss_dist=-1)
    rs.observe(o)
    fin = [{"slot": i, "win": False} for i in range(3)]
    rs.attach(fin, b.le(enc=9))
    f0, f1, f2 = (e["fields"] for e in fin)
    assert [x["boss"] for x in f0["boss_entry"]] == [b.enc.name(boss), b.enc.name(boss2)]
    assert [(x["act"], x["floor"], x["hp"]) for x in f0["boss_entry"]] == [(3, 47, 50), (3, 48, 33)]
    assert f0["room"] == "Boss" and f0["killer"] == b.enc.name(9)
    assert f1["room"] == "Monster" and f1["killer"] is None          # died out of combat (after the fight)
    assert f2["room"] == "Event" and f2["killer"] is None and f2["boss_entry"] == []


def test_fights_won_lost_unfinished_and_lane_offset(schema):
    b = Obs(schema, 2)
    rs = RunStats(schema, 2, fights=True)
    m1, m2 = 2, 3
    o = b.blank()
    seq = [  # (slot 0, slot 1)
        (dict(floor=2, row=1, point="Monster", combat=True, phase="Combat", hp=60, enemies=(m1, m2)),
         dict(floor=2, row=1, point="Elite", combat=True, phase="Combat", hp=55, enemies=(m2,))),
        (dict(floor=2, row=1, point="Monster", combat=True, phase="Combat", hp=52, enemies=(m1,)),
         dict(floor=2, row=1, point="Elite", combat=True, phase="Combat", hp=50, enemies=(m2,))),
        (dict(floor=2, row=1, point="Monster", phase="Rewards", hp=51), dict(floor=2, row=1, point="Elite",
                                                                            combat=True, phase="Combat", hp=40)),
        (dict(floor=3, row=2, point="Elite", combat=True, phase="Combat", hp=51, enemies=(m2,)),
         dict(floor=2, row=1, point="Elite", combat=True, phase="Combat", hp=30)),
    ]
    for a, c in seq:
        b.set(o, 0, **a)
        b.set(o, 1, **c)
        rs.observe(o)
    fin = [{"slot": 10, "win": False}, {"slot": 11, "win": False}]
    le = b.le(enc=7)
    le[1, b.L.end_kind] = 2            # slot 1: truncated in the fight
    le[1, b.L.final_hp] = 30
    rs.attach(fin, le, offset=10)
    f0, f1 = fin[0]["fields"]["fights"], fin[1]["fields"]["fights"]
    n = b.mon.name
    assert [(x["floor"], x["room"], x["enemies"], x["result"], x["hp_before"], x["hp_after"]) for x in f0] == \
        [(2, "Monster", [n(m1), n(m2)], "won", 60, 51), (3, "Elite", [n(m2)], "lost", 51, 0)]
    assert f0[0]["encounter"] is None and f0[1]["encounter"] == b.enc.name(7) and f0[1]["max_hp"] == 70
    assert [(x["result"], x["hp_after"]) for x in f1] == [("unfinished", 30)]
    assert fin[1]["fields"]["killer"] is None and fin[0]["fields"]["killer"] == b.enc.name(7)
    assert rs.fights == [[], []] and rs.bosses == [[], []]          # cleared for the successors


def test_real_env_snapshot_is_the_pre_step_observation(schema):
    """Auto-reset VecEnv with a short step cap: the fields of every ended episode equal the values its slot decided
    at in the step that ended it (saved separately here), and differ from the successor's first observation; the
    current map node read is the node the map marks ``current``; a fight's room is a combat room."""
    n = 16
    G, M = schema.cols("global_i"), schema.cols("map_nodes")
    rs = RunStats(schema, n)
    rng = np.random.default_rng(3)
    ended = differ = 0
    with VecEnv(n, threads=2, pool=PoolSpec(characters=("SILENT", "IRONCLAD"), count=8, seed_prefix="RSTAT"),
                seed_text="RSTAT", step_cap=60) as env:
        obs = env.reset()
        for _ in range(240):
            gi = obs["global_i"]
            rows_n = obs["map_nodes"].shape[1]
            for i in np.flatnonzero((gi[:, G.map_row] >= 0) & (gi[:, G.map_row] < rows_n)):
                assert obs["map_nodes"][i, gi[i, G.map_row], gi[i, G.map_col], M.current] == 1
            rs.observe(obs)
            pre = {k: gi[:, G[k]].copy() for k in ("gold", "deck_n", "relic_n", "total_floor", "is_combat")}
            for i in np.flatnonzero(gi[:, G.is_combat] > 0):
                assert rs._room(rs.last[i]) in ("Monster", "Elite", "Boss"), rs._room(rs.last[i])
            obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
            done = (term | trunc).astype(bool)
            for i in np.flatnonzero(done):
                le = info["last_episode"][i]
                f = rs.end(i, le)
                assert (f["gold"], f["deck"], f["relics"]) == (pre["gold"][i], pre["deck_n"][i], pre["relic_n"][i])
                assert f["hp"] == le[schema.cols("last_episode").final_hp]
                ended += 1
                g = obs["global_i"][i]
                differ += (g[G.gold], g[G.deck_n], g[G.total_floor]) != (f["gold"], f["deck"], pre["total_floor"][i])
            rs.reset(done)
    assert ended >= n and differ > 0


def test_boss_room_of_the_fixture_save(schema):
    assert FIXTURE.exists()
    rs = RunStats(schema, 1)
    with VecEnv(1, threads=1, pool=PoolSpec.saves(FIXTURE)) as env:
        obs = env.reset()
        rs.observe(obs)
        f = rs.end(0, env.buffers["last_episode"][0] * 0)
    assert f["room"] == "Boss" and f["boss_entry"] == [] and f["deck"] > 10 and f["relics"] >= 1


def _check_rows(rows):
    fin = [r for r in rows if r["result"] not in ("dropped", "start_failed")]
    assert fin
    for r in fin:
        assert set(FIELDS) <= set(r), r
        assert isinstance(r["deck"], int) and r["deck"] > 0 and isinstance(r["boss_entry"], list)
        assert r["upgraded"] <= r["deck"] and r["potions"] >= 0 and r["max_hp"] > 0
        if r["result"] == "death":
            assert r["hp"] == 0
        else:
            assert r["killer"] is None
        if r["killer"] is not None:
            assert r["room"] in ("Monster", "Elite", "Boss")
    for r in rows:
        if r["result"] in ("dropped", "start_failed"):
            assert not set(FIELDS) & set(r)
    return fin


TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}


def test_trainer_rows_pool_deck_counts_and_overlap(tmp_path):
    from sts2train.ppo import resolve_config, train
    from sts2train.runlog import read_jsonl

    base = {"envs": 8, "threads": 2, "pool": 8, "rollout": 24, "minibatch": 64, "epochs": 1, "max_updates": 2,
            "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2,
            "log_every": 100, "characters": ("SILENT",), "ascension": 10, "step_cap": 20, **TINY}
    for name, kw in (("pool", {"deck_counts": True}), ("double", {"overlap": "double"})):
        train(resolve_config({**base, "out": str(tmp_path / name), **kw}), log=lambda m: None)
        _check_rows(read_jsonl(tmp_path / name / "episodes.jsonl"))
    train(resolve_config({**base, "out": str(tmp_path / "off"), "episodes_log": False}), log=lambda m: None)
    assert not (tmp_path / "off" / "episodes.jsonl").exists()


def test_trainer_rows_stream_harvest_resume(tmp_path):
    from sts2train.ppo import plan_run, train
    from sts2train.runlog import read_jsonl

    run = tmp_path / "run"
    base = {"envs": 8, "threads": 2, "rollout": 32, "minibatch": 64, "epochs": 1, "eval_every": 0, "ckpt_every": 1,
            "ckpt_every_min": 0, "keep_ckpts": 10, "log_every": 100, "max_hours": 0.5, "torch_threads": 1,
            "characters": ("SILENT",), "ascension": 10, "start_mode": "stream", "step_cap": 40,
            "harvest": "any:16", "harvest_prob": 1.0, "harvest_start_share": 0.5, **TINY}

    def go(updates, resume=None):
        given = {**base, "out": str(run), "max_updates": updates} if resume is None else \
            {"out": str(run), "max_updates": updates}
        cfg, ck, ckp = plan_run(given, resume, log=lambda m: None)
        return train(cfg, log=lambda m: None, resume=ck, resume_path=ckp, given=given)

    go(2)
    first = read_jsonl(run / "episodes.jsonl")
    go(4, resume="auto")
    rows = read_jsonl(run / "episodes.jsonl")
    fin = _check_rows(rows)
    assert rows[:len(first)] == first                                  # session 1's rows kept as written
    assert {r["session"] for r in fin} == {1, 2}
    assert any(r["result"] == "dropped" and r["session"] == 2 for r in rows)
    keys = [(r["update"], r["slot"], r["session"], r["result"], r["steps"], r["seed_text"]) for r in rows]
    assert len(keys) == len(set(keys))
    assert "stream" in {r.get("start_kind") for r in fin}
