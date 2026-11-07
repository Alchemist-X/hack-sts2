"""Macro candidates on the real game (``nn_twin/macros.py``): one twin row, several STS2_MCP clicks, each checked.

The game side is a fake MCP transport (`FakeMcp`): scripted STS2_MCP states shaped like the live v0.111.0 MCP, decoded
by the real ``mcp.view_of`` / ``mcp.acts_of``, and a transition table keyed by the exact wire body the runner sends --
an unexpected wire is an error. No game, no sim, except `test_twin_decodes_room_macro_rows`, which builds synthetic
candidate rows on the library (skips without the Release build)."""

from __future__ import annotations

import json
from collections import Counter

import numpy as np
import pytest
from nn_twin import mcp
from nn_twin.game import ExecResult, Game, Observation
from nn_twin.macros import SPECS, generic_spec, map_open, play_plan, resolve_selects, spec_of
from nn_twin.runner import Runner
from nn_twin.semantics import Act, View
from nn_twin.twin import Decision, Twin
from test_mapping import card, grid, state, twin

# ------------------------------------------------------------------------------------------------ the fake transport


def _w(wire) -> str:
    return json.dumps(wire, sort_keys=True)


class FakeMcp(Game):
    """STS2_MCP stand-in: `states` name -> MCP state; `moves` [((state name, wire), next state name), ...]."""

    name = "fake-mcp"

    def __init__(self, states: dict, moves: dict, start: str):
        self.states, self.cur, self.sent = states, start, []
        self.moves = {(s, _w(w)): nxt for (s, w), nxt in moves}

    def observe(self) -> Observation:
        s = self.states[self.cur]
        return Observation(raw=s, view=mcp.view_of(s), acts=mcp.acts_of(s))

    def execute(self, act: Act) -> ExecResult:
        self.sent.append(act.native)
        nxt = self.moves.get((self.cur, _w(act.native)))
        if nxt is None:
            return ExecResult(False, act.native, f"rejected: no move for {act.native} on {self.cur}")
        self.cur = nxt
        return ExecResult(True, act.native)


def rest_state(can_proceed=False):
    opts = [] if can_proceed else [{"index": 0, "id": "HEAL", "is_enabled": True},
                                   {"index": 1, "id": "SMITH", "is_enabled": True}]
    return state("rest_site", rest_site={"options": opts, "can_proceed": can_proceed})


