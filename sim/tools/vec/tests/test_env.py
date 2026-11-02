import numpy as np
import pytest

from conftest import SMALL_POOL
from sts2vec import STATUS, PoolSpec, Sts2VecError, VecEnv, random_actions
from sts2vec.schema import SIGNAL_BUFFERS, slot_seed

M64 = (1 << 64) - 1


def test_reset_shapes_and_dtypes(small_env):
    env = small_env
    obs = env.reset()
    assert set(obs) == set(env.schema.observation_names)
    for name, a in obs.items():
        spec = env.schema[name]
        assert a.shape == (env.num_envs, *spec.shape), name
        assert a.dtype == spec.dtype, name
        assert np.shares_memory(a, env.buffers[name])  # views of the env-owned arrays
    assert obs["cand_n"].dtype == np.int32 and obs["cand_n"].shape == (4,)
    assert np.all(env.status & STATUS["RESET"])
    assert env.status.dtype == np.int32
    info = env.info()
    assert list(info["episode_id"]) == [1, 2, 3, 4]  # episodes_started_before * N + slot + 1
    assert info["episode_id"].dtype == np.int64
    assert np.all(info["step_index"] == 0)


def test_step_returns(small_env):
    env = small_env
    obs = env.reset()
    out = env.step(np.full(4, -1, dtype=np.int32))
    assert len(out) == 6
    obs, reward, term, trunc, status, info = out
    assert reward.shape == (4,) and reward.dtype == np.float32
    assert term.shape == (4,) and term.dtype == np.uint8
    assert trunc.shape == (4,) and trunc.dtype == np.uint8
    assert status.shape == (4,) and status.dtype == np.int32
    assert set(info) == {"episode_id", "step_index", "start_entry", "last_episode"}
    assert info["last_episode"].shape == (4, len(env.schema["last_episode"].fields))
    assert np.all(status & STATUS["NOOP"])
    assert not np.any(status & STATUS["RESET"])
    assert np.all(reward == 0) and not term.any() and not trunc.any()
    assert set(obs) & set(SIGNAL_BUFFERS) == set()


def test_illegal_action_status_and_state_unchanged(small_env):
    env = small_env
    obs = env.reset()
    h0 = env.state_hash()
    acts = np.full(4, -1, dtype=np.int32)
    acts[1] = obs["cand_n"][1]  # always out of range
    acts[2] = env.schema["cand"].shape[0] + 7
    _, _, _, _, status, _ = env.step(acts)
    assert status[0] & STATUS["NOOP"] and status[3] & STATUS["NOOP"]
    assert status[1] & STATUS["ILLEGAL_ACTION"] and status[2] & STATUS["ILLEGAL_ACTION"]
    assert np.array_equal(env.state_hash(), h0)


def test_copy_option(small_env):
    env = small_env
    obs = env.reset(copy=True)
    assert not any(np.shares_memory(a, env.buffers[k]) for k, a in obs.items())
    obs, reward, term, trunc, status, info = env.step(np.full(4, -1, np.int32), copy=True)
    for a, name in ((reward, "reward"), (term, "terminated"), (trunc, "truncated"), (status, "status")):
        assert not np.shares_memory(a, env.buffers[name])
    assert not np.shares_memory(info["episode_id"], env.buffers["episode"])
    _, reward2, *_ = env.step(np.full(4, -1, np.int32))
    assert np.shares_memory(reward2, env.buffers["reward"])


def test_input_validation(small_env):
    env = small_env
    env.reset()
    with pytest.raises(ValueError, match=r"shape \(4,\)"):
        env.step(np.zeros(3, np.int32))
    with pytest.raises(ValueError):
        env.step(-1)  # no silent broadcasting
    with pytest.raises(TypeError, match="integer"):
        env.step(np.zeros(4, np.float32))
    with pytest.raises(ValueError):
        env.reset(mask=[1, 0])
    env.step([-1, -1, -1, -1])  # lists are fine


def test_partial_reset_touches_only_masked_slots(small_env):
    env = small_env
    env.reset()
    env.step(np.full(4, -1, np.int32))
    before = {k: v.copy() for k, v in env.buffers.items()}
    env.reset(mask=np.array([0, 1, 0, 0], dtype=bool))
    st = env.status
    assert st[1] & STATUS["RESET"]
    for i in (0, 2, 3):
        assert st[i] == before["status"][i]
        for name, a in env.buffers.items():
            assert np.array_equal(a[i], before[name][i]), (name, i)
    assert env.info()["episode_id"][1] == 1 * 4 + 1 + 1  # second episode of slot 1


def test_start_sequence_matches_definition():
    seed_text, n, pool = "SEQ", 6, 5
    with VecEnv(n, threads=2, seed_text=seed_text, pool=PoolSpec(characters=("IRONCLAD",), count=pool)) as env:
        for k in range(3):
            env.reset()
            got = env.info()["start_entry"]
            want = [((slot_seed(seed_text, i) + k) & M64) % pool for i in range(n)]
            assert list(got) == want, k
            assert list(env.info()["episode_id"]) == [k * n + i + 1 for i in range(n)]


