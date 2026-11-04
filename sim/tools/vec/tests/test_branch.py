"""Search branching (ABI 2): VecEnv.branch, VecEnv.restore_many, env.pool.release (sim/VEC.md "Search")."""

import threading

import numpy as np
import pytest

from sts2vec import STATUS, PoolSpec, Sts2VecError, VecEnv, branch_seed, random_actions

VIEW_BITS = STATUS["TABLE_OVERFLOW"] | STATUS["CANDIDATE_OVERFLOW"]
BAD = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
POOL = PoolSpec(characters=("IRONCLAD", "SILENT", "DEFECT"), count=6)
SIGNALS = ("reward", "terminated", "truncated", "status", "episode", "last_episode")


def _views(env):
    """Buffers a branch writes from the source's view (all but the per-slot signals)."""
    return [n for n in env.schema.names if n not in SIGNALS]


def _advance(env, steps, seed, acting=None):
    """Seeded random steps; only the slots in ``acting`` (default: all) take actions."""
    rng = np.random.default_rng(seed)
    for _ in range(steps):
        a = random_actions(rng, env.obs["cand_n"])
        if acting is not None:
            keep = np.zeros(env.num_envs, bool)
            keep[list(acting)] = True
            a[~keep] = -1
        env.step(a)


def test_branch_seed_vectors():
    # the same vectors as the C# test (VecEnv.BranchSeed)
    assert branch_seed(0, 0) == 0xE220A8397B1DCDAF
    assert branch_seed(0, 1) == 0x6E789E6AA1B965F4
    assert branch_seed(2**64 - 1, 0) == 0xE4D971771B652C20
    assert branch_seed(12345, 7) == 0x6E7411B06820371C
    assert branch_seed(-1, 0) == branch_seed(2**64 - 1, 0)  # seeds are taken mod 2**64


def test_capability_flags(lib):
    assert lib.abi_version == 2
    assert lib.supports_branch and lib.supports_restore_many and lib.supports_pool_release
    with VecEnv(2, threads=1, pool=None) as env:
        assert env.abi_version == 2 and env.supports_branch and env.supports_restore_many and env.supports_pool_release
    with pytest.raises(KeyError, match="unknown feature"):
        lib.supports("teleport")


def test_branch_exact_copies_state_view_and_future(full):
    with VecEnv(8, threads=4, pool=POOL, seed_text="PYBR") as env:
        env.reset()
        _advance(env, 30, 1)
        src, dsts = 2, [5, 0, 7]
        before = {k: v.copy() for k, v in env.buffers.items()}
        h = env.state_hash([src])[0]
        episode_ids = env.info(copy=True)["episode_id"]
        env.branch(src, dsts, determinized=False)
        assert list(env.state_hash(dsts)) == [h] * 3
        info = env.info()
        for d in dsts:
            for name in _views(env):
                assert np.array_equal(env.buffers[name][d], env.buffers[name][src]), (name, d)
            assert env.status[d] == STATUS["RESET"] | (before["status"][src] & VIEW_BITS)
            assert info["step_index"][d] == 0 and info["start_entry"][d] == -1
            assert info["episode_id"][d] == episode_ids[d] + env.num_envs
            assert np.array_equal(info["last_episode"][d], before["last_episode"][d])
            assert env.buffers["reward"][d] == 0 and env.buffers["terminated"][d] == 0 and env.buffers["truncated"][d] == 0
        for i in sorted(set(range(8)) - set(dsts)):  # the source and the bystanders are untouched
            for name, a in env.buffers.items():
                assert np.array_equal(a[i], before[name][i]), (name, i)
        rng = np.random.default_rng(4)
        replayed = 0
        for _ in range(12):  # the copies replay the source's future
            n = int(env.obs["cand_n"][src])
            if n == 0:
                break
            a = np.full(8, -1, np.int32)
            a[[src, *dsts]] = rng.integers(n)
            env.step(a)
            if env.info()["step_index"][src] == 0:
                break  # the source's episode ended (auto-reset): futures no longer comparable
            assert np.all(env.state_hash(dsts) == env.state_hash([src])[0])
            replayed += 1
        assert replayed > 0


