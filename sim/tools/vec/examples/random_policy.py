"""Uniform random over the legal candidates, written against the raw VecEnv API (no evaluator).

    uv run --project sim/tools/vec python sim/tools/vec/examples/random_policy.py

Runs 256 auto-resetting games for 2,000 batched steps and prints the finished episodes and their mean floor.
The same policy as an evaluator plug-in is the built-in `--policy random` (sts2vec/policies.py: random_policy).
"""

import numpy as np

from sts2vec import STATUS, PoolSpec, VecEnv, random_actions

with VecEnv(256, threads=8, pool=PoolSpec(count=64, ascension=0)) as env:
    env.pool.warm()
    obs = env.reset()
    rng = np.random.default_rng(0)
    floor_col = env.schema.col("last_episode", "floor")
    floors, wins = [], 0
    for _ in range(2000):
        obs, reward, terminated, truncated, status, info = env.step(random_actions(rng, obs["cand_n"]))
        assert not (status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])).any(), env.slot_errors()
        floors += [int(info["last_episode"][i, floor_col]) for i in np.flatnonzero(terminated | truncated)]
        wins += int(np.count_nonzero(reward > 0))
    print(f"{len(floors)} episodes finished, mean floor {np.mean(floors):.2f}, wins {wins}")
