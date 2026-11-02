import asyncio
import json
import math

import pytest

from fakes import FakeAnthropic, FakeApiError, FakeSim, response
from sts2eval import prices as P
from sts2eval.policies import ClaudePolicy, thinking_param
from sts2eval.prompt import SYSTEM_PROMPT
from sts2eval.runner import RunLimits, RunState, play_game

SEEDSET = {"name": "t", "ascension": 0, "content": "ported_only",
           "games": [{"seed": "T1", "character": "IRONCLAD"}]}


def play(script, tmp_path, length=3, **policy_kw):
    fake = FakeAnthropic(script)
    tracker = P.CostTracker(max_cost_usd=policy_kw.pop("max_cost_usd", None))
    policy = ClaudePolicy(fake, tracker=tracker, **policy_kw)
    sim = FakeSim(length)
    state = RunState(tracker=tracker)
    s = asyncio.run(play_game(sim, policy, SEEDSET["games"][0], SEEDSET, tmp_path, RunLimits(), state, "fake"))
    return s, fake, sim, state


def records(tmp_path):
    lines = (tmp_path / "games" / "IRONCLAD-T1.jsonl").read_text().splitlines()
    return [json.loads(x) for x in lines]


def test_request_shape(tmp_path):
    s, fake, sim, _ = play([response("end\nnothing to do")] * 3, tmp_path, effort="low")
    kw = fake.messages.calls[0]
    assert kw["model"] == "claude-sonnet-5-5"
    assert kw["max_tokens"] == 1024
    assert kw["output_config"] == {"effort": "low"}
    assert "thinking" not in kw  # adaptive = default = omitted
    for banned in ("temperature", "top_p", "top_k", "fallbacks"):
        assert banned not in kw
    assert kw["system"][-1]["cache_control"] == {"type": "ephemeral"}
    assert kw["system"][-1]["text"] == SYSTEM_PROMPT
    msgs = kw["messages"]
    assert len(msgs) == 1 and msgs[0]["role"] == "user"  # no assistant prefill
    assert "legal actions" in msgs[0]["content"]
    # history appears from the second step on
    assert "Recent history" in fake.messages.calls[1]["messages"][0]["content"]


def test_legal_replies(tmp_path):
    s, fake, sim, _ = play([response("play 0 -> 0\nhit"), response("PLAY 0->0"), response("end turn")], tmp_path)
    assert sim.tokens == ["play 0 -> 0", "play 0 -> 0", "end"]
    assert s["result"] == "loss" and s["steps"] == 3
    assert s["illegal"] == 0 and s["retries"] == 0 and s["fallbacks"] == 0 and s["api_errors"] == 0
    assert s["api_calls"] == 3
    assert s["tokens"]["cache_read_input_tokens"] == 3 * 1400
    mp = P.model_prices(P.load_prices(), "claude-sonnet-5-5")
    one = P.cost_usd({"input_tokens": 100, "cache_read_input_tokens": 1400, "output_tokens": 20}, mp)
    assert math.isclose(s["cost_usd"], round(3 * one, 6))
    assert s["replayable"] is True and s["history_tokens"] == sim.tokens and sim.deleted
    recs = records(tmp_path)
    assert recs[0]["type"] == "start" and recs[-1]["type"] == "end"
    step0 = recs[1]
    assert step0["token"] == "play 0 -> 0" and step0["attempts"][0]["raw"] == "play 0 -> 0\nhit"
    assert step0["attempts"][0]["usage"]["cache_read_input_tokens"] == 1400
    assert step0["obs_text"].startswith("== r1 Combat")


def test_illegal_then_legal_on_retry(tmp_path):
    s, fake, sim, _ = play([response("play 7\noops"), response("end"), response("end"), response("end")], tmp_path)
    assert sim.tokens[0] == "end"
    assert s["illegal"] == 0 and s["illegal_replies"] == 1 and s["retries"] == 1 and s["fallbacks"] == 0
    retry_msg = fake.messages.calls[1]["messages"][0]["content"]
    assert "Your previous reply was rejected" in retry_msg and "'play 7' is not a legal token" in retry_msg
    assert "Legal tokens: play 0 -> 0 | end" in retry_msg
    assert records(tmp_path)[1]["retries"] == 1


