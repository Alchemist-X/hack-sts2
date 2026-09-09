"""Public-information draw odds, not an oracle for the actual shuffled order."""
from math import comb


def draw_distribution(n: int, k: int, d: int) -> dict[int, float]:
    if not (0 <= k <= n and 0 <= d <= n):
        raise ValueError("Require 0 <= k <= n and 0 <= d <= n; handle known top cards and reshuffles separately")
    denominator = comb(n, d)
    return {x: comb(k, x) * comb(n-k, d-x) / denominator
            for x in range(max(0, d-(n-k)), min(k, d)+1)}


def at_least_one(n: int, k: int, d: int) -> float:
    return 1 - draw_distribution(n, k, d).get(0, 0.0)
