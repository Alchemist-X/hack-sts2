"""Load Sts2Sim.Vec in-process and expose its C ABI (CONTRACTS.md §D5) as ctypes functions.

Default path: JIT CoreCLR hosted through hostfxr with the shipped ``sts2vec.runtimeconfig.json`` (Server GC):
``hostfxr_initialize_for_runtime_config`` -> ``hostfxr_get_runtime_delegate(load_assembly_and_get_function_pointer)``
-> one ``[UnmanagedCallersOnly]`` function pointer per export (delegate type ``UNMANAGEDCALLERSONLY_METHOD`` = -1).
Optional path: a NativeAOT shared library (``.dylib``/``.so``) exporting the same symbols, loaded with ``ctypes.CDLL``
(``scripts/vec_publish_aot.sh`` builds one).

Discovery (first match wins):
- library: the ``dll=`` argument; else env ``STS2VEC_AOT_LIB`` (a NativeAOT ``.dylib``/``.so``); else env ``STS2VEC_DLL``
  (file or directory; a ``.dylib``/``.so`` file also selects the native path); else the Release build dir
  ``sim/Sts2Sim.Vec/bin/Release/net9.0/`` relative to this repository;
- runtime config: ``sts2vec.runtimeconfig.json`` next to the dll (fallback: the generated ``Sts2Sim.Vec.runtimeconfig.json``);
- libhostfxr: ``DOTNET_ROOT``, ``DOTNET_INSTALL_DIR``, then per platform — macOS: ``/opt/homebrew/opt/dotnet@9/libexec``
  (Homebrew: the real root is ``libexec``, not the ``dotnet`` binary's directory) and the usual install roots; Linux:
  ``$HOME/.dotnet`` (dotnet-install.sh), ``/usr/lib/dotnet``, ``/usr/share/dotnet``, ``/usr/lib64/dotnet``, ``/opt/dotnet``
  — then the real directory of ``dotnet`` on PATH and the roots reported by ``dotnet --list-runtimes``; the highest
  ``host/fxr/<version>`` of the first root that has a ``Microsoft.NETCore.App`` 9.x runtime.

Only one CLR can live in a process (hostfxr cannot re-initialise); the first successful load is cached and reused, and
asking for a different library afterwards raises ``LoaderError``. hostfxr's own error messages are captured and put into
the ``LoaderError`` text.

ABI additions come in two forms, both optional for older library builds: exports appended after the freeze
(``OPTIONAL_EXPORTS`` below) and ``config_json`` keys appended after it (``CONFIG_ADDITIONS``). A library rejects
unknown config keys, so the Python side only sends an appended key when the library's schema shows the feature
(``Schema.supports_floor_step_cap``: the ``EndKind`` enum lists ``FloorStall``); the version string
``sts2vec/<encoder_version>`` changes with every such addition because the schema does.

ABI version (``sts2vec_abi_version``, ``Library.abi_version``): 2 adds the search exports ``sts2vec_branch``,
``sts2vec_branch_many``, ``sts2vec_restore_many`` and ``sts2vec_pool_release`` (released pool entries); a build without
the export is ABI 1.
``Library.supports(feature)`` / ``supports_branch`` / ``supports_restore_many`` / ``supports_pool_release`` say whether the
loaded build has a feature (``ABI_FEATURES``); the wrappers that need one raise ``Sts2VecError`` ("not exported ...
rebuild") on an older build. The ABI version is independent of ``encoder_version`` (the observation schema).
"""

from __future__ import annotations

import ctypes
import os
import re
import shutil
import subprocess
import sys
import threading
from ctypes import POINTER, Structure, c_char_p, c_int32, c_int64, c_size_t, c_uint64, c_void_p
from pathlib import Path

from .errors import LoaderError, Sts2VecError

