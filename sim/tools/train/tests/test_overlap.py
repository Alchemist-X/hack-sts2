"""--overlap double (sts2train.overlap): the lane pair is a drop-in N-slot env (same observations, signals, episode
ids and stream seed assignment as one VecEnv under the same actions), and the overlapped rollout reproduces the
--overlap off rollout under a deterministic policy (same actions, rewards, episodes per slot, cycles, metrics); with
sampling it is reproducible for a fixed seed and keeps the batch shape."""

from __future__ import annotations

import numpy as np
import pytest
import torch
from sts2vec import VecEnv, random_actions

from sts2train import ppo
from sts2train.isolation import IsolationGuard
from sts2train.overlap import LaneStreamEnv, VecPair, lane_bounds
from sts2train.ppo import env_kwargs, resolve_config, train
from sts2train.runlog import read_jsonl
from sts2train.seedstream import SeedStream, StreamEnv

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 12, "threads": 2, "rollout": 16, "minibatch": 64, "epochs": 1, "max_updates": 3, "ckpt_every": 0,
       "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2, "log_every": 100,
       "characters": ("SILENT",), "ascension": 10, "step_cap": 24, **TINY}
TIMING = ("time", "wall_s", "session_s", "env_steps_per_s", "env_steps_per_s_session")


def _quiet(m):
    pass


def _greedy(self, x, *, compiled=True, adjust=None):
    logits, value = self.forward(x, compiled=compiled)
    if adjust is not None:
        logits = adjust(logits)
    a = logits.argmax(-1)
    return a, torch.log_softmax(logits.float(), -1).gather(-1, a[:, None])[:, 0], value


def _run(tmp_path, name, **kw):
    cfg = resolve_config({**RUN, "out": str(tmp_path / name), **kw})
    s = train(cfg, log=_quiet)
    rows = read_jsonl(tmp_path / name / "metrics.jsonl")
    eps = read_jsonl(tmp_path / name / "episodes.jsonl")
    cyc = read_jsonl(tmp_path / name / "cycles.jsonl")
    return s, rows, eps, cyc


def _strip(rows):
    return [{k: v for k, v in r.items() if not k.startswith("t_") and k not in TIMING} for r in rows]


def _same_rows(a_rows, b_rows):
    """Equal metrics rows: discrete values exactly, floats to 1e-4 relative or 1e-5 absolute (the lane forward runs on half
    batches, whose matmuls round differently from the full batch's in the last bits)."""
    assert len(a_rows) == len(b_rows)
    for a, b in zip(_strip(a_rows), _strip(b_rows)):
        assert sorted(a) == sorted(b)
        for k, v in a.items():
            if isinstance(v, float):
                assert b[k] == pytest.approx(v, rel=1e-4, abs=1e-5), k
            else:
                assert b[k] == v, k


def _weights(s):
    return torch.load(s["last_checkpoint"], map_location="cpu", weights_only=False)["model"]


def test_config_rules():
    assert resolve_config({}).overlap == "off"
    assert resolve_config({"overlap": "double"}).overlap == "double"
    with pytest.raises(ValueError):
        resolve_config({"overlap": "triple"})
    with pytest.raises(ValueError, match="envs >= 2"):
        resolve_config({"overlap": "double", "envs": 1})
    with pytest.raises(ValueError, match="start-saves"):
        resolve_config({"overlap": "double", "start_saves": "/tmp/x"})
    with pytest.raises(ValueError, match="--harvest with --overlap double"):
        resolve_config({"overlap": "double", "start_mode": "stream", "harvest": "elite:8"})
    assert resolve_config({"overlap": "off", "start_mode": "stream", "harvest": "elite:8"}).harvest == "elite:8"
    assert lane_bounds(5) == [(0, 2), (2, 5)]


def test_stream_pair_matches_one_env_under_the_same_actions():
    """LaneStreamEnv over a VecPair, stepped lane by lane, vs StreamEnv over one VecEnv: identical observation
    buffers, signals, info (episode ids mapped to the one-env numbering) and stream seeds per slot, through many
    restarts (small step cap)."""
    cfg = resolve_config({**RUN, "start_mode": "stream", "seed": 5, "init_seed": 5, "sample_seed": 5})
    n = 10
    guard = IsolationGuard.from_seedsets()
    mk = lambda: SeedStream("SPOV", cfg.characters, cfg.ascension, guard=guard)  # noqa: E731
    kw = env_kwargs(cfg, "SPENV5")
    one = StreamEnv(VecEnv(n, **kw), mk(), lookahead=16, prefetch=False)
    two = LaneStreamEnv(VecPair(n, **kw), mk(), lookahead=16, prefetch=False)
    try:
        for e in (one, two):
            e.prime()
            e.pool.warm()
        o1, o2 = one.reset(), two.reset()
        rng1, rng2 = np.random.default_rng(1), np.random.default_rng(1)
        ends = 0
        for _ in range(120):
            for k in o1:
                assert np.array_equal(o1[k], o2[k]), k
            i1, i2 = one.info(), two.info()
            for k in i1:
                assert np.array_equal(i1[k], i2[k]), k
            assert one.slot_text == two.slot_text and np.array_equal(one.slot_n, two.slot_n)
            a1, a2 = random_actions(rng1, o1["cand_n"]), random_actions(rng2, o2["cand_n"])
            r1 = one.step(a1)
            sig2 = [two.step_lane(k, a2[lo:hi]) for k, (lo, hi) in enumerate(two.env.bounds)]
            o2 = two.obs
            for j in range(4):
                assert np.array_equal(r1[1 + j], np.concatenate([s[j] for s in sig2]))
            o1 = r1[0]
            ends += int(((r1[2] | r1[3]) != 0).sum())
        assert ends >= n                       # the restarts were exercised
        assert one.stream.state_dict() == two.stream.state_dict()
        assert np.array_equal(one.state_hash(), two.state_hash())
    finally:
        one.close()
        two.close()


