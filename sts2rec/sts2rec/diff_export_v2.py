"""``sts2rec export-bundles-v2``: per-run diff bundles (schema v2) from the recorder corpus.

Contract: ``docs/sim-port/design-diff-gate.md`` section 5 (bundle schema v2, action union,
eligibility v2) and section 4.2 (screen projections); exporter spec:
``sim/specs/diff-bundle-v2.md``. Version 1 (``export-bundles``, :mod:`sts2rec.diff_export`)
is untouched: this module only reuses its pure building blocks.

One JSON file per lineage (``<start_time>-<seed>-<character>.json``): the run is cut into
segments per room window (``combat`` per combat attempt, ``room`` for the run-level screens
of a room, ``map`` for the map moves out of a room). Every segment carries its decisions in
the v2 action union, a pre_state projection per decision, and an eligibility verdict with
reasons; ``reasons.json`` indexes every run and segment.
"""

from __future__ import annotations

import dataclasses
import hashlib
import json
import re
from collections import Counter
from collections.abc import Callable, Iterable, Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .diff_bundle import (
    NON_STATE_EVENT_ENTRIES,
    RELIC_WHITELIST,
    TURN_END_FOLLOW_UP_KIND,
    SimRegistry,
    _CS_CLASS_RE,
    _CS_CONST_RE,
    _class_body,
    _neow_lists,
    card_key,
    deck_multiset,
    extract_monster_ids,
    is_battle_state,
    base_deck,
    multiset_delta,
    niche_offset,
    project_state,
    reconstruct_deck,
    relic_reasons,
    scan_sim_registry,
    seeding_for,
    sim_coverage_reasons,
    snapshot_deck_multiset,
    strip_prefix,
)
from .diff_export import (
    COMBAT_ROOM_PREFIX,
    NEOW_ROOM_ID,
    PRE_STATE_MODES,
    PRE_STATE_SETTLED,
    CorpusVocabulary,
    ExportError,
    PartStreams,
    _combat_end,
    _final_player_hp,
    _inside,
    build_vocabulary,
    extract_decisions,
    load_part,
    read_native_modifiers,
)
from .lineage import group_lineages
from .session import ActionRecord, EventRecord, StateRecord

BUNDLE_VERSION_V2 = 2
REASONS_INDEX_VERSION_V2 = 2
RULES_VERSION = "v0.111.0"
DEFAULT_VERSIONS = ("v0.111.0", "v0.110.1")
MCP_MOD_ID = "STS2_MCP"

V1_COMBAT_KINDS = frozenset({"play_card", "end_turn", TURN_END_FOLLOW_UP_KIND})
COMBAT_V2_KINDS = frozenset({"use_potion", "discard_potion", "player_choice", "undo_end_turn"})
FOLLOW_UP_KINDS = frozenset({"move_to_map_coord", TURN_END_FOLLOW_UP_KIND})
MAP_KINDS = frozenset({"vote_for_map_coord", "vote_to_move_to_next_act"})
# Kinds that only exist outside a combat: the first one bounds a combat whose end left no
# end-of-combat state (a combat window cut by the next room entry).
RUN_LEVEL_KINDS = MAP_KINDS | frozenset(
    {"reward_taken", "rewards_skipped", "open_chest", "pick_relic", "shop_purchase", "shop_card_removal",
     "rest_site_option", "event_option", "event_proceed"}
)
ANCHOR_PENDING = "__anchor_pending__"
SUMMARY_DECK_KEYS = (
    "card_choices", "cards_gained", "cards_removed", "cards_transformed", "upgraded_cards",
    "cards_enchanted", "relic_choices", "ancient_choice", "event_choices", "rest_site_choices",
)
CARD_REWARD_TYPES = frozenset({"card_reward", "special_card_reward"})
SELECT_STATE_TYPES = frozenset({"hand_select", "card_select", "bundle_select", "relic_select"})
CARD_CHOICE_TYPES = frozenset({"CombatCard", "DeckCard", "CanonicalCard", "MutableCard"})

# Run-layer area -> sim source that implements it (relative to sim/Sts2Sim.Core).
RUN_LAYER_SOURCES = {
    "map": "Run/Map/StandardActMap.cs",
    "neow": "Run/Events/Ancients/Neow.cs",
    "events": "Run/Events/EventModel.cs",
    "rewards": "Run/Rewards/RewardsSet.cs",
    "shop": "Run/Rooms/MerchantRoom.cs",
    "rest": "Run/Rooms/RestSiteRoom.cs",
    "treasure": "Run/Rooms/TreasureRoom.cs",
}
ROOM_TYPE_AREA = {"Shop": "shop", "RestSite": "rest", "Treasure": "treasure", "Event": "events"}
SCREEN_AREA = {
    "map": "map",
    "rewards": "rewards",
    "card_reward": "rewards",
    "shop": "shop",
    "rest_site": "rest",
    "treasure": "treasure",
    "event": "events",
}


# --------------------------------------------------------------------------
# Sim content registry v2 (relics, potions, powers, monsters, events, run layer)
# --------------------------------------------------------------------------


_MODEL_BASES = {
    "RelicModel": "relics",
    "PotionModel": "potions",
    "PowerModel": "powers",
    "EventModel": "events",
    "AncientEventModel": "events",
    "MonsterDef": "monsters",
}
_ID_PROPERTY_RE = re.compile(r"override\s+string\s+Id\s*=>\s*(?P<arg>\"[^\"]*\"|[A-Za-z_][\w.]*)")
_BASE_CALL_RE = re.compile(r":\s*base\s*\(\s*(?P<arg>\"[^\"]*\"|[A-Za-z_][\w.]*)")
_STUB_RE = re.compile(r"NotImplementedException")


@dataclass(frozen=True)
class ContentRegistry:
    """What the sim has ported, per content area, as the v2 eligibility filter sees it.

    ``base`` is the v1 registry (characters, encounters, cards, upgradable cards); the other
    areas are scanned from the concrete model classes. An id whose class body throws
    ``NotImplementedException`` (a contract stub) counts as not ported.
    """

    base: SimRegistry
    areas: Mapping[str, frozenset[str]]
    stubbed: Mapping[str, frozenset[str]]
    run_layer: frozenset[str]

    def ported(self, area: str, item_id: str) -> bool:
        return item_id in self.areas.get(area, frozenset()) and item_id not in self.stubbed.get(area, frozenset())

    def to_json(self) -> dict[str, Any]:
        return {
            **self.base.to_json(),
            **{area: sorted(ids) for area, ids in sorted(self.areas.items())},
            "stubbed": {area: sorted(ids) for area, ids in sorted(self.stubbed.items()) if ids},
            "run_layer": sorted(self.run_layer),
        }

    @classmethod
    def from_json(cls, raw: Mapping[str, Any], *, source: str) -> ContentRegistry:
        areas = {area: frozenset(raw.get(area, ())) for area in set(_MODEL_BASES.values())}
        stubbed = {area: frozenset(ids) for area, ids in (raw.get("stubbed") or {}).items()}
        return cls(
            base=SimRegistry.from_json(raw, source=source),
            areas=areas,
            stubbed=stubbed,
            run_layer=frozenset(raw.get("run_layer", ())),
        )


def scan_content_registry(core_dir: str | Path) -> ContentRegistry:
    root = Path(core_dir)
    classes: dict[str, dict[str, Any]] = {}
    for path in sorted(root.rglob("*.cs")):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        file_consts = {m.group("name"): m.group("value") for m in _CS_CONST_RE.finditer(text)}
        for match in _CS_CLASS_RE.finditer(text):
            body = _class_body(text, match.end())
            consts = {**file_consts, **{m.group("name"): m.group("value") for m in _CS_CONST_RE.finditer(body)}}
            found = _ID_PROPERTY_RE.search(body) or _BASE_CALL_RE.search(body)
            class_id = None
            if found is not None:
                arg = found.group("arg")
                class_id = arg.strip('"') if arg.startswith('"') else consts.get(arg.split(".")[-1])
            classes[match.group("name")] = {
                "base": match.group("base"),
                "abstract": "abstract" in match.group("mods").split(),
                "excluded": "SimExclude" in match.group("attrs"),
                "id": class_id,
                "stub": bool(_STUB_RE.search(body)),
            }

    def area_of(name: str) -> str | None:
        seen: set[str] = set()
        while name in classes and name not in seen:
            seen.add(name)
            base = classes[name]["base"]
            if base in _MODEL_BASES:
                return _MODEL_BASES[base]
            name = base
        return None

    areas: dict[str, set[str]] = {area: set() for area in set(_MODEL_BASES.values())}
    stubbed: dict[str, set[str]] = {area: set() for area in areas}
    for name, info in classes.items():
        area = area_of(name)
        if area is None or info["abstract"] or info["excluded"] or not info["id"]:
            continue
        areas[area].add(info["id"])
        if info["stub"]:
            stubbed[area].add(info["id"])
    run_layer = frozenset(area for area, rel in RUN_LAYER_SOURCES.items() if (root / rel).is_file())
    return ContentRegistry(
        base=scan_sim_registry(root),
        areas={area: frozenset(ids) for area, ids in areas.items()},
        stubbed={area: frozenset(ids) for area, ids in stubbed.items()},
        run_layer=run_layer,
    )


# --------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------


def reason_class_v2(reason: str) -> str:
    """Histogram class: the first field, plus the area for the area-scoped v2 classes."""
    parts = reason.split(":")
    if parts[0] in {"content_not_ported", "run_layer_not_ported", "recorder_gap", "anchor_unreadable"} and len(parts) > 1:
        return f"{parts[0]}:{parts[1]}"
    return parts[0]


def _params(action: ActionRecord) -> Mapping[str, Any]:
    raw = action.action.get("params")
    if isinstance(raw, Mapping):
        return raw
    return {k: v for k, v in action.action.items() if k != "kind"}


def _kind(action: ActionRecord) -> str:
    return str(action.action.get("kind"))


def _int_or_none(value: Any) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def _window(records: Sequence[Any], start: int, end: int | None) -> list[Any]:
    return [r for r in records if r.seq > start and (end is None or r.seq < end)]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 16), b""):
            digest.update(chunk)
    return digest.hexdigest()


def lineage_id(start_time: float, seed: str, character: str) -> str:
    stamp = int(start_time) if float(start_time).is_integer() else start_time
    return f"{stamp}-{seed}-{character}"


