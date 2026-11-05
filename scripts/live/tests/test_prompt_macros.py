"""Prompt macros on the real game (``--macros all``; macros.py "Prompt macros"): a twin PromptSelect row is the game's
select plus, only when the twin's env committed the prompt in that step, the game's confirm; DONE / DECLINE rows are
the game's confirm / skip; a cancel row fails closed; the MACRO_FALLBACK room row is the plain open + prompt rows.

The game side is the fake MCP transport of test_macros (scripted STS2_MCP states decoded by the real ``mcp.view_of`` /
``mcp.acts_of``, a transition table keyed by the exact wire; an unexpected wire is rejected). The twin is a scripted
fake whose env state after each step (``prompt_marker``) decides the plan, as the real ``Twin`` does. No game, no sim,
except `test_twin_prompt_macros_on_the_library` (skips without the Release build)."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
from nn_twin import mcp
from nn_twin.game import ExecResult
from nn_twin.macros import MACRO_FALLBACK, PromptMarker, is_true_cancel, pending_selection, prompt_committed
from nn_twin.runner import Runner
from nn_twin.semantics import Act, View
from nn_twin.twin import Decision, Twin
from test_macros import FakeMcp, smith_game
from test_mapping import card, combat_state, grid, state, twin

BASE = dict(act=1, floor=2, hp=60, max_hp=70, gold=99, potions=(), relics=("RING_OF_THE_SNAKE",))


def pview(*options, picked=False):
    """The twin's view of a prompt (``Twin.view``: the options only before the first pick)."""
    return View("prompt", **BASE, detail={} if picked else {"prompt_options": tuple(sorted(options))})


class Frame:
    def __init__(self, view, acts, marker=None, status=0, options=None):
        self.view, self.acts, self.marker, self.status = view, acts, marker, status
        # the prompt's whole option table (``Twin._prompt_options``: index -> kind / item / upgraded / picked)
        self.options = options


class PromptTwin:
    """A scripted twin with prompt macros: `frames` name -> Frame, `moves` {(frame, row): next frame}."""

    def __init__(self, frames, moves, start, prompt_macros=True):
        self.frames, self.moves, self.cur, self.steps, self.obs = frames, moves, start, [], {}
        self.prompt_macros = prompt_macros

    @property
    def frame(self):
        return self.frames[self.cur]

    def view(self):
        return self.frame.view

    def acts(self):
        return self.frame.acts

    def prompt_marker(self):
        return self.frame.marker

    def step(self, row):
        self.steps.append(row)
        self.cur = self.moves[(self.cur, row)]
        return self.frame.status

    def state_hash(self):
        return "0" * 16

    def row_to_token(self, row):
        return None

    # the real Twin.prompt_copy_rank, on this frame's option table
    prompt_copy_rank = Twin.prompt_copy_rank

    def phase_name(self):
        return "Prompt" if self.frame.marker is not None else "Combat"

    def g(self, name):
        assert name == "prompt_picked"
        return self.frame.marker.picked if self.frame.marker is not None else 0

    def _prompt_options(self):
        return self.frame.options or {}


class RowsPolicy:
    """Plays the given rows in order (one per decision)."""

    def __init__(self, rows):
        self.rows, self.committed = list(rows), []

    def decide(self, obs):
        row = self.rows.pop(0)
        n = max(row + 1, 2)
        p = np.full(n, 0.1 / (n - 1))
        p[row] = 0.9
        return Decision(row=row, probs=p, value=0.0, win_prob=None, combat_hp=None)

    def commit(self, row):
        self.committed.append(row)


def runner(tmp_path, game, tw, rows, name="run"):
    r = Runner(game, tw, RowsPolicy(rows), tmp_path / name, character="SILENT", ascension=10, seed="X",
               log=lambda *_: None)
    r.twin_ok = True
    return r


def step(r, game, tw):
    """One decision of the runner's loop on the current frames."""
    r.decide(game.observe(), tw.view(), tw.acts())


def records(tmp_path, name="run"):
    return [json.loads(x) for x in (tmp_path / name / "trajectory.jsonl").read_text().splitlines()]


def sel(cid, pos, row, up=0):
    return twin("select", (cid, up), pos, row=row)


def hand_select(cards, **kw):
    block = {"mode": "simple_select", "cards": cards, "can_confirm": False}
    block.update(kw)
    return state("hand_select", hand_select=block)


def combat_frame():
    return Frame(View("combat", **BASE), [twin("end_turn", row=0)])


def _w(wire) -> str:
    return json.dumps(wire, sort_keys=True)


