"""Seed stream (--start-mode stream), isolation guard, blind-set gate, RNG decoupling, episodes.jsonl / sessions[]
records (sts2train.seedstream / isolation / episodes; ppo.resolve_seeds / seed_rngs / init_rng / evals_history)."""

from __future__ import annotations

import json
from collections import Counter

import numpy as np
import pytest
import torch
from sts2vec import STATUS, VecEnv, random_actions
from sts2vec import protocol as PG

from sts2train.episodes import EPISODE_HOOKS
from sts2train.features import ModelSpec
from sts2train.isolation import IsolationError, IsolationGuard, check_legacy_pool, legacy_pool_starts
from sts2train.model import Policy
from sts2train.objective import EpisodeRewards
from sts2train.ppo import (evals_history, init_rng, plan_run, purpose_seed, resolve_config, seed_rngs, train,
                           weights_hash)
from sts2train.runlog import read_jsonl
from sts2train.seedstream import SeedStream, StreamEnv, check_prefix, default_prefix

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 8, "threads": 2, "rollout": 32, "minibatch": 64, "epochs": 1, "eval_every": 0, "ckpt_every": 1,
       "ckpt_every_min": 0, "keep_ckpts": 10, "log_every": 100, "max_hours": 0.5, "torch_threads": 1,
       "characters": ("SILENT",), "ascension": 10, **TINY}


def _quiet(m):
    pass


@pytest.fixture(scope="module")
def guard():
    return IsolationGuard.from_seedsets()


# ------------------------------------------------------------------------------------------------ config rules


def test_prefix_and_config_rules():
    assert default_prefix(0) == "SP0" and default_prefix(17) == "SP17"
    for bad in ("TRAIN0", "TRAINX", "old1", "sp0", "SP-1", "", "A" * 25):
        with pytest.raises(ValueError):
            check_prefix(bad)
    cfg = resolve_config({"start_mode": "stream", "seed": 3})
    assert (cfg.init_seed, cfg.sample_seed, cfg.env_seed, cfg.start_seed, cfg.rng_scheme) == (3, 3, 3, 3, "decoupled")
    for bad in ({"start_saves": "/tmp/x"}, {"baseline_steps": 10}, {"start_seed": 1}, {"seed_stream_prefix": "TRAIN1"},
                {"stream_lookahead": -1}):
        with pytest.raises(ValueError):
            resolve_config({"start_mode": "stream", **bad})
    with pytest.raises(ValueError, match="stream only"):
        resolve_config({"seed_stream_prefix": "SPX"})
    # pool mode without explicit init / sample seeds = the legacy RNG scheme (bit for bit the old trainer)
    legacy = resolve_config({"seed": 5})
    assert legacy.rng_scheme == "legacy" and (legacy.env_seed, legacy.start_seed) == (5, 5)
    assert resolve_config({"seed": 5, "sample_seed": 1}).rng_scheme == "decoupled"
    # a resume keeps the checkpoint's seeds; an old checkpoint (no seed fields) ran legacy with init = its seed
    old = {"seed": 0, "envs": 16}
    r = resolve_config({"seed": 7}, old)
    assert (r.rng_scheme, r.init_seed, r.sample_seed, r.env_seed, r.start_seed) == ("legacy", 0, 0, 7, 7)
    with pytest.raises(ValueError, match="init-seed"):
        resolve_config({"init_seed": 1}, old)
    new = {**old, "init_seed": 2, "sample_seed": 2, "env_seed": 2, "start_seed": 2, "rng_scheme": "decoupled"}
    r = resolve_config({"seed": 9, "sample_seed": 4}, new)
    assert (r.init_seed, r.sample_seed, r.env_seed, r.rng_scheme) == (2, 4, 2, "decoupled")
    assert resolve_config({}, {**new, "allow_blind": True, "blind_purpose": "x"}).allow_blind is False


# ------------------------------------------------------------------------------------------------ isolation


