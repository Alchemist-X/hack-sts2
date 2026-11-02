import json

import numpy as np
import pytest

from conftest import FIXTURE_SAVE
from sts2vec import STATUS, FreshEntry, PoolSpec, SaveEntry, Sts2VecError, VecEnv
from sts2vec.pool import ALL_CHARACTERS, make_config


def test_poolspec_default_json():
    p = PoolSpec()
    assert p.characters == ALL_CHARACTERS
    assert p.to_json() == {"fresh": {"characters": list(ALL_CHARACTERS), "ascension": 0, "count": 4096,
                                     "seed_prefix": "TRAIN", "act1": "random"}}
    assert p.size == 4096


def test_make_config_keys():
    cfg = make_config(8, pool=PoolSpec(characters=["SILENT"], count=3, seed_prefix="X"))
    assert cfg == {"num_envs": 8, "threads": 8, "step_cap": 3000, "auto_reset": True, "seed_text": "VEC",
                   "content": "ported_only", "pool_cache": 4096,
                   "pool": {"fresh": {"characters": ["SILENT"], "ascension": 0, "count": 3, "seed_prefix": "X",
                                      "act1": "random"}}}
    json.dumps(cfg)
    assert "pool" not in make_config(2, pool=None)
    assert "pool" not in make_config(2, pool=PoolSpec.of(FreshEntry("IRONCLAD", "s")))


def test_seed_texts_follow_the_native_naming():
    p = PoolSpec(characters=("IRONCLAD", "SILENT"), count=3, seed_prefix="P")
    assert p.seed_texts() == [("IRONCLAD", "P-IRONCLAD-0"), ("SILENT", "P-SILENT-1"), ("IRONCLAD", "P-IRONCLAD-2")]


def test_entries_and_validation(tmp_path):
    e = FreshEntry("DEFECT", seed="abc", ascension=3)
    assert e.to_json() == {"character": "DEFECT", "seed": "abc", "ascension": 3, "act1": "random"}
    assert FreshEntry("DEFECT", "abc", content="all").to_json()["content"] == "all"
    p = PoolSpec(count=2, characters=("IRONCLAD",)).with_entries(e, SaveEntry(FIXTURE_SAVE))
    assert p.size == 4 and len(p.entries) == 2
    assert PoolSpec.saves(FIXTURE_SAVE).entries == (SaveEntry(FIXTURE_SAVE),)
    with pytest.raises(ValueError, match="ascension"):
        PoolSpec(ascension=11).validate()
    with pytest.raises(ValueError, match="characters"):
        PoolSpec(characters=()).validate()
    with pytest.raises(ValueError, match="count"):
        PoolSpec(count=-1).validate()
    with pytest.raises(ValueError, match="content"):
        PoolSpec.of(FreshEntry("IRONCLAD", "s", content="some")).validate()
    with pytest.raises(FileNotFoundError):
        PoolSpec.saves(tmp_path / "missing.save").validate()
    with pytest.raises(TypeError):
        PoolSpec(entries=("IRONCLAD",)).validate()
    hash(PoolSpec(characters=["IRONCLAD"]))  # lists are normalised to tuples


def test_env_pool_from_spec_entries():
    spec = PoolSpec(characters=("IRONCLAD",), count=2).with_entries(FreshEntry("SILENT", "extra-0"))
    with VecEnv(3, threads=2, pool=spec) as env:
        assert len(env.pool) == 3
        assert [i for i, _ in env.pool.added] == [2]
        env.reset(start_ids=[0, 1, 2])
        assert np.all(env.status & STATUS["RESET"])
        assert not np.any(env.status & STATUS["POOL_EMPTY"])
        assert list(env.info()["start_entry"]) == [0, 1, 2]
        h = env.state_hash()
        assert np.all(h != 0) and len(set(h.tolist())) == 3


def test_bad_spec_entry_fails_creation():
    with pytest.raises(Sts2VecError, match="pool_add_fresh"):
        VecEnv(2, pool=PoolSpec.of(FreshEntry("NOT_A_CHARACTER", "x")))


def test_add_fresh_at_runtime():
    with VecEnv(2, threads=1, pool=None) as env:
        assert len(env.pool) == 0
        assert env.pool.add_fresh("NECROBINDER", seed="rt-0") == 0
        assert env.pool.add_fresh("REGENT", seed=12345, ascension=1) == 1
        assert len(env.pool) == env.pool.count == 2
        with pytest.raises(Sts2VecError, match="pool_add_fresh"):
            env.pool.add_fresh("REGENT", seed="x", ascension=99)
        assert len(env.pool) == 2
        env.reset(start_ids=[1, 0])
        assert not np.any(env.status & (STATUS["POOL_EMPTY"] | STATUS["SIM_ERROR"]))


def test_add_save_fixture():
    assert FIXTURE_SAVE.exists()
    with VecEnv(2, threads=1, pool=None) as env:
        entry = env.pool.add_save(FIXTURE_SAVE)
        assert entry == 0 and len(env.pool) == 1
        assert isinstance(env.pool.added[0][1], SaveEntry)
        env.reset(start_ids=[0, 0])
        st = env.status
        assert np.all(st & STATUS["RESET"]), env.slot_errors(0xFF)
        assert not np.any(st & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"] | STATUS["POOL_EMPTY"]))
        h = env.state_hash()
        assert h[0] != 0 and h[0] == h[1]
        env.restore(1, entry)
        assert env.state_hash([1])[0] == h[0]


def test_add_save_missing_file_raises(tmp_path):
    with VecEnv(1, threads=1, pool=None) as env:
        with pytest.raises(Sts2VecError, match="pool_add_save"):
            env.pool.add_save(tmp_path / "missing.save")
        assert len(env.pool) == 0


def test_add_save_fixture_starts_at_rewards_with_three_candidates(full):
    with VecEnv(1, threads=1, pool=PoolSpec.saves(FIXTURE_SAVE)) as env:
        obs = env.reset()
        phases = env.schema.vocab.enums["RunPhase"]
        assert phases.name(obs["global_i"][0, env.schema.col("global_i", "phase")]) == "Rewards"
        assert int(obs["cand_n"][0]) == 3, env.candidates(0)