# ------------------------------------------------------------------------------------------------ the rules

def test_commit_is_read_from_the_twin_env_state():
    a = PromptMarker(picked=0, site=7)
    assert prompt_committed(a, None)                                      # left the prompt
    assert not prompt_committed(a, PromptMarker(picked=1, site=7))        # one more pick, same prompt: still open
    assert prompt_committed(a, PromptMarker(picked=0, site=7))            # a new prompt of the same site
    assert prompt_committed(PromptMarker(picked=1, site=7), PromptMarker(picked=1, site=8))   # another prompt
    assert not prompt_committed(None, None)                               # not a prompt pick
    assert MACRO_FALLBACK == 512


def test_true_cancel_vs_skip_and_pending_selection():
    assert is_true_cancel(Act("cancel", (), None, {"action": "cancel_selection"}))
    assert is_true_cancel(Act("cancel", (), None, "cancel"))                          # play server token
    assert not is_true_cancel(Act("cancel", (), None, {"action": "skip_card_selection"}))
    assert not is_true_cancel(Act("confirm", (), None, "confirm"))
    assert not pending_selection(View("prompt", detail={"prompt_options": (("A", 0),)}))
    assert pending_selection(View("prompt")) and not pending_selection(View("combat"))


def test_a_selected_card_is_never_offered_as_a_pick():
    """STS2_MCP keeps selected cards in a grid (selecting one again deselects it); the sim has no deselect."""
    from nn_twin import mcp

    cards = [card("STRIKE_SILENT", 0), card("STRIKE_SILENT", 1)]
    acts = mcp.acts_of(grid("transform", cards, min_select=2, max_select=2, selected_count=1, selected_indices=[0]))
    assert [a.native for a in acts if a.kind == "select"] == [{"action": "select_card", "index": 1}]
    acts = mcp.acts_of(hand_select([card("STRIKE_SILENT", 0, is_selected=True), card("DEFEND_SILENT", 1)]))
    assert [a.key[0] for a in acts if a.kind == "select"] == ["DEFEND_SILENT"]


# ------------------------------------------------------------------------------------------------ exactly-1

def hand_trick_game(close_itself=False, confirm_ok=True, can_confirm=True):
    """HAND_TRICK-like: pick 1 skill from the hand; the game shows the pick and waits for its confirm button."""
    cards = [card("SURVIVOR", 0), card("BLADE_DANCE", 1)]
    states = {"hs0": hand_select(cards),
              "hs1": hand_select([card("SURVIVOR", 0)], selected_cards=[{"index": 1}], can_confirm=can_confirm),
              "cb": combat_state()}
    moves = [(("hs0", {"action": "combat_select_card", "card_index": 1}), "cb" if close_itself else "hs1")]
    if confirm_ok:
        moves.append((("hs1", {"action": "combat_confirm_selection"}), "cb"))
    return FakeMcp(states, moves, "hs0")


def hand_trick_twin():
    p = Frame(pview(("SURVIVOR", 0), ("BLADE_DANCE", 0)), [sel("SURVIVOR", 0, 0), sel("BLADE_DANCE", 1, 1)],
              PromptMarker(picked=0, site=7, needs_confirm=True, lo=1, hi=1))
    return PromptTwin({"p": p, "c": combat_frame()}, {("p", 0): "c", ("p", 1): "c"}, "p")


def test_exactly_one_select_commits_and_confirms_in_the_game(tmp_path):
    game, tw = hand_trick_game(), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}, {"action": "combat_confirm_selection"}]
    assert game.cur == "cb" and tw.steps == [1] and r.mode == "lockstep" and r.twin_ok
    rec = records(tmp_path)[-1]
    assert rec["ok"] and rec["policy"] == "greedy" and len(rec["real_action"]) == 2
    assert rec["prompt_macro"]["committed"] and rec["prompt_macro"]["status"] == "confirmed"
    assert r.stats["prompt_selects"] == 1 and r.stats["prompt_commits"] == 1
    assert r.stats["prompt_commit_confirmed"] == 1 and r.since_boundary == 2


def test_exactly_one_select_when_the_game_closes_the_selection_itself(tmp_path):
    game, tw = hand_trick_game(close_itself=True), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}]
    rec = records(tmp_path)[-1]
    assert rec["ok"] and rec["prompt_macro"]["status"] == "closed_by_game" and r.twin_ok and r.mode == "lockstep"