def test_guard_loads_every_holdout_set(guard):
    ids = {s["id"] for s in guard.sets}
    assert {"silent-a10-v1", "silent-a10-dev-v1", "silent-a10-test-v1", "silent-a10-ab-v1", "silent-a10-blind-v1",
            "std-a10-v1"} <= ids
    roles = {s["id"]: s["role"] for s in guard.sets}
    assert roles["silent-a10-test-v1"] == "dev2" and roles["silent-a10-blind-v1"] == "blind"
    assert guard.collision("SILENT", "TRAIN0-SILENT-4100", 10).startswith("SILENT TRAIN0-SILENT-4100 A10 is a game")
    hit = guard.collision("IRONCLAD", "TRAIN0-SILENT-4100", 0)          # another character / ascension: same run seed
    assert hit is not None and "run seed" in hit and "silent-a10-ab-v1" in hit
    assert guard.collision("SILENT", "TRAIN0-SILENT-4095", 10) is None
    assert guard.collision("SILENT", "SP0-SILENT-0", 10) is None


def test_guard_refuses_a_box_without_seed_sets(monkeypatch):
    monkeypatch.setattr(PG, "seedset_dirs", lambda: [])
    with pytest.raises(IsolationError, match="no frozen seed set"):
        IsolationGuard.from_seedsets()


def test_legacy_pool_refused_when_it_swallows_ab512(guard, tmp_path):
    """`--seed 0 --pool 4608` would put silent-a10-ab-v1 (TRAIN0-SILENT-4096..4607) into the training pool: refused
    before any env or run directory exists; --pool 4096 (the archived runs' pool) passes; A0 collides by run seed."""
    big = resolve_config({"seed": 0, "pool": 4608, "characters": ("SILENT",), "ascension": 10})
    assert legacy_pool_starts(big)[-1] == ("SILENT", "TRAIN0-SILENT-4607", 10)
    with pytest.raises(IsolationError, match=r"512 of the 4608 .*silent-a10-ab-v1"):
        check_legacy_pool(big, guard)
    with pytest.raises(IsolationError, match="silent-a10-ab-v1"):
        check_legacy_pool(resolve_config({"seed": 0, "pool": 4608, "characters": ("SILENT",)}), guard)   # A0
    rec = check_legacy_pool(resolve_config({"seed": 0, "pool": 4096, "characters": ("SILENT",), "ascension": 10}),
                            guard)
    assert rec["start_seeds_checked"] == 4096 and rec["collisions"] == 0 and rec["refused"] is False
    assert check_legacy_pool(resolve_config({"env_seed": 1, "pool": 4608, "characters": ("SILENT",)}), guard)
    run = tmp_path / "run"
    with pytest.raises(IsolationError):
        train(resolve_config({**RUN, "out": str(run), "pool": 4608, "max_updates": 1}), log=_quiet)
    assert not run.exists()
    from sts2train.cli import main

    assert main(["train", "--out", str(run), "--pool", "4608", "--characters", "SILENT", "--ascension", "10",
                 "--envs", "4", "--max-updates", "1"]) == 2
    assert not run.exists()


def test_stream_skips_seeds_that_collide_with_a_holdout(guard):
    """A stream index whose seed has a holdout game's run seed (simulated: no SP text collides for real) or triple is
    skipped and counted below `next`; next == started + skipped."""
    g = IsolationGuard(sets=list(guard.sets), by_seed=dict(guard.by_seed), triples=dict(guard.triples))
    g.by_seed[PG.run_seed("SPT-SILENT-3")] = ("fake-holdout", "XXXX")
    g.triples[("SILENT", "SPT-SILENT-7", 10)] = "fake-holdout-2"
    s = SeedStream("SPT", ("SILENT",), 10, guard=g)
    got = [s.generate() for _ in range(8)]
    assert [n for n, _, _ in got] == [0, 1, 2, 4, 5, 6, 8, 9]
    assert {d["n"] for d in s.session_skips} == {3, 7}
    for n, _, _ in got:
        s.consume(n)
    assert (s.state.next, s.state.started, s.state.skipped) == (10, 8, 2)
    with pytest.raises(RuntimeError, match="reused"):
        s.consume(4)
    # resumed from that state: issuance restarts at next, skips are not counted twice
    r = SeedStream("SPT", ("SILENT",), 10, guard=g, state=s.state_dict())
    assert r.generate()[0] == 10 and r.state.skipped == 2