ASSEMBLY = "Sts2Sim.Vec"
EXPORTS_TYPE = "Sts2Sim.Vec.Exports, Sts2Sim.Vec"
RUNTIMECONFIG_NAMES = ("sts2vec.runtimeconfig.json", "Sts2Sim.Vec.runtimeconfig.json")
UNMANAGEDCALLERSONLY_METHOD = c_void_p(-1)
HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER = 5
NATIVE_SUFFIXES = (".dylib", ".so")
HOMEBREW_DOTNET_ROOT = "/opt/homebrew/opt/dotnet@9/libexec"

# export name -> (restype, argtypes), in §D5 order (the scaffold's frozen set, then exports added later). Pointers are
# passed as integers (numpy ``.ctypes.data``) or None; strings as ``bytes`` (c_char_p). CFUNCTYPE / CDLL functions release
# the GIL for the duration of the call.
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
    "sts2vec_pool_warm": (c_int32, [c_int64, c_int32]),  # additive (E2c)
    "sts2vec_clone_determinized": (c_int32, [c_int64, c_int32, c_uint64]),  # additive (determinized clones)
    "sts2vec_abi_version": (c_int32, []),  # additive (ABI 2)
    # (h, src_slot, dst_slots*, n, mode, seed, buffers) -> 0 / -1
    "sts2vec_branch": (c_int32, [c_int64, c_int32, c_void_p, c_int32, c_int32, c_uint64, c_void_p]),  # additive (ABI 2)
    # (h, src_slots*, dst_slots*, n, mode, seed, buffers) -> 0 / -1
    "sts2vec_branch_many": (c_int32, [c_int64, c_void_p, c_void_p, c_int32, c_int32, c_uint64, c_void_p]),  # additive (ABI 2)
    # (h, slots*, entries*, n, buffers) -> 0 / -1
    "sts2vec_restore_many": (c_int32, [c_int64, c_void_p, c_void_p, c_int32, c_void_p]),  # additive (ABI 2)
    # (h, entries*, n) -> n released / -1
    "sts2vec_pool_release": (c_int32, [c_int64, c_void_p, c_int32]),  # additive (ABI 2)
}

# Exports added after the scaffold froze the ABI. A library built before one of them still loads: its attribute is None,
# ``Library.missing_exports`` names it, and the Python wrapper that needs it raises Sts2VecError ("rebuild the library").
OPTIONAL_EXPORTS = frozenset({"sts2vec_pool_warm", "sts2vec_clone_determinized", "sts2vec_abi_version", "sts2vec_branch",
                              "sts2vec_branch_many", "sts2vec_restore_many", "sts2vec_pool_release"})

# The ABI version this package is written against (sts2vec_abi_version; a build without that export is ABI 1).
ABI_VERSION = 2

# feature -> (export, minimum ABI version): what Library.supports(feature) checks.
ABI_FEATURES: dict[str, tuple[str, int]] = {
    "branch": ("sts2vec_branch", 2),
    "branch_many": ("sts2vec_branch_many", 2),
    "restore_many": ("sts2vec_restore_many", 2),
    "pool_release": ("sts2vec_pool_release", 2),
}

# config_json keys appended after the freeze -> how the Python side detects that a library build accepts them.
# floor_step_cap (int, default 80, 0 = off): truncate after that many non-combat decisions on one floor (FLOOR_STALL).
CONFIG_ADDITIONS = {"floor_step_cap": "schema EndKind lists FloorStall (Schema.supports_floor_step_cap)"}