def test_without_prompt_macros_a_select_is_one_click(tmp_path):
    """Prompt macros off (``rest,shop`` / none): no confirm is planned (the existing glue handles the game's button)."""
    game, tw = hand_trick_game(), hand_trick_twin()
    tw.prompt_macros = False
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}]
    assert "prompt_macro" not in records(tmp_path)[-1] and r.stats["prompt_selects"] == 0


# ------------------------------------------------------------------------------------------------ exactly-k

def test_exactly_k_transform_commits_at_the_kth_pick(tmp_path):
    """A two-card transform event (exactly-2): the first pick does not commit (the twin is still in the prompt, one
    more pick), the second does and the game's preview is confirmed. The second Strike pairs with the game's
    unselected Strike (index 1), never with the selected one (re-selecting it would deselect it)."""
    cards = [card("STRIKE_SILENT", 0), card("STRIKE_SILENT", 1), card("DEFEND_SILENT", 2)]
    g = dict(min_select=2, max_select=2)
    game = FakeMcp({"g0": grid("transform", cards, **g),
                    "g1": grid("transform", cards, selected_count=1, selected_indices=[0], **g),
                    "g2": grid("transform", cards, selected_count=2, selected_indices=[0, 1], preview_showing=True,
                               can_confirm=True, **g),
                    "done": state("event", event={"event_id": "MORPHIC_GROVE", "options": []})},
                   [(("g0", {"action": "select_card", "index": 0}), "g1"),
                    (("g1", {"action": "select_card", "index": 1}), "g2"),
                    (("g2", {"action": "confirm_selection"}), "done")], "g0")
    site = 9
    t0 = Frame(pview(("STRIKE_SILENT", 0), ("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0)),
               [sel("STRIKE_SILENT", 0, 0), sel("STRIKE_SILENT", 1, 1), sel("DEFEND_SILENT", 2, 2)],
               PromptMarker(picked=0, site=site, lo=2, hi=2))
    t1 = Frame(pview(picked=True), [sel("STRIKE_SILENT", 1, 0), sel("DEFEND_SILENT", 2, 1)],
               PromptMarker(picked=1, site=site, lo=2, hi=2))
    ev = Frame(View("event", **BASE, detail={"event": "MORPHIC_GROVE"}), [twin("leave", ("event",), row=0)])
    tw = PromptTwin({"t0": t0, "t1": t1, "ev": ev}, {("t0", 0): "t1", ("t1", 0): "ev"}, "t0")
    r = runner(tmp_path, game, tw, [0, 0])
    step(r, game, tw)
    assert game.sent == [{"action": "select_card", "index": 0}] and game.cur == "g1"
    assert records(tmp_path)[-1]["prompt_macro"] == {"committed": False, "picked": 0, "needs_confirm": True,
                                                     "bounds": [2, 2]}
    step(r, game, tw)
    assert game.sent[1:] == [{"action": "select_card", "index": 1}, {"action": "confirm_selection"}]
    assert game.cur == "done" and r.mode == "lockstep" and r.twin_ok and not r.first_desyncs
    assert r.stats["prompt_selects"] == 2 and r.stats["prompt_commits"] == 1


# ------------------------------------------------------------------------------------------------ up-to-N

def purity_game():
    """PURITY-like: exhaust up to 2 cards from the hand (min 0); the confirm button is available throughout."""
    a, b, c = card("STRIKE_SILENT", 0), card("DEFEND_SILENT", 1), card("NEUTRALIZE", 2)
    states = {"h0": hand_select([a, b, c], can_confirm=True),
              "h1": hand_select([b, c], selected_cards=[{"index": 0}], can_confirm=True),
              "h2": hand_select([c], selected_cards=[{"index": 0}, {"index": 1}], can_confirm=True),
              "cb": combat_state()}
    moves = [(("h0", {"action": "combat_select_card", "card_index": 0}), "h1"),
             (("h1", {"action": "combat_select_card", "card_index": 1}), "h2"),
             (("h1", {"action": "combat_confirm_selection"}), "cb"),
             (("h2", {"action": "combat_confirm_selection"}), "cb")]
    return FakeMcp(states, moves, "h0")


def purity_twin():
    site = 21

    def m(k):
        return PromptMarker(picked=k, site=site, needs_confirm=True, lo=0, hi=2)

    p0 = Frame(pview(("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0), ("NEUTRALIZE", 0)),
               [sel("STRIKE_SILENT", 0, 0), sel("DEFEND_SILENT", 1, 1), sel("NEUTRALIZE", 2, 2),
                twin("confirm", ("empty",), row=3)], m(0))
    p1 = Frame(pview(picked=True), [sel("DEFEND_SILENT", 1, 0), sel("NEUTRALIZE", 2, 1), twin("confirm", row=2)], m(1))
    return PromptTwin({"p0": p0, "p1": p1, "c": combat_frame()},
                      {("p0", 0): "p1", ("p1", 0): "c", ("p1", 2): "c"}, "p0")


def test_up_to_n_done_before_n(tmp_path):
    game, tw = purity_game(), purity_twin()
    r = runner(tmp_path, game, tw, [0, 2])                      # pick Strike, then DONE
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 0}]
    assert not records(tmp_path)[-1]["prompt_macro"]["committed"]
    step(r, game, tw)
    assert game.sent[1:] == [{"action": "combat_confirm_selection"}] and game.cur == "cb"
    assert "prompt_macro" not in records(tmp_path)[-1]           # DONE is the game's confirm, nothing more
    assert r.mode == "lockstep" and r.twin_ok and r.stats["prompt_commits"] == 0


def test_up_to_n_auto_confirms_at_the_nth_pick(tmp_path):
    game, tw = purity_game(), purity_twin()
    r = runner(tmp_path, game, tw, [0, 0])                      # Strike, then Defend = the 2nd of at most 2
    step(r, game, tw)
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 0},
                         {"action": "combat_select_card", "card_index": 1}, {"action": "combat_confirm_selection"}]
    assert game.cur == "cb" and records(tmp_path)[-1]["prompt_macro"]["status"] == "confirmed"
    assert r.mode == "lockstep" and r.stats["prompt_commits"] == 1