# ------------------------------------------------------------------------------------------------ StreamEnv


def test_stream_env_starts_every_episode_from_the_stream():
    """Random actions on 8 slots: every episode starts from a new stream seed (distinct indices, consumed in order),
    the env never starts an episode by itself (slot i's k-th episode id = (k - 1) * N + i + 1), the returned signals
    are the step's (restore overwrites them in the buffers), last_episode / info describe the ended / new episodes,
    and the live pool stays at the lookahead."""
    N, LA = 8, 4
    env = VecEnv(N, threads=2, auto_reset=False, pool=None, seed_text="T", step_cap=3000)
    s = StreamEnv(env, SeedStream("SPU", ("SILENT",), 10), lookahead=LA, prefetch=True)
    try:
        assert s.prime() == N + LA and env.pool.warm() == N + LA
        obs = s.reset()
        assert s.info()["start_entry"].tolist() == list(range(N)) and s.slot_n.tolist() == list(range(N))
        le_f = {f: i for i, f in enumerate(env.schema["last_episode"].fields)}
        rewards = EpisodeRewards("protocol", ModelSpec.from_schema(env.schema).aux_fields,
                                 env.schema["last_episode"].fields, N, 10)
        rewards.reset(obs["aux"], start_entry=s.info()["start_entry"])
        rng = np.random.default_rng(1)
        steps = np.zeros(N, dtype=np.int64)
        seen_n: list[int] = list(range(N))
        done_n = finished_n = 0
        for k in range(700):
            obs, rew, term, trunc, status, info = s.step(random_actions(rng, obs["cand_n"]))
            done = (term | trunc).astype(bool)
            steps += 1
            assert (np.asarray(env.status)[done] & STATUS["RESET"]).all()        # the buffers show the restart
            assert not (status[done] & STATUS["RESET"]).any()                    # the returned signals: the step's
            r, fin = rewards.step(rew, done, info["last_episode"], obs["aux"], start_entry=info["start_entry"])
            s.annotate(fin)
            for e in fin:
                i = e["slot"]
                assert e["steps"] == steps[i] == info["last_episode"][i, le_f["steps"]]
                assert e["seed_n"] is not None and e["seed_text"] == f"SPU-SILENT-{e['seed_n']}"
            for i in np.flatnonzero(done):
                assert info["step_index"][i] == 0 and info["start_entry"][i] == s.slot_entry[i]
                seen_n.append(int(s.slot_n[i]))
            steps[done] = 0
            done_n += int(done.sum())
            finished_n += len(fin)
            assert (obs["cand_n"] > 0).all() and len(env.pool) == LA
            if k % 50 == 0:
                s.prefetch()
        assert done_n == finished_n > 3 * N
        assert len(seen_n) == len(set(seen_n)) == N + done_n == s.stream.state.started
        assert sorted(seen_n) == list(range(s.stream.state.next)) and s.stream.state.skipped == 0
        ids = s.info()["episode_id"]
        assert (ids == (s.slot_attempts - 1) * N + np.arange(N) + 1).all() and (s.slot_attempts == s.slot_starts).all()
        assert sum(s.slot_starts) == s.stream.state.started
        with pytest.raises(ValueError):
            s.reset(mask=np.ones(N))
    finally:
        s.close()


class _FakePool:
    def __init__(self):
        self.live, self.next_id, self.texts = set(), 0, {}

    def add_fresh(self, ch, *, seed, ascension=0, act1="random"):
        eid, self.next_id = self.next_id, self.next_id + 1
        self.live.add(eid)
        self.texts[eid] = seed
        return eid

    def release(self, ids):
        for i in ids:
            self.live.remove(int(i))

    def warm(self):
        return 0

    def __len__(self):
        return len(self.live)