def test_branch_determinized_same_view_resampled_hidden_state(full):
    with VecEnv(8, threads=4, pool=POOL, seed_text="PYBD") as env:
        env.reset()
        rng = np.random.default_rng(2)
        checked = 0
        for t in range(40):
            a = random_actions(rng, env.obs["cand_n"])
            a[2:] = -1
            env.step(a)
            if t % 4 != 3:
                continue
            src, dsts, seed = t % 2, [3, 4, 5], 1000 + t
            h = env.state_hash([src])[0]
            env.branch(src, dsts, seed=seed)  # determinized is the default
            hs = env.state_hash(dsts)
            for d in dsts:
                for name in _views(env):
                    assert np.array_equal(env.buffers[name][d], env.buffers[name][src]), (t, name, d)
            assert np.all(hs != h) and len(set(hs.tolist())) == 3
            # position j is a function of (state, seed, j): the same branch elsewhere, and the pre-branch path
            env.branch(src, [6, 7], seed=seed)
            assert list(env.state_hash([6, 7])) == list(hs[:2])
            e = env.clone_determinized(src, branch_seed(seed, 2))
            env.restore(7, e)
            assert env.state_hash([7])[0] == hs[2]
            assert env.pool.release(e) == 1
            checked += 1
        assert checked == 10 and len(env.pool) == 6
        assert not np.any(env.status & BAD)


def test_branch_many_roots_in_one_call(full):
    # src as an array: one source per destination (sts2vec_branch_many); position j seeds BranchSeed(seed, j)
    with VecEnv(12, threads=4, pool=POOL, seed_text="PYBM") as env:
        env.reset()
        _advance(env, 25, 6)
        srcs, dsts = [0, 1, 2, 0, 1, 2, 0, 1], [3, 4, 5, 6, 7, 8, 9, 10]
        h = dict(zip([0, 1, 2], env.state_hash([0, 1, 2])))
        env.branch(srcs, dsts, determinized=False)
        assert list(env.state_hash(dsts)) == [h[s] for s in srcs]
        for s, d in zip(srcs, dsts):
            for name in _views(env):
                assert np.array_equal(env.buffers[name][d], env.buffers[name][s]), (name, s, d)
        env.branch(srcs, dsts, seed=7)
        hs = env.state_hash(dsts)
        assert all(x != h[s] for x, s in zip(hs, srcs)) and len(set(hs.tolist())) == len(dsts)
        for j in (0, 5):  # position j in the call = clone_determinized(src, branch_seed(seed, j)) restored
            e = env.clone_determinized(srcs[j], branch_seed(7, j))
            env.restore(11, e)
            assert env.state_hash([11])[0] == hs[j]
            env.pool.release(e)
        env.branch(np.full(3, 1), [3, 4, 5], seed=9)  # one source repeated = the single-source call
        a = env.state_hash([3, 4, 5])
        env.branch(1, [6, 7, 8], seed=9)
        assert list(a) == list(env.state_hash([6, 7, 8]))
        with pytest.raises(Sts2VecError, match="source slot of this call"):
            env.branch([0, 1], [3, 1])
        with pytest.raises(ValueError, match="one source per destination"):
            env.branch([0, 1], [3])


def test_branch_rejects_bad_arguments_and_changes_nothing(full):
    with VecEnv(4, threads=2, pool=POOL) as env:
        env.reset(mask=[1, 1, 1, 0])  # slot 3 holds no run
        before = {k: v.copy() for k, v in env.buffers.items()}
        h = env.state_hash()
        for (src, dsts), match in [((3, [1]), "holds no run"), ((0, [0]), "source slot"), ((0, [1, 1]), "listed twice"),
                                   ((0, [4]), "outside"), ((9, [1]), "outside"), ((-1, [1]), "outside")]:
            with pytest.raises(Sts2VecError, match=match):
                env.branch(src, dsts)
        with pytest.raises(TypeError, match="integers"):
            env.branch(0, [1.0])
        with pytest.raises(ValueError, match="1-D"):
            env.branch(0, [[1, 2]])
        env.branch(0, [])  # n = 0: a no-op
        for k, v in env.buffers.items():
            assert np.array_equal(v, before[k]), k
        assert np.array_equal(env.state_hash(), h)