# ------------------------------------------------------------------------------------------------ DECLINE

def choose_twin(rows):
    p = Frame(pview(("PRODUCTION", 0), ("ULTIMATE_DEFEND", 0)), rows,
              PromptMarker(picked=0, site=33, needs_confirm=False, lo=0, hi=1))
    return PromptTwin({"p": p, "e": Frame(View("event", **BASE), [twin("leave", ("event",), row=0)])},
                      {("p", a.native): "e" for a in rows}, "p")


def test_decline_is_the_games_skip_where_the_game_allows_it(tmp_path):
    cards = [card("PRODUCTION", 0), card("ULTIMATE_DEFEND", 1)]
    game = FakeMcp({"g": grid("choose", cards, can_skip=True), "e": state("event", event={"event_id": "X"})},
                   [(("g", {"action": "skip_card_selection"}), "e")], "g")
    tw = choose_twin([sel("PRODUCTION", 0, 0), sel("ULTIMATE_DEFEND", 1, 1), twin("confirm", ("empty",), row=2)])
    r = runner(tmp_path, game, tw, [2])
    step(r, game, tw)
    assert game.sent == [{"action": "skip_card_selection"}] and r.mode == "lockstep" and tw.steps == [2]
    assert "prompt_macro" not in records(tmp_path)[-1]


def test_decline_never_becomes_the_cancel_of_a_cancelable_site(tmp_path):
    """A game prompt that can only be left by its cancel (back to the screen that opened it): the twin's DECLINE does
    not map; in sync, the runner substitutes the best row the game does offer (a select, taken on both sides), never
    the cancel."""
    cards = [card("PRODUCTION", 0), card("ULTIMATE_DEFEND", 1)]
    game = FakeMcp({"g": grid("choose", cards, can_cancel=True), "e": state("event", event={"event_id": "X"})},
                   [(("g", {"action": "select_card", "index": 0}), "e")], "g")
    tw = choose_twin([sel("PRODUCTION", 0, 0), twin("confirm", ("empty",), row=1)])
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "select_card", "index": 0}] and tw.steps == [0]
    rec = records(tmp_path)[-1]
    assert rec["policy"] == "substitute" and rec["substituted_for"] == "twin confirm"
    assert r.mode == "lockstep" and r.twin_ok


