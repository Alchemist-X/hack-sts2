import re

import pytest

from sts2vec import PoolSpec, Sts2VecError, VecEnv, load
from sts2vec.loader import EXPORTS, managed_name


def test_loads_all_exports():
    lib = load()
    assert sorted(lib.exports) == sorted(EXPORTS)
    assert len(lib.exports) == 14
    if lib.mode == "hostfxr":
        assert lib.runtimeconfig is not None and lib.runtimeconfig.name == "sts2vec.runtimeconfig.json"


def test_version_string():
    lib = load()
    v = lib.version()
    assert re.fullmatch(r"sts2vec/[0-9a-f]{16}", v), v


def test_server_gc_create_succeeds():
    # sts2vec_create refuses to run under Workstation GC, so a successful create proves Server GC.
    with VecEnv(2, threads=2, pool=PoolSpec(characters=("IRONCLAD",), count=1)) as env:
        assert env.handle > 0
        assert len(env.pool) == 1


def test_invalid_handle_is_minus_one_with_text():
    lib = load()
    assert lib.sts2vec_destroy(987654321) == -1
    assert "invalid handle" in lib.last_error(0)
    assert lib.sts2vec_pool_count(987654321) == -1


def test_bad_config_is_rejected():
    lib = load()
    cfg = b'{"num_envs": 2, "nope": 1}'
    assert lib.sts2vec_create(cfg, len(cfg)) == -1
    assert "unknown config key" in lib.last_error(0)
    with pytest.raises(Sts2VecError, match="content"):
        VecEnv(2, content="everything", pool=None)


def test_managed_names():
    assert managed_name("sts2vec_pool_add_fresh") == "PoolAddFresh"
    assert managed_name("sts2vec_schema") == "Schema"
