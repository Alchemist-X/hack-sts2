"""Price table (prices.json) and cost accounting from API usage."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

PRICES_PATH = Path(__file__).with_name("prices.json")

USAGE_KEYS = ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens")


def load_prices(path: str | Path | None = None) -> dict:
    with open(path or PRICES_PATH, encoding="utf-8") as f:
        return json.load(f)


def model_prices(prices: dict, model: str) -> dict:
    try:
        return prices["models"][model]
    except KeyError:
        known = ", ".join(sorted(prices.get("models", {})))
        raise ValueError(f"no prices for model {model!r} in prices.json (known: {known})") from None


def usage_dict(usage) -> dict:
    """Normalise an SDK Usage object (or dict, or None) into the four counted fields (ints, 0 when absent)."""
    out = {}
    for k in USAGE_KEYS:
        v = usage.get(k) if isinstance(usage, dict) else getattr(usage, k, None)
        out[k] = int(v or 0)
    return out


def cost_usd(usage: dict, mp: dict | None) -> float:
    """Cost of one call: uncached input, cache writes, cache reads and output, each at its own rate."""
    if not mp:
        return 0.0
    u = usage_dict(usage)
    return (u["input_tokens"] * mp["input"]
            + u["cache_creation_input_tokens"] * mp["cache_write"]
            + u["cache_read_input_tokens"] * mp["cache_read"]
            + u["output_tokens"] * mp["output"]) / 1_000_000


def add_usage(total: dict, usage: dict) -> dict:
    u = usage_dict(usage)
    for k in USAGE_KEYS:
        total[k] = total.get(k, 0) + u[k]
    return total


@dataclass
class CostTracker:
    """Run-wide spend; shared by every game so the budget guard sees the whole run."""
    max_cost_usd: float | None = None
    spent_usd: float = 0.0
    calls: int = 0
    usage: dict = field(default_factory=lambda: {k: 0 for k in USAGE_KEYS})

    def add(self, usage: dict, cost: float) -> None:
        self.spent_usd += cost
        self.calls += 1
        add_usage(self.usage, usage)

    def exhausted(self) -> bool:
        return self.max_cost_usd is not None and self.spent_usd >= self.max_cost_usd