def test_decline_with_prompt_macros_off_is_the_skip_never_the_cancel(tmp_path):
    """Prompt macros off (``--macros rest,shop``, the main line): the same DECLINE rule. On a choose-a-card screen that
    publishes cancel and skip together DECLINE is the skip; on a cancel-only screen it does not map (in sync the runner
    substitutes the best row the game does offer); the PromptCancel row still maps to the game's cancel."""
    cards = [card("PRODUCTION", 0), card("ULTIMATE_DEFEND", 1)]
    # cancel + skip: the skip, end to end in lockstep
    game = FakeMcp({"g": grid("choose", cards, can_cancel=True, can_skip=True),
                    "e": state("event", event={"event_id": "X"})},
                   [(("g", {"action": "skip_card_selection"}), "e")], "g")
    tw = choose_twin([sel("PRODUCTION", 0, 0), sel("ULTIMATE_DEFEND", 1, 1), twin("confirm", ("empty",), row=2),
                      twin("cancel", row=3)])
    tw.prompt_macros = False
    r = runner(tmp_path, game, tw, [2])
    racts = game.observe().acts
    assert [a.native["action"] for a in racts if a.kind == "cancel"] == ["cancel_selection", "skip_card_selection"]
    decline, cancel = tw.frames["p"].acts[2], tw.frames["p"].acts[3]
    assert r.map_row(decline, tw.frames["p"].acts, racts, relaxed=False).native == {"action": "skip_card_selection"}
    assert r.map_row(cancel, tw.frames["p"].acts, racts, relaxed=False).native == {"action": "cancel_selection"}
    step(r, game, tw)
    assert game.sent == [{"action": "skip_card_selection"}] and tw.steps == [2]
    assert records(tmp_path)[-1]["policy"] == "greedy" and r.mode == "lockstep" and r.twin_ok
    # the real STS2_MCP fixture (can_cancel and can_skip together)
    raw = json.loads((FIXTURE / "card_select_choose.json").read_text())
    assert r.map_row(decline, [decline], mcp.acts_of(raw), relaxed=False).native == {"action": "skip_card_selection"}
    # cancel only: DECLINE does not map; in sync the runner substitutes a select, never the cancel
    game2 = FakeMcp({"g": grid("choose", cards, can_cancel=True), "e": state("event", event={"event_id": "X"})},
                    [(("g", {"action": "select_card", "index": 0}), "e")], "g")
    tw2 = choose_twin([sel("PRODUCTION", 0, 0), twin("confirm", ("empty",), row=1), twin("cancel", row=2)])
    tw2.prompt_macros = False
    r2 = runner(tmp_path, game2, tw2, [1], name="cancel_only")
    racts2 = game2.observe().acts
    assert r2.map_row(tw2.frames["p"].acts[1], tw2.frames["p"].acts, racts2, relaxed=False) is None
    assert r2.map_row(tw2.frames["p"].acts[2], tw2.frames["p"].acts, racts2, relaxed=False).native == \
        {"action": "cancel_selection"}
    step(r2, game2, tw2)
    assert game2.sent == [{"action": "select_card", "index": 0}] and tw2.steps == [0]
    rec = records(tmp_path, "cancel_only")[-1]
    assert rec["policy"] == "substitute" and rec["substituted_for"] == "twin confirm"
    assert r2.mode == "lockstep" and r2.twin_ok


# ------------------------------------------------------------------------------------------------ no cancel

