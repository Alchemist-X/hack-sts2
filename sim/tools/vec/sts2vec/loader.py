"""Load Sts2Sim.Vec in-process and expose its C ABI (CONTRACTS.md §D5) as ctypes functions.

Default path: JIT CoreCLR hosted through hostfxr with the shipped ``sts2vec.runtimeconfig.json`` (Server GC):
``hostfxr_initialize_for_runtime_config`` -> ``hostfxr_get_runtime_delegate(load_assembly_and_get_function_pointer)``
-> one ``[UnmanagedCallersOnly]`` function pointer per export (delegate type ``UNMANAGEDCALLERSONLY_METHOD`` = -1).
Optional path: a NativeAOT shared library (``.dylib``/``.so``) exporting the same symbols, loaded with ``ctypes.CDLL``.

Discovery:
- library: ``dll=`` argument, else env ``STS2VEC_DLL`` (file or directory), else the Release build dir
  ``sim/Sts2Sim.Vec/bin/Release/net9.0/`` relative to this repository;
- runtime config: ``sts2vec.runtimeconfig.json`` next to the dll (fallback ``Sts2Sim.Vec.runtimeconfig.json``);
- libhostfxr: ``DOTNET_ROOT``, ``/opt/homebrew/opt/dotnet@9/libexec``, the usual install roots, then the roots reported by
  ``dotnet --list-runtimes``; the highest ``host/fxr/<version>`` that has a ``Microsoft.NETCore.App`` 9.x runtime next to it.

Only one CLR can live in a process; the first successful load is cached and reused.
"""

from __future__ import annotations

import ctypes
import os
import re
import subprocess
import sys
import threading
from ctypes import POINTER, Structure, c_char_p, c_int32, c_int64, c_size_t, c_void_p
from pathlib import Path

ASSEMBLY = "Sts2Sim.Vec"
EXPORTS_TYPE = "Sts2Sim.Vec.Exports, Sts2Sim.Vec"
RUNTIMECONFIG_NAMES = ("sts2vec.runtimeconfig.json", "Sts2Sim.Vec.runtimeconfig.json")
UNMANAGEDCALLERSONLY_METHOD = c_void_p(-1)
HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER = 5

# export name -> (restype, argtypes); pointers are passed as integers (numpy .ctypes.data) or None.
EXPORTS: dict[str, tuple[type, list[type]]] = {
    "sts2vec_version": (c_int32, [c_void_p, c_int32]),
    "sts2vec_schema": (c_int32, [c_void_p, c_int32]),
    "sts2vec_create": (c_int64, [c_char_p, c_int32]),
    "sts2vec_destroy": (c_int32, [c_int64]),
    "sts2vec_set_threads": (c_int32, [c_int64, c_int32]),
    "sts2vec_pool_add_fresh": (c_int32, [c_int64, c_char_p, c_int32]),
    "sts2vec_pool_add_save": (c_int32, [c_int64, c_char_p, c_int32]),
    "sts2vec_pool_count": (c_int32, [c_int64]),
    "sts2vec_snapshot": (c_int32, [c_int64, c_int32]),
    "sts2vec_restore": (c_int32, [c_int64, c_int32, c_int32, c_void_p]),
    "sts2vec_reset": (c_int32, [c_int64, c_void_p, c_void_p, c_void_p]),
    "sts2vec_step": (c_int32, [c_int64, c_void_p, c_void_p]),
    "sts2vec_state_hash": (c_int32, [c_int64, c_void_p, c_int32, c_void_p]),
    "sts2vec_last_error": (c_int32, [c_int64, c_int32, c_void_p, c_int32]),
}


class LoaderError(RuntimeError):
    pass


def managed_name(export: str) -> str:
    """'sts2vec_pool_add_fresh' -> 'PoolAddFresh' (the C# method behind the export)."""
    return "".join(p[:1].upper() + p[1:] for p in export[len("sts2vec_"):].split("_"))


def repo_sim_dir() -> Path:
    # sim/tools/vec/sts2vec/loader.py -> sim/
    return Path(__file__).resolve().parents[3]


