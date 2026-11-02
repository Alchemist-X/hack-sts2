"""sts2vec: batched numpy interface to the Sts2Sim rules simulator (CONTRACTS.md Part D)."""

from .env import PoolSpec, Sts2VecError, VecEnv
from .loader import Library, load
from .schema import STATUS, Schema

__all__ = ["Library", "PoolSpec", "STATUS", "Schema", "Sts2VecError", "VecEnv", "load"]