def _grid_frame(rows):
    opts = (("STRIKE_SILENT", 0), ("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0), ("SURVIVOR", 0))
    return Frame(pview(*opts), rows, PromptMarker(picked=0, site=5, needs_confirm=True, lo=1, hi=1))


def test_a_cancel_row_with_prompt_macros_on_fails_closed(tmp_path):
    game = smith_game()
    game.cur = "grid"
    p = _grid_frame([sel("STRIKE_SILENT", 0, 0), twin("cancel", row=1)])
    tw = PromptTwin({"p": p}, {}, "p")
    r = runner(tmp_path, game, tw, [0])
    step(r, game, tw)
    assert not r.twin_ok and r.mode == "follow" and tw.steps == []
    assert r.first_desyncs[0]["fields"][0]["field"] == "prompt_cancel_row" and r.stats["prompt_cancel_rows"] == 1
    assert records(tmp_path)[-1]["policy"] == "fallback"
    # prompt macros off: a cancel row is an ordinary candidate, the pick goes through in lockstep
    game2 = smith_game()
    game2.cur = "grid"
    game2.moves[("grid", _w({"action": "select_card", "index": 0}))] = "preview"
    tw2 = PromptTwin({"p": p, "q": _grid_frame([])}, {("p", 0): "q"}, "p", prompt_macros=False)
    r2 = runner(tmp_path, game2, tw2, [0], name="run2")
    step(r2, game2, tw2)
    assert r2.twin_ok and r2.mode == "lockstep" and r2.stats["prompt_cancel_rows"] == 0 and tw2.steps == [0]


# ------------------------------------------------------------------------------------------------ MACRO_FALLBACK

def test_macro_fallback_room_row_then_a_committing_prompt_pick(tmp_path):
    """MACRO_FALLBACK: the rest site lists the plain SMITH row (the expansion would exceed 64 rows); SMITH opens the
    upgrade grid (no cancel row), and the pick commits there: select + the game's confirm of the upgrade preview."""
    game = smith_game()
    rest = Frame(View("rest", **BASE, detail={"rest": ("HEAL", "SMITH")}),
                 [twin("rest", ("HEAL",), 0, row=0), twin("rest", ("SMITH",), 1, row=1)], status=MACRO_FALLBACK)
    opts = (("STRIKE_SILENT", 0), ("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0), ("SURVIVOR", 0))
    grid_f = _grid_frame([sel(c, i, i) for i, (c, _) in enumerate(opts)])
    done = Frame(View("rest", **BASE, detail={"rest": ()}), [twin("leave", ("rest",), row=0)])
    tw = PromptTwin({"rest": rest, "grid": grid_f, "done": done}, {("rest", 1): "grid", ("grid", 1): "done"}, "rest")
    r = runner(tmp_path, game, tw, [1, 1])
    r.count_status(rest.status)                                  # the rest site observation carried the bit
    step(r, game, tw)
    assert game.sent == [{"action": "choose_rest_option", "index": 1}] and game.cur == "grid"
    assert "prompt_macro" not in records(tmp_path)[-1]
    step(r, game, tw)                                            # the second Strike: grid index 1
    assert game.sent[1:] == [{"action": "select_card", "index": 1}, {"action": "confirm_selection"}]
    assert game.cur == "done" and r.mode == "lockstep" and r.twin_ok
    assert r.stats["twin_macro_fallback"] == 1 and r.stats["prompt_commit_confirmed"] == 1
    r.count_status(0)
    r.count_status(1 | MACRO_FALLBACK)
    assert r.stats["twin_macro_fallback"] == 2


# ------------------------------------------------------------------------------------------------ desyncs

def test_commit_without_a_game_confirm_fails_closed(tmp_path):
    """The twin's env committed the prompt, but the game still shows the pick and offers no confirm: the two sides
    disagree about the selection; the twin is disabled (fallback until a save point re-seeds it)."""
    game, tw = hand_trick_game(can_confirm=False), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}]
    assert not r.twin_ok and r.mode == "follow"
    assert r.first_desyncs[0]["where"] == "prompt_macro"
    assert r.first_desyncs[0]["fields"][0]["field"] == "prompt_commit"
    rec = records(tmp_path)[-1]
    assert rec["ok"] is False and rec["prompt_macro"]["status"] == "failed" and rec["twin_hash"] is None
    assert r.stats["prompt_commit_failures"] == 1


def test_rejected_game_confirm_fails_closed(tmp_path):
    game, tw = hand_trick_game(confirm_ok=False), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}, {"action": "combat_confirm_selection"}]
    assert not r.twin_ok and r.mode == "follow" and records(tmp_path)[-1]["ok"] is False


def test_twin_commit_while_the_game_shows_a_fresh_prompt_fails_closed(tmp_path):
    """The twin left its prompt (it is in combat), the game shows a new, unpicked selection: not the twin's state."""
    game = hand_trick_game()
    game.moves[("hs0", _w({"action": "combat_select_card", "card_index": 1}))] = "hs0"
    tw = hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert not r.twin_ok and r.first_desyncs[0]["fields"][0]["field"] == "prompt_commit"


# ------------------------------------------------------------------------------------------------ the library

def test_twin_prompt_macros_on_the_library():
    pytest.importorskip("sts2vec")
    from nn_twin.twin import Twin

    try:
        tw = Twin(content="all", macros="all")
    except Exception as e:  # noqa: BLE001
        pytest.skip(f"no sts2vec library: {e}")
    try:
        assert tw.macros == ("rest", "shop", "prompt") and tw.prompt_macros
        assert tw.STATUS["MACRO_FALLBACK"] == MACRO_FALLBACK
        tw.start_fresh("SILENT", "TW1NA10S01", 10)
        assert tw.prompt_marker() is None                       # Neow: an event, no prompt
    finally:
        tw.close()
    for spec, on in (("rest,shop,prompt", True), ("rest,shop", False)):
        tw = Twin(content="all", macros=spec)
        try:
            assert tw.prompt_macros is on
        finally:
            tw.close()


# ------------------------------------------------------------------------------------------------ review fixes (w5)

FIXTURE = Path(__file__).resolve().parents[3] / "sts2rec" / "tests" / "fixtures" / "live_frictions"


