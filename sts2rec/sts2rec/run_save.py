"""Parser for the game's in-progress run save (``current_run.save``, schema 20).

The game writes ``<profile>/saves/current_run.save`` at three points
(docs/sim-port/design-run-layer.md section 7.1): entering a map point, after a
won combat (``pre_finished_room`` = the combat room), and after an ancient event
finished (``pre_finished_room`` = the event room). This module mirrors the C#
importer (``sim/Sts2Sim.Core/Run/Save``): it locates saves the same way as
``paths.profile_saves_dirs`` (vanilla and modded profile trees), parses the
section 7.3 subset, strips model-id categories (``CARD.X`` -> ``X``) and reads
odds as float32 like ``Utf8JsonReader.GetSingle``. Saves are read-only; callers
work on copies.
"""

from __future__ import annotations

import json
import struct
from collections.abc import Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .errors import Sts2RecError
from .paths import profile_saves_dirs

RUN_SAVE_SCHEMA_VERSION = 20
CURRENT_RUN_FILE_NAME = "current_run.save"
CURRENT_RUN_MP_FILE_NAME = "current_run_mp.save"
STANDARD_GAME_MODE = "standard"

ENTERING_MAP_POINT = "entering_map_point"
POST_COMBAT_REWARDS = "post_combat_rewards"
ANCIENT_FINISHED = "ancient_finished"
NO_VISITED_COORD = "no_visited_coord"

_COMBAT_ROOM_TYPES = frozenset({"monster", "elite", "boss"})

_REQUIRED_KEYS = ("schema_version", "rng", "players", "acts", "current_act_index")


class RunSaveError(Sts2RecError):
    """A current_run.save file is missing, malformed, or of another schema."""


def strip_category(model_id: str) -> str:
    """``CARD.STRIKE_SILENT`` -> ``STRIKE_SILENT`` (the sim's model id)."""
    _, dot, rest = model_id.partition(".")
    return rest if dot else model_id


def float32(value: float) -> float:
    """Round a JSON number to the nearest IEEE-754 single, as the game stores odds."""
    return float(struct.unpack("<f", struct.pack("<f", float(value)))[0])


@dataclass(frozen=True, slots=True)
class RngState:
    counter: int
    s0: int
    s1: int
    s2: int
    s3: int


@dataclass(frozen=True, slots=True)
class SavedCard:
    id: str
    upgrade_level: int
    enchantment_id: str | None
    enchantment_amount: int
    has_props: bool
    floor_added_to_deck: int | None


@dataclass(frozen=True, slots=True)
class SavedRelic:
    id: str
    has_props: bool
    floor_added_to_deck: int | None


@dataclass(frozen=True, slots=True)
class SavedPlayer:
    character_id: str
    current_hp: int
    max_hp: int
    max_energy: int
    max_potion_slot_count: int
    gold: int
    deck: tuple[SavedCard, ...]
    relics: tuple[SavedRelic, ...]
    potions: tuple[tuple[str, int], ...]
    rng_seed: int
    rngs: Mapping[str, RngState]
    card_rarity_odds: float
    potion_reward_odds: float
    relic_grab_bag: Mapping[str, tuple[str, ...]]


@dataclass(frozen=True, slots=True)
class SavedAct:
    id: str
    event_ids: tuple[str, ...]
    normal_encounter_ids: tuple[str, ...]
    elite_encounter_ids: tuple[str, ...]
    boss_id: str | None
    second_boss_id: str | None
    ancient_id: str | None
    visited: Mapping[str, int]
    has_saved_map: bool


@dataclass(frozen=True, slots=True)
class RunSave:
    schema_version: int
    seed: str
    ascension: int
    game_mode: str
    current_act_index: int
    acts: tuple[SavedAct, ...]
    players: tuple[SavedPlayer, ...]
    rngs: Mapping[str, RngState]
    visited_map_coords: tuple[tuple[int, int], ...]
    events_seen: tuple[str, ...]
    unknown_map_point_odds: Mapping[str, float]
    shared_relic_grab_bag: Mapping[str, tuple[str, ...]]
    pre_finished_room: Mapping[str, Any] | None
    modifiers: tuple[str, ...]
    floors: int
    raw: Mapping[str, Any] = field(repr=False)

    @property
    def entry_point(self) -> str:
        room = self.pre_finished_room
        if room is not None:
            room_type = room.get("room_type")
            if room_type in _COMBAT_ROOM_TYPES:
                return POST_COMBAT_REWARDS
            if room_type == "event":
                return ANCIENT_FINISHED
            raise RunSaveError(f"pre_finished_room room_type {room_type!r} cannot be resumed")
        return ENTERING_MAP_POINT if self.visited_map_coords else NO_VISITED_COORD