class _FakeEnv:
    """Just enough VecEnv for StreamEnv's start logic: seeds in `bad` fail their start (SIM_ERROR, no candidates)."""

    num_envs, config, supports_restore_many, supports_pool_release = 3, {"auto_reset": False}, True, True

    def __init__(self, bad):
        self.pool, self.bad = _FakePool(), set(bad)
        self.status = np.zeros(3, dtype=np.int32)
        self.buffers = {"cand_n": np.zeros(3, dtype=np.int32)}

    def restore_many(self, slots, entries):
        for i, e in zip(slots, entries):
            fail = self.pool.texts[int(e)] in self.bad
            self.status[i] = STATUS["RESET"] | (STATUS["SIM_ERROR"] if fail else 0)
            self.buffers["cand_n"][i] = 0 if fail else 5

    def last_error(self, i):
        return "boom: NewRun failed\nstack"


def test_failed_start_consumes_its_seed_and_the_slot_takes_the_next():
    env = _FakeEnv(bad={"SPF-SILENT-1", "SPF-SILENT-4"})
    s = StreamEnv(env, SeedStream("SPF", ("SILENT",), 10), lookahead=2, prefetch=False)
    s._start(np.arange(3))                  # seed 1 fails on slot 1, which takes seed 3; lookahead 4, 5
    assert s.slot_n.tolist() == [0, 3, 2] and [f[0] for f in s.fifo] == [4, 5]
    s._start(np.array([1]))                 # seed 4 fails, slot 1 takes 5
    assert s.slot_n.tolist() == [0, 5, 2]
    st = s.stream.state
    assert (st.next, st.started, st.failed, st.skipped) == (6, 6, 2, 0)
    rows = s.pop_failed()
    assert [r["n"] for r in rows] == [1, 4] and rows[0]["reason"] == "boom: NewRun failed" and not s.pop_failed()
    assert s.pop_update() == {"starts": 4, "start_failed": 2, "t_restart": 0.0, "t_prefetch": 0.0,
                              "t_prefetch_wait": 0.0}
    assert len(env.pool) == 2                               # only the lookahead stays live


# ------------------------------------------------------------------------------------------------ the trainer


def _stream_run(tmp_path, name, updates, resume=None, **kw):
    given = {**RUN, "start_mode": "stream", "out": str(tmp_path / name), "max_updates": updates, **kw}
    if resume is not None:
        given = {"out": str(tmp_path / name), "max_updates": updates, **kw}
    cfg, ck, ck_path = plan_run(given, resume, log=_quiet)
    return train(cfg, log=_quiet, resume=ck, resume_path=ck_path, given=given)


