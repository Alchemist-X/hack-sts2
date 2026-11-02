import asyncio
import copy
import hashlib
import json
import shutil

import pytest

from fakes import FakeAnthropic, FakeSim, response
from sts2eval import prices as P
from sts2eval.compact import (COMPACT_SYSTEM_PROMPT, CompactView, compact_prompt_hash, legal_lines,
                              parity_problems, render_step)
from sts2eval.policies import ClaudePolicy
from sts2eval.prompt import SYSTEM_PROMPT, build_user_message, prompt_hash
from sts2eval.runner import RunLimits, RunState, play_game
from sts2eval.server import server_dll

SEEDSET = {"name": "t", "ascension": 0, "content": "ported_only",
           "games": [{"seed": "T1", "character": "IRONCLAD"}]}


# --- --obs-mode full stays byte-identical to protocol v0.2 -------------------------------------------

def test_full_mode_prompt_builder_unchanged():
    # pinned on the v0.2 builder (baseline-sonnet-std-a0-v1 ran with prompt_hash e01daedf52519a6c)
    assert prompt_hash() == "e01daedf52519a6c"
    msg = build_user_message("== r1 Combat | act 1 floor 2\n-- legal actions\n play 0 -> 0  : strike\n end  : end the turn\n",
                             ["play 0 -> 0", "end", "potion 1"],
                             {"log": ["a: ok", "b"], "actions": ["floor 1 Map: travel 0"], "loop_hint": "loop!"},
                             feedback="'x' is not a legal token")
    assert hashlib.sha256(msg.encode()).hexdigest()[:16] == "17fc7f869a916b48"
    pol = ClaudePolicy(FakeAnthropic([]))
    kw = pol.request_kwargs("u")
    assert kw["system"] == [{"type": "text", "text": SYSTEM_PROMPT, "cache_control": {"type": "ephemeral"}}]
    assert pol.request_kwargs("u", None) == kw


def test_full_mode_runner_sends_server_text(tmp_path):
    fake = FakeAnthropic([response("end")] * 3)
    sim = FakeSim(3)
    s = asyncio.run(play_game(sim, ClaudePolicy(fake), SEEDSET["games"][0], SEEDSET, tmp_path, RunLimits(),
                              RunState(tracker=P.CostTracker()), "fake"))
    kw = fake.messages.calls[0]
    assert len(kw["system"]) == 1 and kw["system"][0]["text"] == SYSTEM_PROMPT
    assert kw["messages"][0]["content"].startswith("== r1 Combat | act 1 floor 2 | turn 1\n-- legal actions\n play 0 -> 0\n end")
    assert s["obs_mode"] == "full" and not hasattr(sim, "piles_calls")
    rec = json.loads((tmp_path / "games" / "IRONCLAD-T1.jsonl").read_text().splitlines()[1])
    assert "prompt_text" not in rec and "context" not in rec


# --- compact prompt -----------------------------------------------------------------------------------

def test_compact_static_prompt():
    assert "## Card keywords (game text)" in COMPACT_SYSTEM_PROMPT  # the glossary stays in the static part
    assert "## Reading the observation" in COMPACT_SYSTEM_PROMPT and "run context" in COMPACT_SYSTEM_PROMPT
    assert compact_prompt_hash() != prompt_hash()
    # long enough to cache on its own (512-token minimum of the 5.x models; ~4 chars per token of prose)
    assert len(COMPACT_SYSTEM_PROMPT) > 4 * 1024


def card(index, cid, text, upgraded=False, cost=1, ctype="Attack", target="AnyEnemy", vars_=None, **kw):
    node = {"index": index, "id": cid, "name": cid.title(), "upgraded": upgraded, "cost": cost, "base_cost": None,
            "type": ctype, "target": target, "rarity": "Basic", "keywords": [], "vars": vars_ or {"damage": 6},
            "effective": None, "text": text, "enchantments": [], "affliction": None, "dampened": False}
    node.update(kw)
    return node


