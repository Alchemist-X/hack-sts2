import asyncio
import json
import re

from fakes import FakeAnthropic, FakeRewardsSim, FakeSim, response
from sts2eval import prices as P
from sts2eval.policies import ClaudePolicy, RandomPolicy
from sts2eval.prompt import SYSTEM_PROMPT, build_user_message, loop_hint
from sts2eval.runner import LoopGuard, RunLimits, RunState, play_game

SEEDSET = {"name": "t", "ascension": 0, "content": "ported_only",
           "games": [{"seed": "T1", "character": "IRONCLAD"}]}
REWARDS_HINT = 'Skipping a card reward keeps the offer; use "proceed" to leave the rewards screen.'


def legal_from(kw) -> list[str]:
    msg = kw["messages"][0]["content"]
    block = msg.split("-- legal actions", 1)[1].split("==", 1)[0]
    return [m.group(1).strip() for m in re.finditer(r"^ (\S.*?)\s{2,}:", block, re.M)]


def looper(follow_hint: bool):
    """A fake model that re-opens the card reward and skips it forever (the Sonnet baseline loop);
    with follow_hint it answers `proceed` once the prompt carries the loop warning."""
    def answer(kw):
        if follow_hint and "== Loop warning" in kw["messages"][0]["content"]:
            return response("proceed\nleaving as warned")
        legal = legal_from(kw)
        return response(("reward 0" if "reward 0" in legal else "skip") + "\nlook again")
    return answer


def play(sim, tmp_path, script=None, policy=None, **limits):
    tracker = P.CostTracker()
    fake = FakeAnthropic(script or [])
    policy = policy or ClaudePolicy(fake, tracker=tracker)
    s = asyncio.run(play_game(sim, policy, SEEDSET["games"][0], SEEDSET, tmp_path, RunLimits(**limits),
                              RunState(tracker=tracker), "fake"))
    return s, fake


def records(tmp_path):
    return [json.loads(x) for x in (tmp_path / "games" / "IRONCLAD-T1.jsonl").read_text().splitlines()]


def test_loop_guard_hints_once_per_floor():
    g = LoopGuard(repeat=3, floor_step_cap=0)
    out = [g.observe(ph, 5, txt) for ph, txt in [("Rewards", "A"), ("CardReward", "B")] * 4]
    hints = [h for h, _ in out if h]
    assert len(hints) == 1 and g.hints == 1
    assert out[4][0] == hints[0]  # the third time A comes up
    assert REWARDS_HINT in hints[0] and hints[0].startswith("The last 3 observations were identical")
    assert not any(stuck for _, stuck in out)
    # a new floor starts a new count: the same texts hint again there, once
    again = [g.observe("Rewards", 6, "A")[0] for _ in range(5)]
    assert sum(1 for h in again if h) == 1 and again[2] and g.hints == 2


def test_loop_guard_distinct_observations_never_hint():
    g = LoopGuard(repeat=2, floor_step_cap=0)
    assert all(g.observe("Combat", 2, f"turn {i}")[0] is None for i in range(50))
    assert LoopGuard(repeat=0).observe("Rewards", 1, "A") == (None, False)


def test_loop_guard_floor_step_cap_counts_noncombat_steps_only():
    g = LoopGuard(repeat=0, floor_step_cap=4)
    assert [g.observe("Rewards", 3, str(i))[1] for i in range(4)] == [False] * 4
    assert all(not g.observe("Combat", 3, f"c{i}")[1] for i in range(20)), "combat steps are not budgeted"
    assert g.observe("Rewards", 3, "x") == (None, True)
    assert g.observe("Map", 4, "y") == (None, False), "a new floor has a fresh budget"
    assert LoopGuard(repeat=0, floor_step_cap=0).observe("Rewards", 3, "z") == (None, False)


def test_loop_hint_text():
    assert loop_hint(6, "CardReward") == ("The last 6 observations were identical: you are not making progress. "
                                          + REWARDS_HINT)
    other = loop_hint(4, "Shop")
    assert other.startswith("The last 4 observations were identical") and "proceed" not in other
    msg = build_user_message("== r1 Rewards\n-- legal actions\n proceed  : leave", ["proceed"],
                             {"log": [], "actions": [], "loop_hint": loop_hint(6, "Rewards")})
    assert "== Loop warning\n" + loop_hint(6, "Rewards") in msg
    assert "Loop warning" not in build_user_message("x", ["proceed"], {"log": ["a"], "actions": ["b"]})


def test_system_prompt_rewards_rule():
    flat = " ".join(SYSTEM_PROMPT.split())
    assert "`skip` only closes the card choice and keeps that card reward on the rewards screen" in flat
    assert "`skip` and then `proceed` on the rewards screen" in flat
    assert "A chain like reward -> skip -> reward -> skip is pointless" in flat


def test_rewards_loop_gets_one_hint_in_the_next_prompt(tmp_path):
    s, fake = play(FakeRewardsSim(), tmp_path, [looper(follow_hint=True)] * 100)
    calls = [kw["messages"][0]["content"] for kw in fake.messages.calls]
    warned = [i for i, m in enumerate(calls) if "== Loop warning" in m]
    # Rewards (R) and CardReward (C) alternate R C R C ...: the 6th R is step 10
    assert warned == [10], warned
    assert REWARDS_HINT in calls[10]
    assert s["loop_hints"] == 1 and s["result"] == "loss" and s["steps"] == 11
    assert s["stuck"] is None and s["truncated"] is False and s["reward"] == 0
    recs = [r for r in records(tmp_path) if r["type"] == "step"]
    assert [r["step"] for r in recs if r.get("loop_hint")] == [10]
    assert recs[10]["token"] == "proceed"


def test_floor_step_cap_ends_the_game_stuck(tmp_path):
    s, fake = play(FakeRewardsSim(), tmp_path, [looper(follow_hint=False)] * 100, floor_step_cap=20)
    assert s["result"] == "stuck" and s["cap_reason"] is None
    assert s["stuck"] == {"phase": "Rewards", "floor": 3, "noncombat_steps": 20}
    assert s["steps"] == 20 and len(fake.messages.calls) == 20, "no API call for the step over the budget"
    assert s["truncated"] is True and s["reward"] == 0
    assert s["loop_hints"] == 1, "the hint is given once, not on every repeat"
    assert s["replayable"] is True
    end = records(tmp_path)[-1]
    assert end["type"] == "end" and end["result"] == "stuck" and end["stuck"]["floor"] == 3


def test_combat_steps_are_not_capped_per_floor(tmp_path):
    s, _ = play(FakeSim(30), tmp_path, policy=RandomPolicy(), floor_step_cap=5, loop_repeat=0)
    assert s["result"] == "loss" and s["steps"] == 30 and s["loop_hints"] == 0


def test_reveal_moves_is_sent_on_create(tmp_path):
    sim = FakeSim(1)
    s, _ = play(sim, tmp_path, policy=RandomPolicy())
    assert sim.new_run_kwargs == {"reveal_moves": False} and s["reveal_moves"] is False
    sim = FakeSim(1)
    tracker = P.CostTracker()
    s = asyncio.run(play_game(sim, RandomPolicy(), SEEDSET["games"][0], SEEDSET, tmp_path, RunLimits(),
                              RunState(tracker=tracker), "fake", reveal_moves=True))
    assert sim.new_run_kwargs == {"reveal_moves": True} and s["reveal_moves"] is True
    assert records(tmp_path)[0]["reveal_moves"] is True