def find_library(dll: str | os.PathLike | None = None) -> Path:
    cand = dll or os.environ.get("STS2VEC_DLL")
    if cand:
        p = Path(cand).expanduser()
        if p.is_dir():
            p = p / f"{ASSEMBLY}.dll"
        if not p.exists():
            raise LoaderError(f"sts2vec library not found at {p}")
        return p.resolve()
    p = repo_sim_dir() / "Sts2Sim.Vec" / "bin" / "Release" / "net9.0" / f"{ASSEMBLY}.dll"
    if not p.exists():
        raise LoaderError(
            f"{p} not found: build it with 'dotnet build sim/Sts2Sim.sln -c Release' or set STS2VEC_DLL")
    return p


def find_runtimeconfig(dll: Path) -> Path:
    for name in RUNTIMECONFIG_NAMES:
        p = dll.parent / name
        if p.exists():
            return p
    raise LoaderError(f"no runtime config ({' / '.join(RUNTIMECONFIG_NAMES)}) next to {dll}")


def _hostfxr_name() -> str:
    if sys.platform == "darwin":
        return "libhostfxr.dylib"
    if sys.platform == "win32":
        return "hostfxr.dll"
    return "libhostfxr.so"


def _version_key(name: str) -> tuple:
    return tuple(int(x) if x.isdigit() else 0 for x in re.split(r"[.\-]", name))


def _dotnet_roots() -> list[Path]:
    roots: list[Path] = []
    env = os.environ.get("DOTNET_ROOT")
    if env:
        roots.append(Path(env))
    roots += [Path("/opt/homebrew/opt/dotnet@9/libexec"), Path("/opt/homebrew/opt/dotnet/libexec"),
              Path("/usr/local/share/dotnet"), Path("/usr/share/dotnet"), Path("/usr/lib/dotnet"),
              Path.home() / ".dotnet"]
    try:
        out = subprocess.run(["dotnet", "--list-runtimes"], capture_output=True, text=True, timeout=30).stdout
        for line in out.splitlines():
            m = re.match(r"Microsoft\.NETCore\.App \S+ \[(.+)\]", line.strip())
            if m:
                roots.append(Path(m.group(1)).parent.parent)  # <root>/shared/Microsoft.NETCore.App
    except (OSError, subprocess.SubprocessError):
        pass
    seen, uniq = set(), []
    for r in roots:
        key = str(r)
        if key not in seen:
            seen.add(key)
            uniq.append(r)
    return uniq


def find_hostfxr() -> tuple[Path, Path]:
    """(libhostfxr path, dotnet root) for a root that has a Microsoft.NETCore.App 9.x runtime."""
    tried = []
    for root in _dotnet_roots():
        fxr_dir = root / "host" / "fxr"
        shared = root / "shared" / "Microsoft.NETCore.App"
        tried.append(str(root))
        if not fxr_dir.is_dir() or not shared.is_dir():
            continue
        if not any(d.name.startswith("9.") for d in shared.iterdir()):
            continue
        versions = sorted((d for d in fxr_dir.iterdir() if (d / _hostfxr_name()).exists()),
                          key=lambda d: _version_key(d.name))
        if versions:
            return versions[-1] / _hostfxr_name(), root
    raise LoaderError("libhostfxr with a .NET 9 runtime not found; set DOTNET_ROOT. Tried: " + ", ".join(tried))


class _InitParams(Structure):
    _fields_ = [("size", c_size_t), ("host_path", c_char_p), ("dotnet_root", c_char_p)]


_LOAD_FN = ctypes.CFUNCTYPE(c_int32, c_char_p, c_char_p, c_char_p, c_void_p, c_void_p, POINTER(c_void_p))


class Library:
    """The loaded exports: ``lib.sts2vec_step(h, actions_ptr, buffers_ptr)`` etc., plus string helpers."""

    def __init__(self, path: Path, mode: str, functions: dict[str, object], runtimeconfig: Path | None = None,
                 hostfxr: Path | None = None):
        self.path = path
        self.mode = mode  # "hostfxr" or "native"
        self.runtimeconfig = runtimeconfig
        self.hostfxr = hostfxr
        for name, fn in functions.items():
            setattr(self, name, fn)
        self._functions = functions

    @property
    def exports(self) -> list[str]:
        return list(self._functions)

    @staticmethod
    def _read(fn, *args) -> str:
        n = fn(*args, None, 0)
        if n < 0:
            raise LoaderError("sts2vec string export failed")
        buf = ctypes.create_string_buffer(max(n, 1))
        fn(*args, buf, n)
        return buf.raw[:n].decode("utf-8")

    def version(self) -> str:
        return self._read(self.sts2vec_version)

    def schema_json(self) -> str:
        return self._read(self.sts2vec_schema)

    def last_error(self, handle: int = 0, slot: int = -1) -> str:
        n = self.sts2vec_last_error(handle, slot, None, 0)
        if n < 0:
            return self.last_error(0, -1) if handle != 0 else ""
        buf = ctypes.create_string_buffer(max(n, 1))
        self.sts2vec_last_error(handle, slot, buf, n)
        return buf.raw[:n].decode("utf-8", errors="replace")


