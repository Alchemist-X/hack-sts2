"""Macro candidates on the real game: one twin decision, several game clicks, checked after each click.

The simulator's Vec layer can list *macro* rows -- one candidate for a whole choice that the game makes in several
clicks (open a screen, select one or more cards on it, confirm):

  TakeRewardCard / TakeRewardAlternative   rewards screen: open card reward A, take card B / alternative B
                                           (``RewardMacros``; always on in libraries that have the kinds)
  SMITH                                    rest site, ``--macros rest``: SMITH, select deck card d, confirm
  REMOVE                                   shop, ``--macros shop``: buy the card removal, select deck card d, confirm
                                           (``RoomMacros``; rows ChooseRestOption / BuyEntry with B = deck row >= 0)

The twin decodes such a row into ``Act("macro", (NAME, ...), plan=(act, act, ...), expect=...)`` (``twin.py``): the
plan is the game's clicks as semantic acts, ``expect`` the options the selection screen of the macro must show, as
``((key, twin_pos), ...)`` (for SMITH: every upgradable deck card, keyed (card_id, upgraded), at its deck row). The twin
executes the row atomically (one ``step``); the runner plays the plan in the game and checks each sub-step here.

How a plan is played is data: ``SPECS`` maps the macro name to one ``Step`` per plan act -- the screens the game must
show for it, whether it is optional, whether the selection screen is checked against ``expect`` there -- plus how the
first act pairs with the game's candidates and whether a failed check backs out of the opened screen with the game's
cancel. A new macro kind (``--macros all`` prompts: hand select, choose-a-card, k-card transform / remove / upgrade
events) registers its name here; a name that is not registered gets ``generic_spec`` (every act required, on whatever
screen the game shows, a trailing confirm optional).

Selections are resolved **once**, on the first selection frame, against the game's grid: the j-th twin option of a key
(ordered by twin position) is the j-th game option of that key (ordered by grid index), and the game's ``index`` of
that card is what every later select sends. Re-matching after each click would be wrong: STS2_MCP keeps selected
cards in a multi-select grid and selecting one again deselects it. Card identity on the wire is the card's grid
position; STS2_MCP publishes no instance id, so copies of one card (same id and upgrade) pair by their order -- the
order both sides take from the deck (``RoomPrompts.FromDeckForUpgrade`` / ``FromDeckForRemoval``, curses first). Copies
that differ only in an enchantment are not told apart by either the view or the wire (see README-nn-twin.md).

Information parity: a macro only exposes what the game's own screens show the player before the choice (the deck, the
rest option, the shop's removal price); the grid check compares exactly the cards the game's grid offers.
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field

from .semantics import Act, match_act

ROOM_ABORT_SCREENS = ("prompt",)


@dataclass(frozen=True)
class Step:
    """How one plan act is played in the game.

    on        screens the game must show before this act (() = any)
    optional  skip the act when the game does not offer it (it already moved on); else a missing act fails the macro
    grid      before this act: the game's selection options must be the macro's ``expect`` (lockstep only), and every
              select of the plan is resolved against this frame's grid
    """

    on: tuple[str, ...] = ()
    optional: bool = False
    grid: bool = False


@dataclass(frozen=True)
class Spec:
    """open   how plan[0] pairs with the game's candidates: "exact" (by meaning, ranked among the twin's own acts) or
              "card_reward_rank" (the macro's card reward is the k-th unclaimed card reward on both sides)
    abort  after a failed check on an opened screen, press the game's cancel until it leaves the selection screen
           (rest site / shop: the game is back where the twin is, nothing was chosen)"""

    steps: tuple[Step, ...]
    open: str = "exact"
    abort: bool = False
    note: str = ""


SPECS: dict[str, Spec] = {
    "TakeRewardCard": Spec((Step(("rewards",)), Step(("card_reward", "prompt"))), open="card_reward_rank",
                           note="ClaimReward(A) + PickRewardCard(B)"),
    "TakeRewardAlternative": Spec((Step(("rewards",)), Step(("card_reward", "prompt"))), open="card_reward_rank",
                                  note="ClaimReward(A) + RewardAlternative(B): REROLL / SACRIFICE"),
    "SMITH": Spec((Step(("rest",)), Step(("prompt",), grid=True), Step(("prompt",))), abort=True,
                  note="ChooseRestOption(SMITH) + select deck card + confirm (upgrade preview)"),
    "REMOVE": Spec((Step(("shop",)), Step(("prompt",), grid=True), Step(("prompt",))), abort=True,
                   note="BuyEntry(card removal) + select deck card + confirm"),
}


def generic_spec(plan: tuple) -> Spec:
    """An unregistered macro name: every act required, the first selection frame checked against ``expect``, a
    trailing confirm optional (the game may close a selection on its last pick)."""
    steps = []
    gridded = False
    for i, a in enumerate(plan):
        grid = a.kind == "select" and not gridded
        gridded = gridded or grid
        steps.append(Step(optional=(a.kind == "confirm" and i == len(plan) - 1), grid=grid))
    return Spec(tuple(steps), note="generic")


def spec_of(macro: Act) -> Spec:
    name = macro.key[0] if macro.key else ""
    spec = SPECS.get(name)
    if spec is None or len(spec.steps) != len(macro.plan):
        return generic_spec(macro.plan)
    return spec


# ------------------------------------------------------------------------------------------------ first act

def map_open(macro: Act, twin_acts: list[Act], real_acts: list[Act], relaxed: bool) -> Act | None:
    """The game candidate of the macro's first act (None: the game does not offer it)."""
    if not macro.plan:
        return None
    spec = spec_of(macro)
    if spec.open == "card_reward_rank":
        # Card rewards are identical claims on both sides, so they pair by rank: A's rank among the twin's unclaimed
        # card rewards (one open act per distinct A of the macro rows).
        opens: dict = {}
        for t in twin_acts:
            if t.kind == "macro" and t.plan and spec_of(t).open == "card_reward_rank":
                opens.setdefault(t.plan[0].pos, t.plan[0])
        return match_act(macro.plan[0], real_acts, [opens[p] for p in sorted(opens)] or None, relaxed=relaxed)
    if spec.steps and spec.steps[0].grid:
        # an in-prompt macro (its first act is a select on the screen the game shows now): resolved on this grid
        if not relaxed and check_grid(macro, real_acts) is not None:
            return None
        got = resolve_selects(macro, real_acts, relaxed)
        return None if isinstance(got, str) else got.get(0)
    return match_act(macro.plan[0], real_acts, None, relaxed=relaxed)


# ------------------------------------------------------------------------------------------------ selections

def grid_keys(acts: list[Act]) -> Counter:
    return Counter(a.key for a in acts if a.kind == "select")


def check_grid(macro: Act, real_acts: list[Act]) -> dict | None:
    """None when the game's selection options are exactly the macro's expected options, else the difference."""
    if not macro.expect:
        return None
    want = Counter(k for k, _ in macro.expect)
    have = grid_keys(real_acts)
    if want == have:
        return None
    return {"field": "macro_grid", "twin": sorted(map(list, (want - have).elements())),
            "real": sorted(map(list, (have - want).elements())),
            "twin_n": sum(want.values()), "real_n": sum(have.values())}


def resolve_selects(macro: Act, real_acts: list[Act], relaxed: bool) -> dict[int, Act] | str:
    """plan index -> the game's select act, for every select of the plan, by key and rank on this grid frame; or the
    reason it cannot be resolved."""
    peers: dict[tuple, list[int]] = {}
    for key, pos in macro.expect:
        peers.setdefault(tuple(key), []).append(pos)
    for v in peers.values():
        v.sort()
    real_by_key: dict[tuple, list[Act]] = {}
    for a in real_acts:
        if a.kind == "select":
            real_by_key.setdefault(a.key, []).append(a)
    for v in real_by_key.values():
        v.sort(key=lambda a: (a.pos is None, a.pos if a.pos is not None else 0))
    out: dict[int, Act] = {}
    used: set[int] = set()
    for i, sub in enumerate(macro.plan):
        if sub.kind != "select":
            continue
        same = real_by_key.get(sub.key, [])
        if not same and relaxed and len(sub.key) == 2:
            same = sorted((a for a in real_acts if a.kind == "select" and a.key[:1] == sub.key[:1]),
                          key=lambda a: a.pos if a.pos is not None else 0)
        rank = peers.get(sub.key, []).index(sub.pos) if sub.pos in peers.get(sub.key, []) else 0
        if rank >= len(same):
            if relaxed and same:
                rank = 0
            else:
                return f"select {sub.describe()}: rank {rank} of {len(same)} on the game's grid"
        pick = same[rank]
        while id(pick) in used and rank + 1 < len(same):       # the same key picked twice: the next copy
            rank += 1
            pick = same[rank]
        used.add(id(pick))
        out[i] = pick
    return out


def offered(act: Act, real_acts: list[Act]) -> Act | None:
    """The game candidate with the same wire as a pre-resolved act on the current frame."""
    return next((a for a in real_acts if a.kind == act.kind and a.native == act.native), None)


@dataclass
class MacroRun:
    """The outcome of playing a macro's plan after its first act."""

    ok: bool = True
    executed: list = field(default_factory=list)     # Act.to_json() of each act sent (after the first)
    wires: list = field(default_factory=list)
    steps: list = field(default_factory=list)        # per plan act: {"i", "act", "screen", "status"}
    failure: dict | None = None                      # the desync record of the failed check
    aborted: bool = False                            # the opened screen was backed out of with the game's cancel
    error: str | None = None


def play_plan(macro: Act, observe, execute, *, relaxed: bool = False, first_acts: list[Act] | None = None,
              max_abort: int = 3) -> MacroRun:
    """Play ``macro.plan[1:]`` in the game (the first act was executed by the caller).

    ``observe() -> Observation`` (a settled frame) and ``execute(act) -> ExecResult`` are the runner's game calls;
    ``first_acts`` the game's candidates of the frame the first act was chosen on (an in-prompt macro resolves all its
    selects there, before the first click changed the grid). Before each act the game's screen is checked against the
    step's ``on``; at the grid step the selection options against ``expect`` (lockstep) and every select of the plan is
    resolved; then the act must be offered and is sent. A failed check stops the plan (``failure``) and, for specs with
    ``abort``, cancels out of the opened screen."""
    spec = spec_of(macro)
    run = MacroRun()
    resolved: dict[int, Act] = {}
    if spec.steps and spec.steps[0].grid and first_acts is not None:
        got = resolve_selects(macro, first_acts, relaxed)
        if isinstance(got, dict):
            resolved = got
    obs = None
    for i in range(1, len(macro.plan)):
        sub, step = macro.plan[i], spec.steps[i]
        obs = observe()
        screen = obs.view.screen
        rec = {"i": i, "act": sub.describe(), "screen": screen}
        run.steps.append(rec)
        if step.on and screen not in step.on:
            if step.optional:
                rec["status"] = "skipped"
                continue
            run.failure = {"field": "macro_step", "twin": f"{sub.describe()} on {list(step.on)}", "real": screen}
            break
        if step.grid:
            diff = None if relaxed else check_grid(macro, obs.acts)
            if diff is not None:
                run.failure = diff
                break
            got = resolve_selects(macro, obs.acts, relaxed)
            if isinstance(got, str):
                run.failure = {"field": "macro_step", "twin": got, "real": [a.describe() for a in obs.acts][:12]}
                break
            resolved = got
        if i in resolved:
            m = offered(resolved[i], obs.acts)
        else:
            m = match_act(sub, obs.acts, None, relaxed=relaxed)
        if m is None:
            if step.optional:
                rec["status"] = "skipped"
                continue
            run.failure = {"field": "macro_step", "twin": sub.describe(),
                           "real": [a.describe() for a in obs.acts][:12]}
            break
        res = execute(m)
        run.executed.append(m.to_json())
        run.wires.append(res.wire)
        rec["status"] = "ok" if res.ok else f"failed: {res.error}"
        if not res.ok:
            run.ok, run.error = False, res.error
            return run
    if run.failure is not None:
        run.ok = False
        run.error = f"macro {macro.key[0] if macro.key else '?'}: {run.failure['field']}"
        if spec.abort:
            cancels = 0
            for k in range(max_abort + 1):
                cur = observe()
                if cur.view.screen not in ROOM_ABORT_SCREENS:
                    # backed out: at least one cancel sent and the game shows the macro's starting screen again
                    run.aborted = bool(cancels) and bool(spec.steps[0].on) and cur.view.screen in spec.steps[0].on
                    break
                cancel = [a for a in cur.acts if a.kind == "cancel"]
                if not cancel or k == max_abort:
                    break
                res = execute(cancel[0])
                cancels += 1
                run.executed.append(cancel[0].to_json())
                run.wires.append(res.wire)
                run.steps.append({"i": None, "act": "abort: " + cancel[0].describe(), "screen": cur.view.screen,
                                  "status": "ok" if res.ok else f"failed: {res.error}"})
                if not res.ok:
                    break
    return run