# hostfxr / hostpolicy status codes worth naming (src/native/corehost/error_codes.h)
HOSTFXR_CODES = {
    0x80008081: "InvalidArgFailure",
    0x80008082: "CoreHostLibLoadFailure",
    0x80008083: "CoreHostLibMissingFailure",
    0x80008084: "CoreHostEntryPointFailure",
    0x80008085: "CoreHostCurHostFindFailure",
    0x80008087: "CoreClrResolveFailure",
    0x80008088: "CoreClrBindFailure",
    0x80008089: "CoreClrInitFailure",
    0x8000808A: "CoreClrExeFailure",
    0x8000808B: "ResolverInitFailure",
    0x8000808C: "ResolverResolveFailure",
    0x8000808E: "LibHostInitFailure",
    0x80008092: "LibHostInvalidArgs",
    0x80008093: "InvalidConfigFile",
    0x80008094: "AppArgNotRunnable",
    0x80008096: "FrameworkMissingFailure",
    0x8000809B: "BundleExtractionFailure",
    0x800080A2: "HostApiUnsupportedVersion",
    0x800080A3: "HostInvalidState",
    0x800080A4: "HostPropertyNotFound",
    0x800080A5: "CoreHostIncompatibleConfig",
    0x800080A6: "HostApiUnsupportedScenario",
    0x80070002: "COR_E_FILENOTFOUND (assembly not found)",
    0x80131522: "COR_E_TYPELOAD (type not found)",
    0x80131513: "COR_E_MISSINGMETHOD (method not found / not [UnmanagedCallersOnly])",
}


def _code(rc: int) -> str:
    u = rc & 0xFFFFFFFF
    name = HOSTFXR_CODES.get(u)
    return f"0x{u:08x}" + (f" {name}" if name else "")


def managed_name(export: str) -> str:
    """'sts2vec_pool_add_fresh' -> 'PoolAddFresh' (the C# method behind the export)."""
    return "".join(p[:1].upper() + p[1:] for p in export[len("sts2vec_"):].split("_"))


def repo_sim_dir() -> Path:
    # sim/tools/vec/sts2vec/loader.py -> sim/
    return Path(__file__).resolve().parents[3]


def default_dll() -> Path:
    return repo_sim_dir() / "Sts2Sim.Vec" / "bin" / "Release" / "net9.0" / f"{ASSEMBLY}.dll"


def is_native(path: Path) -> bool:
    return path.suffix in NATIVE_SUFFIXES


def find_library(dll: str | os.PathLike | None = None) -> Path:
    """Resolve the library to load (see the module docstring for the order)."""
    if dll is not None:
        cand, source = dll, "dll argument"
    elif os.environ.get("STS2VEC_AOT_LIB"):
        cand, source = os.environ["STS2VEC_AOT_LIB"], "STS2VEC_AOT_LIB"
    elif os.environ.get("STS2VEC_DLL"):
        cand, source = os.environ["STS2VEC_DLL"], "STS2VEC_DLL"
    else:
        p = default_dll()
        if not p.exists():
            raise LoaderError(
                f"{p} not found: build it with 'dotnet build sim/Sts2Sim.sln -c Release', or set STS2VEC_DLL "
                "(managed dll) / STS2VEC_AOT_LIB (NativeAOT .dylib/.so)")
        return p
    p = Path(cand).expanduser()
    if p.is_dir():
        # a build dir holds Sts2Sim.Vec.dll; an AOT publish dir holds Sts2Sim.Vec.dylib / .so
        names = [f"{ASSEMBLY}.dll"] + [f"{ASSEMBLY}{s}" for s in NATIVE_SUFFIXES]
        if source == "STS2VEC_AOT_LIB":
            names = names[1:] + names[:1]
        p = next((p / n for n in names if (p / n).exists()), p / names[0])
    if not p.exists():
        raise LoaderError(f"sts2vec library not found at {p} (from {source})")
    return p.resolve()


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


