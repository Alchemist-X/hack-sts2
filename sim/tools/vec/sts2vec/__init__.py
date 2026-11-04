"""sts2vec: batched numpy interface to the Sts2Sim rules simulator (CONTRACTS.md Part D).

    from sts2vec import VecEnv, PoolSpec, Status, random_actions
    with VecEnv(1024, threads=8, pool=PoolSpec(count=4096)) as env:
        obs = env.reset()
        rng = np.random.default_rng(0)
        obs, reward, terminated, truncated, status, info = env.step(random_actions(rng, obs["cand_n"]))
"""

from .env import FLOOR_STEP_CAP, VecEnv, random_actions, schema_of
from .errors import LoaderError, ServerGCError, Sts2VecError
from .loader import Library, load
from .pool import ALL_CHARACTERS, FreshEntry, PoolSpec, SaveEntry
from .schema import STATUS, Buffers, Schema, Status, status_counts, status_names

__all__ = [
    "ALL_CHARACTERS", "Buffers", "FLOOR_STEP_CAP", "FreshEntry", "Library", "LoaderError", "PoolSpec", "STATUS", "SaveEntry", "Schema",
    "ServerGCError", "Status", "Sts2VecError", "VecEnv", "load", "random_actions", "schema_of", "status_counts",
    "status_names",
]