# a Silent deck: rows 0..4 (deck order); NEUTRALIZE+ is not upgradable
DECK = [("STRIKE_SILENT", 0), ("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0), ("NEUTRALIZE", 1), ("SURVIVOR", 0)]
UPGRADABLE = [0, 1, 2, 4]


def room_macro(name, d, rows=UPGRADABLE, deck=DECK, open_act=None, row=10):
    """A room macro Act as ``Twin._room_macro`` builds it."""
    cid, up = deck[d]
    open_act = open_act or (twin("rest", ("SMITH",), 1, row=None) if name == "SMITH"
                            else twin("buy", ("card_removal", None), 13, row=None))
    plan = (open_act, twin("select", (cid, up), d, row=None), twin("confirm", row=None))
    return Act("macro", (name, cid, up, d), d, row, f"{name} {cid} (deck {d})", plan,
               tuple(((deck[e][0], deck[e][1]), e) for e in rows))


def upgrade_grid(cards, **kw):
    return grid("upgrade", cards, can_cancel=True, **kw)


def smith_game():
    cards = [card("STRIKE_SILENT", 0), card("STRIKE_SILENT", 1), card("DEFEND_SILENT", 2), card("SURVIVOR", 3)]
    states = {"rest": rest_state(), "grid": upgrade_grid(cards),
              "preview": upgrade_grid(cards, preview_showing=True, can_confirm=True, selected_count=1,
                                      selected_indices=[1]),
              "done": rest_state(can_proceed=True)}
    moves = [(("rest", {"action": "choose_rest_option", "index": 1}), "grid"),
             (("grid", {"action": "select_card", "index": 1}), "preview"),
             (("grid", {"action": "cancel_selection"}), "rest"),
             (("preview", {"action": "confirm_selection"}), "done"),
             (("preview", {"action": "cancel_selection"}), "grid")]
    return FakeMcp(states, moves, "rest")


def play(macro, game, relaxed=False):
    """What the runner does: map and send the first act, then play the rest of the plan."""
    first = map_open(macro, [macro], game.observe().acts, relaxed)
    assert first is not None, "the game does not offer the macro's first act"
    assert game.execute(first).ok
    return play_plan(macro, game.observe, game.execute, relaxed=relaxed)


# ------------------------------------------------------------------------------------------------ the table

def test_spec_table_covers_every_known_macro_and_matches_its_plans():
    assert set(SPECS) == {"TakeRewardCard", "TakeRewardAlternative", "SMITH", "REMOVE"}
    assert len(SPECS["SMITH"].steps) == len(room_macro("SMITH", 0).plan) == 3
    assert len(SPECS["TakeRewardCard"].steps) == 2
    assert SPECS["SMITH"].abort and SPECS["REMOVE"].abort and not SPECS["TakeRewardCard"].abort
    # an unregistered name (owner G's prompt macros) gets the generic spec: grid check at the first select, a trailing
    # confirm optional
    plan = (twin("select", ("A", 0), 0), twin("select", ("B", 0), 1), twin("confirm"))
    g = spec_of(Act("macro", ("PICK_K",), plan=plan))
    assert g == generic_spec(plan)
    assert [(s.grid, s.optional) for s in g.steps] == [(True, False), (False, False), (False, True)]


# ------------------------------------------------------------------------------------------------ rest site: SMITH

def test_smith_macro_plays_open_select_confirm_with_the_right_copy():
    """SMITH on the second Strike (deck row 1): SMITH, select grid index 1 (the second copy, not the first), confirm the
    upgrade preview; every step on its expected screen."""
    game = smith_game()
    run = play(room_macro("SMITH", 1), game)
    assert run.ok and run.failure is None and not run.aborted
    assert game.sent == [{"action": "choose_rest_option", "index": 1}, {"action": "select_card", "index": 1},
                         {"action": "confirm_selection"}]
    assert [s["status"] for s in run.steps] == ["ok", "ok"] and [s["screen"] for s in run.steps] == ["prompt"] * 2
    assert game.cur == "done"


def test_smith_macro_first_copy_and_other_cards():
    for d, idx in ((0, 0), (2, 2), (4, 3)):
        game = smith_game()
        game.moves[("grid", _w({"action": "select_card", "index": idx}))] = "preview"
        assert play(room_macro("SMITH", d), game).ok
        assert game.sent[1] == {"action": "select_card", "index": idx}


def test_smith_grid_that_is_not_the_twins_aborts_back_to_the_rest_site():
    """The game's grid lacks a card the twin's deck has (the decks differ): no pick, the game's cancel closes the grid,
    the game stands at the rest site again (aborted), the failure names the difference."""
    game = smith_game()
    game.states["grid"] = upgrade_grid([card("STRIKE_SILENT", 0), card("DEFEND_SILENT", 1), card("SURVIVOR", 2)])
    run = play(room_macro("SMITH", 1), game)
    assert not run.ok and run.aborted and game.cur == "rest"
    assert run.failure["field"] == "macro_grid" and run.failure["twin"] == [["STRIKE_SILENT", 0]]
    assert game.sent[1:] == [{"action": "cancel_selection"}]
    # follow mode (relaxed): the grid is not compared, the pick still pairs by key + rank, and falls back to the first
    # copy when the game has fewer
    game = smith_game()
    game.states["grid"] = upgrade_grid([card("STRIKE_SILENT", 0), card("DEFEND_SILENT", 1), card("SURVIVOR", 2)])
    game.states["preview"] = upgrade_grid([card("STRIKE_SILENT", 0)], preview_showing=True, can_confirm=True,
                                          selected_count=1)
    game.moves[("grid", _w({"action": "select_card", "index": 0}))] = "preview"
    run = play(room_macro("SMITH", 1), game, relaxed=True)
    assert run.ok and game.sent[1] == {"action": "select_card", "index": 0}


def test_smith_open_that_does_not_reach_a_grid_fails_without_clicking():
    game = smith_game()
    game.moves[("rest", _w({"action": "choose_rest_option", "index": 1}))] = "done"      # no grid opened
    run = play(room_macro("SMITH", 1), game)
    assert not run.ok and run.failure["field"] == "macro_step" and run.failure["real"] == "rest"
    assert len(game.sent) == 1 and not run.aborted


def test_preview_without_confirm_backs_out_with_two_cancels():
    """A preview the game will not confirm: the plan stops at the confirm; the abort cancels the preview (back to the
    grid) and the grid (back to the rest site)."""
    game = smith_game()
    game.states["preview"] = upgrade_grid([card("STRIKE_SILENT", 0)], preview_showing=True, can_confirm=False,
                                          selected_count=1)
    run = play(room_macro("SMITH", 1), game)
    assert not run.ok and run.aborted and game.cur == "rest"
    assert game.sent[-2:] == [{"action": "cancel_selection"}, {"action": "cancel_selection"}]


# ------------------------------------------------------------------------------------------------ shop: REMOVE

SHOP_DECK = [("STRIKE_SILENT", 0), ("STRIKE_SILENT", 0), ("DEFEND_SILENT", 0), ("CLUMSY", 0)]


def shop_game(order=("CLUMSY", "STRIKE_SILENT", "STRIKE_SILENT", "DEFEND_SILENT")):
    from test_mapping import shop_state

    cards = [card(c, i) for i, c in enumerate(order)]
    states = {"shop": shop_state(), "grid": grid("select", cards, can_cancel=True),
              "preview": grid("select", cards, preview_showing=True, can_confirm=True, can_cancel=True,
                              selected_count=1),
              "after": shop_state()}
    moves = [(("shop", {"action": "shop_purchase", "index": 13}), "grid"),
             (("grid", {"action": "cancel_selection"}), "shop"),
             (("preview", {"action": "confirm_selection"}), "after")]
    for i in range(len(order)):
        moves.append((("grid", {"action": "select_card", "index": i}), "preview"))
    return FakeMcp(states, moves, "shop")


def test_remove_macro_buys_the_removal_and_picks_by_identity_with_curses_first():
    """The game's removal grid lists curses first (RoomPrompts.FromDeckForRemoval); the twin's rows are in deck order.
    Copies pair by their order, which both sides keep from the deck."""
    for d, idx in ((3, 0), (0, 1), (1, 2), (2, 3)):
        game = shop_game()
        run = play(room_macro("REMOVE", d, rows=[0, 1, 2, 3], deck=SHOP_DECK), game)
        assert run.ok, run.failure
        assert game.sent == [{"action": "shop_purchase", "index": 13}, {"action": "select_card", "index": idx},
                             {"action": "confirm_selection"}]


def test_remove_macro_needs_the_removal_on_offer():
    from test_mapping import shop_state

    s = shop_state()
    s["shop"]["items"] = [i for i in s["shop"]["items"] if i["category"] != "card_removal"]
    m = room_macro("REMOVE", 0, rows=[0, 1, 2, 3], deck=SHOP_DECK)
    assert map_open(m, [m], mcp.acts_of(s), relaxed=False) is None     # not affordable / sold: the macro does not map


# ------------------------------------------------------------------------------------------------ k-pick (generic)

def test_k_pick_selects_are_resolved_once_on_the_grid():
    """A two-card transform (an unregistered macro name: the generic spec). STS2_MCP keeps a selected card in the grid
    and a second select on it deselects it, so both picks are resolved on the first frame: Strike #0 and Strike #1 go
    out as grid indices 0 and 1 (re-matching after the first pick would send index 0 twice)."""
    cards = [card("STRIKE_SILENT", 0), card("STRIKE_SILENT", 1), card("DEFEND_SILENT", 2)]
    g0 = grid("transform", cards, min_select=2, max_select=2)
    g1 = grid("transform", cards, min_select=2, max_select=2, selected_count=1, selected_indices=[0])
    g2 = grid("transform", cards, min_select=2, max_select=2, selected_count=2, selected_indices=[0, 1],
              preview_showing=True, can_confirm=True)
    game = FakeMcp({"g0": g0, "g1": g1, "g2": g2, "done": state("event", event={"event_id": "X", "options": []})},
                   [(("g0", {"action": "select_card", "index": 0}), "g1"),
                    (("g1", {"action": "select_card", "index": 1}), "g2"),
                    (("g2", {"action": "confirm_selection"}), "done")], "g0")
    sel = [twin("select", ("STRIKE_SILENT", 0), 0, row=None), twin("select", ("STRIKE_SILENT", 0), 1, row=None)]
    m = Act("macro", ("TRANSFORM_K", 0, 1), None, 3, "transform 2", (*sel, twin("confirm", row=None)),
            ((("STRIKE_SILENT", 0), 0), (("STRIKE_SILENT", 0), 1), (("DEFEND_SILENT", 0), 2)))
    # the first act of an in-prompt macro is itself a select: every select is resolved on the frame before the click
    frame = game.observe().acts
    got = resolve_selects(m, frame, False)
    assert [got[i].native for i in (0, 1)] == [{"action": "select_card", "index": 0},
                                               {"action": "select_card", "index": 1}]
    first = map_open(m, [m], frame, False)
    assert first.native == {"action": "select_card", "index": 0}
    assert game.execute(first).ok
    run = play_plan(m, game.observe, game.execute, first_acts=frame)
    assert run.ok, run.failure
    assert game.sent == [{"action": "select_card", "index": 0}, {"action": "select_card", "index": 1},
                         {"action": "confirm_selection"}]


def test_k_pick_trailing_confirm_is_optional_when_the_game_closes_the_selection():
    cards = [card("STRIKE_SILENT", 0), card("DEFEND_SILENT", 1)]
    g0 = grid("simple_select", cards, min_select=1, max_select=1)
    game = FakeMcp({"g0": g0, "done": state("event", event={"event_id": "X", "options": []})},
                   [(("g0", {"action": "select_card", "index": 1}), "done")], "g0")
    m = Act("macro", ("PICK_1",), None, 0, "pick 1", (twin("select", ("DEFEND_SILENT", 0), 1, row=None),
                                                     twin("confirm", row=None)))
    assert game.execute(map_open(m, [m], game.observe().acts, False)).ok
    run = play_plan(m, game.observe, game.execute)
    assert run.ok and [s["status"] for s in run.steps] == ["skipped"]


# ------------------------------------------------------------------------------------------------ the runner

class FakeTwin:
    """The twin at a rest site whose only rows are HEAL and SMITH macros; step() records the row."""

    def __init__(self, tacts, after_view: View):
        self.tacts, self.after, self.steps, self.obs = tacts, after_view, [], {}
        self._view = View("rest", act=1, floor=2, hp=60, max_hp=70, gold=99, potions=(),
                          relics=("RING_OF_THE_SNAKE",), detail={"rest": ("HEAL", "SMITH")})

    def view(self):
        return self._view

    def acts(self):
        return self.tacts

    def step(self, row):
        self.steps.append(row)
        self._view = self.after
        return 0

    def state_hash(self):
        return "0" * 16

    def row_to_token(self, row):
        return None


class FakePolicy:
    def __init__(self, row, n):
        self.row, self.n, self.committed = row, n, []

    def decide(self, obs):
        p = np.full(self.n, 0.1 / max(1, self.n - 1))
        p[self.row] = 0.9
        return Decision(row=self.row, probs=p, value=0.0, win_prob=None, combat_hp=None)

    def commit(self, row):
        self.committed.append(row)


def _runner(tmp_path, game, tw, pol):
    r = Runner(game, tw, pol, tmp_path / "run", character="SILENT", ascension=10, seed="X", log=lambda *_: None)
    r.twin_ok = True
    return r


def test_runner_plays_a_smith_macro_in_lockstep(tmp_path):
    game = smith_game()
    tacts = [twin("rest", ("HEAL",), 0, row=0)] + [room_macro("SMITH", d, row=1 + k) for k, d in enumerate(UPGRADABLE)]
    tw = FakeTwin(tacts, View("rest", act=1, floor=2, hp=60, max_hp=70, gold=99, potions=(),
                              relics=("RING_OF_THE_SNAKE",)))
    pol = FakePolicy(2, len(tacts))                                # SMITH on deck row 1
    r = _runner(tmp_path, game, tw, pol)
    obs = game.observe()
    r.decide(obs, tw.view(), tacts)
    assert tw.steps == [2] and pol.committed == [2] and r.mode == "lockstep"
    assert game.cur == "done" and len(game.sent) == 3
    rec = json.loads((tmp_path / "run" / "trajectory.jsonl").read_text().splitlines()[-1])
    assert rec["ok"] and rec["policy"] == "greedy" and len(rec["real_action"]) == 3
    assert rec["macro"]["name"] == "SMITH" and [s["status"] for s in rec["macro"]["steps"]] == ["ok", "ok"]
    assert r.stats["macros"] == 1 and r.stats["macro_failures"] == 0


def test_runner_macro_grid_mismatch_follows_without_stepping_the_twin(tmp_path):
    game = smith_game()
    game.states["grid"] = upgrade_grid([card("STRIKE_SILENT", 0), card("DEFEND_SILENT", 1), card("SURVIVOR", 2)])
    tacts = [twin("rest", ("HEAL",), 0, row=0)] + [room_macro("SMITH", d, row=1 + k) for k, d in enumerate(UPGRADABLE)]
    tw = FakeTwin(tacts, View("rest"))
    r = _runner(tmp_path, game, tw, FakePolicy(2, len(tacts)))
    r.decide(game.observe(), tw.view(), tacts)
    assert tw.steps == [] and r.twin_ok and r.mode == "follow" and game.cur == "rest"
    assert r.first_desyncs[0]["where"] == "macro" and r.first_desyncs[0]["fields"][0]["field"] == "macro_grid"
    rec = json.loads((tmp_path / "run" / "trajectory.jsonl").read_text().splitlines()[-1])
    assert rec["ok"] is False and rec["macro"]["aborted"] is True
    assert r.stats["macro_aborts"] == 1


# ------------------------------------------------------------------------------------------------ the twin's decoding

def test_removal_prompt_order_puts_curses_first():
    tw = Twin.__new__(Twin)
    tw._deck = lambda: [("STRIKE_SILENT", 0, 1), ("CLUMSY", 0, 2), ("DEFEND_SILENT", 0, 3), ("DOUBT", 0, 4)]
    tw._is_curse = lambda cid: cid in (2, 4)
    assert tw._prompt_order("BuyEntry", [0, 1, 2, 3]) == [1, 3, 0, 2]
    assert tw._prompt_order("ChooseRestOption", [0, 2]) == [0, 2]


def test_twin_decodes_room_macro_rows():
    """Synthetic SMITH / removal macro rows on the library (B = deck row): the macro Act, its plan, its grid and its
    play-server token (the removal's pick is the curse-first prompt rank)."""
    pytest.importorskip("sts2vec")
    try:
        tw = Twin(content="all")
    except Exception as e:  # noqa: BLE001
        pytest.skip(f"no sts2vec library: {e}")
    try:
        obs, cc = tw.obs, tw.c["cand"]
        dc = tw.schema.cols("deck")
        deck = [("STRIKE_SILENT", 0), ("CLUMSY", 0), ("STRIKE_SILENT", 0), ("NEUTRALIZE", 1)]
        for d, (cid, up) in enumerate(deck):
            obs["deck"][0][d, [dc["card_id"], dc["upgrade"]]] = (tw.V.cards.index_of(cid), up)
        obs["global_i"][0, tw.c["global_i"]["deck_n"]] = len(deck)
        assert tw._deck()[1][:2] == ("CLUMSY", 0) and tw._is_curse(tw.V.cards.index_of("CLUMSY"))
        rest, buy = tw.kinds.index_of("ChooseRestOption"), tw.kinds.index_of("BuyEntry")
        cand = obs["cand"][0]
        cand[:, :] = 0

        def rows(kind, a, ds):
            cand[:, :] = 0
            for j, d in enumerate(ds):
                cand[j, [cc["kind"], cc["A"], cc["B"], cc["option_index"]]] = (kind, a, d, a)
            obs["cand_n"][0] = len(ds)
            return tw.acts()

        acts = rows(rest, 1, [0, 2])                             # SMITH on both Strikes (CLUMSY / NEUTRALIZE+ not)
        assert [a.key for a in acts] == [("SMITH", "STRIKE_SILENT", 0, 0), ("SMITH", "STRIKE_SILENT", 0, 2)]
        # (the rest buffer is zero here: the open act's option name falls back to SMITH)
        assert [(p.kind, p.key) for p in acts[1].plan] == [("rest", ("SMITH",)), ("select", ("STRIKE_SILENT", 0)),
                                                           ("confirm", ())]
        assert acts[1].expect == ((("STRIKE_SILENT", 0), 0), (("STRIKE_SILENT", 0), 2))
        assert [tw.row_to_token(j) for j in range(2)] == ["rest 1; pick 0; confirm", "rest 1; pick 1; confirm"]
        acts = rows(buy, 5, [0, 1, 2])                           # removal: CLUMSY is listed first by the game
        assert [a.key[0] for a in acts] == ["REMOVE"] * 3 and acts[0].plan[0].key == ("card_removal", None)
        assert [tw.row_to_token(j) for j in range(3)] == ["remove; pick 1; confirm", "remove; pick 0; confirm",
                                                          "remove; pick 2; confirm"]
        assert Counter(k for k, _ in acts[0].expect) == Counter({("STRIKE_SILENT", 0): 2, ("CLUMSY", 0): 1})
        # B = -1 rows stay the plain kinds
        cand[:, :] = 0
        cand[0, [cc["kind"], cc["A"], cc["B"], cc["option_index"]]] = (rest, 1, -1, 1)
        obs["cand_n"][0] = 1
        assert tw.acts()[0].kind == "rest"
    finally:
        tw.close()