def test_stream_run_never_reuses_a_seed_across_a_resume(tmp_path):
    """Session 1 (3 updates) then `--resume auto` (3 more): every consumed stream index is exactly one of a finished
    episode, a dropped in-flight episode (logged by the resumed session) or an episode in flight at the end; no index
    twice; the counter is persisted and continues; the resumed session's seeds are all new."""
    s1 = _stream_run(tmp_path, "run", 3)
    run = tmp_path / "run"
    ck3 = torch.load(run / "checkpoints" / "ckpt-0000003.pt", weights_only=False)
    n1 = ck3["seed_stream"]["next"]
    assert ck3["seed_stream"]["started"] == n1 and ck3["seed_stream"]["prefix"] == "SP0"
    assert len(set(ck3["inflight"]["n"])) == 8 and max(ck3["inflight"]["n"]) < n1
    rows1 = read_jsonl(run / "episodes.jsonl")
    assert rows1 and all(r["session"] == 1 and r["result"] in ("death", "win", "trunc", "stall", "error")
                         for r in rows1)
    assert s1["seed_stream"]["seed_stream_next"] == n1 and s1["inflight_at_end"] == 8

    s2 = _stream_run(tmp_path, "run", 6, resume="auto")
    rows = read_jsonl(run / "episodes.jsonl")
    ck6 = torch.load(run / "checkpoints" / "ckpt-0000006.pt", weights_only=False)
    n2 = ck6["seed_stream"]["next"]
    dropped = [r for r in rows if r["result"] == "dropped"]
    assert {(r["update"], r["session"]) for r in dropped} == {(3, 2)}
    assert sorted(r["n"] for r in dropped) == sorted(ck3["inflight"]["n"])
    fin2 = [r for r in rows if r["session"] == 2 and r["result"] != "dropped"]
    assert fin2 and all(r["n"] >= n1 for r in fin2)                           # session 2 only started new seeds
    consumed = [r["n"] for r in rows if r["result"] != "dropped"] + [r["n"] for r in dropped] + ck6["inflight"]["n"]
    assert len(consumed) == len(set(consumed)) and sorted(consumed) == list(range(n2))
    assert n2 >= n1 + 8 and ck6["seed_stream"]["started"] == n2
    m = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in m] == [1, 2, 3, 4, 5, 6] and [r["session"] for r in m] == [1, 1, 1, 2, 2, 2]
    assert [r["session_update"] for r in m] == [1, 2, 3, 1, 2, 3]
    assert [r["dropped_total"] for r in m] == [0, 0, 0, 8, 8, 8] and m[3]["starts_update"] >= 8
    assert m[2]["seed_stream_next"] == n1 and m[-1]["seed_stream_next"] == n2
    assert all(a["seed_stream_next"] <= b["seed_stream_next"] for a, b in zip(m, m[1:]))
    assert all(r["seeds_used_total"] == r["starts_total"] == r["seed_stream_next"] for r in m)
    for k in ("ent_coef", "vf_coef", "clip", "t_stream_s", "t_prefetch_s", "stream_lookahead"):
        assert k in m[-1], k
    conf = json.loads((run / "config.json").read_text())
    sess = conf["sessions"]
    assert [x["seed_stream_next"] for x in sess] == [0, n1] and sess[1]["dropped_at_start"] == 8
    assert sess[1]["rng_restored"] == {"numpy": "ok", "torch": "ok", "cuda": "absent"} and sess[1]["adam_step"] > 0
    assert sess[1]["config_diff"] == {"max_updates": [3, 6]} and sess[1]["given"]["max_updates"] == 6
    assert sess[0]["init_weights_hash"] and sess[1]["init_weights_hash"] is None
    assert conf["isolation"]["mode"] == "stream" and conf["isolation"]["collisions_skipped"] == 0
    assert conf["seeds"]["seed_stream_prefix"] == "SP0" and s2["dropped_total"] == 8
    # a third session resumed from the same checkpoint again does not duplicate the dropped rows
    _stream_run(tmp_path, "run", 6, resume=str(run / "checkpoints" / "ckpt-0000003.pt"))
    again = read_jsonl(run / "episodes.jsonl")
    assert Counter((r["update"], r["result"]) for r in again)[(3, "dropped")] == 8


def test_two_forks_of_one_checkpoint_are_identical(tmp_path):
    """Resume determinism in stream mode: two forks of one checkpoint into new directories with the same flags play
    the same seeds and compute the same updates; a fork with a new --sample-seed re-seeds (and differs)."""
    _stream_run(tmp_path, "base", 2)
    ck = str(tmp_path / "base" / "checkpoints" / "ckpt-0000002.pt")
    a = _stream_run(tmp_path, "a", 4, resume=ck)
    b = _stream_run(tmp_path, "b", 4, resume=ck)
    c = _stream_run(tmp_path, "c", 4, resume=ck, sample_seed=9)
    skip = {"time", "wall_s", "session_s", "env_steps_per_s", "env_steps_per_s_session"}
    rows = {k: [{x: v for x, v in r.items() if x not in skip and not x.startswith("t_")}
                for r in read_jsonl(tmp_path / k / "metrics.jsonl")] for k in "abc"}
    assert rows["a"] == rows["b"] and rows["a"] != rows["c"]
    assert a["weights_hash"] == b["weights_hash"] != c["weights_hash"]
    ep = {k: [(r["n"], r["result"], r["steps"]) for r in read_jsonl(tmp_path / k / "episodes.jsonl")] for k in "ab"}
    assert ep["a"] == ep["b"]
    sc = json.loads((tmp_path / "c" / "config.json").read_text())["sessions"][-1]
    assert all(v.startswith("reseeded (sample_seed 0 -> 9)") for v in sc["rng_restored"].values())
    # the fork's best_eval comes from the source run's evals.jsonl (none here)
    assert sc["best_eval_source"]["source"] == "source run directory"