def test_pool_pair_lane_a_matches_one_env():
    """Pool mode: lane A (same seed text, same local slots) starts and steps exactly like slots [0, N//2) of one env;
    lane B's start offsets come from another seed text (same pool)."""
    cfg = resolve_config({**RUN, "pool": 8, "seed": 2})
    n = 8
    kw = env_kwargs(cfg, "PPO2")
    one, two = VecEnv(n, **kw), VecPair(n, **kw)
    try:
        assert len(one.pool) == len(two.pool) == 8
        o1, o2 = one.reset(), two.reset()
        rng = np.random.default_rng(3)
        h = n // 2
        for _ in range(60):
            for k in o1:
                assert np.array_equal(o1[k][:h], o2[k][:h]), k
            a = random_actions(rng, o1["cand_n"])
            a2 = a.copy()
            a2[h:] = random_actions(np.random.default_rng(int(rng.integers(1 << 30))), o2["cand_n"][h:])
            o1 = one.step(a)[0]
            o2 = two.step(a2)[0]
        assert np.array_equal(one.state_hash()[:h], two.state_hash()[:h])
    finally:
        one.close()
        two.close()


@pytest.mark.parametrize("extra", [{}, {"cycle_cost": 0.05, "acq_shaping": True, "rollout_loop_breaker": True,
                                        "aux_heads": True}, {"hp_shaping_coef": 0.1},
                                   {"hp_shaping_coef": 0.1, "acq_shaping": True}], ids=["plain", "flags", "hp", "hp-acq"])
def test_double_reproduces_off_under_a_greedy_policy(tmp_path, monkeypatch, extra):
    """Deterministic (greedy) policy, stream mode, episodes ending in the window (step cap 24): --overlap double gives
    the same actions, so the same metrics (all but timings; floats to rounding), the same episodes.jsonl (slot, seed,
    update, steps, end, progress ...), the same cycle log and the same weights (to rounding) as --overlap off."""
    monkeypatch.setattr(ppo.InferenceRunner, "sample", _greedy)
    kw = {"start_mode": "stream", "seed": 4, "init_seed": 4, "sample_seed": 4, **extra}
    s0, r0, e0, c0 = _run(tmp_path, "off", overlap="off", **kw)
    s1, r1, e1, c1 = _run(tmp_path, "double", overlap="double", **kw)
    assert len(r0) == len(r1) == 3 and sum(r["episodes"] for r in r0) >= 12
    assert all("t_overlap_wait_s" in r and "t_rollout_s" in r for r in r1)
    assert not any("t_overlap_wait_s" in r for r in r0)
    _same_rows(r0, r1)
    floats = ("return", "shaped_return", "shaping", "hp_shaping")
    assert [{k: v for k, v in e.items() if k not in floats} for e in e0] == \
        [{k: v for k, v in e.items() if k not in floats} for e in e1]
    for a, b in zip(e0, e1):                    # the episode returns (unshaped) to rounding
        assert (a["return"] is None) == (b["return"] is None)
        if a["return"] is not None:
            assert b["return"] == pytest.approx(a["return"], abs=1e-5)
    assert c0 == c1
    w0, w1 = _weights(s0), _weights(s1)
    assert sorted(w0) == sorted(w1)
    assert all(torch.allclose(w0[k].float(), w1[k].float(), rtol=1e-4, atol=1e-6) for k in w0)
    assert s0["seed_stream"]["seed_stream_next"] == s1["seed_stream"]["seed_stream_next"]
    assert s0["cycles"] == s1["cycles"] and s0["episodes"] == s1["episodes"]
    assert s1["overlap"]["lanes"] == [6, 6]


def test_double_sampling_is_reproducible_and_keeps_the_batch(tmp_path):
    """Sampling: double is reproducible for a fixed seed; every update holds envs x rollout steps (valid slots) and
    every slot's episodes start from stream seeds in increasing order, as with off."""
    kw = {"start_mode": "stream", "seed": 6, "init_seed": 6, "sample_seed": 6}
    s0, r0, e0, _ = _run(tmp_path, "off", overlap="off", **kw)
    s1, r1, e1, _ = _run(tmp_path, "d1", overlap="double", **kw)
    s2, r2, e2, _ = _run(tmp_path, "d2", overlap="double", **kw)
    assert _strip(r1) == _strip(r2) and e1 == e2 and s1["weights_hash"] == s2["weights_hash"]
    assert [r["env_steps_update"] for r in r0] == [r["env_steps_update"] for r in r1] == [12 * 16] * 3
    for eps in (e0, e1):
        ended = [e for e in eps if e["result"] not in ("dropped", "start_failed") and e.get("n") is not None]
        assert ended
        by_slot: dict = {}
        for e in ended:
            by_slot.setdefault(e["slot"], []).append(e["n"])
        assert all(v == sorted(v) for v in by_slot.values())
        ns = [e["n"] for e in ended]
        assert len(ns) == len(set(ns))          # every stream seed is played by one slot at most


def test_double_pool_mode_runs(tmp_path):
    s, rows, eps, _ = _run(tmp_path, "pool", overlap="double", pool=16, seed=1)
    assert s["updates"] == 3 and len(rows) == 3
    assert all(r["env_steps_update"] == 12 * 16 for r in rows)
    assert s["overlap"]["mode"] == "double"