_lock = threading.Lock()
_loaded: Library | None = None


def _load_hostfxr(dll: Path) -> Library:
    rc_path = find_runtimeconfig(dll)
    fxr_path, root = find_hostfxr()
    fxr = ctypes.CDLL(str(fxr_path))
    init = fxr.hostfxr_initialize_for_runtime_config
    init.argtypes = [c_char_p, POINTER(_InitParams), POINTER(c_void_p)]
    init.restype = c_int32
    get_delegate = fxr.hostfxr_get_runtime_delegate
    get_delegate.argtypes = [c_void_p, c_int32, POINTER(c_void_p)]
    get_delegate.restype = c_int32
    close = fxr.hostfxr_close
    close.argtypes = [c_void_p]
    close.restype = c_int32

    params = _InitParams(ctypes.sizeof(_InitParams), None, str(root).encode())
    handle = c_void_p()
    rc = init(str(rc_path).encode(), ctypes.byref(params), ctypes.byref(handle))
    # 0 Success, 1 Success_HostAlreadyInitialized, 2 Success_DifferentRuntimeProperties; negative = HRESULT failure.
    if rc < 0 or not handle:
        raise LoaderError(f"hostfxr_initialize_for_runtime_config({rc_path}) failed: 0x{rc & 0xFFFFFFFF:08x}")
    try:
        if rc == 2:
            sys.stderr.write("sts2vec: a CLR with different runtime properties is already loaded in this process; "
                             "Server GC may be off (sts2vec_create will refuse if so)\n")
        load_ptr = c_void_p()
        rc2 = get_delegate(handle, HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER, ctypes.byref(load_ptr))
        if rc2 < 0 or not load_ptr:
            raise LoaderError(f"hostfxr_get_runtime_delegate failed: 0x{rc2 & 0xFFFFFFFF:08x}")
    finally:
        close(handle)
    load_fn = _LOAD_FN(load_ptr.value)

    functions = {}
    for export, (restype, argtypes) in EXPORTS.items():
        out = c_void_p()
        rc3 = load_fn(str(dll).encode(), EXPORTS_TYPE.encode(), managed_name(export).encode(),
                      UNMANAGEDCALLERSONLY_METHOD, None, ctypes.byref(out))
        if rc3 != 0 or not out:
            raise LoaderError(f"load_assembly_and_get_function_pointer({managed_name(export)}) failed: 0x{rc3 & 0xFFFFFFFF:08x}")
        functions[export] = ctypes.CFUNCTYPE(restype, *argtypes)(out.value)  # CFUNCTYPE releases the GIL during the call
    return Library(dll, "hostfxr", functions, runtimeconfig=rc_path, hostfxr=fxr_path)


def _load_native(path: Path) -> Library:
    lib = ctypes.CDLL(str(path))
    functions = {}
    for export, (restype, argtypes) in EXPORTS.items():
        fn = getattr(lib, export)
        fn.restype = restype
        fn.argtypes = argtypes
        functions[export] = fn
    return Library(path, "native", functions)


def load(dll: str | os.PathLike | None = None) -> Library:
    """Load (once per process) and return the library. A ``.dylib``/``.so`` path selects the NativeAOT build."""
    global _loaded
    with _lock:
        if _loaded is not None:
            if dll is not None and find_library(dll) != _loaded.path:
                raise LoaderError(f"sts2vec already loaded from {_loaded.path}; one CLR per process")
            return _loaded
        path = find_library(dll)
        if path.suffix in (".dylib", ".so"):
            _loaded = _load_native(path)
        else:
            _loaded = _load_hostfxr(path)
        return _loaded