def _read_raw_manifest(path: Path) -> Mapping[str, Any]:
    try:
        raw = json.loads((path / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return raw if isinstance(raw, Mapping) else {}


def _mod_ids(raw_manifest: Mapping[str, Any]) -> list[str]:
    mods = raw_manifest.get("mods") or ()
    ids = []
    for mod in mods:
        if isinstance(mod, Mapping):
            ids.append(str(mod.get("id") or mod.get("name") or mod))
        else:
            ids.append(str(mod))
    return ids


# --------------------------------------------------------------------------
# Projections (oracle side of design-diff-gate.md section 4.2)
# --------------------------------------------------------------------------


def project_player(player: Any) -> dict[str, Any] | None:
    """Run-level player projection: HP, gold, relics (id, counter), potions by slot."""
    if not isinstance(player, Mapping):
        return None
    relics = [
        {"id": str(r.get("id")), "counter": _int_or_none(r.get("counter"))}
        for r in player.get("relics") or ()
        if isinstance(r, Mapping) and r.get("id") is not None
    ]
    potions = [
        {"slot": _int_or_none(p.get("slot")), "id": str(p.get("id"))}
        for p in player.get("potions") or ()
        if isinstance(p, Mapping) and p.get("id") is not None
    ]
    return {
        "hp": _int_or_none(player.get("hp")),
        "max_hp": _int_or_none(player.get("max_hp")),
        "gold": _int_or_none(player.get("gold")),
        "relics": relics,
        "potions": potions,
        "max_potion_slots": _int_or_none(player.get("max_potion_slots")),
    }


def combat_projection_extras(player: Any) -> dict[str, Any]:
    """Combat projection v2 additions read from the snapshot's player block."""
    if not isinstance(player, Mapping):
        return {}
    base = project_player(player) or {}
    extras: dict[str, Any] = {
        "potions": base.get("potions", []),
        "relics": base.get("relics", []),
    }
    if isinstance(player.get("orbs"), list):
        extras["orbs"] = [
            {"id": str(o.get("id")), "passive": o.get("passive_val"), "evoke": o.get("evoke_val")}
            for o in player["orbs"]
            if isinstance(o, Mapping)
        ]
        extras["orb_slots"] = _int_or_none(player.get("orb_slots"))
    if player.get("stars") is not None:
        extras["stars"] = _int_or_none(player.get("stars"))
    if isinstance(player.get("pets"), list):
        extras["pets"] = [
            {
                "id": str(p.get("id")),
                "alive": p.get("alive"),
                "hp": _int_or_none(p.get("hp")),
                "max_hp": _int_or_none(p.get("max_hp")),
                "block": _int_or_none(p.get("block")),
            }
            for p in player["pets"]
            if isinstance(p, Mapping)
        ]
    return extras


def _card_view(card: Mapping[str, Any]) -> dict[str, Any]:
    upgraded = card.get("is_upgraded")
    if upgraded is None and _int_or_none(card.get("upgrade_level")) is not None:
        upgraded = card["upgrade_level"] > 0
    view: dict[str, Any] = {
        "index": _int_or_none(card.get("index")),
        "id": strip_prefix(card.get("id"), "CARD."),
        "upgraded": bool(upgraded),
    }
    if isinstance(card.get("enchantment"), Mapping):
        view["enchantment"] = card["enchantment"]
    return view


def _cards(raw: Any) -> list[dict[str, Any]] | None:
    if not isinstance(raw, list):
        return None
    return [_card_view(c) for c in raw if isinstance(c, Mapping) and c.get("id") is not None]


def project_map(block: Mapping[str, Any], *, with_graph: bool) -> dict[str, Any]:
    def coord(node: Any) -> dict[str, Any] | None:
        if not isinstance(node, Mapping):
            return None
        return {"col": _int_or_none(node.get("col")), "row": _int_or_none(node.get("row")), "type": node.get("type")}

    projection: dict[str, Any] = {
        "current": coord(block.get("current_position")),
        "next_options": [coord(n) for n in block.get("next_options") or () if isinstance(n, Mapping)],
    }
    if with_graph:
        projection["nodes"] = [
            {
                "col": _int_or_none(n.get("col")),
                "row": _int_or_none(n.get("row")),
                "type": n.get("type"),
                "children": [list(c) for c in n.get("children") or () if isinstance(c, list)],
            }
            for n in block.get("nodes") or ()
            if isinstance(n, Mapping)
        ]
        bosses = block.get("bosses")
        if isinstance(bosses, list):
            projection["bosses"] = [
                strip_prefix(b.get("id") if isinstance(b, Mapping) else b, "ENCOUNTER.") for b in bosses
            ]
        elif isinstance(block.get("boss"), Mapping):
            projection["bosses"] = [strip_prefix(block["boss"].get("id"), "ENCOUNTER.")]
    return projection


def project_rewards(block: Mapping[str, Any]) -> dict[str, Any]:
    items = []
    for item in block.get("items") or ():
        if not isinstance(item, Mapping):
            continue
        entry: dict[str, Any] = {"index": _int_or_none(item.get("index")), "type": item.get("type")}
        if "gold_amount" in item:
            entry["gold_amount"] = _int_or_none(item.get("gold_amount"))
        if item.get("potion_id") is not None:
            entry["potion_id"] = strip_prefix(item.get("potion_id"), "POTION.")
        if item.get("type") == "relic":
            entry["relic_id"] = strip_prefix(item["relic_id"], "RELIC.") if item.get("relic_id") else None
        if item.get("type") in ("card", "special_card"):
            entry["cards"] = _cards(item.get("cards"))
            if isinstance(item.get("card"), Mapping):
                entry["cards"] = [_card_view(item["card"])]
        items.append(entry)
    return {"items": items, "can_proceed": block.get("can_proceed")}


def project_card_reward(block: Mapping[str, Any]) -> dict[str, Any]:
    return {"cards": _cards(block.get("cards")) or [], "can_skip": block.get("can_skip")}


def project_shop(block: Mapping[str, Any]) -> dict[str, Any]:
    items = []
    for item in block.get("items") or ():
        if not isinstance(item, Mapping):
            continue
        category = item.get("category")
        item_id = item.get("card_id") or item.get("relic_id") or item.get("potion_id")
        items.append(
            {
                "index": _int_or_none(item.get("index")),
                "category": category,
                "id": strip_prefix(item_id, "CARD.") if item_id else None,
                "price": _int_or_none(item.get("price")),
                "on_sale": bool(item.get("on_sale")),
                "stocked": item.get("is_stocked"),
            }
        )
    return {"items": items, "inventory_open": block.get("inventory_open")}


def project_rest(block: Mapping[str, Any]) -> dict[str, Any]:
    return {
        "options": [
            {"index": _int_or_none(o.get("index")), "id": o.get("id"), "enabled": o.get("is_enabled")}
            for o in block.get("options") or ()
            if isinstance(o, Mapping)
        ]
    }


def project_treasure(block: Mapping[str, Any]) -> dict[str, Any]:
    relics = block.get("relics")
    return {
        "chest_opened": block.get("chest_opened"),
        "relics": (
            [{"index": _int_or_none(r.get("index")), "id": r.get("id")} for r in relics if isinstance(r, Mapping)]
            if isinstance(relics, list)
            else None
        ),
    }


def project_event(block: Mapping[str, Any]) -> dict[str, Any]:
    options = []
    for option in block.get("options") or ():
        if not isinstance(option, Mapping):
            continue
        entry = {
            "index": _int_or_none(option.get("index")),
            "text_key": option.get("text_key"),
            "locked": option.get("is_locked"),
            "proceed": option.get("is_proceed"),
        }
        if option.get("relic_id") is not None:
            entry["relic_id"] = option.get("relic_id")
        options.append(entry)
    return {
        "event_id": block.get("event_id"),
        "is_ancient": block.get("is_ancient"),
        "page_key": block.get("page_key"),
        "options": options,
    }


def project_select(state_type: str, block: Mapping[str, Any]) -> dict[str, Any]:
    projection: dict[str, Any] = {"kind": state_type}
    if "screen_type" in block:
        projection["screen_type"] = block.get("screen_type")
    if "mode" in block:
        projection["mode"] = block.get("mode")
    if state_type == "bundle_select":
        projection["bundles"] = [
            {"index": _int_or_none(b.get("index")), "cards": _cards(b.get("cards")) or []}
            for b in block.get("bundles") or ()
            if isinstance(b, Mapping)
        ]
    elif state_type == "relic_select":
        projection["relics"] = [
            {"index": _int_or_none(r.get("index")), "id": r.get("id")} for r in block.get("relics") or () if isinstance(r, Mapping)
        ]
    else:
        projection["candidates"] = _cards(block.get("cards")) or []
    for key in ("can_skip", "can_confirm", "can_cancel"):
        if key in block:
            projection[key] = block.get(key)
    return projection


SCREEN_PROJECTORS: dict[str, Callable[[Mapping[str, Any]], dict[str, Any]]] = {
    "rewards": project_rewards,
    "card_reward": project_card_reward,
    "shop": project_shop,
    "rest_site": project_rest,
    "treasure": project_treasure,
    "event": project_event,
}


def project_run_screen(state: Mapping[str, Any], *, map_graph: bool) -> dict[str, Any] | None:
    """pre_state of a run-level decision: the screen block plus the player."""
    state_type = state.get("state_type")
    if not isinstance(state_type, str):
        return None
    projection: dict[str, Any] = {"screen": state_type, "player": project_player(state.get("player"))}
    block = state.get(state_type)
    if not isinstance(block, Mapping):
        return projection
    if state_type == "map":
        projection["map"] = project_map(block, with_graph=map_graph)
    elif state_type in SCREEN_PROJECTORS:
        projection[state_type] = SCREEN_PROJECTORS[state_type](block)
    elif state_type in SELECT_STATE_TYPES:
        projection["select"] = project_select(state_type, block)
    return projection


# --------------------------------------------------------------------------
# Deck tracker (deck multiset / order across floors from floor_summary deltas)
# --------------------------------------------------------------------------


@dataclass
class DeckTracker:
    """Master deck between combats, rebuilt from the game's own floor summaries.

    Additions append, removals and in-place upgrades need the copy, which the summary
    names only by id: when the candidate copies give different orders the order is lost
    (``order`` None) and only the multiset is kept. ``multiset`` None = unknown.
    """

    order: list[tuple[str, bool]] | None
    multiset: Counter | None
    notes: list[str] = field(default_factory=list)

    @classmethod
    def from_deck(cls, deck: list[tuple[str, bool]] | None) -> DeckTracker:
        if deck is None:
            return cls(order=None, multiset=None)
        return cls(order=list(deck), multiset=deck_multiset(deck))

    def _drop_order(self, why: str) -> None:
        if self.order is not None:
            self.notes.append(why)
        self.order = None

    def _remove_one(self, card_id: str, upgraded: bool | None) -> None:
        if self.multiset is not None:
            for key in ([card_key(card_id, upgraded)] if upgraded is not None else [card_key(card_id, False), card_key(card_id, True)]):
                if self.multiset.get(key, 0) > 0:
                    self.multiset[key] -= 1
                    if not self.multiset[key]:
                        del self.multiset[key]
                    break
            else:
                self.multiset = None
                self.order = None
                return
        if self.order is not None:
            candidates = [i for i, (cid, up) in enumerate(self.order) if cid == card_id and (upgraded is None or up == upgraded)]
            if not candidates:
                self._drop_order(f"remove_target_missing:{card_id}")
                return
            outcomes = {tuple(self.order[:i] + self.order[i + 1 :]) for i in candidates}
            if len(outcomes) > 1:
                self._drop_order(f"remove_ambiguous:{card_id}")
                return
            del self.order[candidates[0]]

    def _upgrade_one(self, card_id: str) -> None:
        if self.multiset is not None:
            key = card_key(card_id, False)
            if self.multiset.get(key, 0) <= 0:
                self.multiset = None
                self.order = None
                return
            self.multiset[key] -= 1
            if not self.multiset[key]:
                del self.multiset[key]
            self.multiset[card_key(card_id, True)] += 1
        if self.order is not None:
            candidates = [i for i, (cid, up) in enumerate(self.order) if cid == card_id and not up]
            outcomes = set()
            for i in candidates:
                trial = list(self.order)
                trial[i] = (card_id, True)
                outcomes.add(tuple(trial))
            if len(outcomes) != 1:
                self._drop_order(f"upgrade_ambiguous:{card_id}")
                return
            self.order[candidates[0]] = (card_id, True)

    def _append(self, card_id: str, upgraded: bool) -> None:
        if self.multiset is not None:
            self.multiset[card_key(card_id, upgraded)] += 1
        if self.order is not None:
            self.order.append((card_id, upgraded))

    def apply_summary(self, stats: Mapping[str, Any]) -> None:
        lists = _neow_lists(stats)
        removed = [
            (strip_prefix(r.get("id"), "CARD."), _upgraded_field(r))
            for r in stats.get("cards_removed") or ()
            if isinstance(r, Mapping)
        ]
        gains = [
            (strip_prefix(g.get("id"), "CARD."), bool(_upgraded_field(g)))
            for g in stats.get("cards_gained") or ()
            if isinstance(g, Mapping)
        ]
        transforms = lists["transforms"]
        if gains and transforms:
            self._drop_order("gain_and_transform_same_floor")
        for card_id, upgraded in removed:
            self._remove_one(card_id, upgraded)
        for original, _final in transforms:
            self._remove_one(original, None)
        for card_id in lists["upgrades"]:
            self._upgrade_one(card_id)
        for _original, final in transforms:
            self._append(final, False)
        for card_id, upgraded in gains:
            self._append(card_id, upgraded)

    def reset(self, observed: Counter) -> None:
        self.multiset = Counter(observed)
        self.order = None


def _ids_only(multiset: Counter) -> Counter:
    ids: Counter = Counter()
    for key, count in multiset.items():
        ids[key.removesuffix("+")] += count
    return ids


def _upgraded_field(card: Mapping[str, Any]) -> bool | None:
    level = card.get("current_upgrade_level", card.get("upgrade_level"))
    if isinstance(level, int) and not isinstance(level, bool):
        return level > 0
    return None


# --------------------------------------------------------------------------
# X1: room windows and segment plan
# --------------------------------------------------------------------------


@dataclass
class RoomWindow:
    part_index: int
    room: EventRecord
    end_seq: int | None
    states: list[StateRecord]
    actions: list[ActionRecord]
    events: list[EventRecord]

    @property
    def act(self) -> int:
        act = self.room.data.get("act")
        return act + 1 if isinstance(act, int) else 0

    @property
    def floor(self) -> int | None:
        return _int_or_none(self.room.data.get("floor"))

    @property
    def room_type(self) -> str | None:
        return self.room.data.get("room_type")

    @property
    def model_id(self) -> str | None:
        model = self.room.data.get("room_model_id")
        return str(model) if model else None

    @property
    def key(self) -> tuple[Any, ...]:
        return (self.act, self.floor, self.room_type, self.model_id)

    @property
    def is_combat(self) -> bool:
        return bool(self.model_id and self.model_id.startswith(COMBAT_ROOM_PREFIX))

    @property
    def has_battle(self) -> bool:
        return any(is_battle_state(s.state) for s in self.states)


def room_windows(parts: Sequence[PartStreams]) -> tuple[list[RoomWindow], list[str]]:
    """Every room entry of the lineage, in part order, with its records.

    A window runs from a ``room_entered`` to the next one in the same part (or the part
    end). Records before a part's first room entry have no room; they are reported.
    """
    windows: list[RoomWindow] = []
    notes: list[str] = []
    for part_index, part in enumerate(parts):
        rooms = [e for e in part.events if e.entry == "room_entered" and isinstance(e.data, Mapping)]
        if rooms:
            orphan = [a for a in part.actions if a.seq < rooms[0].seq]
            if orphan:
                notes.append(f"{part.path.name}: {len(orphan)} actions before the first room entry")
        elif part.actions:
            notes.append(f"{part.path.name}: {len(part.actions)} actions and no room entry")
        part_windows: list[RoomWindow] = []
        for position, room in enumerate(rooms):
            end = rooms[position + 1].seq if position + 1 < len(rooms) else None
            part_windows.append(
                RoomWindow(
                    part_index=part_index,
                    room=room,
                    end_seq=end,
                    states=_window(part.states, room.seq, end),
                    actions=_window(part.actions, room.seq, end),
                    events=_window(part.events, room.seq, end),
                )
            )
        notes += hoist_combat_starting_event_options(part_windows)
        windows += part_windows
    return windows, notes


def hoist_combat_starting_event_options(windows: Sequence[RoomWindow]) -> list[str]:
    """Move an event option that started a combat back into its event's window.

    An event that fights without leaving (``EventModel.EnterCombatWithoutExitingEvent``: PUNCH_OFF FIGHT,
    Models.Events/PunchOff.cs:522-535; THE_LANTERN_KEY, DENSE_VEGETATION, BATTLEWORN_DUMMY) enters the combat room
    inside the option's handler, and the recorder's ``EventSynchronizer.SaveEventOptionToHistory`` hook logs the option
    once the handler returns: after the combat's ``room_entered``, with a battle snapshot as its state. The leading
    ``event_option`` actions of such a combat window (same act and floor as the event window before it, in the same
    part) belong to the event; their pre-state is the event window's last event snapshot (the option page; a combat
    message snapshot can follow it before the room entry).
    """
    notes: list[str] = []
    for prev, window in zip(windows, windows[1:]):
        if not window.is_combat or not (prev.model_id or "").startswith("EVENT.") or (prev.act, prev.floor) != (window.act, window.floor):
            continue
        leading: list[ActionRecord] = []
        for action in window.actions:
            if _kind(action) != "event_option":
                break
            leading.append(action)
        if not leading:
            continue
        before = [s for s in prev.states if s.seq < window.room.seq]
        pages = [s for s in before if isinstance(s.state, Mapping) and s.state.get("state_type") == "event"]
        state_seq = pages[-1].seq if pages else (before[-1].seq if before else None)
        prev.actions.extend(dataclasses.replace(a, state_seq=state_seq) for a in leading)
        del window.actions[: len(leading)]
        notes.append(
            f"{prev.model_id} f{prev.floor}: event_option seq {', '.join(str(a.seq) for a in leading)} logged after "
            f"{window.model_id}'s room entry moved to the event (pre-state seq {state_seq})"
        )
    return notes


# --------------------------------------------------------------------------
# Choice resolution
# --------------------------------------------------------------------------


def _instance_key(card: Mapping[str, Any]) -> tuple[Any, ...]:
    return tuple(sorted((k, json.dumps(v, sort_keys=True, ensure_ascii=False)) for k, v in card.items() if k not in ("index", "description")))


def resolve_card_picks(
    picked_ids: Sequence[str],
    candidates: Sequence[Mapping[str, Any]] | None,
    deck: Counter | None = None,
) -> tuple[list[dict[str, Any]], list[str]]:
    """Picks named by card id only -> candidate indexes in the select screen.

    Copies that differ in no recorded instance field are interchangeable (first unused is
    taken); otherwise the pick is not identifiable (``recorder_gap:choice_identity``).
    """
    picks: list[dict[str, Any]] = []
    reasons: list[str] = []
    used: set[int] = set()
    for raw_id in picked_ids:
        card_id = strip_prefix(raw_id, "CARD.")
        if candidates is None:
            variants = [up for up in (False, True) if deck is not None and deck.get(card_key(card_id, up), 0) > 0]
            if len(variants) == 1:
                # No select-screen snapshot, but the tracked deck holds one variant of the id:
                # every candidate copy is the same (id, upgraded); the index is the harness's.
                picks.append({"index": None, "card_id": card_id, "upgraded": variants[0], "resolved_by": "deck_multiset"})
            else:
                picks.append({"index": None, "card_id": card_id, "upgraded": None})
                reasons.append("recorder_gap:choice_identity")
            continue
        matches = [
            i for i, c in enumerate(candidates) if strip_prefix(c.get("id"), "CARD.") == card_id and i not in used
        ]
        if not matches:
            picks.append({"index": None, "card_id": card_id, "upgraded": None})
            reasons.append(f"choice_pick_not_in_candidates:{card_id}")
            continue
        if len({_instance_key(candidates[i]) for i in matches}) > 1:
            reasons.append("recorder_gap:choice_identity")
        chosen = matches[0]
        used.add(chosen)
        view = _card_view(candidates[chosen])
        index = view["index"] if view["index"] is not None else chosen
        picks.append({"index": index, "card_id": card_id, "upgraded": view["upgraded"]})
    return picks, reasons


def prompt_open_state(
    states: Sequence[StateRecord], pre: StateRecord | None, *, character: str, names: Any
) -> tuple[Mapping[str, Any] | None, list[str]]:
    """The hand-select snapshot as the prompt opened.

    A hand selection moves picked cards out of the selectable list (into
    ``selected_cards``, by localized name only), so the snapshot just before the
    confirm can lack the very card that was picked. Walk back over the contiguous
    hand-select snapshots to the earliest one; cards still listed as selected there are
    resolved by name and appended (their original positions are not recorded).
    """
    if pre is None or not isinstance(pre.state, Mapping) or pre.state.get("state_type") != "hand_select":
        return (pre.state if pre is not None else None), []
    ordered = [s for s in states if s.seq <= pre.seq]
    earliest = pre
    for record in reversed(ordered):
        if not isinstance(record.state, Mapping) or record.state.get("state_type") != "hand_select":
            break
        earliest = record
    block = earliest.state.get("hand_select")
    if not isinstance(block, Mapping):
        return earliest.state, []
    selected = [c for c in block.get("selected_cards") or () if isinstance(c, Mapping)]
    if not selected:
        return earliest.state, []
    reasons: list[str] = []
    cards = [c for c in block.get("cards") or () if isinstance(c, Mapping)]
    for card in selected:
        resolved = names.resolve(character, card.get("name")) if isinstance(card.get("name"), str) else None
        if resolved is None:
            reasons.append(f"unresolved_pile_name:{card.get('name')}")
            continue
        cards.append({"id": resolved[0], "is_upgraded": resolved[1], "name": card.get("name"), "index": len(cards)})

    def key(card: Mapping[str, Any]) -> tuple[str, bool]:
        return (strip_prefix(card.get("id"), "CARD."), bool(card.get("is_upgraded")))

    # The snapshot's player hand still lists the picked cards in hand order: when every hand
    # copy of each selectable (id, upgraded) is selectable, the prompt's candidates are those
    # hand cards in hand order (exact); otherwise the positions are unknown.
    hand = [c for c in ((earliest.state.get("player") or {}).get("hand") or ()) if isinstance(c, Mapping)]
    selectable = Counter(key(c) for c in cards)
    in_hand = Counter(key(c) for c in hand if key(c) in selectable)
    if not reasons and hand and in_hand == selectable:
        ordered = [c for c in hand if key(c) in selectable]
        cards = [{**c, "index": i} for i, c in enumerate(ordered)]
    else:
        reasons.append("recorder_gap:choice_identity")
    merged = dict(earliest.state)
    merged["hand_select"] = {**block, "cards": cards, "selected_cards": []}
    return merged, reasons


def _select_candidates(state: Mapping[str, Any] | None) -> tuple[str | None, list[Mapping[str, Any]] | None, Mapping[str, Any] | None]:
    """(state_type, raw candidate cards, block) of a select screen snapshot."""
    if not isinstance(state, Mapping):
        return None, None, None
    state_type = state.get("state_type")
    block = state.get(state_type) if isinstance(state_type, str) else None
    if not isinstance(block, Mapping):
        return state_type, None, None
    if state_type in ("hand_select", "card_select", "card_reward"):
        cards = block.get("cards")
        return state_type, [c for c in cards if isinstance(c, Mapping)] if isinstance(cards, list) else None, block
    return state_type, None, block


def _choice_kind(state_type: str | None, block: Mapping[str, Any] | None, choice_type: Any) -> str:
    if state_type == "hand_select":
        return "hand"
    if state_type == "card_select":
        return "choose_one" if (block or {}).get("screen_type") == "choose" else "grid"
    if state_type == "bundle_select":
        return "bundle"
    if state_type == "relic_select":
        return "relic"
    return "index" if choice_type == "Index" else "cards"


def build_choose(
    action: ActionRecord, state: Mapping[str, Any] | None, *, context: str, deck: Counter | None = None
) -> tuple[dict[str, Any], list[str]]:
    params = _params(action)
    choice_type = params.get("choice_type")
    state_type, candidates, block = _select_candidates(state)
    reasons: list[str] = []
    if choice_type == "Index":
        indexes = [i for i in params.get("indexes") or () if isinstance(i, int)]
        if not indexes and isinstance(params.get("index"), int):
            indexes = [params["index"]]
        skipped = not indexes or any(i < 0 for i in indexes)
        picks = []
        for i in indexes:
            if i < 0:
                continue
            pick: dict[str, Any] = {"index": i, "card_id": None, "upgraded": None}
            if candidates is not None and i < len(candidates):
                view = _card_view(candidates[i])
                pick.update(card_id=view["id"], upgraded=view["upgraded"])
            picks.append(pick)
    elif choice_type in CARD_CHOICE_TYPES:
        cards = [str(c) for c in params.get("cards") or () if c is not None]
        skipped = not cards
        picks, reasons = resolve_card_picks(cards, candidates, deck if choice_type == "DeckCard" else None)
        index_key = "combat_card_index" if choice_type == "CombatCard" else "deck_index"
        for pick in picks:
            pick[index_key] = None
    else:
        skipped = not params.get("picked")
        picks = []
        reasons.append(f"unsupported_choice_type:{choice_type}")
    action_out = {
        "type": "choose",
        "context": context,
        "kind": _choice_kind(state_type, block, choice_type),
        "choice_type": choice_type,
        "picks": picks,
        "skipped": bool(skipped),
    }
    return action_out, list(dict.fromkeys(reasons))


# --------------------------------------------------------------------------
# Lineage builder
# --------------------------------------------------------------------------


@dataclass
class ExportOptions:
    pre_state_mode: str = PRE_STATE_SETTLED
    mcr_readable: bool = False


@dataclass
class LineageContext:
    lineage: str
    parts: list[PartStreams]
    raw_manifests: list[Mapping[str, Any]]
    vocabulary: CorpusVocabulary
    registry: ContentRegistry
    options: ExportOptions
    character: str
    ascension: int
    game_version: str
    run_reasons: list[str] = field(default_factory=list)
    anchors: list[dict[str, Any]] = field(default_factory=list)
    segments: list[dict[str, Any]] = field(default_factory=list)
    run_index: int = 0
    map_graph_acts: set[int] = field(default_factory=set)

    def part_name(self, index: int) -> str:
        return self.parts[index].path.name

    def next_segment_id(self) -> str:
        return f"s{len(self.segments)}"


def _oracle(ctx: LineageContext, window: RoomWindow, action: ActionRecord, state_seq: int | None) -> dict[str, Any]:
    params = _params(action)
    oracle: dict[str, Any] = {
        "action_seq": action.seq,
        "state_seq": state_seq,
        "part": ctx.part_name(window.part_index),
        "source_hook": action.source,
        "status": action.status,
    }
    if isinstance(params.get("human"), bool):
        oracle["human"] = params["human"]
    return oracle


def _content_reasons(ctx: LineageContext, area: str, ids: Iterable[str]) -> list[str]:
    reasons = []
    singular = {"relics": "relic", "potions": "potion", "powers": "power", "monsters": "monster", "events": "event"}[area]
    for item_id in ids:
        if item_id and not ctx.registry.ported(area, item_id):
            reasons.append(f"content_not_ported:{singular}:{item_id}")
    return reasons


def attach_mcr_anchors(ctx: LineageContext, combat_parts: Mapping[str, int]) -> None:
    """``replay.mcr`` holds the last combat won inside its part (the game writes it on the
    win path, ``Combat/CombatManager.cs:1763``): attach it to that combat segment, then
    settle every combat's anchor reason."""
    by_part = {a["part_index"]: a for a in ctx.anchors if a["kind"] == "mcr_initial"}
    last_won: dict[int, dict[str, Any]] = {}
    for segment in ctx.segments:
        if segment["kind"] == "combat" and (segment["outcome"] or {}).get("result") == "win":
            last_won[combat_parts[segment["id"]]] = segment
    for part_index, segment in last_won.items():
        anchor = by_part.get(part_index)
        if anchor is None:
            continue
        segment["start_anchor"] = anchor["id"]
        segment["start"]["anchor"] = anchor["id"]
        anchor.update(act=segment["act"], floor=segment["floor"])
    for segment in ctx.segments:
        reasons = segment["eligibility"]["reasons"]
        if ANCHOR_PENDING not in reasons:
            continue
        replacement = "anchor_missing"
        if segment["start_anchor"] is not None:
            replacement = None if ctx.options.mcr_readable else "anchor_unreadable:mcr_decoder_missing"
        segment["eligibility"]["reasons"] = [r for r in (replacement if x == ANCHOR_PENDING else x for x in reasons) if r]


def index_anchors(ctx: LineageContext) -> None:
    """Anchors present on disk: ``native/replay.mcr`` (the part's last won combat) and,
    when the recorder wrote them (R1), ``native/saves/*.save``."""
    for part_index, part in enumerate(ctx.parts):
        native = part.path / "native"
        mcr = native / "replay.mcr"
        if mcr.is_file():
            ctx.anchors.append(
                {
                    "id": f"a{len(ctx.anchors)}",
                    "kind": "mcr_initial",
                    "seq": None,
                    "act": None,
                    "floor": None,
                    "ref": f"{part.path.name}/native/replay.mcr",
                    "sha256": _sha256(mcr),
                    "bytes": mcr.stat().st_size,
                    "part_index": part_index,
                }
            )
        saves = native / "saves"
        if saves.is_dir():
            # The recorder names each copy after its ``run_saved`` event's own (top-level) seq and
            # states the anchor kind in the event data (R1, docs/hook-map.md).
            saved_events = {
                e.seq: e
                for e in part.events
                if e.entry == "run_saved" and isinstance(e.data, Mapping)
            }
            for save in sorted(saves.glob("*.save")):
                seq = int(save.stem) if save.stem.isdigit() else None
                event = saved_events.get(seq)
                data = event.data if event is not None else {}
                kind = data.get("anchor_kind")
                if kind not in SAVE_ANCHOR_KINDS:
                    kind = "combat_exit" if data.get("pre_finished") else "room_entry"
                ctx.anchors.append(
                    {
                        "id": f"a{len(ctx.anchors)}",
                        "kind": kind,
                        "seq": seq,
                        "act": _int_or_none(data.get("act")),
                        "floor": _int_or_none(data.get("floor")),
                        "ref": f"{part.path.name}/native/saves/{save.name}",
                        "sha256": _sha256(save),
                        "bytes": save.stat().st_size,
                        "part_index": part_index,
                    }
                )


# Save-anchor kinds the recorder writes (R1): before a room is entered, after an ancient's
# event, after a combat's room is finished.
SAVE_ANCHOR_KINDS = ("room_entry", "ancient_exit", "combat_exit")

# Eligibility reasons about rebuilding the start state from the trajectory; a save anchor
# carries the real start (deck order, RNG counters, relic state), so they do not apply.
START_RECONSTRUCTION_REASONS = (
    ANCHOR_PENDING,
    "anchor_missing",
    "anchor_unreadable",
    "recorder_gap:deck_order",
    "deck_reconstruction_failed",
    "deck_upgrade_mismatch",
    "relic_not_whitelisted",
    "relic_excluded",
    "neow_",
)


def _is_start_reason(reason: str) -> bool:
    for prefix in START_RECONSTRUCTION_REASONS:
        if reason == prefix or reason.startswith(prefix + ":") or (prefix.endswith("_") and reason.startswith(prefix)):
            return True
    return False


def bind_save_anchors(ctx: LineageContext) -> None:
    """Design §3: a segment that starts where the game wrote a save starts from that save.

    A ``room_entry`` save is written after map travel, before ``room_entered`` (the room is
    built on load); it starts the combat segment, or the room segment, of the room entered
    next in its part. A ``combat_exit`` save (pre-finished combat room) starts the
    post-combat room segment of its window. ``ancient_exit`` saves are indexed only (the
    sim resumes pre-finished combat rooms, not events)."""
    rooms_by_part: dict[int, list[int]] = {}
    for part_index, part in enumerate(ctx.parts):
        rooms_by_part[part_index] = sorted(e.seq for e in part.events if e.entry == "room_entered")
    entry_anchor: dict[tuple[int, int], dict[str, Any]] = {}
    exit_anchors: dict[int, list[dict[str, Any]]] = {}
    for anchor in ctx.anchors:
        part_index, seq = anchor.get("part_index"), anchor.get("seq")
        if part_index is None or seq is None:
            continue
        if anchor["kind"] == "room_entry":
            following = [r for r in rooms_by_part.get(part_index, ()) if r > seq]
            if following:
                # The latest room_entry save before a room entry wins (a reload saves again).
                key = (part_index, following[0])
                if key not in entry_anchor or entry_anchor[key]["seq"] < seq:
                    entry_anchor[key] = anchor
        elif anchor["kind"] == "combat_exit":
            exit_anchors.setdefault(part_index, []).append(anchor)
    part_of = {ctx.part_name(i): i for i in range(len(ctx.parts))}
    for segment in ctx.segments:
        part_index = part_of.get(segment.get("part"))
        window = segment.get("window") or {}
        start_seq, end_seq = window.get("start_seq"), window.get("end_seq")
        if part_index is None or start_seq is None:
            continue
        anchor = None
        if segment["kind"] == "combat" or (segment["kind"] == "room" and segment.get("phase") is None):
            anchor = entry_anchor.get((part_index, start_seq))
        elif segment["kind"] == "room" and segment.get("phase") == "post_combat":
            room_seq = segment.get("room_seq")
            candidates = [
                a for a in exit_anchors.get(part_index, ())
                if (room_seq is None or a["seq"] > room_seq) and (end_seq is None or a["seq"] < end_seq)
            ]
            anchor = candidates[-1] if candidates else None
        if anchor is None:
            continue
        segment["start_anchor"] = anchor["id"]
        if segment["kind"] == "combat":
            # The v1 fields stay for the record; "from": "anchor" makes the harness load the save.
            segment["start"] = {"from": "anchor", "anchor": anchor["id"], **(segment.get("start") or {})}
        elif isinstance(segment.get("start"), dict):
            segment["start"]["from"] = "anchor"
            segment["start"]["anchor"] = anchor["id"]
        if anchor.get("act") is None:
            anchor["act"] = segment.get("act")
        if anchor.get("floor") is None:
            anchor["floor"] = segment.get("floor")
        reasons = segment["eligibility"]["reasons"]
        segment["eligibility"]["reasons"] = [r for r in reasons if not _is_start_reason(r)]


def _floor_summary(window: RoomWindow) -> dict[str, Any] | None:
    summaries = [e for e in window.events if e.entry == "floor_summary" and isinstance(e.data, Mapping)]
    if not summaries:
        return None
    data = summaries[-1].data
    stats = data.get("player_stats") or ()
    return {
        "seq": summaries[-1].seq,
        "map_point_type": data.get("map_point_type"),
        "rooms": data.get("rooms") or [],
        "player_stats": stats[0] if stats and isinstance(stats[0], Mapping) else None,
    }


def _segment(
    ctx: LineageContext,
    window: RoomWindow,
    *,
    kind: str,
    attempt: int,
    window_start: int,
    window_end: int | None,
    phase: str | None = None,
) -> dict[str, Any]:
    model = window.model_id
    return {
        "id": ctx.next_segment_id(),
        "kind": kind,
        "act": window.act,
        "floor": window.floor,
        "room_type": window.room_type,
        "room_model_id": model,
        "attempt": attempt,
        "phase": phase,
        "part": ctx.part_name(window.part_index),
        "window": {"start_seq": window_start, "end_seq": window_end},
        "start_anchor": None,
        "end_anchor": None,
        "start": None,
        "decisions": [],
        "outcome": None,
        "floor_summary": None,
        "eligibility": {"eligible": False, "reasons": []},
        "notes": [],
    }


def _finish_decisions(ctx: LineageContext, decisions: list[dict[str, Any]]) -> None:
    for index, decision in enumerate(decisions):
        decision["index"] = index
        decision["run_index"] = ctx.run_index
        ctx.run_index += 1


# ---- combat segment (X3 + X4) ---------------------------------------------


def build_combat_segment(
    ctx: LineageContext,
    window: RoomWindow,
    *,
    attempt: int,
    first_combat: bool,
    neow_stats: Mapping[str, Any] | None,
    tracker: DeckTracker,
) -> tuple[dict[str, Any], int | None]:
    """Combat segment of one attempt; returns (segment, combat end seq)."""
    segment = _segment(ctx, window, kind="combat", attempt=attempt, window_start=window.room.seq, window_end=window.end_seq)
    reasons: list[str] = []
    notes: list[str] = segment["notes"]
    encounter = strip_prefix(window.model_id, COMBAT_ROOM_PREFIX)

    end_seq, outcome, end_how = _combat_end(window.states, window.events)
    if end_seq is None:
        last_battle = max(s.seq for s in window.states if is_battle_state(s.state))
        run_level = [a.seq for a in window.actions if _kind(a) in RUN_LEVEL_KINDS and a.seq > last_battle]
        if run_level:
            end_seq, outcome, end_how = min(run_level), "win", "run_level_action"
    if outcome is None:
        outcome, end_how = ("win", "next_room_entered") if window.end_seq is not None else ("abandoned", "part_ended")

    def in_combat(item: Any) -> bool:
        return end_seq is None or item.seq < end_seq

    battle_states = [s for s in window.states if is_battle_state(s.state) and in_combat(s)]
    first_state = battle_states[0].state
    if first_state["battle"].get("round") != 1:
        reasons.append(f"attempt_not_from_combat_start:round{first_state['battle'].get('round')}")
    player = first_state["player"]
    relics = [str(r.get("id")) for r in player.get("relics") or () if isinstance(r, Mapping) and r.get("id") is not None]
    potions = [
        {"slot": _int_or_none(p.get("slot")), "id": str(p.get("id"))}
        for p in player.get("potions") or ()
        if isinstance(p, Mapping) and p.get("id") is not None
    ]

    # Deck: v1 reconstruction for the first combat, the floor-summary tracker after it.
    observed, snapshot_issues = snapshot_deck_multiset(first_state, character=ctx.character, names=ctx.vocabulary.names)
    reasons += snapshot_issues
    deck: list[tuple[str, bool]] | None
    if first_combat:
        deck, deck_issues = reconstruct_deck(ctx.character, ctx.ascension, neow_stats)
        reasons += [r for r in deck_issues if r != "neow_summary_missing"]
        if "neow_summary_missing" in deck_issues:
            reasons.append("deck_reconstruction_failed:neow_summary_missing")
            deck = None
        if deck is None and not snapshot_issues:
            base = base_deck(ctx.character, ctx.ascension) or []
            if deck_multiset(base) == observed:
                deck = base
                reasons = [r for r in reasons if r != "deck_reconstruction_failed:neow_summary_missing"]
                notes.append("neow summary missing; combat deck equals the starter deck, so its order is known")
    else:
        deck = list(tracker.order) if tracker.order is not None else None
    tracked = deck_multiset(deck) if deck is not None else tracker.multiset
    generated_before = any(e.entry == "card_generated" and e.seq < battle_states[0].seq for e in window.events)
    if not snapshot_issues:
        if tracked is None:
            reasons.append("recorder_gap:deck_order")
            if not generated_before:
                tracker.reset(observed)
        else:
            delta = multiset_delta(Counter(tracked), observed)
            if delta and _ids_only(Counter(tracked)) == _ids_only(observed):
                # Same cards, different upgrade flags: an in-combat upgrade before the first
                # snapshot (varies per combat in the corpus), or an upgrade the summaries did
                # not name. The tracked master deck is kept in the bundle and in the tracker.
                reasons.append(f"deck_upgrade_mismatch:{delta}")
            elif delta:
                reasons.append(f"deck_reconstruction_failed:{delta}")
                deck = None
                if not generated_before:
                    tracker.reset(observed)
        if deck is None and tracked is not None and not any(r.startswith("deck_reconstruction_failed") for r in reasons):
            reasons.append("recorder_gap:deck_order")
    if first_combat and deck is not None:
        tracker.order = list(deck)
        tracker.multiset = deck_multiset(deck)

    # Start state: rng_offsets from the v1 recipe only for the run's first combat under the
    # rules version (its counters follow from Neow); otherwise an anchor is required.
    start: dict[str, Any] = {
        "encounter_id": encounter,
        "player": {"hp": player.get("hp"), "max_hp": player.get("max_hp")},
        "deck": [{"id": cid, "upgraded": up} for cid, up in deck] if deck is not None else None,
        "deck_multiset": sorted(observed.elements()) if not snapshot_issues else None,
        "relics": relics,
        "potions": potions,
        "max_potion_slots": _int_or_none(player.get("max_potion_slots")),
        "gold": _int_or_none(player.get("gold")),
    }
    if first_combat and (window.act, window.floor) == (1, 2) and ctx.game_version == RULES_VERSION:
        start["rng_offsets"] = {"Niche": niche_offset(relics)}
        reasons += relic_reasons(relics)
    else:
        # Resolved by attach_mcr_anchors once every combat of the part is known.
        reasons.append(ANCHOR_PENDING)
    segment["start"] = start

    # Content coverage.
    reasons += sim_coverage_reasons(ctx.registry.base, character=ctx.character, encounter_id=encounter, deck=deck)
    if deck is None:
        for key in sorted(observed):
            card_id, upgraded = (key[:-1], True) if key.endswith("+") else (key, False)
            if card_id not in ctx.registry.base.cards:
                reasons.append(f"card_not_ported:{card_id}")
            elif upgraded and card_id not in ctx.registry.base.upgradable_cards:
                reasons.append(f"card_upgrade_not_ported:{card_id}")
    reasons += _content_reasons(ctx, "relics", relics)
    monster_ids = extract_monster_ids(e.data for e in window.events)
    reasons += _content_reasons(ctx, "monsters", sorted({strip_prefix(m, "MONSTER.") for m in monster_ids.values()}))
    powers_seen: set[str] = set()
    for record in battle_states:
        for holder in [record.state["player"], *(record.state["battle"].get("enemies") or ())]:
            if isinstance(holder, Mapping):
                for status in holder.get("status") or ():
                    if isinstance(status, Mapping) and isinstance(status.get("id"), str):
                        powers_seen.add(status["id"])
    for event in window.events:
        if event.entry == "power_received" and in_combat(event) and isinstance(event.data, Mapping):
            power = event.data.get("power")
            if isinstance(power, str):
                powers_seen.add(strip_prefix(power, "POWER."))
    reasons += _content_reasons(ctx, "powers", sorted(powers_seen))

    # Decisions: v1 rules for play_card / end_turn, v2 additions merged by seq.
    combat_actions = [a for a in window.actions if in_combat(a)]
    v1_actions = [a for a in combat_actions if _kind(a) in V1_COMBAT_KINDS]
    v1_decisions, v1_reasons, v1_notes, counts = extract_decisions(
        states=window.states,
        actions=v1_actions,
        events=window.events,
        character=ctx.character,
        vocabulary=ctx.vocabulary,
        monster_ids=monster_ids,
        pre_state_mode=ctx.options.pre_state_mode,
    )
    reasons += v1_reasons
    notes += v1_notes
    states_by_seq = {s.seq: s for s in window.states}
    actions_by_seq = {a.seq: a for a in combat_actions}
    decisions: list[dict[str, Any]] = []
    for decision in v1_decisions:
        action = actions_by_seq.get(decision["oracle"].get("action_seq"))
        out = {
            "index": 0,
            "run_index": 0,
            "screen": "combat",
            "pre_state": decision["pre_state"],
            "oracle": {**decision["oracle"], **(_oracle(ctx, window, action, decision["oracle"].get("state_seq")) if action else {})},
            "action": decision["action"],
        }
        out["oracle"]["settled"] = decision["oracle"].get("settled")
        if out["pre_state"] is not None and action is not None:
            snapshot = states_by_seq.get(out["oracle"]["state_seq"])
            if snapshot is not None:
                out["pre_state"] = {**out["pre_state"], "player": {**out["pre_state"]["player"], **combat_projection_extras(snapshot.state.get("player"))}}
        decisions.append(out)
    has_undo = False
    for action in combat_actions:
        kind = _kind(action)
        if kind in V1_COMBAT_KINDS:
            continue
        if action.status == "cancelled":
            notes.append(f"cancelled {kind} at seq {action.seq} skipped")
            continue
        params = _params(action)
        pre = states_by_seq.get(action.state_seq) if action.state_seq is not None else None
        pre_state: dict[str, Any] | None = None
        oracle = _oracle(ctx, window, action, action.state_seq)
        if pre is not None and is_battle_state(pre.state):
            projection, issues = project_state(pre.state, character=ctx.character, names=ctx.vocabulary.names, monster_ids=monster_ids)
            pending = [e.entry for e in window.events if pre.seq < e.seq < action.seq and e.entry not in NON_STATE_EVENT_ENTRIES]
            oracle["settled"] = not pending
            oracle["pending_events"] = pending
            if pending:
                oracle["unobserved_reason"] = "mid_resolution_snapshot"
            else:
                pre_state = {**projection, "player": {**projection["player"], **combat_projection_extras(pre.state.get("player"))}}
                issues = [i for i in issues if not i.startswith("pre_state_not_player_play")]
                reasons += issues
        else:
            oracle["unobserved_reason"] = "no_battle_snapshot"
        if kind == "use_potion":
            target = _int_or_none(params.get("target_id"))
            out_action = {
                "type": "use_potion",
                "slot": _int_or_none(params.get("potion_index")),
                "potion_id": strip_prefix(params.get("potion_id"), "POTION.") if params.get("potion_id") else None,
                "target_combat_id": target,
                "in_combat": True,
            }
            if out_action["potion_id"]:
                reasons += _content_reasons(ctx, "potions", [out_action["potion_id"]])
        elif kind == "discard_potion":
            slot = _int_or_none(params.get("potion_index"))
            potion_id = None
            if pre is not None:
                for potion in (pre.state.get("player") or {}).get("potions") or ():
                    if isinstance(potion, Mapping) and potion.get("slot") == slot:
                        potion_id = potion.get("id")
            out_action = {"type": "discard_potion", "slot": slot, "potion_id": potion_id, "target_combat_id": None, "in_combat": True}
        elif kind == "undo_end_turn":
            has_undo = True
            out_action = {"type": "undo_end_turn"}
        elif kind == "player_choice":
            prompt_state, prompt_reasons = prompt_open_state(
                window.states, pre, character=ctx.character, names=ctx.vocabulary.names
            )
            out_action, choice_reasons = build_choose(action, prompt_state, context="combat")
            if not out_action["skipped"]:
                reasons += prompt_reasons
            reasons += choice_reasons
            _, candidates, block = _select_candidates(prompt_state)
            if candidates is not None:
                select = project_select(prompt_state.get("state_type"), block)
                if pre_state is not None:
                    pre_state = {**pre_state, "select": select}
                elif not is_battle_state(pre.state):
                    pre_state = {"select": select}
                oracle["candidates"] = select.get("candidates")
        else:
            reasons.append(f"unsupported_action:{kind}")
            continue
        decisions.append({"index": 0, "run_index": 0, "screen": "combat", "pre_state": pre_state, "oracle": oracle, "action": out_action})
    if has_undo and any("duplicate end_turn" in n for n in v1_notes):
        reasons.append("unsupported_action:end_turn_after_undo")
    decisions.sort(key=lambda d: d["oracle"].get("action_seq") or 0)
    _finish_decisions(ctx, decisions)
    segment["decisions"] = decisions
    if not decisions:
        reasons.append("no_decisions")
    if any(d["action"] is None for d in decisions):
        reasons.append("decision_unresolved")
    final_hp = _final_player_hp(battle_states[-1], [e for e in window.events if in_combat(e)])
    segment["outcome"] = {"result": outcome, "final_hp": final_hp, "evidence": end_how, "end_seq": end_seq}
    segment["counts"] = {
        "unsettled": counts["unsettled"],
        "rolled_forward": counts["rolled_forward"],
        "unobserved": counts["unobserved"],
    }
    segment["eligibility"]["reasons"] = list(dict.fromkeys(reasons))
    return segment, end_seq


# ---- run-level decisions (X5) --------------------------------------------


def _event_option_index(params: Mapping[str, Any], state: Mapping[str, Any] | None) -> tuple[str | None, int | None]:
    # R5 records the option's own key as text_key; the title key can be a relic's (OROBAS: "SEA_GLASS.IRONCLAD.title").
    option_key = params.get("text_key")
    if isinstance(option_key, str) and ".pages." in option_key and ".options." in option_key:
        key = option_key
    else:
        key = params.get("title_key") or params.get("text_key")
    text_key = str(key).removesuffix(".title") if key else None
    if not isinstance(state, Mapping) or not isinstance(state.get("event"), Mapping):
        return text_key, _int_or_none(params.get("index"))
    options = [o for o in state["event"].get("options") or () if isinstance(o, Mapping)]
    for option in options:
        if text_key and option.get("text_key") in (text_key, key):
            return text_key, _int_or_none(option.get("index"))
    title = params.get("title")
    matches = [o for o in options if title is not None and o.get("title") == title]
    if len(matches) == 1:
        return text_key, _int_or_none(matches[0].get("index"))
    return text_key, _int_or_none(params.get("index"))


def _shop_slot(
    params: Mapping[str, Any], state: Mapping[str, Any] | None, after: Mapping[str, Any] | None = None
) -> tuple[int | None, str | None, bool]:
    """(slot, item id, unique) of a purchase, matched on category, price and sale flag.

    The recorder logs ``item_id: null`` for card purchases; when two stocked entries share
    category, price and sale flag, the shop state after the purchase names the slot that
    emptied (``after``)."""
    if not isinstance(state, Mapping) or not isinstance(state.get("shop"), Mapping):
        return None, None, False
    category = params.get("item")
    cost = params.get("cost")
    matches = [
        item
        for item in state["shop"].get("items") or ()
        if isinstance(item, Mapping)
        and item.get("category") == category
        and item.get("price") == cost
        and item.get("is_stocked", True)
        and (params.get("on_sale") is None or bool(item.get("on_sale")) == bool(params.get("on_sale")))
    ]
    if not matches:
        return None, None, False
    ids = {m.get("card_id") or m.get("relic_id") or m.get("potion_id") for m in matches}
    if len(ids) > 1 and isinstance(after, Mapping) and isinstance(after.get("shop"), Mapping):
        emptied = {
            _int_or_none(item.get("index"))
            for item in after["shop"].get("items") or ()
            if isinstance(item, Mapping) and item.get("is_stocked", True) is False
        }
        narrowed = [m for m in matches if _int_or_none(m.get("index")) in emptied]
        if len({m.get("card_id") or m.get("relic_id") or m.get("potion_id") for m in narrowed}) == 1:
            matches = narrowed
            ids = {narrowed[0].get("card_id") or narrowed[0].get("relic_id") or narrowed[0].get("potion_id")}
    item = matches[0]
    item_id = item.get("card_id") or item.get("relic_id") or item.get("potion_id")
    return _int_or_none(item.get("index")), item_id, len(ids) == 1


def _room_context(window: RoomWindow, phase: str | None) -> str:
    if phase == "post_combat":
        return "reward"
    return {"Shop": "shop", "RestSite": "rest", "Event": "event", "Treasure": "reward"}.get(str(window.room_type), "deck_op")


def build_run_decisions(
    ctx: LineageContext,
    window: RoomWindow,
    actions: Sequence[ActionRecord],
    *,
    phase: str | None,
    summary: Mapping[str, Any] | None,
    deck: Counter | None = None,
) -> tuple[list[dict[str, Any]], list[str], set[str]]:
    """Run-level decisions of one room (or post-combat) window, in logical order.

    The recorder logs a pick before the action that opened it (the card-reward ``Index``
    choice before its ``reward_taken``, the deck choice before ``rest_site_option`` /
    ``shop_card_removal``); such pairs are emitted opener first (``oracle.recorded_order``).
    """
    states_by_seq = {s.seq: s for s in window.states}
    decisions: list[dict[str, Any]] = []
    reasons: list[str] = []
    screens: set[str] = set()
    stats = (summary or {}).get("player_stats") or {}
    offered_groups = [
        c for c in stats.get("card_choices") or () if isinstance(c, Mapping)
    ]
    card_choice_ids = [strip_prefix((c.get("card") or {}).get("id"), "CARD.") for c in offered_groups]
    pending_choice: dict[str, Any] | None = None
    items = [a for a in actions if _kind(a) not in FOLLOW_UP_KINDS and a.status != "cancelled"]
    position = 0
    while position < len(items):
        action = items[position]
        nxt = items[position + 1] if position + 1 < len(items) else None
        position += 1
        kind = _kind(action)
        params = _params(action)
        pre = states_by_seq.get(action.state_seq) if action.state_seq is not None else None
        state = pre.state if pre is not None else None
        screen = state.get("state_type") if isinstance(state, Mapping) else None
        oracle = _oracle(ctx, window, action, action.state_seq)
        pre_state = None
        if isinstance(state, Mapping):
            graph = screen == "map" and window.act not in ctx.map_graph_acts
            pre_state = project_run_screen(state, map_graph=graph)
            if graph:
                ctx.map_graph_acts.add(window.act)
        out: list[dict[str, Any]] = []

        def emit(action_out: dict[str, Any], screen_name: str, *, oracle_extra: Mapping[str, Any] | None = None, base=None) -> None:
            source = base or action
            record_oracle = _oracle(ctx, window, source, source.state_seq) if base is not None else dict(oracle)
            if oracle_extra:
                record_oracle.update(oracle_extra)
            source_state = states_by_seq.get(source.state_seq) if source.state_seq is not None else None
            projection = pre_state if base is None else (
                project_run_screen(source_state.state, map_graph=False) if source_state is not None else None
            )
            out.append({"index": 0, "run_index": 0, "screen": screen_name, "pre_state": projection, "oracle": record_oracle, "action": action_out})
            screens.add(screen_name)

        if kind == "vote_for_map_coord":
            destination = params.get("destination") or {}
            emit({"type": "map_move", "col": _int_or_none(destination.get("col")), "row": _int_or_none(destination.get("row"))}, "map")
        elif kind == "vote_to_move_to_next_act":
            emit({"type": "next_act"}, "map")
        elif kind == "event_option":
            option_state = state
            if not (isinstance(state, Mapping) and isinstance(state.get("event"), Mapping) and state["event"].get("options")):
                earlier = [
                    s for s in window.states
                    if s.seq <= action.seq and isinstance(s.state.get("event"), Mapping) and s.state["event"].get("options")
                ]
                if earlier:
                    option_state = earlier[-1].state
                    oracle["options_state_seq"] = earlier[-1].seq
            text_key, index = _event_option_index(params, option_state)
            if index is None:
                reasons.append("recorder_gap:event_option_key")
            emit({"type": "event_option", "text_key": text_key, "index": index}, "event")
        elif kind == "event_proceed":
            emit({"type": "event_proceed", "text_key": params.get("text_key"), "index": _int_or_none(params.get("index"))}, "event")
        elif kind == "player_choice" and screen in ("rewards", "card_reward", "event", "monster", "elite", "boss") and (
            closed := card_reward_closed_by_skip(action, items[position:])
        ) is not None:
            # A card-reward selection closed through Skip: the reward stays on the screen and a later Index pick completes
            # it (card_reward_closed_by_skip). Opened, then skipped; the later pick opens it again.
            reward = _params(closed)
            emit(
                {"type": "reward_take", "set_index": _int_or_none(reward.get("set_index")), "reward_type": reward.get("reward_type"), "item_id": None},
                "rewards",
                oracle_extra={"recorded_order": "skipped_selection", "completed_by_seq": closed.seq},
            )
            emit(
                {"type": "card_reward_skip", "index": _int_or_none(params.get("index")), "card_id": None},
                "card_reward",
                oracle_extra={"offered": card_choice_ids or None},
            )
        # The last snapshot before a card pick can still be the combat room's (``monster``/``elite``/``boss``) when the
        # rewards screen opened without a new snapshot (E3 farm: GREMLIN_MERC_NORMAL after the gold steal).
        elif kind == "player_choice" and params.get("choice_type") == "Index" and screen in ("rewards", "card_reward", "event", "monster", "elite", "boss") and (
            nxt is not None and _kind(nxt) == "reward_taken" and _params(nxt).get("reward_type") in CARD_REWARD_TYPES
        ):
            reward = _params(nxt)
            position += 1
            index = _int_or_none(params.get("index"))
            card_id = None
            if isinstance(state, Mapping) and isinstance(state.get("card_reward"), Mapping):
                cards = _cards(state["card_reward"].get("cards")) or []
                card_id = cards[index]["id"] if index is not None and index < len(cards) else None
            offer = reward.get("offer") if isinstance(reward.get("offer"), Mapping) else None
            offer_ids = [strip_prefix(str(c.get("id")), "CARD.") for c in (offer or {}).get("cards") or () if isinstance(c, Mapping)]
            if card_id is None and offer_ids and index is not None and index < len(offer_ids):
                # R4: the recorder lists the offered cards on reward_taken.
                card_id = offer_ids[index]
            if card_id is None:
                picked = [cid for c, cid in zip(offered_groups, card_choice_ids) if c.get("was_picked")]
                card_id = picked[0] if len(picked) == 1 else None
                if not (isinstance(state, Mapping) and state.get("state_type") == "card_reward"):
                    reasons.append("recorder_gap:card_reward_offer")
            emit(
                {"type": "reward_take", "set_index": _int_or_none(reward.get("set_index")), "reward_type": reward.get("reward_type"), "item_id": None},
                "rewards",
                oracle_extra={"recorded_order": "after_pick"},
                base=nxt,
            )
            emit(
                {"type": "card_reward_pick", "index": index, "card_id": card_id},
                "card_reward",
                oracle_extra={"offered": card_choice_ids or None},
            )
        elif kind == "player_choice" and params.get("choice_type") == "Index" and screen in ("rewards", "card_reward"):
            index = _int_or_none(params.get("index"))
            if not (isinstance(state, Mapping) and state.get("state_type") == "card_reward"):
                reasons.append("recorder_gap:card_reward_offer")
            emit({"type": "card_reward_skip", "index": index, "card_id": None}, "card_reward", oracle_extra={"offered": card_choice_ids or None})
        elif kind == "player_choice" and nxt is not None and _kind(nxt) in ("rest_site_option", "shop_card_removal"):
            pending_choice = {"action": action, "state": state}
            continue
        elif kind == "player_choice":
            choose, choice_reasons = build_choose(action, state, context=_room_context(window, phase), deck=deck)
            reasons += choice_reasons
            select_screen = screen if screen in SELECT_STATE_TYPES else "select"
            emit(choose, "select")
            if select_screen != "select":
                out[-1]["oracle"]["select_state"] = select_screen
        elif kind in ("rest_site_option", "shop_card_removal"):
            if pending_choice is not None and isinstance(pending_choice["state"], Mapping):
                # The opener is recorded after its deck choice resolved (MerchantCardRemovalEntry.OnTryPurchaseWrapper
                # pays first, so its own pre_state is post-payment); the state before the choice is the opener's
                # pre_state.
                pre_state = project_run_screen(pending_choice["state"], map_graph=False)
                oracle["pre_state_seq"] = pending_choice["action"].state_seq
            if kind == "rest_site_option":
                emit({"type": "rest_option", "option_id": params.get("option_id")}, "rest_site")
                context = "rest"
            else:
                emit({"type": "shop_remove", "price": _int_or_none(params.get("cost"))}, "shop")
                context = "shop"
            if pending_choice is not None:
                choice_action = pending_choice["action"]
                choose, choice_reasons = build_choose(choice_action, pending_choice["state"], context=context, deck=deck)
                choose["context"] = context
                reasons += choice_reasons
                emit(choose, "select", oracle_extra={"recorded_order": "before_opener"}, base=choice_action)
                pending_choice = None
        elif kind == "reward_taken":
            reward_type = params.get("reward_type")
            item_id = params.get("item_id")
            if reward_type == "relic_reward" and not item_id:
                reasons.append("recorder_gap:relic_reward_id")
            emit(
                {
                    "type": "reward_take",
                    "set_index": _int_or_none(params.get("set_index")),
                    "reward_type": reward_type,
                    "item_id": strip_prefix(strip_prefix(item_id, "POTION."), "RELIC.") if item_id else None,
                },
                "rewards",
            )
            if reward_type == "potion_reward" and item_id:
                reasons.extend(_content_reasons(ctx, "potions", [strip_prefix(item_id, "POTION.")]))
            if reward_type == "relic_reward" and item_id:
                reasons.extend(_content_reasons(ctx, "relics", [strip_prefix(item_id, "RELIC.")]))
        elif kind == "rewards_skipped":
            emit({"type": "rewards_skip"}, "rewards")
        elif kind == "open_chest":
            emit({"type": "treasure_open"}, "treasure")
        elif kind == "pick_relic":
            relic_id = strip_prefix(params.get("relic_id"), "RELIC.") if params.get("relic_id") else None
            emit(
                {"type": "treasure_pick", "index": _int_or_none(params.get("relic_index")), "relic_id": relic_id, "skipped": bool(params.get("skipped"))},
                "treasure",
            )
            if relic_id:
                reasons.extend(_content_reasons(ctx, "relics", [relic_id]))
        elif kind == "shop_purchase":
            after = next(
                (r.state for r in window.states if r.seq > action.seq and isinstance(r.state.get("shop"), Mapping)), None
            )
            slot, item_id, unique = _shop_slot(params, state, after)
            if slot is None:
                reasons.append("shop_purchase_unresolved")
            elif not unique:
                reasons.append("shop_purchase_ambiguous")
            item_id = item_id or params.get("item_id")
            emit(
                {
                    "type": "shop_buy",
                    "category": params.get("item"),
                    "item_id": strip_prefix(strip_prefix(strip_prefix(item_id, "CARD."), "RELIC."), "POTION.") if item_id else None,
                    "price": _int_or_none(params.get("cost")),
                    "slot": slot,
                },
                "shop",
            )
        elif kind in ("use_potion", "discard_potion"):
            slot = _int_or_none(params.get("potion_index"))
            potion_id = strip_prefix(params.get("potion_id"), "POTION.") if params.get("potion_id") else None
            if potion_id is None and isinstance(state, Mapping):
                for potion in (state.get("player") or {}).get("potions") or ():
                    if isinstance(potion, Mapping) and potion.get("slot") == slot:
                        potion_id = potion.get("id")
            emit(
                {"type": kind, "slot": slot, "potion_id": potion_id, "target_combat_id": _int_or_none(params.get("target_id")), "in_combat": False},
                str(screen or "unknown"),
            )
            if kind == "use_potion" and potion_id:
                reasons.extend(_content_reasons(ctx, "potions", [potion_id]))
        elif kind in ("play_card", "end_turn", "undo_end_turn"):
            reasons.append(f"combat_action_outside_combat:{kind}")
            continue
        else:
            reasons.append(f"unsupported_action:{kind}")
            continue
        decisions.extend(out)
    if pending_choice is not None:
        reasons.append("choice_without_opener")
    return decisions, list(dict.fromkeys(reasons)), screens


def card_reward_closed_by_skip(action: ActionRecord, later: Sequence[ActionRecord]) -> ActionRecord | None:
    """The ``reward_taken`` of the card reward whose selection ``action`` closed through its Skip alternative.

    CardReward.OnSelect syncs one Index per selection (Rewards/CardReward.cs:604-634): 0..N-1 take an offered card, N..
    pick an alternative (:646-658), generated as Skip when the reward can be skipped, then REROLL when it can be rerolled
    (Entities.CardRewardAlternatives/CardRewardAlternative.cs Generate). Skip is EndSelectionAndDoNotCompleteReward: the
    selection ends and the reward stays on the rewards screen, so only a later pick that completes it is followed by a
    ``reward_taken`` (whose R4 offer gives N, can_skip and can_reroll). ``later`` are the window's actions after
    ``action``; the completing pick must follow it with only Index choices in between (farm UUR25UNBL5 f5 BRAIN_LEECH:
    Index 3 of 3 cards, then Index 2 and its reward_taken). None when ``action`` is not such a choice.
    """
    params = _params(action)
    index = _int_or_none(params.get("index"))
    if params.get("choice_type") != "Index" or index is None or index < 0:
        return None
    for position, nxt in enumerate(later):
        if _kind(nxt) == "reward_taken":
            return None
        if _kind(nxt) != "player_choice" or _params(nxt).get("choice_type") != "Index":
            return None
        taken = later[position + 1] if position + 1 < len(later) else None
        if taken is None or _kind(taken) != "reward_taken" or _params(taken).get("reward_type") not in CARD_REWARD_TYPES:
            continue
        offer = _params(taken).get("offer")
        if not isinstance(offer, Mapping) or not isinstance(offer.get("cards"), list):
            return None
        alternatives = (["Skip"] if offer.get("can_skip") else []) + (["REROLL"] if offer.get("can_reroll") else [])
        alternative = index - len(offer["cards"])
        if 0 <= alternative < len(alternatives) and alternatives[alternative] == "Skip":
            return taken
        return None
    return None


def _room_area(window: RoomWindow) -> str | None:
    if window.model_id == NEOW_ROOM_ID or (window.model_id or "").startswith("EVENT.") and window.floor == 1:
        return "neow"
    if window.is_combat:
        return "rewards"
    return ROOM_TYPE_AREA.get(str(window.room_type))


def _finish_run_segment(ctx: LineageContext, segment: dict[str, Any], window: RoomWindow, decisions, reasons, screens) -> None:
    _finish_decisions(ctx, decisions)
    segment["decisions"] = decisions
    reasons = list(reasons)
    areas = {SCREEN_AREA[s] for s in screens if s in SCREEN_AREA}
    if segment["kind"] == "room":
        area = _room_area(window)
        if area:
            areas.add(area)
    for area in sorted(areas):
        if area not in ctx.registry.run_layer:
            reasons.append(f"run_layer_not_ported:{area}")
    if segment["kind"] == "room" and (window.model_id or "").startswith("EVENT."):
        reasons += _content_reasons(ctx, "events", [strip_prefix(window.model_id, "EVENT.")])
    # No anchor here: a save anchor is bound later (bind_save_anchors); otherwise the harness
    # chains the segment from an agreeing predecessor or skips it as anchor_missing.
    if not decisions:
        reasons.append("no_decisions")
    segment["eligibility"]["reasons"] = list(dict.fromkeys(reasons))


def build_lineage_bundle(
    lineage: Sequence[tuple[Path, Any]],
    *,
    vocabulary: CorpusVocabulary,
    registry: ContentRegistry,
    options: ExportOptions,
) -> dict[str, Any]:
    parts = [load_part(path, manifest) for path, manifest in lineage]
    raw_manifests = [_read_raw_manifest(path) for path, _ in lineage]
    first = lineage[0][1]
    run = first.run
    ctx = LineageContext(
        lineage=lineage_id(run.start_time, run.seed, run.character),
        parts=parts,
        raw_manifests=raw_manifests,
        vocabulary=vocabulary,
        registry=registry,
        options=options,
        character=run.character,
        ascension=run.ascension,
        game_version=first.game.version,
    )
    versions = {m.game.version for _, m in lineage}
    seeding, seed_reasons = seeding_for(first.game.version, run.seed)
    ctx.run_reasons += seed_reasons
    if len(versions) > 1:
        ctx.run_reasons.append("lineage_version_mixed:" + ",".join(sorted(versions)))
    if run.game_mode != "standard":
        modifiers = read_native_modifiers(lineage)
        if modifiers is None:
            ctx.run_reasons.append(f"game_mode_modifiers_unknown:{run.game_mode}")
        elif modifiers:
            ctx.run_reasons.append(f"game_mode_modifiers:{run.game_mode}")
    for part in parts:
        for stream in part.errors:
            ctx.run_reasons.append(f"stream_unreadable:{part.path.name}:{stream}")
    mods = sorted({mod for raw in raw_manifests for mod in _mod_ids(raw)})
    drivers = {"mcp" if MCP_MOD_ID in _mod_ids(raw) else "human" for raw in raw_manifests}
    if len(drivers) > 1:
        ctx.run_reasons.append("provenance_mixed")
    untested = any(bool((raw.get("game") or {}).get("untested")) for raw in raw_manifests)
    recorder_versions = sorted({str(raw.get("recorder_version")) for raw in raw_manifests if raw.get("recorder_version")})
    index_anchors(ctx)

    windows, window_notes = room_windows(parts)
    attempts: Counter = Counter()
    tracker = DeckTracker(order=None, multiset=None)
    neow_stats: Mapping[str, Any] | None = None
    first_combat_key: tuple[Any, ...] | None = None
    last_summary: str | None = None
    combat_parts: dict[str, int] = {}
    for window in windows:
        key = window.key
        summary = _floor_summary(window)
        if window.is_combat and window.has_battle:
            attempts[key] += 1
            if first_combat_key is None:
                first_combat_key = key
            segment, end_seq = build_combat_segment(
                ctx,
                window,
                attempt=attempts[key],
                first_combat=key == first_combat_key,
                neow_stats=neow_stats,
                tracker=tracker,
            )
            ctx.segments.append(segment)
            combat_parts[segment["id"]] = window.part_index
            post_actions = [a for a in window.actions if end_seq is not None and a.seq >= end_seq]
            phase = "post_combat"
            post_start = end_seq
        elif window.is_combat:
            attempts[key] = max(attempts[key], 1)
            post_actions = list(window.actions)
            phase = "post_combat_resume"
            post_start = window.room.seq
            segment = None
        else:
            attempts[key] += 1
            post_actions = list(window.actions)
            phase = None
            post_start = window.room.seq
            segment = None
        room_actions = [a for a in post_actions if _kind(a) not in MAP_KINDS]
        map_actions = [a for a in post_actions if _kind(a) in MAP_KINDS]
        room_segment = None
        if not window.is_combat or [a for a in room_actions if _kind(a) not in FOLLOW_UP_KINDS]:
            room_segment = _segment(
                ctx, window, kind="room", attempt=attempts[key], window_start=post_start or window.room.seq, window_end=window.end_seq,
                phase=phase,
            )
            room_segment["room_seq"] = window.room.seq
            ctx.segments.append(room_segment)
            first_state = next((s for s in window.states if s.seq >= (post_start or 0)), None)
            room_segment["start"] = {
                "player": project_player(first_state.state.get("player")) if first_state is not None else None,
                "deck_multiset": sorted(tracker.multiset.elements()) if tracker.multiset is not None else None,
                "deck": [{"id": c, "upgraded": u} for c, u in tracker.order] if tracker.order is not None else None,
            }
            decisions, reasons, screens = build_run_decisions(
                ctx, window, room_actions, phase=phase, summary=summary, deck=tracker.multiset
            )
            _finish_run_segment(ctx, room_segment, window, decisions, reasons, screens)
            room_segment["outcome"] = {"result": "left" if window.end_seq is not None else "abandoned"}
        if map_actions:
            map_segment = _segment(
                ctx, window, kind="map", attempt=attempts[key], window_start=map_actions[0].seq, window_end=window.end_seq
            )
            ctx.segments.append(map_segment)
            decisions, reasons, screens = build_run_decisions(ctx, window, map_actions, phase=None, summary=None)
            _finish_run_segment(ctx, map_segment, window, decisions, reasons, screens | {"map"})
            map_segment["outcome"] = {"result": "left"}
        if summary is not None:
            owner = room_segment or segment
            if owner is not None:
                owner["floor_summary"] = summary
        # The game logs an act boss's summary again after the act transition (same rooms and
        # deck deltas, later HP/gold): a summary equal to the previous one on those fields is
        # that repeat and is applied once.
        for event in window.events:
            if event.entry != "floor_summary" or not isinstance(event.data, Mapping):
                continue
            raw_stats = event.data.get("player_stats") or ()
            stats = raw_stats[0] if raw_stats and isinstance(raw_stats[0], Mapping) else None
            if stats is None:
                continue
            fingerprint = json.dumps(
                {"rooms": event.data.get("rooms"), **{k: stats.get(k) for k in SUMMARY_DECK_KEYS}},
                sort_keys=True,
                ensure_ascii=False,
            )
            if fingerprint == last_summary:
                continue
            last_summary = fingerprint
            if any(isinstance(r, Mapping) and r.get("model_id") == NEOW_ROOM_ID for r in event.data.get("rooms") or ()):
                if first_combat_key is None:
                    neow_stats = stats
                    deck, _issues = reconstruct_deck(run.character, run.ascension, stats)
                    tracker = DeckTracker.from_deck(deck)
            else:
                tracker.apply_summary(stats)

    attach_mcr_anchors(ctx, combat_parts)
    bind_save_anchors(ctx)
    for segment in ctx.segments:
        segment.pop("room_seq", None)
        reasons = segment["eligibility"]["reasons"]
        reasons += [r for r in ctx.run_reasons if r not in reasons]
        segment["eligibility"]["eligible"] = not reasons
        if not segment["notes"]:
            del segment["notes"]

    last = lineage[-1][1]
    if last.result is None:
        result = "incomplete"
    elif last.result.abandoned:
        result = "abandoned"
    else:
        result = "win" if last.result.win else "loss"
    floors = [w.floor for w in windows if w.floor is not None]
    for anchor in ctx.anchors:
        anchor.pop("part_index", None)
    return {
        "bundle_version": BUNDLE_VERSION_V2,
        "kind": "run",
        "source": {
            "lineage": ctx.lineage,
            "parts": [p.path.name for p in parts],
            "provenance": {
                "driver": "mcp" if drivers == {"mcp"} else ("mixed" if len(drivers) > 1 else "human"),
                "mods": mods,
                "recorder_version": recorder_versions[0] if len(recorder_versions) == 1 else recorder_versions,
                "untested": untested,
            },
        },
        "game_version": first.game.version,
        "rules_version": RULES_VERSION,
        "seeding": seeding,
        "seed": run.seed,
        "character": run.character,
        "ascension": run.ascension,
        "game_mode": run.game_mode,
        "modifiers": read_native_modifiers(lineage) or [],
        "anchors": ctx.anchors,
        "segments": ctx.segments,
        "outcome": {"result": result, "floor": max(floors) if floors else None},
        "eligibility": {"run_reasons": list(dict.fromkeys(ctx.run_reasons))},
        "notes": window_notes,
    }


# --------------------------------------------------------------------------
# Corpus export
# --------------------------------------------------------------------------


def check_output_dir_v2(out_dir: Path, *, corpus_root: Path, repo_root: Path | None) -> None:
    """Refuse the corpus and the repository, except the repository's git-ignored ``tmp/``."""
    if _inside(out_dir, corpus_root) or _inside(corpus_root, out_dir):
        raise ExportError(f"refusing to write {out_dir}: it overlaps the corpus {corpus_root}")
    if repo_root is not None and _inside(out_dir, repo_root) and not _inside(out_dir, repo_root / "tmp"):
        raise ExportError(f"refusing to write {out_dir}: it is inside {repo_root} (only {repo_root / 'tmp'} is allowed)")


def summarize_v2(bundles: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
    segments_by_kind: Counter = Counter()
    eligible_by_kind: Counter = Counter()
    decisions_by_screen: Counter = Counter()
    decisions_by_type: Counter = Counter()
    reason_classes: Counter = Counter()
    sole: Counter = Counter()
    run_reasons: Counter = Counter()
    eligible_decisions = 0
    for bundle in bundles:
        for reason in bundle["eligibility"]["run_reasons"]:
            run_reasons[reason_class_v2(reason)] += 1
        for segment in bundle["segments"]:
            kind = segment["kind"]
            segments_by_kind[kind] += 1
            if segment["eligibility"]["eligible"]:
                eligible_by_kind[kind] += 1
                eligible_decisions += len(segment["decisions"])
            classes = sorted({reason_class_v2(r) for r in segment["eligibility"]["reasons"]})
            for cls in classes:
                reason_classes[cls] += 1
            if len(classes) == 1:
                sole[classes[0]] += 1
            for decision in segment["decisions"]:
                decisions_by_screen[decision["screen"]] += 1
                action = decision.get("action") or {}
                decisions_by_type[str(action.get("type"))] += 1
    return {
        "runs": len(bundles),
        "runs_by_version": dict(sorted(Counter(b["game_version"] for b in bundles).items())),
        "segments": sum(segments_by_kind.values()),
        "segments_by_kind": dict(sorted(segments_by_kind.items())),
        "eligible_segments_by_kind": dict(sorted(eligible_by_kind.items())),
        "decisions": sum(decisions_by_screen.values()),
        "eligible_decisions": eligible_decisions,
        "decisions_by_screen": dict(sorted(decisions_by_screen.items())),
        "decisions_by_type": dict(sorted(decisions_by_type.items())),
        "reason_classes": dict(sorted(reason_classes.items(), key=lambda kv: (-kv[1], kv[0]))),
        "sole_reason_classes": dict(sorted(sole.items(), key=lambda kv: (-kv[1], kv[0]))),
        "run_reason_classes": dict(sorted(run_reasons.items(), key=lambda kv: (-kv[1], kv[0]))),
    }


def copy_save_anchors(bundle: Mapping[str, Any], lineage: Sequence[tuple[Path, Any]], out_dir: Path) -> int:
    """Anchor refs are relative to the bundle directory (design §5): copy each save anchor
    (byte copy, never the corpus original) to ``<out_dir>/<ref>``. Only ``.save`` files are
    copied, so the harness's bundle discovery sees no extra JSON."""
    parts = {path.name: path for path, _ in lineage}
    copied = 0
    for anchor in bundle.get("anchors") or ():
        ref = anchor.get("ref")
        if anchor.get("kind") not in SAVE_ANCHOR_KINDS or not isinstance(ref, str):
            continue
        part_name, _, rest = ref.partition("/")
        source = parts.get(part_name)
        if source is None or not rest:
            continue
        target = out_dir / ref
        if target.is_file() and target.stat().st_size == anchor.get("bytes"):
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((source / rest).read_bytes())
        copied += 1
    return copied


def export_bundles_v2(
    corpus_root: Path,
    out_dir: Path,
    registry: ContentRegistry,
    *,
    versions: Iterable[str] | None = DEFAULT_VERSIONS,
    pre_state_mode: str = PRE_STATE_SETTLED,
    mcr_readable: bool = False,
    repo_root: Path | None = None,
    log: Callable[[str], None] | None = None,
) -> dict[str, Any]:
    """Export one v2 bundle per lineage whose game version is in ``versions`` (None = all)."""
    corpus_root = Path(corpus_root)
    out_dir = Path(out_dir)
    if pre_state_mode not in PRE_STATE_MODES:
        raise ExportError(f"unknown pre_state mode {pre_state_mode!r} (expected one of {PRE_STATE_MODES})")
    if not corpus_root.is_dir():
        raise ExportError(f"no sessions directory: {corpus_root}")
    check_output_dir_v2(out_dir, corpus_root=corpus_root, repo_root=repo_root)
    say = log or (lambda _msg: None)
    wanted = set(versions) if versions is not None else None
    lineages = [
        lineage
        for lineage in group_lineages(corpus_root)
        if wanted is None or lineage[0][1].game.version in wanted
    ]
    say(f"{len(lineages)} lineages selected under {corpus_root}")
    vocabulary = build_vocabulary(group_lineages(corpus_root))
    options = ExportOptions(pre_state_mode=pre_state_mode, mcr_readable=mcr_readable)
    out_dir.mkdir(parents=True, exist_ok=True)
    bundles: list[dict[str, Any]] = []
    index_runs: list[dict[str, Any]] = []
    for lineage in lineages:
        bundle = build_lineage_bundle(lineage, vocabulary=vocabulary, registry=registry, options=options)
        copy_save_anchors(bundle, lineage, out_dir)
        target = out_dir / f"{bundle['source']['lineage']}.json"
        target.write_text(json.dumps(bundle, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        bundles.append(bundle)
        index_runs.append(
            {
                "lineage": bundle["source"]["lineage"],
                "file": str(target),
                "game_version": bundle["game_version"],
                "character": bundle["character"],
                "ascension": bundle["ascension"],
                "parts": len(bundle["source"]["parts"]),
                "outcome": bundle["outcome"],
                "run_reasons": bundle["eligibility"]["run_reasons"],
                "segments": [
                    {
                        "id": s["id"],
                        "kind": s["kind"],
                        "act": s["act"],
                        "floor": s["floor"],
                        "room_type": s["room_type"],
                        "room_model_id": s["room_model_id"],
                        "attempt": s["attempt"],
                        "phase": s["phase"],
                        "decisions": len(s["decisions"]),
                        "eligible": s["eligibility"]["eligible"],
                        "reasons": s["eligibility"]["reasons"],
                    }
                    for s in bundle["segments"]
                ],
            }
        )
        say(f"{bundle['source']['lineage']}: {len(bundle['segments'])} segments")
    index = {
        "index_version": REASONS_INDEX_VERSION_V2,
        "bundle_contract_version": BUNDLE_VERSION_V2,
        "corpus_root": str(corpus_root),
        "out_dir": str(out_dir),
        "versions": sorted(wanted) if wanted is not None else None,
        "pre_state_mode": pre_state_mode,
        "mcr_readable": mcr_readable,
        "sim_registry": registry.to_json(),
        "relic_whitelist_first_combat": sorted(RELIC_WHITELIST),
        "summary": summarize_v2(bundles),
        "runs": index_runs,
    }
    (out_dir / "reasons.json").write_text(json.dumps(index, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    (out_dir / "name_map.json").write_text(
        json.dumps(vocabulary.names.to_json(), ensure_ascii=False, indent=1) + "\n", encoding="utf-8"
    )
    return index