def test_rng_decoupling_init_vs_sample(tmp_path):
    """--init-seed changes only the initial weights, --sample-seed only the sampling / shuffling streams; with both
    equal two runs are identical; the legacy scheme is the old coupling (torch.manual_seed(seed) then the init)."""
    from sts2vec import load, schema_of

    spec = ModelSpec.from_schema(schema_of(load()))

    def setup(**kw):
        cfg = resolve_config({"start_mode": "stream", **TINY, **kw})
        rng = seed_rngs(cfg)
        with init_rng(cfg):
            m = Policy(spec, **TINY)
        return weights_hash(m), torch.rand(4).tolist(), rng.random(3).tolist()

    a, b, c = setup(init_seed=1, sample_seed=1), setup(init_seed=1, sample_seed=2), setup(init_seed=2, sample_seed=1)
    assert a == setup(init_seed=1, sample_seed=1)
    assert a[0] == b[0] != c[0]                       # init weights: init_seed only
    assert a[1:] == c[1:] and a[1] != b[1] and a[2] != b[2]   # sampling / shuffle streams: sample_seed only
    legacy = resolve_config({"seed": 4, **TINY})
    rng = seed_rngs(legacy)
    with init_rng(legacy):
        m = Policy(spec, **TINY)
    torch.manual_seed(4)
    ref = Policy(spec, **TINY)
    assert legacy.rng_scheme == "legacy" and weights_hash(m) == weights_hash(ref) and rng.random() == \
        np.random.default_rng(4).random()
    assert purpose_seed(1, "init") != purpose_seed(1, "sample")
    # end to end: one update each; the init hash is recorded, sampling changes the rollout
    runs = {}
    for name, kw in (("a", {"init_seed": 1, "sample_seed": 1}), ("a2", {"init_seed": 1, "sample_seed": 1}),
                     ("b", {"init_seed": 1, "sample_seed": 2}), ("c", {"init_seed": 2, "sample_seed": 1})):
        _stream_run(tmp_path, name, 1, **kw)
        conf = json.loads((tmp_path / name / "config.json").read_text())
        row = read_jsonl(tmp_path / name / "metrics.jsonl")[0]
        runs[name] = (conf["sessions"][0]["init_weights_hash"], row["entropy"], row["policy_loss"], row["value_loss"])
    assert runs["a"] == runs["a2"]
    assert runs["a"][0] == runs["b"][0] != runs["c"][0] and runs["a"][1:] != runs["b"][1:]


def test_episode_hooks_and_pool_mode_records(tmp_path):
    """Pool mode: episodes.jsonl rows carry the pool entry's seed text and hook fields; metrics count starts and the
    distinct entries started; the checkpoint keeps the per-entry counts and the in-flight episodes, which the resumed
    session logs as dropped."""
    calls = []
    EPISODE_HOOKS.register("t_hook", lambda e: calls.append(e["slot"]) or {"hook_slot": e["slot"], "hook_t": e["t"]})
    try:
        given = {**RUN, "out": str(tmp_path / "p"), "pool": 6, "max_updates": 3, "seed": 1}
        cfg, _, _ = plan_run(given, None, log=_quiet)
        assert cfg.start_mode == "pool" and cfg.rng_scheme == "legacy"
        s = train(cfg, log=_quiet, given=given, episode_hooks=[lambda e: {"extra_hook": 1}])
    finally:
        EPISODE_HOOKS.unregister("t_hook")
    rows = read_jsonl(tmp_path / "p" / "episodes.jsonl")
    assert rows and len(calls) == len(rows) == s["episodes"]
    assert all(r["seed_text"].startswith("TRAIN1-SILENT-") and r["n"] is None and r["hook_slot"] == r["slot"]
               and r["extra_hook"] == 1 and 0 <= r["hook_t"] < RUN["rollout"] for r in rows)
    m = read_jsonl(tmp_path / "p" / "metrics.jsonl")
    assert m[0]["starts_update"] >= 8 and sum(r["starts_update"] for r in m) == m[-1]["starts_total"]
    assert m[-1]["seeds_used_total"] <= 6 and m[-1]["seed_stream_next"] is None
    ck = torch.load(tmp_path / "p" / "checkpoints" / "ckpt-0000003.pt", weights_only=False)
    assert int(ck["start_counts"].sum()) == m[-1]["starts_total"] and len(ck["inflight"]["slot"]) == 8
    conf = json.loads((tmp_path / "p" / "config.json").read_text())
    assert conf["isolation"]["mode"] == "pool" and conf["isolation"]["start_seeds_checked"] == 6
    assert conf["seeds"]["pool_family"] == "TRAIN1" and conf["sessions"][0]["seeds"]["start_seed"] == 1
    cfg2, ck2, p2 = plan_run({"out": str(tmp_path / "p"), "max_updates": 4}, "auto", log=_quiet)
    train(cfg2, log=_quiet, resume=ck2, resume_path=p2)
    rows = read_jsonl(tmp_path / "p" / "episodes.jsonl")
    dropped = [r for r in rows if r["result"] == "dropped"]
    assert len(dropped) == 8 and all(r["seed_text"].startswith("TRAIN1-SILENT-") for r in dropped)
    assert read_jsonl(tmp_path / "p" / "metrics.jsonl")[-1]["dropped_total"] == 8