def test_decline_on_a_skippable_choose_screen_that_also_publishes_cancel(tmp_path):
    """A choose-a-card screen publishes can_cancel and can_skip together (the real STS2_MCP fixture; the recorder's
    passive builder sets can_cancel = can_skip): DECLINE is its skip, never refused and never a substituted pick."""
    raw = json.loads((FIXTURE / "card_select_choose.json").read_text())
    cs = raw["card_select"]
    assert cs["screen_type"] == "choose" and cs["can_cancel"] and cs["can_skip"] and not cs["can_confirm"]
    racts = mcp.acts_of(raw)
    assert [a.native["action"] for a in racts if a.kind == "cancel"] == ["cancel_selection", "skip_card_selection"]
    decline = twin("confirm", ("empty",), row=0)
    probe = runner(tmp_path, FakeMcp({"g": raw}, [], "g"), choose_twin([decline]), [], name="probe")
    assert probe.map_row(decline, [decline], racts, relaxed=False).native == {"action": "skip_card_selection"}
    # the decision, end to end on a scripted screen of the same shape
    cards = [card("PRODUCTION", 0), card("ULTIMATE_DEFEND", 1)]
    game = FakeMcp({"g": grid("choose", cards, can_cancel=True, can_skip=True),
                    "e": state("event", event={"event_id": "X"})},
                   [(("g", {"action": "skip_card_selection"}), "e")], "g")
    tw = choose_twin([sel("PRODUCTION", 0, 0), sel("ULTIMATE_DEFEND", 1, 1), twin("confirm", ("empty",), row=2)])
    r = runner(tmp_path, game, tw, [2])
    step(r, game, tw)
    assert game.sent == [{"action": "skip_card_selection"}] and tw.steps == [2]
    rec = records(tmp_path)[-1]
    assert rec["policy"] == "greedy" and r.mode == "lockstep" and r.twin_ok


def test_relic_skip_is_a_decline_too(tmp_path):
    r = runner(tmp_path, FakeMcp({}, [], None), choose_twin([]), [], name="probe")
    relics = state("relic_select", relic_select={"relics": [{"index": 0, "id": "ANCHOR"}], "can_skip": True})
    decline = twin("confirm", ("empty",), row=0)
    assert r.map_row(decline, [decline], mcp.acts_of(relics), relaxed=False).native == \
        {"action": "skip_relic_selection"}


class FlakyMcp(FakeMcp):
    """FakeMcp whose `flaky` wires move the game but report a failure (the commit witness timed out)."""

    def __init__(self, *a, flaky=(), **kw):
        super().__init__(*a, **kw)
        self.flaky = {_w(w) for w in flaky}

    def execute(self, act):
        res = super().execute(act)
        if res.ok and _w(act.native) in self.flaky:
            return ExecResult(False, act.native, "timeout: no commit witness")
        return res


def flaky_hand_trick(moved=True):
    cards = [card("SURVIVOR", 0), card("BLADE_DANCE", 1)]
    pick = {"action": "combat_select_card", "card_index": 1}
    states = {"hs0": hand_select(cards),
              "hs1": hand_select([card("SURVIVOR", 0)], selected_cards=[{"index": 1}], can_confirm=True),
              "cb": combat_state()}
    moves = [(("hs1", {"action": "combat_confirm_selection"}), "cb")]
    if moved:
        moves.append((("hs0", pick), "hs1"))
    game = FlakyMcp(states, moves, "hs0", flaky=[pick] if moved else [])
    return game


def test_failed_but_executed_select_still_commits_the_prompt(tmp_path):
    """The game took the pick but its select reported a failure: the twin's PromptSelect (which commits and confirms
    in one step) is kept and the game's confirm is played, instead of a reverted twin one pick behind the game."""
    game, tw = flaky_hand_trick(), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}, {"action": "combat_confirm_selection"}]
    assert game.cur == "cb" and tw.steps == [1] and r.twin_ok and r.mode == "lockstep"
    rec = records(tmp_path)[-1]
    assert rec["prompt_macro"]["committed"] and rec["prompt_macro"]["status"] == "confirmed"
    assert r.stats["exec_failures"] == 1 and r.stats["twin_step_kept_after_failure"] == 1


def test_failed_select_that_did_not_happen_leaves_the_twin_unstepped(tmp_path):
    game, tw = flaky_hand_trick(moved=False), hand_trick_twin()
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}] and game.cur == "hs0"
    assert tw.steps == [] and r.twin_ok and "prompt_macro" not in records(tmp_path)[-1]


