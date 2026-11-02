"""Exception types of the sts2vec package."""

from __future__ import annotations


class Sts2VecError(RuntimeError):
    """A sts2vec export returned an error code; the message is the export's last_error text."""


class LoaderError(Sts2VecError):
    """The library, the runtime config or libhostfxr could not be found or initialised."""


class ServerGCError(Sts2VecError):
    """sts2vec_create refused to run because the CLR is not on Server GC (GCSettings.IsServerGC == false).

    Host the library with the shipped ``sts2vec.runtimeconfig.json`` (``System.GC.Server = true``), do not set
    ``DOTNET_gcServer=0``, and do not load another CLR first in the same process (pythonnet / clr_loader with a different
    runtime config). ``STS2VEC_ALLOW_WORKSTATION_GC=1`` disables the check (debugging only: ~5x lower batched throughput).
    """


# substring of the sts2vec_create error text when Server GC is off (Exports.Create)
SERVER_GC_MARKER = "Server GC is off"
