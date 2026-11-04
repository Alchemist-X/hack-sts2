import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from sts2vec import LoaderError, PoolSpec, Sts2VecError, VecEnv, load
from sts2vec import loader as loader_module
from sts2vec.loader import EXPORTS, OPTIONAL_EXPORTS, default_dll, find_hostfxr, find_library, is_native, managed_name

EXPECTED_EXPORTS = [
    "sts2vec_version", "sts2vec_schema", "sts2vec_create", "sts2vec_destroy", "sts2vec_set_threads",
    "sts2vec_pool_add_fresh", "sts2vec_pool_add_save", "sts2vec_pool_count", "sts2vec_snapshot", "sts2vec_restore",
    "sts2vec_reset", "sts2vec_step", "sts2vec_state_hash", "sts2vec_last_error",
    "sts2vec_pool_warm",  # additive (E2c)
    "sts2vec_clone_determinized",  # additive (determinized clones)
    "sts2vec_abi_version", "sts2vec_branch", "sts2vec_branch_many", "sts2vec_restore_many",  # additive (ABI 2)
    "sts2vec_pool_release",
]
ABI2_EXPORTS = ["sts2vec_abi_version", "sts2vec_branch", "sts2vec_branch_many", "sts2vec_restore_many",
                "sts2vec_pool_release"]


def _run_py(code: str, env_extra: dict | None = None, timeout: int = 120) -> subprocess.CompletedProcess:
    env = {**os.environ, **(env_extra or {})}
    return subprocess.run([sys.executable, "-c", code], capture_output=True, text=True, env=env, timeout=timeout)


def test_loads_all_exports(lib):
    assert list(EXPORTS) == EXPECTED_EXPORTS  # §D5 order
    assert lib.exports == EXPECTED_EXPORTS
    assert lib.missing_exports == []
    assert lib.abi_version == 2 and lib.supports_branch and lib.supports_restore_many and lib.supports_pool_release
    for name in EXPECTED_EXPORTS:
        assert callable(getattr(lib, name))
        assert lib.require(name) is getattr(lib, name)
    if lib.mode == "hostfxr":
        assert lib.runtimeconfig is not None and lib.runtimeconfig.name == "sts2vec.runtimeconfig.json"
        assert lib.hostfxr is not None and lib.hostfxr.exists()
        assert lib.init_rc in (0, 1)


def test_load_is_cached(lib):
    assert load() is lib
    assert load(lib.path) is lib


def test_second_runtime_is_refused(lib, tmp_path):
    other = tmp_path / "Sts2Sim.Vec.dll"
    other.write_bytes(b"not a dll")
    with pytest.raises(LoaderError, match="one runtime per process"):
        load(other)


def test_version_string(lib):
    v = lib.version()
    assert re.fullmatch(r"sts2vec/[0-9a-f]{16}", v), v


def test_server_gc(lib):
    # sts2vec_create refuses to run under Workstation GC, so a successful create proves Server GC.
    if lib.mode == "hostfxr":
        assert lib.gc_server is True
        assert lib.runtime_properties.get("System.GC.Server") == "true"
    with VecEnv(2, threads=2, pool=PoolSpec(characters=("IRONCLAD",), count=1)) as env:
        assert env.handle > 0
        assert len(env.pool) == 1


@pytest.mark.skipif(os.environ.get("STS2VEC_ALLOW_WORKSTATION_GC") == "1", reason="workstation GC explicitly allowed")
def test_workstation_gc_is_a_clear_python_error(lib):
    if lib.mode != "hostfxr":
        pytest.skip("GC mode of a NativeAOT build is fixed at publish time")
    code = ("import sts2vec\n"
            "try:\n"
            "    sts2vec.VecEnv(2, pool=None)\n"
            "    print('CREATED')\n"
            "except sts2vec.ServerGCError as e:\n"
            "    print('SERVERGC', e)\n")
    r = _run_py(code, {"DOTNET_gcServer": "0"})
    assert r.returncode == 0, r.stderr
    assert r.stdout.startswith("SERVERGC"), r.stdout + r.stderr
    assert "Server GC is off" in r.stdout and "DOTNET_gcServer=0" in r.stdout