def dotnet_roots() -> list[Path]:
    """Candidate dotnet roots in probing order (deduplicated)."""
    roots: list[Path] = []
    env = os.environ.get("DOTNET_ROOT")
    if env:
        roots.append(Path(env))
    if os.environ.get("DOTNET_INSTALL_DIR"):  # dotnet-install.sh --install-dir
        roots.append(Path(os.environ["DOTNET_INSTALL_DIR"]))
    if sys.platform == "darwin":
        roots += [Path(HOMEBREW_DOTNET_ROOT), Path("/opt/homebrew/opt/dotnet/libexec"),
                  Path("/usr/local/opt/dotnet@9/libexec"), Path("/usr/local/share/dotnet")]
    else:
        # dotnet-install.sh default ($HOME/.dotnet), distro packages, Microsoft packages, snap
        roots += [Path.home() / ".dotnet", Path("/usr/lib/dotnet"), Path("/usr/share/dotnet"), Path("/usr/lib64/dotnet"),
                  Path("/opt/dotnet"), Path("/snap/dotnet-sdk/current")]
    roots.append(Path.home() / ".dotnet")
    exe = shutil.which("dotnet")
    if exe:  # the root is the real directory of the dotnet host (resolves /usr/bin/dotnet -> /usr/lib/dotnet/dotnet)
        roots.append(Path(os.path.realpath(exe)).parent)
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
    """(libhostfxr path, dotnet root) for the first root that has a Microsoft.NETCore.App 9.x runtime."""
    tried = []
    for root in dotnet_roots():
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
    hint = f"Homebrew: {HOMEBREW_DOTNET_ROOT}" if sys.platform == "darwin" else "dotnet-install.sh: $HOME/.dotnet"
    raise LoaderError(f"libhostfxr with a .NET 9 runtime not found; set DOTNET_ROOT ({hint}). Tried: " + ", ".join(tried))


class _InitParams(Structure):
    _fields_ = [("size", c_size_t), ("host_path", c_char_p), ("dotnet_root", c_char_p)]


_LOAD_FN = ctypes.CFUNCTYPE(c_int32, c_char_p, c_char_p, c_char_p, c_void_p, c_void_p, POINTER(c_void_p))
_ERROR_WRITER = ctypes.CFUNCTYPE(None, c_char_p)