def combat_obs():
    hand = [card(0, "STRIKE_IRONCLAD", "Deal 6 damage.", playable=True, reason=None),
            card(1, "SHIV", "Deal 4 damage.", cost=0, vars_={"damage": 4}, playable=True, reason=None)]
    return {
        "game_id": "r1", "kind": "run", "phase": "Combat", "terminal": False, "result": None, "act": 1, "floor": 2,
        "turn": 1,
        "player": {"character": "IRONCLAD", "hp": 80, "max_hp": 80, "block": 0, "energy": 3, "max_energy": 3,
                   "gold": 99, "powers": [], "facing": None,
                   "relics": [{"id": "BURNING_BLOOD", "name": "Burning Blood", "text": "Heal 6 HP."},
                              {"id": "PEN_NIB", "name": "Pen Nib", "text": "Every 10th Attack deals double.", "counter": 3}],
                   "potions": [None, {"slot": 1, "id": "FIRE_POTION", "name": "Fire Potion", "text": "Deal 20 damage."}],
                   "deck_size": 2, "deck": ["STRIKE_IRONCLAD", "STRIKE_IRONCLAD"]},
        "combat": {"hand": hand, "draw_count": 1, "discard_count": 0, "exhaust_count": 0, "draw_pile": [],
                   "discard_pile": [],
                   "enemies": [{"index": 0, "uid": "u1", "id": "NIBBIT", "name": "Nibbit", "hp": 44, "max_hp": 44,
                                "block": 0, "flank": None, "behind": None,
                                "intents": [{"type": "Attack", "move": None, "move_name": None, "damage": 12, "times": 1}],
                                "powers": []}],
                   "danger": {"incoming": 12, "hits": 1, "by_enemy": [{"uid": "u1", "id": "NIBBIT", "damage": 12, "times": 1}],
                              "block": 0, "net_hp_loss": 12, "hp_after": 68, "timers": []}},
        "map": None, "rewards": None, "card_reward": None, "shop": None, "rest": None, "treasure": None,
        "event": None, "prompt": None,
        "legal_actions": [{"token": "play 0 -> 0", "desc": "play STRIKE_IRONCLAD [1e] (6 dmg) on enemy 0 NIBBIT (u1)"},
                          {"token": "play 1 -> 0", "desc": "play SHIV [0e] (4 dmg) on enemy 0 NIBBIT (u1)"},
                          {"token": "potion 1 -> 0", "desc": "use potion FIRE_POTION on enemy 0 NIBBIT (u1)"},
                          {"token": "discard 1", "desc": "discard potion FIRE_POTION"},
                          {"token": "end", "desc": "end the turn"}],
        "text": "",
    }


PILES = {"deck": [card(0, "STRIKE_IRONCLAD", "Deal 6 damage."), card(1, "STRIKE_IRONCLAD", "Deal 6 damage.")]}


def test_context_byte_identical_while_unchanged():
    view = CompactView(0)
    a = combat_obs()
    assert view.needs_piles(a)
    view.set_piles(a, PILES)
    ctx1, step1, first = view.render(a)
    assert first == 0 and len(ctx1) == 3
    assert "STRIKE_IRONCLAD x2 (Strike_Ironclad) 1e Attack — Deal 6 damage." in ctx1[1]
    # same deck, relics, potions, act: hp, energy, hand, enemy and the relic counter change
    b = copy.deepcopy(a)
    b["player"]["hp"], b["player"]["energy"] = 70, 1
    b["player"]["relics"][1]["counter"] = 4
    b["combat"]["enemies"][0]["hp"] = 30
    b["combat"]["hand"] = b["combat"]["hand"][:1]
    assert not view.needs_piles(b)  # in combat, same deck list: no /piles call
    ctx2, step2, first = view.render(b)
    assert first is None and ctx2 == ctx1 and all(x is y for x, y in zip(ctx1, ctx2))
    assert "relic counters: PEN_NIB(4)" in step2 and "PEN_NIB(4)" not in "".join(ctx2)
    # a potion is used: only the last (act, potions) block changes
    c = copy.deepcopy(b)
    c["player"]["potions"][1] = None
    ctx3, _, first = view.render(c)
    assert first == 2 and ctx3[0] is ctx1[0] and ctx3[1] is ctx1[1] and ctx3[2] != ctx1[2]
    # a card is added: the deck block changes, the relic block is kept
    d = copy.deepcopy(c)
    d["player"]["deck"].append("BASH")
    d["player"]["deck_size"] = 3
    assert view.needs_piles(d)
    view.set_piles(d, {"deck": PILES["deck"] + [card(2, "BASH", "Deal 8 damage. Apply 2 Vulnerable.", cost=2)]})
    ctx4, _, first = view.render(d)
    assert first == 1 and ctx4[0] is ctx1[0] and "BASH (Bash) 2e Attack" in ctx4[1]
    assert view.rebuilds == 3 and view.block_rebuilds == [1, 2, 2]


def test_step_text_dedupes_hand_text_and_groups_tokens():
    view = CompactView(0)
    a = combat_obs()
    view.set_piles(a, PILES)
    _, step, _ = view.render(a)
    assert " [0] STRIKE_IRONCLAD 1e Attack->AnyEnemy (6 dmg)\n" in step       # text is on the deck line
    assert " [1] SHIV (Shiv) 0e Attack->AnyEnemy (4 dmg) — Deal 4 damage.\n" in step  # not in the deck
    assert step.startswith("== Combat | act 1 floor 2 | turn 1 | HP 80/80 block 0 energy 3/3 gold 99\n")
    assert "incoming: 12 dmg in 1 hit (u1 12x1), block 0, net -12 HP -> 68/80\n" in step
    assert step.endswith("-- legal actions\n play 0 -> 0\n play 1 -> 0\n potion 1 -> 0\n discard 1\n end  : end the turn\n")
    assert legal_lines([{"token": "travel 0", "desc": "x"}, {"token": "travel 1", "desc": "y"},
                        {"token": "rest 0", "desc": "rest: heal 30% of max HP"}]) == [
        " travel 0 | travel 1", " rest 0  : rest: heal 30% of max HP"]
    # the user message keeps the v0.2 layout around the step text
    msg = build_user_message(step, [x["token"] for x in a["legal_actions"]], None)
    assert "not shown above" not in msg


