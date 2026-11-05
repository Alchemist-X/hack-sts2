"""Side-independent vocabulary of the twin lockstep: semantic actions (`Act`), state views (`View`), matching, diffing.

Both sides of the lockstep -- the simulator twin (``twin.py``) and the game it mirrors (``mcp.py`` for the real game
through STS2_MCP, ``standin.py`` for the sim play server in ``--dry-run-server`` mode) -- translate their native
candidates and states into these types. Matching never uses positions alone: an action is identified by its *meaning*
(card id + upgrade + target creature, map coordinate, reward identity, shop item, rest option, ...) and positions only
break ties between identical copies, by occurrence rank (the k-th identical Strike on one side is the k-th on the other).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

# Screens shared by both sides. "prompt" = any selection screen inside an effect (hand select, deck grid, choose-a-card,
# bundle, relic choice); "card_reward" = the card screen of a reward; "end" = the run is over.
SCREENS = ("combat", "prompt", "card_reward", "rewards", "map", "event", "shop", "rest", "treasure", "end", "menu",
           "other")

# Act kinds. `advance` exists on the real side only (event dialogue); `macro` on the twin side only (rewards-screen
# macro rows, and the rest-site / shop room macros of ``--macros rest,shop``, executed on the real side as a plan of
# acts checked step by step: ``macros.py``).
KINDS = ("play", "end_turn", "potion", "discard_potion", "travel", "claim_reward", "leave", "pick_card", "card_alt",
         "buy", "rest", "open_chest", "take_relic", "event_option", "select", "confirm", "cancel", "advance", "macro",
         "other")

# Kinds that count as the same choice on the two sides (a prompt pick on one side can be a card-reward pick on the
# other: a relic's "gain a card reward" is a Prompt(CardReward) in the sim and a card_reward screen in the game).
EQUIVALENT = {"select": ("select", "pick_card"), "pick_card": ("pick_card", "select")}


@dataclass(frozen=True)
class Act:
    """One candidate action, by meaning.

    kind   one of KINDS
    key    identity tuple compared across sides (kind-specific; see `twin.py` for the definitions)
    pos    the side's own position of the item (hand slot, option index ...) - only orders identical copies
    native the side's executable form: twin candidate row (int), MCP wire dict, play-server token
    label  human-readable text
    plan   for kind "macro": the acts to run on the other side, in order
    expect for kind "macro" with a selection screen: the options that screen must show, ((key, twin_pos), ...)
           (``macros.py``: the grid check and the rank pairing of the plan's selects)
    """

    kind: str
    key: tuple = ()
    pos: int | None = None
    native: Any = None
    label: str = ""
    plan: tuple = ()
    expect: tuple = ()

    def describe(self) -> str:
        return self.label or f"{self.kind}{list(self.key) if self.key else ''}"

    def to_json(self) -> dict:
        d = {"kind": self.kind, "key": list(self.key), "label": self.describe()}
        if self.pos is not None:
            d["pos"] = self.pos
        if self.native is not None:
            d["native"] = self.native
        if self.plan:
            d["plan"] = [a.to_json() for a in self.plan]
        if self.expect:
            d["expect_n"] = len(self.expect)
        return d


@dataclass
class View:
    """What both sides can be compared on at a decision point (None = not known on that side)."""

    screen: str
    act: int | None = None
    floor: int | None = None
    hp: int | None = None
    max_hp: int | None = None
    gold: int | None = None
    energy: int | None = None
    block: int | None = None
    hand: tuple | None = None          # ((card_id, upgraded 0/1), ...) in hand order
    enemies: tuple | None = None       # ((uid, monster_id, hp, block), ...) live enemies, sorted by uid
    potions: tuple | None = None       # ((slot, potion_id), ...)
    relics: tuple | None = None        # sorted relic ids
    detail: dict = field(default_factory=dict)   # screen content: map_options, event, card_reward, shop, ...
    terminal: str | None = None        # "victory" / "defeat" once the run is over

    def to_json(self) -> dict:
        d = {k: v for k, v in self.__dict__.items() if v is not None and k != "detail"}
        for k in ("hand", "enemies", "potions", "relics"):
            if k in d:
                d[k] = [list(x) if isinstance(x, tuple) else x for x in d[k]]
        if self.detail:
            d["detail"] = {k: _jsonable(v) for k, v in self.detail.items()}
        return d


def _jsonable(v):
    if isinstance(v, tuple):
        return [_jsonable(x) for x in v]
    return v


def norm_upgrade(value) -> int:
    """MCP is_upgraded (bool) / sim upgrade level (int) -> 0/1."""
    try:
        return 1 if int(value) > 0 else 0
    except (TypeError, ValueError):
        return 1 if value else 0


def norm_reward_type(value: str | None) -> str:
    """Reward types of both sides -> gold / card / potion / relic / special_card / remove_card / ..."""
    t = str(value or "").strip().lower().replace(" ", "_")
    return {"removecard": "remove_card", "card_removal": "remove_card", "specialcard": "special_card",
            "special": "special_card"}.get(t, t)


def norm_shop_category(value: str | None) -> str:
    """Colorless cards are a separate category in the sim and plain cards in the MCP: both become "card"."""
    c = str(value or "").strip().lower()
    return "card" if c in ("card", "colorless_card", "colorless") else c


def norm_alt(value: str | None) -> str:
    return str(value or "").strip().lower()


# ------------------------------------------------------------------------------------------------ matching

def _order(acts: list[Act]) -> list[Act]:
    return sorted(acts, key=lambda a: (a.pos is None, a.pos if a.pos is not None else 0))


def occurrence_rank(act: Act, acts: list[Act]) -> int:
    """Rank of `act` among the acts of the same kind and key on its own side (ordered by position)."""
    same = _order([a for a in acts if a.kind == act.kind and a.key == act.key])
    for i, a in enumerate(same):
        if a is act or (a.pos == act.pos and a.native == act.native):
            return i
    return 0


def _relaxed_key(act: Act) -> tuple:
    """Identity with upgrades ignored (follow mode only): a card that differs only in its upgrade state is still the
    same card choice; targets keep their creature id."""
    k = act.key
    if act.kind == "play" and len(k) == 3:
        return (k[0], k[2])
    if act.kind in ("select", "pick_card") and len(k) >= 2:
        return (k[0],)
    return k


def match_act(twin_act: Act, real_acts: list[Act], twin_acts: list[Act] | None = None, *,
              relaxed: bool = False) -> Act | None:
    """The real act that means the same as `twin_act`, or None.

    Exact: same kind (or an EQUIVALENT kind) and the same key; identical copies are paired by occurrence rank.
    `relaxed` (follow mode): also accept a card choice that differs only in its upgrade state.
    """
    kinds = EQUIVALENT.get(twin_act.kind, (twin_act.kind,))
    same = [r for r in real_acts if r.kind in kinds and r.key == twin_act.key]
    if not same and twin_act.kind == "confirm" and twin_act.key == ("empty",):
        # an empty confirmation is the game's confirm button when it has one, else its skip / cancel button
        same = [r for r in real_acts if r.kind == "confirm"] or \
            [r for r in real_acts if r.kind == "cancel" or (r.kind == "card_alt" and r.key == ("skip",))]
    if not same and twin_act.kind == "cancel":
        same = [r for r in real_acts if r.kind == "card_alt" and r.key == ("skip",)]
    if not same and twin_act.kind == "card_alt" and twin_act.key == ("skip",):
        same = [r for r in real_acts if r.kind == "cancel"]
    if not same and twin_act.kind == "claim_reward" and len(twin_act.key) == 2:
        # STS2_MCP describes relic / special-card rewards without their id (McpMod.StateBuilder BuildRewardsState):
        # a real (type, None) reward stands for any reward of that type
        same = [r for r in real_acts if r.kind == "claim_reward" and r.key == (twin_act.key[0], None)]
    if not same and relaxed:
        rk = _relaxed_key(twin_act)
        same = [r for r in real_acts if r.kind in kinds and _relaxed_key(r) == rk]
    if not same:
        return None
    same = _order(same)
    rank = occurrence_rank(twin_act, twin_acts) if twin_acts else 0
    return same[rank] if rank < len(same) else same[0]


# ------------------------------------------------------------------------------------------------ diffing

# Fields compared at every decision; per-screen content is compared through `detail` keys present on both sides.
CORE_FIELDS = ("screen", "act", "floor", "hp", "max_hp", "gold", "potions", "relics")
COMBAT_FIELDS = ("energy", "block", "enemies")
# A relic's "gain a card reward" is a Prompt(CardReward) in the sim and a card_reward screen in the game.
SELECTION_SCREENS = ("prompt", "card_reward")


def screens_compatible(a: str, b: str) -> bool:
    return a == b or (a in SELECTION_SCREENS and b in SELECTION_SCREENS)


def _unknown_items_as_real(twin_rewards, real_rewards):
    """Rewards whose item the real side cannot name ((type, None): relics, special cards on STS2_MCP) compare by type."""
    blind = {t for t, item in real_rewards if item is None}
    return tuple(sorted(((t, None if t in blind else item) for t, item in twin_rewards), key=repr))


def compare_views(twin: View, real: View) -> tuple[list[dict], list[dict]]:
    """(hard, soft) differences. Hard ones mean the twin no longer mirrors the game; soft ones are reported only
    (hand order when the hand holds the same cards)."""
    hard: list[dict] = []
    soft: list[dict] = []

    def diff(name, a, b, bucket=hard):
        if a is None or b is None:
            return
        if a != b:
            bucket.append({"field": name, "twin": _jsonable(a), "real": _jsonable(b)})

    for f in CORE_FIELDS:
        if f == "screen" and screens_compatible(twin.screen, real.screen):
            continue
        diff(f, getattr(twin, f), getattr(real, f))
    if twin.screen == real.screen == "combat":
        for f in COMBAT_FIELDS:
            diff(f, getattr(twin, f), getattr(real, f))
    if twin.screen == real.screen == "combat" and twin.hand is not None and real.hand is not None:
        if sorted(twin.hand) != sorted(real.hand):
            diff("hand", twin.hand, real.hand)
        elif twin.hand != real.hand:
            diff("hand_order", twin.hand, real.hand, soft)
    if twin.screen == real.screen:
        for k in sorted(set(twin.detail) & set(real.detail)):
            a, b = twin.detail[k], real.detail[k]
            if k == "rewards":
                a = _unknown_items_as_real(a, b)
            diff(k, a, b)
    if twin.terminal != real.terminal and (twin.terminal or real.terminal):
        hard.append({"field": "terminal", "twin": twin.terminal, "real": real.terminal})
    return hard, soft