def test_best_eval_recomputed_from_the_source_runs_evals(tmp_path):
    """A checkpoint is written before its update's eval, so its best_eval misses that eval (Q9): a resume recomputes
    best / last from the run's evals.jsonl rows up to the checkpoint's update, on this session's eval seed set."""
    run = tmp_path / "src"
    (run / "checkpoints").mkdir(parents=True)
    rows = [{"update": u, "seedset": "s", "progress_mean": p, "games": 2, "replayable": 2, "results": {"loss": 2}}
            for u, p in ((1, 0.1), (2, 0.3), (3, 0.2), (4, 0.4), (5, 0.9))]
    rows.insert(3, {"update": 3, "seedset": "other", "progress_mean": 0.99, "games": 2, "replayable": 2})
    (run / "evals.jsonl").write_text("".join(json.dumps(r) + "\n" for r in rows))
    stale = rows[1]
    best, last, rec = evals_history(run / "checkpoints" / "ckpt-0000004.pt", tmp_path / "fork", 4, "s", stale, rows[2])
    assert best["update"] == 4 and best["progress_mean"] == 0.4 and last["update"] == 4
    assert rec["source"] == "source run directory" and rec["checkpoint_best_update"] == 2 and rec["rows"] == 5
    best, _, rec = evals_history(run / "checkpoints" / "ckpt-0000004.pt", run, 4, "nope", stale, None)
    assert best is None and rec["source"] == "same run directory"
    best, last, rec = evals_history(tmp_path / "x" / "ckpt.pt", tmp_path, 4, "s", stale, None)
    assert best is stale and rec["source"] == "checkpoint"


# ------------------------------------------------------------------------------------------------ blind gate


