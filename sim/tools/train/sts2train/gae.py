"""PPO advantages for stream rollouts with external time limits."""

import numpy as np


def time_limit_advantages(rewards, values, last_values, done, valid, final_values, gamma, lam):
    """Use same-episode final values only for valid external cutoffs.

    final_values is NaN except where the collector captured a finite step-cap
    critic value. Episode ends always cut the trace, even when they bootstrap.
    Invalid transitions produce no target and cut the trace.
    """
    done = np.asarray(done, dtype=bool)  # RolloutStore records terminal flags as float32.
    advantages = np.zeros_like(rewards)
    carry = np.zeros_like(last_values)
    for t in reversed(range(len(rewards))):
        next_value = last_values if t == len(rewards) - 1 else values[t + 1]
        trace = valid[t] & ~done[t]
        continuation = np.where(trace, next_value, 0.0)
        cutoff = valid[t] & done[t] & np.isfinite(final_values[t])
        continuation[cutoff] = final_values[t, cutoff]
        delta = rewards[t] + gamma * continuation - values[t]
        carry = np.where(valid[t], delta, 0.0) + gamma * lam * np.where(trace, carry, 0.0)
        advantages[t] = carry
    return advantages