class Library:
    """The loaded exports: ``lib.sts2vec_step(h, actions_ptr, buffers_ptr)`` etc., plus string helpers.

    Attributes: ``path`` (dll or native library), ``mode`` ("hostfxr" or "native"), ``runtimeconfig``, ``hostfxr``,
    ``dotnet_root``, ``runtime_properties`` (the hostfxr context's properties we read: ``System.GC.Server`` …).
    """

    def __init__(self, path: Path, mode: str, functions: dict[str, object], *, runtimeconfig: Path | None = None,
                 hostfxr: Path | None = None, dotnet_root: Path | None = None,
                 runtime_properties: dict[str, str | None] | None = None, init_rc: int = 0):
        self.path = path
        self.mode = mode
        self.runtimeconfig = runtimeconfig
        self.hostfxr = hostfxr
        self.dotnet_root = dotnet_root
        self.runtime_properties = dict(runtime_properties or {})
        self.init_rc = init_rc
        for name, fn in functions.items():
            setattr(self, name, fn)
        self._functions = functions

    @property
    def exports(self) -> list[str]:
        """The exports this library provides (§D5 order)."""
        return [name for name, fn in self._functions.items() if fn is not None]

    @property
    def missing_exports(self) -> list[str]:
        """Optional exports (``OPTIONAL_EXPORTS``) this library lacks because it was built before they were added."""
        return [name for name, fn in self._functions.items() if fn is None]

    def require(self, export: str):
        """The ctypes function of ``export``; raises Sts2VecError when this (older) library build lacks it."""
        fn = self._functions.get(export)
        if fn is None:
            raise Sts2VecError(f"{export}: not exported by {self.path} (library ABI {self.abi_version}, built before the "
                               "export was added), so it is not supported; rebuild it (dotnet build sim/Sts2Sim.sln -c "
                               "Release, or scripts/vec_publish_aot.sh for NativeAOT)")
        return fn

    @property
    def abi_version(self) -> int:
        """``sts2vec_abi_version()`` of this build; 1 for a build that predates the export (its additive exports are then
        detected by presence, ``floor_step_cap`` by the schema)."""
        fn = self._functions.get("sts2vec_abi_version")
        return int(fn()) if fn is not None else 1

    def supports(self, feature: str) -> bool:
        """Whether this build implements ``feature`` (a key of ``ABI_FEATURES``: "branch", "branch_many", "restore_many",
        "pool_release"): its export is present and the build's ABI version is high enough."""
        try:
            export, version = ABI_FEATURES[feature]
        except KeyError:
            raise KeyError(f"unknown feature {feature!r} (known: {', '.join(ABI_FEATURES)})") from None
        return self._functions.get(export) is not None and self.abi_version >= version

    @property
    def supports_branch(self) -> bool:
        """``sts2vec_branch`` and ``sts2vec_branch_many`` (``VecEnv.branch``) are available (ABI >= 2)."""
        return self.supports("branch") and self.supports("branch_many")

    @property
    def supports_restore_many(self) -> bool:
        """``sts2vec_restore_many`` (``VecEnv.restore_many``) is available (ABI >= 2)."""
        return self.supports("restore_many")

    @property
    def supports_pool_release(self) -> bool:
        """``sts2vec_pool_release`` (``env.pool.release``) is available (ABI >= 2)."""
        return self.supports("pool_release")

    @property
    def gc_server(self) -> bool | None:
        """Configured Server GC (hostfxr: the runtime property; native: None = fixed at publish time).

        ``sts2vec_create`` asserts ``GCSettings.IsServerGC`` itself, so a created env proves Server GC either way."""
        v = self.runtime_properties.get("System.GC.Server")
        return None if v is None else v.strip().lower() == "true"

    @property
    def gc_mode(self) -> str:
        if self.mode == "native":
            return "server (NativeAOT ServerGarbageCollection; create asserts it)"
        g = self.gc_server
        return {True: "server", False: "workstation", None: "unknown"}[g] + " (runtimeconfig System.GC.Server; create asserts it)"

    @staticmethod
    def _read(fn, *args) -> str:
        n = fn(*args, None, 0)
        if n < 0:
            raise LoaderError(f"sts2vec string export {getattr(fn, '__name__', fn)} failed")
        buf = ctypes.create_string_buffer(max(n, 1))
        fn(*args, buf, n)
        return buf.raw[:n].decode("utf-8")

    def version(self) -> str:
        """``"sts2vec/<encoder_version>"``."""
        return self._read(self.sts2vec_version)

    def schema_json(self) -> str:
        return self._read(self.sts2vec_schema)

    def last_error(self, handle: int = 0, slot: int = -1) -> str:
        """Error text: ``handle=0`` global; ``slot=-1`` env-level; else the slot's last exception (full ToString)."""
        n = self.sts2vec_last_error(handle, slot, None, 0)
        if n < 0:
            # invalid handle / slot: the export stored why in the global error
            return self.last_error(0, -1) if handle != 0 else ""
        buf = ctypes.create_string_buffer(max(n, 1))
        self.sts2vec_last_error(handle, slot, buf, n)
        return buf.raw[:n].decode("utf-8", errors="replace")

    def __repr__(self) -> str:
        return f"<sts2vec.Library {self.mode} {self.path}>"


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
    get_prop = fxr.hostfxr_get_runtime_property_value
    get_prop.argtypes = [c_void_p, c_char_p, POINTER(c_char_p)]
    get_prop.restype = c_int32
    close = fxr.hostfxr_close
    close.argtypes = [c_void_p]
    close.restype = c_int32
    set_writer = fxr.hostfxr_set_error_writer
    set_writer.argtypes = [c_void_p]
    set_writer.restype = c_void_p

    messages: list[str] = []
    writer = _ERROR_WRITER(lambda m: messages.append(m.decode("utf-8", "replace") if m else ""))
    set_writer(ctypes.cast(writer, c_void_p))

    def fail(what: str, rc: int) -> LoaderError:
        detail = ("; hostfxr: " + " | ".join(x.strip() for x in messages if x.strip())) if messages else ""
        return LoaderError(f"{what} failed: {_code(rc)}{detail}")

    try:
        params = _InitParams(ctypes.sizeof(_InitParams), None, str(root).encode())
        handle = c_void_p()
        rc = init(str(rc_path).encode(), ctypes.byref(params), ctypes.byref(handle))
        # 0 Success, 1 Success_HostAlreadyInitialized, 2 Success_DifferentRuntimeProperties; negative = HRESULT failure.
        if rc < 0 or not handle:
            raise fail(f"hostfxr_initialize_for_runtime_config({rc_path}) [dotnet root {root}]", rc)
        props: dict[str, str | None] = {}
        try:
            if rc == 2:
                sys.stderr.write("sts2vec: a CLR with different runtime properties is already loaded in this process; "
                                 "Server GC may be off (sts2vec_create refuses if so)\n")
            for name in ("System.GC.Server", "System.GC.Concurrent", "System.Globalization.Invariant"):
                val = c_char_p()
                props[name] = val.value.decode() if get_prop(handle, name.encode(), ctypes.byref(val)) == 0 and val.value else None
            load_ptr = c_void_p()
            rc2 = get_delegate(handle, HDT_LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER, ctypes.byref(load_ptr))
            if rc2 < 0 or not load_ptr:
                raise fail("hostfxr_get_runtime_delegate(load_assembly_and_get_function_pointer)", rc2)
        finally:
            close(handle)
        load_fn = _LOAD_FN(load_ptr.value)

        functions = {}
        for export, (restype, argtypes) in EXPORTS.items():
            out = c_void_p()
            seen = len(messages)
            rc3 = load_fn(str(dll).encode(), EXPORTS_TYPE.encode(), managed_name(export).encode(),
                          UNMANAGEDCALLERSONLY_METHOD, None, ctypes.byref(out))
            if rc3 != 0 or not out:
                if export in OPTIONAL_EXPORTS:
                    functions[export] = None  # a build that predates the export (see OPTIONAL_EXPORTS)
                    del messages[seen:]
                    continue
                raise fail(f"load_assembly_and_get_function_pointer({dll.name}, {EXPORTS_TYPE}, {managed_name(export)})", rc3)
            fn = ctypes.CFUNCTYPE(restype, *argtypes)(out.value)
            fn.__name__ = export
            functions[export] = fn
    finally:
        set_writer(None)
    return Library(dll, "hostfxr", functions, runtimeconfig=rc_path, hostfxr=fxr_path, dotnet_root=root,
                   runtime_properties=props, init_rc=rc)