def find_run_saves(base: Path, *, include_multiplayer: bool = False) -> tuple[Path, ...]:
    """Every current_run.save under the user-data root (vanilla first, then modded)."""
    names = (CURRENT_RUN_FILE_NAME, CURRENT_RUN_MP_FILE_NAME) if include_multiplayer else (CURRENT_RUN_FILE_NAME,)
    return tuple(
        saves / name
        for saves in profile_saves_dirs(base)
        for name in names
        if (saves / name).is_file()
    )


def load_run_save(path: Path) -> RunSave:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise RunSaveError(f"{path}: cannot read run save: {exc}") from exc
    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        raise RunSaveError(f"{path}: invalid JSON: {exc}") from exc
    try:
        return parse_run_save(data)
    except RunSaveError as exc:
        raise RunSaveError(f"{path}: {exc}") from exc


def parse_run_save(data: Any) -> RunSave:
    if not isinstance(data, Mapping):
        raise RunSaveError("a run save is a JSON object")
    missing = [key for key in _REQUIRED_KEYS if key not in data]
    if missing:
        raise RunSaveError(f"missing keys {missing}")
    schema = data["schema_version"]
    if schema != RUN_SAVE_SCHEMA_VERSION:
        raise RunSaveError(f"schema_version {schema!r} is not supported (only {RUN_SAVE_SCHEMA_VERSION})")
    acts = tuple(_act(a) for a in _seq(data, "acts"))
    index = _int(data, "current_act_index")
    if not 0 <= index < len(acts):
        raise RunSaveError(f"current_act_index {index} is outside the {len(acts)} acts")
    rng = _map(data, "rng")
    odds = _map(data, "odds")
    history = _seq(data, "map_point_history")
    return RunSave(
        schema_version=schema,
        seed=str(rng.get("seed", "")),
        ascension=_int(data, "ascension"),
        game_mode=str(data.get("game_mode", STANDARD_GAME_MODE)),
        current_act_index=index,
        acts=acts,
        players=tuple(_player(p) for p in _seq(data, "players")),
        rngs=_rngs(rng),
        visited_map_coords=tuple((_int(c, "col"), _int(c, "row")) for c in _seq(data, "visited_map_coords")),
        events_seen=tuple(strip_category(e) for e in _seq(data, "events_seen")),
        unknown_map_point_odds={
            kind: float32(odds.get(f"unknown_map_point_{kind}_odds_value", 0.0))
            for kind in ("monster", "elite", "treasure", "shop")
        },
        shared_relic_grab_bag=_grab_bag(_map(data, "shared_relic_grab_bag")),
        pre_finished_room=data.get("pre_finished_room") if isinstance(data.get("pre_finished_room"), Mapping) else None,
        modifiers=tuple(strip_category(str(m.get("id", ""))) for m in _seq(data, "modifiers")),
        floors=sum(len(act) for act in history if isinstance(act, Sequence)),
        raw=data,
    )


def unsupported_reasons(save: RunSave) -> list[str]:
    """Run-shape reasons the sim importer rejects regardless of ported content."""
    reasons: list[str] = []
    if save.game_mode != STANDARD_GAME_MODE:
        reasons.append(f"game_mode {save.game_mode} is not standard")
    if "dailyTime" in save.raw:
        reasons.append("daily runs are not supported")
    reasons.extend(f"modifier {m} is not supported" for m in save.modifiers)
    if len(save.players) != 1:
        reasons.append(f"{len(save.players)} players: only single-player saves are supported")
    return reasons