def test_pool_release_drops_entries_for_good(full):
    with VecEnv(4, threads=2, pool=PoolSpec(characters=("SILENT",), count=3), seed_text="PYREL") as env:
        env.reset()
        _advance(env, 8, 3)
        a, b, c = env.snapshot(0), env.snapshot(1), env.clone_determinized(2, 9)
        assert (a, b, c) == (3, 4, 5) and len(env.pool) == 6
        assert env.pool.release([a, c]) == 2
        assert len(env.pool) == 4
        with pytest.raises(Sts2VecError, match="was released"):
            env.restore(0, a)
        with pytest.raises(Sts2VecError, match="was released"):
            env.reset(start_ids=[c, -1, -1, -1])
        with pytest.raises(Sts2VecError, match="was released"):
            env.restore_many([1], [a])
        with pytest.raises(Sts2VecError, match="was released"):
            env.pool.release([b, a])  # all or nothing: b stays live
        with pytest.raises(Sts2VecError, match="listed twice"):
            env.pool.release([b, b])
        with pytest.raises(Sts2VecError, match="does not exist"):
            env.pool.release(99)
        assert len(env.pool) == 4
        env.restore(3, b)
        assert env.pool.release(b) == 1 and len(env.pool) == 3
        assert env.snapshot(0) == 6  # ids are never reused
        assert env.pool.release([]) == 0
        with pytest.raises(TypeError):
            env.pool.release([1.5])
        env.reset()  # the start sequence runs over the live entries
        assert set(env.info()["start_entry"].tolist()) <= {0, 1, 2, 6}


def test_restore_many_equals_sequential_restores(full):
    kw = dict(threads=4, pool=POOL, seed_text="PYRM")
    with VecEnv(8, **kw) as many, VecEnv(8, **kw) as seq:
        for env in (many, seq):
            env.reset()
            _advance(env, 20, 5)
            e0, e1 = env.snapshot(0), env.clone_determinized(1, 3)
        slots, entries = [6, 2, 7, 0, 4], [e0, e0, e1, 2, e1]
        many.restore_many(slots, entries)
        for s, e in zip(slots, entries):
            seq.restore(s, e)
        for name in many.schema.names:
            assert np.array_equal(many.buffers[name], seq.buffers[name]), name
        assert np.array_equal(many.state_hash(), seq.state_hash())
        assert np.all(many.status[slots] & STATUS["RESET"]) and list(many.info()["start_entry"][slots]) == entries
        before = {k: v.copy() for k, v in many.buffers.items()}
        with pytest.raises(Sts2VecError, match="listed twice"):
            many.restore_many([1, 1], [0, 0])
        with pytest.raises(Sts2VecError, match="outside"):
            many.restore_many([8], [0])
        with pytest.raises(Sts2VecError, match="does not exist"):
            many.restore_many([1], [99])
        with pytest.raises(ValueError, match="entries"):
            many.restore_many([1, 2], [0])
        for k, v in many.buffers.items():
            assert np.array_equal(v, before[k]), k


def test_branch_from_another_thread_while_stepping(full):
    # the native lock serialises the two threads' calls: the stepped slots end as in an env without branching, the
    # source (no-ops only) never changes, and the copies equal it
    kw = dict(threads=2, pool=POOL, seed_text="PYTH")
    with VecEnv(12, **kw) as env, VecEnv(12, **kw) as ref:
        env.reset()
        ref.reset()
        src, dsts = 6, [7, 8, 9, 10, 11]
        h = env.state_hash([src])[0]
        errors, log = [], []

        def brancher():
            try:
                for r in range(120):
                    env.branch(src, dsts, determinized=bool(r % 2), seed=r)
            except Exception as e:  # pragma: no cover - reported below
                errors.append(e)

        t = threading.Thread(target=brancher)
        t.start()
        rng = np.random.default_rng(0)
        for _ in range(120):
            a = random_actions(rng, env.obs["cand_n"])
            a[6:] = -1
            log.append(a.copy())
            env.step(a)
        t.join()
        assert not errors, errors
        for a in log:
            ref.step(a)
        assert np.array_equal(env.state_hash(range(6)), ref.state_hash(range(6)))
        assert env.state_hash([src])[0] == h
        env.branch(src, dsts, determinized=False)
        assert np.all(env.state_hash(dsts) == h)
        assert not np.any(env.status & BAD)