def test_parity_detects_missing_information():
    view = CompactView(0)
    a = combat_obs()
    view.set_piles(a, PILES)
    ctx, step, _ = view.render(a)
    server = ("== r1 Combat | act 1 floor 2 | turn 1 | IRONCLAD HP 80/80 block 0 energy 3/3 gold 99\n"
              "-- enemies\n [0] u1 NIBBIT (Nibbit) 44/44 | intent: Attack 12\n-- legal actions\n end  : end the turn\n")
    assert parity_problems(server, ctx, step, a) == []
    assert any("SECRET_ID" in p for p in parity_problems(server.replace("Attack 12", "Attack 12 SECRET_ID 777"), ctx, step, a))
    assert parity_problems(server, ctx, render_step(a, {}).replace(" play 1 -> 0\n", ""), a) == ["legal play 1 -> 0"]


def test_runner_compact_mode(tmp_path):
    fake = FakeAnthropic([response("end")] * 3)
    sim = FakeSim(3)
    s = asyncio.run(play_game(sim, ClaudePolicy(fake, system_prompt=COMPACT_SYSTEM_PROMPT), SEEDSET["games"][0],
                              SEEDSET, tmp_path, RunLimits(), RunState(tracker=P.CostTracker()), "fake",
                              obs_mode="compact"))
    assert s["obs_mode"] == "compact" and s["result"] == "loss" and s["steps"] == 3
    assert sim.piles_calls == 1 and s["piles_fetches"] == 1 and s["context_rebuilds"] == 1
    systems = [c["system"] for c in fake.messages.calls]
    assert len(systems[0]) == 4 and systems[0][0]["text"] == COMPACT_SYSTEM_PROMPT
    assert all(b["cache_control"] == {"type": "ephemeral"} for b in systems[0])
    assert systems[0][1]["text"].startswith("== Run context: ")
    assert systems[1] == systems[0] == systems[2]  # re-sent verbatim
    user = fake.messages.calls[0]["messages"][0]["content"]
    assert user.startswith("== Combat | act 1 floor 2 | turn 1 | HP 50/80") and "== r1" not in user
    assert "-- legal actions\n play 0 -> 0\n end  : end turn\n" in user
    recs = [json.loads(x) for x in (tmp_path / "games" / "IRONCLAD-T1.jsonl").read_text().splitlines()][1:-1]
    assert recs[0]["obs_text"].startswith("== r1 Combat")  # the server text is still transcribed
    assert recs[0]["context_changed_from"] == 0 and len(recs[0]["context"]) == 3
    assert "context" not in recs[1] and recs[1]["context_sha"] == recs[0]["context_sha"]
    assert recs[1]["prompt_text"].startswith("== Combat")


def test_runner_rejects_unknown_mode(tmp_path):
    with pytest.raises(ValueError):
        asyncio.run(play_game(FakeSim(1), ClaudePolicy(FakeAnthropic([])), SEEDSET["games"][0], SEEDSET, tmp_path,
                              RunLimits(), RunState(tracker=P.CostTracker()), "fake", obs_mode="tiny"))


# --- live parity against the real server (skipped when the Release server is not built) --------------

@pytest.mark.skipif(not server_dll().exists() or shutil.which("dotnet") is None,
                    reason="Release play server not built (dotnet build sim/Sts2Sim.sln -c Release)")
def test_parity_live_random_games(tmp_path):
    from sts2eval.parity import ParityStats, play_random
    from sts2eval.server import SpawnedServer
    from sts2eval.simclient import SimClient

    async def go(url):
        client = SimClient(url)
        stats = ParityStats(samples=200, seed=7)
        try:
            await play_random(client, stats, [("PARITYA", "IRONCLAD"), ("PARITYB", "DEFECT"), ("PARITYC", "REGENT")],
                              step_cap=300, seed=7)
        finally:
            await client.aclose()
        return stats.result()

    with SpawnedServer(tmp_path) as srv:
        res = asyncio.run(go(srv.url))
    assert res["observations"] >= 200, res
    assert res["checked"] == 200 and res["failures"] == 0, res["failure_examples"]
    assert res["unstable_contexts"] == 0
    assert res["compact_step_chars_mean"] < res["server_text_chars_mean"]