def test_blind_set_refused_everywhere_before_any_game(tmp_path, capsys):
    """`sts2train eval`, report-card and the trainer's periodic eval refuse silent-a10-blind-v1 without
    --allow-blind --purpose, before any game is played (nothing evaluates the blind set in this wave)."""
    from sts2train.cli import main
    from sts2train.ppo import TrainConfig, save_checkpoint
    from sts2train.reportcard import report_card
    from sts2vec import load, schema_of

    out = tmp_path / "ev"
    assert main(["eval", "--seedset", "silent-a10-blind-v1", "--checkpoint", str(tmp_path / "none.pt"),
                 "--out", str(out)]) == 2
    assert "blind" in capsys.readouterr().out and not out.exists()
    assert main(["eval", "--seedset", "silent-a10-blind-v1", "--checkpoint", str(tmp_path / "none.pt"),
                 "--out", str(out), "--allow-blind"]) == 2          # --purpose missing
    spec = ModelSpec.from_schema(schema_of(load()))
    ck = tmp_path / "ck.pt"
    torch.manual_seed(0)
    save_checkpoint(ck, Policy(spec, **TINY), spec, TrainConfig(**TINY), {"updates": 0})
    with pytest.raises(PG.BlindSetError):
        report_card(ck, tmp_path / "card", seedsets=["silent-a10-blind-v1"], baselines=[], log=_quiet)
    assert not (tmp_path / "card" / "evals").exists()
    assert main(["report-card", "--checkpoint", str(ck), "--out", str(tmp_path / "card2"), "--seedsets",
                 "silent-a10-blind-v1", "--baselines", "none"]) == 2
    run = tmp_path / "run"
    with pytest.raises(PG.BlindSetError):
        train(resolve_config({**RUN, "out": str(run), "eval_every": 1, "eval_seedset": "silent-a10-blind-v1",
                              "max_updates": 1}), log=_quiet)
    assert not run.exists()
    assert main(["train", "--out", str(run), "--eval-every", "1", "--eval-seedset", "silent-a10-blind-v1",
                 "--envs", "4", "--max-updates", "1"]) == 2
    assert not run.exists()
    from sts2vec.evaluate import evaluate as vec_evaluate

    with pytest.raises(PG.BlindSetError):
        vec_evaluate("silent-a10-blind-v1", "random", tmp_path / "vec")
    assert not (tmp_path / "vec").exists()


def test_blind_allowed_path_logs_each_access(tmp_path, monkeypatch, capsys):
    """With --allow-blind --purpose an evaluation on blind games runs and logs one line per access, in the eval
    directory (`sts2train eval`) and in the run directory (trainer). Exercised on a synthetic blind set
    (STS2VEC_SEEDSETS), never on the frozen one."""
    from sts2train.cli import main
    from sts2train.ppo import TrainConfig, save_checkpoint
    from sts2vec import load, schema_of

    sets = tmp_path / "sets"
    sets.mkdir()
    games = [{"seed": "Z9TESTBL01", "character": "SILENT"}, {"seed": "Z9TESTBL02", "character": "SILENT"}]
    (sets / "syn-blind.json").write_text(json.dumps({"name": "syn-blind", "id": "syn-blind-v1", "role": "blind",
                                                     "ascension": 10, "content": "ported_only", "games": games}))
    monkeypatch.setenv("STS2VEC_SEEDSETS", str(sets))
    spec = ModelSpec.from_schema(schema_of(load()))
    ck = tmp_path / "ck.pt"
    torch.manual_seed(0)
    wh = save_checkpoint(ck, Policy(spec, **TINY), spec, TrainConfig(**TINY), {"updates": 0})
    out = tmp_path / "ev"
    assert main(["eval", "--seedset", "syn-blind-v1", "--checkpoint", str(ck), "--out", str(out), "--threads", "2",
                 "--allow-blind", "--purpose", "test of the access log"]) == 0
    capsys.readouterr()
    rows = [json.loads(x) for x in (out / "blind-access.jsonl").read_text().splitlines()]
    assert len(rows) == 1 and rows[0]["purpose"] == "test of the access log"
    assert rows[0]["weights_hash"] == f"weights:{wh}" and rows[0]["seedset"]["id"] == "syn-blind-v1"
    run = tmp_path / "run"
    s = train(resolve_config({**RUN, "start_mode": "stream", "out": str(run), "eval_every": 1,
                              "eval_seedset": "syn-blind-v1", "eval_threads": 2, "max_updates": 2,
                              "allow_blind": True, "blind_purpose": "trainer access log"}), log=_quiet)
    assert s["updates"] == 2
    rows = [json.loads(x) for x in (run / "blind-access.jsonl").read_text().splitlines()]
    assert [r["purpose"] for r in rows] == ["trainer access log"] * 2           # one per periodic eval
    assert all((run / "evals" / f"u{u:07d}" / "blind-access.jsonl").exists() for u in (1, 2))
    # the guard keeps the run's eval set (here a blind one) out of the stream as well
    conf = json.loads((run / "config.json").read_text())
    assert "syn-blind-v1" in {x["id"] for x in conf["isolation"]["checked_sets"]}
    assert conf["blind_eval"]["purpose"] == "trainer access log"