def test_hostfxr_failure_is_a_loader_error_with_text(lib, tmp_path):
    if lib.mode != "hostfxr":
        pytest.skip("hostfxr path only")
    build = tmp_path / "build"
    shutil.copytree(lib.path.parent, build)
    for name in ("sts2vec.runtimeconfig.json", "Sts2Sim.Vec.runtimeconfig.json"):
        p = build / name
        if p.exists():
            cfg = json.loads(p.read_text())
            cfg["runtimeOptions"]["framework"] = {"name": "Microsoft.NETCore.App", "version": "99.0.0"}
            cfg["runtimeOptions"].pop("frameworks", None)
            cfg["runtimeOptions"].pop("rollForward", None)
            p.write_text(json.dumps(cfg))
    code = ("import sts2vec\n"
            "try:\n"
            "    sts2vec.load()\n"
            "    print('LOADED')\n"
            "except sts2vec.LoaderError as e:\n"
            "    print('LOADERERROR', e)\n")
    r = _run_py(code, {"STS2VEC_DLL": str(build)})
    assert r.returncode == 0, r.stderr
    assert r.stdout.startswith("LOADERERROR"), r.stdout + r.stderr
    assert "hostfxr_initialize_for_runtime_config" in r.stdout
    assert "0x8000" in r.stdout


def test_invalid_handle_is_minus_one_with_text(lib):
    assert lib.sts2vec_destroy(987654321) == -1
    assert "invalid handle" in lib.last_error(0)
    assert lib.sts2vec_pool_count(987654321) == -1
    assert lib.sts2vec_last_error(987654321, -1, None, 0) == -1


def test_invalid_slot_last_error(small_env, lib):
    assert lib.sts2vec_last_error(small_env.handle, 99, None, 0) == -1
    assert "slot 99" in lib.last_error(0)
    assert small_env.last_error(0) == ""


def test_bad_config_is_rejected(lib):
    cfg = b'{"num_envs": 2, "nope": 1}'
    assert lib.sts2vec_create(cfg, len(cfg)) == -1
    assert "unknown config key" in lib.last_error(0)
    with pytest.raises(Sts2VecError, match="content"):
        VecEnv(2, content="everything", pool=None)
    with pytest.raises(Sts2VecError, match="num_envs"):
        VecEnv(0, pool=None)
    with pytest.raises(Sts2VecError, match="threads"):
        VecEnv(2, threads=0, pool=None)


def test_managed_names():
    assert managed_name("sts2vec_pool_add_fresh") == "PoolAddFresh"
    assert managed_name("sts2vec_schema") == "Schema"
    assert managed_name("sts2vec_state_hash") == "StateHash"
    assert [managed_name(e) for e in EXPECTED_EXPORTS] == [
        "Version", "Schema", "Create", "Destroy", "SetThreads", "PoolAddFresh", "PoolAddSave", "PoolCount", "Snapshot",
        "Restore", "Reset", "Step", "StateHash", "LastError", "PoolWarm", "CloneDeterminized", "AbiVersion", "Branch",
        "BranchMany", "RestoreMany", "PoolRelease"]


def test_find_library_discovery(monkeypatch, tmp_path):
    monkeypatch.delenv("STS2VEC_DLL", raising=False)
    monkeypatch.delenv("STS2VEC_AOT_LIB", raising=False)
    assert find_library() == default_dll()
    # STS2VEC_DLL may name the build directory
    monkeypatch.setenv("STS2VEC_DLL", str(default_dll().parent))
    assert find_library() == default_dll().resolve()
    # STS2VEC_AOT_LIB wins over STS2VEC_DLL and selects the native path (file or publish directory)
    aot = tmp_path / "Sts2Sim.Vec.dylib"
    aot.write_bytes(b"")
    monkeypatch.setenv("STS2VEC_AOT_LIB", str(aot))
    assert find_library() == aot.resolve() and is_native(find_library())
    monkeypatch.setenv("STS2VEC_AOT_LIB", str(tmp_path))
    assert find_library() == aot.resolve()
    # an explicit argument wins over both
    assert find_library(default_dll()) == default_dll().resolve()
    monkeypatch.setenv("STS2VEC_AOT_LIB", str(tmp_path / "missing.dylib"))
    with pytest.raises(LoaderError, match="STS2VEC_AOT_LIB"):
        find_library()


def test_find_hostfxr():
    fxr, root = find_hostfxr()
    assert fxr.name.startswith(("libhostfxr", "hostfxr")) and fxr.exists()
    assert any(d.name.startswith("9.") for d in (root / "shared" / "Microsoft.NETCore.App").iterdir())


