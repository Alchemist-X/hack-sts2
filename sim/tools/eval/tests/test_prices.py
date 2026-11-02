import math
from types import SimpleNamespace

import pytest

from sts2eval import prices as P


def test_price_table_has_all_models():
    t = P.load_prices()
    for m in ("claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5", "claude-fable-5-1"):
        mp = P.model_prices(t, m)
        assert set(mp) >= {"input", "output", "cache_read", "cache_write"}
    s = P.model_prices(t, "claude-sonnet-5-5")
    assert (s["input"], s["output"], s["cache_read"], s["cache_write"]) == (2.0, 10.0, 0.20, 2.50)
    assert P.model_prices(t, "claude-opus-5-5")["cache_read"] == 0.20
    assert P.model_prices(t, "claude-fable-5-1")["cache_read"] == 0.25
    with pytest.raises(ValueError):
        P.model_prices(t, "claude-nope")


def test_cost_from_usage_dict():
    mp = {"input": 2.0, "output": 10.0, "cache_read": 0.2, "cache_write": 2.5}
    usage = {"input_tokens": 1000, "cache_creation_input_tokens": 1500, "cache_read_input_tokens": 3000,
             "output_tokens": 200}
    expect = (1000 * 2.0 + 1500 * 2.5 + 3000 * 0.2 + 200 * 10.0) / 1e6
    assert math.isclose(P.cost_usd(usage, mp), expect)
    assert math.isclose(expect, 0.00835)


def test_cost_from_sdk_like_object_and_missing_fields():
    mp = {"input": 4.0, "output": 20.0, "cache_read": 0.2, "cache_write": 5.0}
    u = SimpleNamespace(input_tokens=10, output_tokens=5, cache_creation_input_tokens=None,
                        cache_read_input_tokens=None)
    assert P.usage_dict(u) == {"input_tokens": 10, "cache_creation_input_tokens": 0,
                               "cache_read_input_tokens": 0, "output_tokens": 5}
    assert math.isclose(P.cost_usd(u, mp), (10 * 4 + 5 * 20) / 1e6)
    assert P.cost_usd({"input_tokens": 10}, None) == 0.0


def test_tracker_budget():
    t = P.CostTracker(max_cost_usd=0.01)
    assert not t.exhausted()
    t.add({"input_tokens": 5, "output_tokens": 1}, 0.006)
    assert not t.exhausted()
    t.add({"input_tokens": 5}, 0.005)
    assert t.exhausted()
    assert t.calls == 2 and t.usage["input_tokens"] == 10
    assert not P.CostTracker().exhausted()