def _load_native(path: Path) -> Library:
    try:
        lib = ctypes.CDLL(str(path))
    except OSError as e:
        hint = (" (the macOS AOT build links Homebrew openssl/brotli: /opt/homebrew/lib must hold libssl.3 / "
                "libcrypto.3 / libbrotli*)") if sys.platform == "darwin" else ""
        raise LoaderError(f"cannot load NativeAOT library {path}: {e}{hint}") from e
    functions = {}
    for export, (restype, argtypes) in EXPORTS.items():
        try:
            fn = getattr(lib, export)
        except AttributeError as e:
            if export in OPTIONAL_EXPORTS:
                functions[export] = None  # published before the export was added (see OPTIONAL_EXPORTS)
                continue
            raise LoaderError(f"{path} does not export {export} (not a sts2vec NativeAOT build?)") from e
        fn.restype = restype
        fn.argtypes = argtypes
        functions[export] = fn
    return Library(path, "native", functions)


def loaded() -> Library | None:
    """The library loaded in this process, if any."""
    return _loaded


def load(dll: str | os.PathLike | None = None) -> Library:
    """Load (once per process) and return the library. A ``.dylib``/``.so`` path selects the NativeAOT build."""
    global _loaded
    with _lock:
        if _loaded is not None:
            if dll is not None and find_library(dll) != _loaded.path:
                raise LoaderError(f"sts2vec already loaded from {_loaded.path}; one runtime per process")
            return _loaded
        path = find_library(dll)
        _loaded = _load_native(path) if is_native(path) else _load_hostfxr(path)
        return _loaded