def test_native_loader_rejects_a_non_library(tmp_path):
    from sts2vec.loader import _load_native
    bogus = tmp_path / "Sts2Sim.Vec.dylib"
    bogus.write_bytes(b"\0" * 16)
    with pytest.raises(LoaderError, match="cannot load NativeAOT library"):
        _load_native(bogus)


class _FakeFunction:
    """Stands in for a ctypes function of a fake native library (accepts restype/argtypes)."""

    def __call__(self, *args):
        return 0


def test_optional_exports_tolerate_an_older_native_build(monkeypatch, tmp_path):
    # a NativeAOT build published before the additive exports loads; the missing ones are None and raise when needed
    from types import SimpleNamespace
    assert OPTIONAL_EXPORTS == {"sts2vec_pool_warm", "sts2vec_clone_determinized", *ABI2_EXPORTS}
    assert OPTIONAL_EXPORTS <= set(EXPORTS)
    old = {name: _FakeFunction() for name in EXPORTS if name not in OPTIONAL_EXPORTS}
    monkeypatch.setattr(loader_module.ctypes, "CDLL", lambda path: SimpleNamespace(**old))
    lib = loader_module._load_native(tmp_path / "Sts2Sim.Vec.dylib")
    assert lib.missing_exports == ["sts2vec_pool_warm", "sts2vec_clone_determinized", *ABI2_EXPORTS]
    assert lib.sts2vec_pool_warm is None and lib.sts2vec_clone_determinized is None and lib.sts2vec_branch is None
    assert lib.exports == [n for n in EXPECTED_EXPORTS if n not in OPTIONAL_EXPORTS]
    with pytest.raises(Sts2VecError, match="sts2vec_pool_warm: not exported .* rebuild"):
        lib.require("sts2vec_pool_warm")
    # an ABI 1 build (no sts2vec_abi_version) reports the search exports as not supported, cleanly
    assert lib.abi_version == 1
    assert not (lib.supports_branch or lib.supports_restore_many or lib.supports_pool_release)
    for name in ("sts2vec_branch", "sts2vec_branch_many", "sts2vec_restore_many", "sts2vec_pool_release"):
        with pytest.raises(Sts2VecError, match=f"{name}: not exported .*ABI 1.* not supported; rebuild"):
            lib.require(name)
    # a build of the E2c/determinized generation (pool_warm + clone_determinized, still ABI 1)
    mid = {**old, "sts2vec_pool_warm": _FakeFunction(), "sts2vec_clone_determinized": _FakeFunction()}
    monkeypatch.setattr(loader_module.ctypes, "CDLL", lambda path: SimpleNamespace(**mid))
    lib = loader_module._load_native(tmp_path / "Sts2Sim.Vec.dylib")
    assert lib.missing_exports == ABI2_EXPORTS and lib.abi_version == 1 and not lib.supports_branch
    # a required export is still mandatory
    broken = {name: fn for name, fn in old.items() if name != "sts2vec_step"}
    monkeypatch.setattr(loader_module.ctypes, "CDLL", lambda path: SimpleNamespace(**broken))
    with pytest.raises(LoaderError, match="does not export sts2vec_step"):
        loader_module._load_native(tmp_path / "Sts2Sim.Vec.dylib")


AOT_LIB = Path(__file__).resolve().parents[3] / "Sts2Sim.Vec" / "bin" / "aot" / ("Sts2Sim.Vec.dylib" if sys.platform == "darwin" else "Sts2Sim.Vec.so")


# Defined only when a NativeAOT build exists (scripts/vec_publish_aot.sh), so the default run has no skip for it.
if AOT_LIB.exists():
    def test_native_aot_library_in_subprocess():
        _native_aot_roundtrip()


def _native_aot_roundtrip():
    code = ("import sts2vec, numpy as np\n"
            "lib = sts2vec.load()\n"
            "assert lib.mode == 'native', lib.mode\n"
            "with sts2vec.VecEnv(4, threads=2, pool=sts2vec.PoolSpec(characters=('IRONCLAD',), count=2)) as env:\n"
            "    env.reset(); env.step(np.full(4, -1, np.int32))\n"
            "    print('OK', lib.version(), int(env.state_hash()[0] != 0))\n")
    r = _run_py(code, {"STS2VEC_AOT_LIB": str(AOT_LIB)}, timeout=300)
    assert r.returncode == 0, r.stdout + r.stderr
    assert r.stdout.startswith("OK sts2vec/") and r.stdout.strip().endswith(" 1"), r.stdout