def test_failed_select_with_an_unexpected_pick_count_fails_closed(tmp_path):
    game, tw = flaky_hand_trick(), hand_trick_twin()
    game.states["hs1"] = hand_select([], selected_cards=[{"index": 0}, {"index": 1}], can_confirm=True)
    r = runner(tmp_path, game, tw, [1])
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1}]          # no confirm
    assert tw.steps == [] and not r.twin_ok and r.mode == "follow"
    assert r.first_desyncs[0]["fields"][0]["field"] == "prompt_picked" and r.stats["prompt_select_failures"] == 1


def test_commit_with_a_game_pick_count_unlike_the_twins_fails_closed(tmp_path):
    """Up to 2 (min 0): the twin's 2nd pick commits, but the game shows 1 pick (a select that did not add one) with
    its confirm enabled: the confirm is not sent; the twin is disabled."""
    game, tw = purity_game(), purity_twin()
    game.states["h2"] = hand_select([card("DEFEND_SILENT", 1), card("NEUTRALIZE", 2)],
                                    selected_cards=[{"index": 0}], can_confirm=True)
    r = runner(tmp_path, game, tw, [0, 0])
    step(r, game, tw)
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 0},
                         {"action": "combat_select_card", "card_index": 1}]
    assert not r.twin_ok and r.mode == "follow" and r.stats["prompt_commit_failures"] == 1
    rec = records(tmp_path)[-1]
    assert rec["prompt_macro"]["status"] == "failed" and rec["prompt_macro"]["failure"]["field"] == "prompt_picked"
    assert mcp.view_of(game.states["h2"]).picked == 1 and mcp.view_of(game.states["h0"]).picked is None
    assert mcp.view_of(grid("transform", [card("STRIKE_SILENT", 0)], selected_count=0)).picked == 0


def _opts(*cards, picked=()):
    return {i: {"kind": "card", "item": c, "upgraded": 0, "picked": int(i in picked), "ordinal": i}
            for i, c in enumerate(cards)}


def test_later_pick_pairs_the_same_copy_as_the_twin(tmp_path):
    """Discard 2 from [Strike0, Strike1, Defend, Strike2]: the twin picks Strike1, then Strike2 (the sim lists only the
    options above its last pick: [Defend, Strike2]). The game lists every unselected copy ([Strike0, Strike2]); the
    second pick is Strike2 (its rank among the unpicked Strikes), not Strike0."""
    hand = ["STRIKE_SILENT", "STRIKE_SILENT", "DEFEND_SILENT", "STRIKE_SILENT"]
    cards = [card(c, i) for i, c in enumerate(hand)]
    rest = [c for c in cards if c["index"] != 1]
    states = {"h0": hand_select(cards), "h1": hand_select(rest, selected_cards=[{"index": 1}]),
              "h2": hand_select([rest[0], rest[1]], selected_cards=[{"index": 1}, {"index": 3}], can_confirm=True),
              "cb": combat_state()}
    game = FakeMcp(states, [(("h0", {"action": "combat_select_card", "card_index": 1}), "h1"),
                            (("h1", {"action": "combat_select_card", "card_index": 3}), "h2"),
                            (("h2", {"action": "combat_confirm_selection"}), "cb")], "h0")

    def m(k):
        return PromptMarker(picked=k, site=44, needs_confirm=True, lo=2, hi=2)

    p0 = Frame(pview(*((c, 0) for c in hand)), [sel(c, i, i) for i, c in enumerate(hand)], m(0), options=_opts(*hand))
    p1 = Frame(pview(picked=True), [sel("DEFEND_SILENT", 2, 0), sel("STRIKE_SILENT", 3, 1)], m(1),
               options=_opts(*hand, picked=(1,)))
    tw = PromptTwin({"p0": p0, "p1": p1, "c": combat_frame()}, {("p0", 1): "p1", ("p1", 1): "c"}, "p0")
    assert tw.prompt_copy_rank(p0.acts[1]) is None                       # a first pick: the plain rank pairing
    r = runner(tmp_path, game, tw, [1, 1])
    step(r, game, tw)
    assert tw.prompt_copy_rank(p1.acts[1]) == (1, 2)
    step(r, game, tw)
    assert game.sent == [{"action": "combat_select_card", "card_index": 1},
                         {"action": "combat_select_card", "card_index": 3}, {"action": "combat_confirm_selection"}]
    assert game.cur == "cb" and r.twin_ok and r.mode == "lockstep" and r.stats["prompt_commit_confirmed"] == 1
    # a side that lists only the options above the last pick (the play server stand-in) keeps the plain pairing
    tw.cur = "p1"
    standin_like = [Act("select", ("STRIKE_SILENT", 0), 3, "pick 3")]
    assert r.map_row(p1.acts[1], p1.acts, standin_like, relaxed=False).native == "pick 3"
