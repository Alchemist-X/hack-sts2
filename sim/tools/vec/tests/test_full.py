"""Behaviour that needs the real V1 encoder and V2 env-core (CONTRACTS.md §D7 Python list).

Every test takes the ``full`` fixture: against the scaffold stubs it skips with reason ``needs V1/V2``; with
``STS2VEC_REQUIRE_FULL=1`` a stub build fails instead.
"""

import json
import os
import subprocess
import sys

import numpy as np
import pytest

from conftest import SMALL_POOL
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions

BAD = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]


def _digest(threads: int, envs: int = 64, steps: int = 150) -> dict:
    r = subprocess.run([sys.executable, "-m", "sts2vec.cli", "digest", "--envs", str(envs), "--steps", str(steps),
                        "--threads", str(threads), "--pool", "16", "--seed", "11"],
                       capture_output=True, text=True, timeout=600, env=dict(os.environ))
    assert r.returncode == 0, r.stdout + r.stderr
    d = json.loads(r.stdout.strip().splitlines()[-1])
    return d


def test_candidates_non_empty(full):
    with VecEnv(8, threads=4, pool=PoolSpec(count=8)) as env:
        obs = env.reset()
        assert np.all(obs["cand_n"] > 0)
        cap = env.schema["cand"].shape[0]
        for i in range(8):
            rows = env.candidates(i)
            assert rows and all(r["kind"] in env.schema.vocab.enums["RunActionKind"] for r in rows)
            n = int(obs["cand_n"][i])
            if n < cap:
                assert not obs["cand"][i, n:].any()  # rows beyond cand_n are zero
        hp = obs["global_i"][:, env.schema.col("global_i", "hp")]
        assert np.all(hp > 0)


def test_determinism_threads_1_vs_8_nontrivial(full):
    n, steps = 128, 200
    envs = [VecEnv(n, threads=t, pool=PoolSpec(count=32)) for t in (1, 8)]
    try:
        obs = [e.reset() for e in envs]
        rngs = [np.random.default_rng(3), np.random.default_rng(3)]
        taken = 0
        for step in range(steps):
            acts = [random_actions(r, o["cand_n"]) for r, o in zip(rngs, obs)]
            taken += int(np.count_nonzero(acts[0] >= 0))
            outs = [e.step(a) for e, a in zip(envs, acts)]
            obs = [o[0] for o in outs]
            for name in envs[0].schema.names:
                assert np.array_equal(envs[0].buffers[name], envs[1].buffers[name]), (step, name)
        assert taken > n * steps // 2
        assert np.array_equal(envs[0].state_hash(), envs[1].state_hash())
    finally:
        for e in envs:
            e.close()


@pytest.mark.slow
def test_determinism_across_processes(full):
    a, b = _digest(1), _digest(8)
    assert a["actions_taken"] > 0
    for key in ("state_hash_sha256", "obs_sha256", "actions_taken", "nonzero_hashes"):
        assert a[key] == b[key], key


@pytest.mark.slow
def test_random_soak_1024x300_no_sim_error_or_content_gap(full):
    n, steps = 1024, 300
    with VecEnv(n, threads=8, content="ported_only", pool=PoolSpec(count=256)) as env:
        obs = env.reset()
        assert not np.any(env.status & BAD), env.slot_errors()
        rng = np.random.default_rng(0)
        ended = wins = 0
        for _ in range(steps):
            obs, reward, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
            bad = np.flatnonzero(status & BAD)
            assert bad.size == 0, {int(i): env.last_error(int(i)).splitlines()[:3] for i in bad[:3]}
            assert not np.any(status & STATUS["ILLEGAL_ACTION"])
            assert np.all(reward[term == 0] == 0)  # reward only at a terminal
            assert np.all((reward == 0) | (reward == 1))
            ended += int(np.count_nonzero(term | trunc))
            wins += int(np.count_nonzero(reward > 0))
            assert np.all(obs["cand_n"] > 0)  # auto-reset: every slot is always live
        assert ended > 0


def test_step_cap_truncation_and_auto_reset(full):
    cap = 5
    with VecEnv(16, threads=4, step_cap=cap, pool=PoolSpec(count=8)) as env:
        obs = env.reset()
        first_ids = env.info(copy=True)["episode_id"]
        rng = np.random.default_rng(1)
        for _ in range(cap):
            obs, reward, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
        ended = (term | trunc).astype(bool)
        assert ended.all()  # every episode ended by step `cap` at the latest
        assert np.all(info["episode_id"] != first_ids)
        assert np.all(status & STATUS["RESET"])
        end_kinds = env.schema.vocab.enums["EndKind"]
        for i in np.flatnonzero(trunc):
            assert env.last_episode(int(i))["end_kind"] == "Truncated"
            assert reward[i] == 0
        assert np.all(info["step_index"] == 0)
        assert set(info["last_episode"][:, env.schema.col("last_episode", "end_kind")]) <= {
            end_kinds.index_of("Terminal"), end_kinds.index_of("Truncated")}


def test_snapshot_restore_mid_episode(full):
    with VecEnv(4, threads=2, pool=SMALL_POOL) as env:
        obs = env.reset()
        rng = np.random.default_rng(5)
        for _ in range(7):
            obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
        h = env.state_hash()
        entry = env.snapshot(0)
        env.restore(2, entry)
        assert env.state_hash([2])[0] == h[0]
        for name in env.schema.observation_names:
            assert np.array_equal(env.buffers[name][2], env.buffers[name][0]), name


def test_clone_determinized_same_view_resampled_hidden_state(full):
    # sts2vec_clone_determinized: the restored clone shows the source slot's observation byte for byte, its hidden
    # state differs (hash), the same seed gives the same clone, another seed another one; works mid-episode
    with VecEnv(4, threads=2, pool=SMALL_POOL) as env:
        obs = env.reset()
        rng = np.random.default_rng(13)
        compared = 0
        for t in range(60):
            obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
            if t % 6 != 5:
                continue
            h0 = env.state_hash([0])[0]
            a = env.clone_determinized(0, 2**64 - 1 - t)
            b = env.clone_determinized(0, 2**64 - 1 - t)
            c = env.clone_determinized(0, t)
            env.restore(1, a)
            env.restore(2, b)
            env.restore(3, c)
            ha, hb, hc = env.state_hash([1, 2, 3])
            assert ha == hb and ha != h0 and hc != ha
            for name in env.schema.observation_names:
                for s in (1, 2, 3):
                    assert np.array_equal(env.buffers[name][s], env.buffers[name][0]), (name, s)
            compared += 1
            obs = {"cand_n": env.buffers["cand_n"]}
        assert compared == 10


def test_illegal_index_leaves_live_state_unchanged(full):
    with VecEnv(4, threads=2, pool=SMALL_POOL) as env:
        obs = env.reset()
        h = env.state_hash()
        before = obs["cand"].copy()
        _, _, _, _, status, _ = env.step(obs["cand_n"].copy())  # index == cand_n is out of range
        assert np.all(status & STATUS["ILLEGAL_ACTION"])
        assert np.array_equal(env.state_hash(), h)
        assert np.array_equal(env.buffers["cand"], before)