def _act(a: Any) -> SavedAct:
    if not isinstance(a, Mapping):
        raise RunSaveError("acts[] entries are objects")
    rooms = _map(a, "rooms")
    return SavedAct(
        id=strip_category(str(a.get("id", ""))),
        event_ids=_ids(rooms, "event_ids"),
        normal_encounter_ids=_ids(rooms, "normal_encounter_ids"),
        elite_encounter_ids=_ids(rooms, "elite_encounter_ids"),
        boss_id=_opt_id(rooms, "boss_id"),
        second_boss_id=_opt_id(rooms, "second_boss_id"),
        ancient_id=_opt_id(rooms, "ancient_id"),
        visited={
            key: _int(rooms, key)
            for key in ("events_visited", "normal_encounters_visited", "elite_encounters_visited", "boss_encounters_visited")
        },
        has_saved_map=isinstance(a.get("saved_map"), Mapping),
    )


def _player(p: Any) -> SavedPlayer:
    if not isinstance(p, Mapping):
        raise RunSaveError("players[] entries are objects")
    rng = _map(p, "rng")
    odds = _map(p, "odds")
    return SavedPlayer(
        character_id=strip_category(str(p.get("character_id", ""))),
        current_hp=_int(p, "current_hp"),
        max_hp=_int(p, "max_hp"),
        max_energy=_int(p, "max_energy"),
        max_potion_slot_count=_int(p, "max_potion_slot_count"),
        gold=_int(p, "gold"),
        deck=tuple(_card(c) for c in _seq(p, "deck")),
        relics=tuple(
            SavedRelic(strip_category(str(r.get("id", ""))), "props" in r, r.get("floor_added_to_deck"))
            for r in _seq(p, "relics")
        ),
        potions=tuple((strip_category(str(x.get("id", ""))), _int(x, "slot_index")) for x in _seq(p, "potions")),
        rng_seed=_int(rng, "seed"),
        rngs=_rngs(rng),
        card_rarity_odds=float32(odds.get("card_rarity_odds_value", 0.0)),
        potion_reward_odds=float32(odds.get("potion_reward_odds_value", 0.0)),
        relic_grab_bag=_grab_bag(_map(p, "relic_grab_bag")),
    )


def _card(c: Any) -> SavedCard:
    if not isinstance(c, Mapping):
        raise RunSaveError("deck[] entries are objects")
    ench = c.get("enchantment") if isinstance(c.get("enchantment"), Mapping) else None
    return SavedCard(
        id=strip_category(str(c.get("id", ""))),
        upgrade_level=_int(c, "current_upgrade_level"),
        enchantment_id=strip_category(str(ench.get("id", ""))) if ench else None,
        enchantment_amount=_int(ench, "amount") if ench else 0,
        has_props="props" in c,
        floor_added_to_deck=c.get("floor_added_to_deck"),
    )


def _rngs(rng: Mapping[str, Any]) -> dict[str, RngState]:
    streams = rng.get("rngs") or {}
    if not isinstance(streams, Mapping):
        raise RunSaveError("rng.rngs is an object")
    return {
        name: RngState(_int(s, "counter"), _int(s, "s0"), _int(s, "s1"), _int(s, "s2"), _int(s, "s3"))
        for name, s in streams.items()
    }


def _grab_bag(bag: Mapping[str, Any]) -> dict[str, tuple[str, ...]]:
    lists = bag.get("relic_id_lists") or {}
    if not isinstance(lists, Mapping):
        raise RunSaveError("relic_id_lists is an object")
    return {rarity: tuple(strip_category(str(i)) for i in ids) for rarity, ids in lists.items()}


def _ids(m: Mapping[str, Any], key: str) -> tuple[str, ...]:
    return tuple(strip_category(str(i)) for i in _seq(m, key))


def _opt_id(m: Mapping[str, Any], key: str) -> str | None:
    value = m.get(key)
    return strip_category(value) if isinstance(value, str) else None


def _seq(m: Mapping[str, Any], key: str) -> Sequence[Any]:
    value = m.get(key)
    if value is None:
        return ()
    if not isinstance(value, list):
        raise RunSaveError(f"{key} is a list")
    return value


def _map(m: Mapping[str, Any], key: str) -> Mapping[str, Any]:
    value = m.get(key)
    if value is None:
        return {}
    if not isinstance(value, Mapping):
        raise RunSaveError(f"{key} is an object")
    return value


def _int(m: Mapping[str, Any], key: str) -> int:
    value = m.get(key, 0)
    if isinstance(value, bool) or not isinstance(value, int):
        raise RunSaveError(f"{key} is an integer, got {value!r}")
    return value