def test_explicit_start_ids_and_out_of_range(small_env):
    env = small_env
    env.reset(start_ids=[1, 0, -1, 1])
    se = env.info()["start_entry"]
    assert se[0] == 1 and se[1] == 0 and se[3] == 1
    status_before = env.status.copy()
    with pytest.raises(Sts2VecError, match="pool has 2 entries"):
        env.reset(start_ids=[0, 0, 2, 0])
    assert np.array_equal(env.status, status_before)  # nothing changed


def test_empty_pool_reports_pool_empty():
    with VecEnv(3, threads=1, pool=None) as env:
        obs = env.reset()
        assert np.all(env.status & STATUS["POOL_EMPTY"])
        assert np.all(obs["cand_n"] == 0)
        assert np.all(env.state_hash() == 0)
        with pytest.raises(Sts2VecError, match="snapshot"):
            env.snapshot(0)


def test_snapshot_restore_roundtrip(small_env):
    env = small_env
    env.reset()
    h = env.state_hash()
    assert h.dtype == np.uint64 and np.all(h != 0)
    entry = env.snapshot(0)
    assert entry == len(env.pool) - 1 == 2
    env.restore(3, entry)
    assert env.status[3] & STATUS["RESET"]
    assert env.state_hash([3])[0] == h[0]
    assert env.info()["start_entry"][3] == entry
    with pytest.raises(Sts2VecError, match="restore"):
        env.restore(0, 999)
    with pytest.raises(Sts2VecError, match="restore"):
        env.restore(99, 0)


def test_state_hash_slots(small_env):
    env = small_env
    env.reset()
    full = env.state_hash()
    assert np.array_equal(env.state_hash([2, 0]), full[[2, 0]])
    assert env.state_hash([]).shape == (0,)
    with pytest.raises(Sts2VecError, match="state_hash"):
        env.state_hash([7])


def test_set_threads(small_env):
    env = small_env
    env.set_threads(1)
    assert env.threads == 1
    env.reset()
    with pytest.raises(Sts2VecError, match="threads"):
        env.set_threads(0)
    assert env.threads == 1


def test_candidates_and_masks_decode(small_env):
    env = small_env
    obs = env.reset()
    for i in range(env.num_envs):
        rows = env.candidates(i)
        assert len(rows) == int(obs["cand_n"][i])
        kinds = env.schema.vocab.enums["RunActionKind"]
        for r in rows:
            assert r["kind"] in kinds and kinds.index_of(r["kind"]) == r["kind_id"]
            assert set(env.schema["cand"].fields) <= set(r)
    m = env.legal_mask()
    assert m.shape == (4, env.schema.caps["CANDIDATES"]) and m.dtype == bool
    assert np.array_equal(m.sum(axis=1), obs["cand_n"])
    d = env.last_episode(0)
    assert set(env.schema["last_episode"].fields) <= set(d)
    assert d["end_kind"] in env.schema.vocab.enums["EndKind"]


def test_random_actions_helper():
    rng = np.random.default_rng(0)
    cand_n = np.array([0, 1, 5, 192, 0], dtype=np.int32)
    for _ in range(50):
        a = random_actions(rng, cand_n)
        assert a.dtype == np.int32
        assert a[0] == -1 and a[4] == -1 and a[1] == 0
        assert 0 <= a[2] < 5 and 0 <= a[3] < 192
    a1 = random_actions(np.random.default_rng(7), cand_n)
    a2 = random_actions(np.random.default_rng(7), cand_n)
    assert np.array_equal(a1, a2)


def test_context_manager_and_close():
    with VecEnv(2, threads=1, pool=SMALL_POOL) as env:
        env.reset()
        h = env.handle
        assert h > 0 and not env.closed
    assert env.closed and env.handle == 0
    env.close()  # idempotent
    env.close()
    with pytest.raises(Sts2VecError, match="closed"):
        env.step(np.full(2, -1, np.int32))
    with pytest.raises(Sts2VecError, match="closed"):
        env.reset()
    with pytest.raises(Sts2VecError, match="closed"):
        len(env.pool)
    assert env.lib.sts2vec_pool_count(h) == -1  # the native env is gone
    assert env.buffers["status"].shape == (2,)  # buffers stay readable
    assert "closed" in repr(env)


def test_auto_reset_off_and_options_accepted():
    with VecEnv(2, threads=1, auto_reset=False, step_cap=10, seed_text="X", content="all", pool_cache=0,
                pool=SMALL_POOL, fast_path=False) as env:
        assert env.config["auto_reset"] is False and env.config["pool_cache"] == 0 and env.config["fast_path"] is False
        env.reset()
        env.step(np.full(2, -1, np.int32))


def test_many_envs_one_call():
    with VecEnv(256, threads=8, pool=PoolSpec(characters=("IRONCLAD", "SILENT", "DEFECT"), count=8)) as env:
        env.reset()
        assert np.all(env.status & STATUS["RESET"])
        assert not np.any(env.status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]))
        for _ in range(3):
            env.step(np.full(256, -1, np.int32))
        assert np.all(env.state_hash() != 0)