def test_illegal_twice_falls_back_random(tmp_path):
    s, fake, sim, _ = play([response("fly away"), response("still no"), response("end"), response("end")], tmp_path)
    assert s["illegal"] == 1 and s["illegal_replies"] == 2 and s["fallbacks"] == 1
    assert sim.tokens[0] in ("play 0 -> 0", "end")
    assert records(tmp_path)[1]["fallback"] == "illegal"
    assert s["replayable"] is True


def test_refusal_falls_back_without_reask(tmp_path):
    s, fake, sim, _ = play([response("", stop_reason="refusal", category="general_harms"),
                            response("end"), response("end")], tmp_path)
    assert s["refusals"] == 1 and s["fallbacks"] == 1 and s["retries"] == 0 and s["illegal"] == 0
    assert len(fake.messages.calls) == 3
    rec = records(tmp_path)[1]
    assert rec["fallback"] == "refusal" and rec["attempts"][0]["refusal_category"] == "general_harms"
    assert s["steps"] == 3 and s["result"] == "loss"


def test_max_tokens_stop(tmp_path):
    # cut off inside thinking (no text) -> re-ask; finished first line before the cut -> accepted
    s, fake, sim, _ = play([response("", stop_reason="max_tokens"), response("end"),
                            response("end\nbecause the enemy is atta", stop_reason="max_tokens"),
                            response("end")], tmp_path)
    assert s["max_tokens_stops"] == 2
    assert s["retries"] == 1 and s["illegal"] == 0 and s["fallbacks"] == 0
    assert sim.tokens == ["end", "end", "end"]


def test_api_exception_falls_back_and_game_continues(tmp_path):
    s, fake, sim, state = play([FakeApiError(529, "overloaded"), response("end"), response("end")], tmp_path)
    assert s["api_errors"] == 1 and s["fallbacks"] == 1 and s["retries"] == 0
    assert s["steps"] == 3 and s["result"] == "loss" and state.abort_reason is None
    rec = records(tmp_path)[1]
    assert rec["fallback"] == "api_error" and "overloaded" in rec["attempts"][0]["exception"]


def test_fatal_api_error_aborts(tmp_path):
    s, fake, sim, state = play([FakeApiError(401, "invalid x-api-key")], tmp_path)
    assert s["result"] == "aborted" and s["abort_reason"] == "api_fatal"
    assert state.abort_reason == "api_fatal"
    assert (tmp_path / "games" / "IRONCLAD-T1.summary.json").exists()


def test_budget_aborts(tmp_path):
    big = {"input_tokens": 1_000_000}  # $2 per call at Sonnet prices
    s, fake, sim, state = play([response("end", usage=big)] * 3, tmp_path, max_cost_usd=1.0)
    assert len(fake.messages.calls) == 1
    assert s["result"] == "aborted" and s["abort_reason"] == "budget" and state.abort_reason == "budget"
    assert s["steps"] == 1


def test_thinking_param_rules():
    assert thinking_param("claude-sonnet-5-5", "adaptive", None) is None
    assert thinking_param("claude-sonnet-5-5", "off", "low") == {"type": "between_tools"}
    assert thinking_param("claude-haiku-5-5", "off", "high") == {"type": "disabled"}
    for m in ("claude-opus-5-5", "claude-fable-5-1"):
        with pytest.raises(ValueError, match="cannot disable thinking"):
            thinking_param(m, "off", "low")
    with pytest.raises(ValueError):
        thinking_param("claude-sonnet-5-5", "off", "max")
    with pytest.raises(ValueError):
        ClaudePolicy(FakeAnthropic([]), model="claude-opus-5-5", thinking="off")
    with pytest.raises(ValueError):
        ClaudePolicy(FakeAnthropic([]), model="gpt-4")


def test_thinking_off_request(tmp_path):
    s, fake, sim, _ = play([response("end")] * 3, tmp_path, thinking="off", effort="low")
    assert fake.messages.calls[0]["thinking"] == {"type": "between_tools"}
