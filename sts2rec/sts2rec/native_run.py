"""Parser for the game's own ``.run`` history files.

Schema version 8 is covered by checked-in game fixtures. Schema version 9 is
accepted on a best-effort compatibility basis because its known changes are
additive/identifier migrations, but this repository does not yet include a real
version-9 fixture.

These live at <user_data>/steam/<steamid>/profileN/saves/history/<start>.run
and are plain JSON written by the game at run end. They are treated strictly
read-only; sessions get *copies* under native/run_history.run.

map_point_history / player_stats payloads are deeply game-version-dependent,
so they are retained as raw (never-mutated) mappings rather than fully typed.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import NativeRunError

# Compatibility alias retained for callers that historically imported the
# single fixture-backed schema version.
NATIVE_RUN_SCHEMA_VERSION = 8

SUPPORTED_NATIVE_RUN_SCHEMA_VERSIONS = frozenset({NATIVE_RUN_SCHEMA_VERSION, 9})

_REQUIRED_KEYS = (
    "schema_version",
    "seed",
    "build_id",
    "win",
    "was_abandoned",
    "ascension",
    "game_mode",
    "start_time",
    "run_time",
    "players",
    "acts",
    "map_point_history",
)


@dataclass(frozen=True, slots=True)
class OwnedItem:
    """A card or relic in a player's end-of-run inventory."""

    id: str
    floor_added_to_deck: int | None


@dataclass(frozen=True, slots=True)
class PlayerSummary:
    id: int
    character: str
    deck: tuple[OwnedItem, ...]
    relics: tuple[OwnedItem, ...]
    potions: tuple[str, ...]
    max_potion_slot_count: int


@dataclass(frozen=True, slots=True)
class RoomEntry:
    model_id: str
    room_type: str
    turns_taken: int


@dataclass(frozen=True, slots=True)
class MapPoint:
    map_point_type: str
    rooms: tuple[RoomEntry, ...]
    player_stats: tuple[Mapping[str, Any], ...]


@dataclass(frozen=True, slots=True)
class RunSummary:
    schema_version: int
    seed: str
    build_id: str
    win: bool
    was_abandoned: bool
    ascension: int
    game_mode: str
    platform_type: str | None
    start_time: int
    run_time: int
    acts: tuple[str, ...]
    killed_by_encounter: str | None
    killed_by_event: str | None
    modifiers: tuple[str, ...]
    players: tuple[PlayerSummary, ...]
    map_point_history: tuple[tuple[MapPoint, ...], ...]

    @property
    def floors_visited(self) -> int:
        return sum(len(act) for act in self.map_point_history)


def _parse_owned_item(raw: Any, where: str) -> OwnedItem:
    if not isinstance(raw, Mapping) or "id" not in raw:
        raise NativeRunError(f"{where}: item must be an object with an 'id'")
    floor = raw.get("floor_added_to_deck")
    return OwnedItem(
        id=str(raw["id"]),
        floor_added_to_deck=int(floor) if floor is not None else None,
    )


def _parse_player(raw: Any, where: str) -> PlayerSummary:
    if not isinstance(raw, Mapping):
        raise NativeRunError(f"{where}: player must be an object")
    try:
        return PlayerSummary(
            id=int(raw["id"]),
            character=str(raw["character"]),
            deck=tuple(
                _parse_owned_item(card, f"{where}.deck")
                for card in raw.get("deck", ())
            ),
            relics=tuple(
                _parse_owned_item(relic, f"{where}.relics")
                for relic in raw.get("relics", ())
            ),
            potions=tuple(
                str(potion.get("id", potion)) if isinstance(potion, Mapping) else str(potion)
                for potion in raw.get("potions", ())
            ),
            max_potion_slot_count=int(raw.get("max_potion_slot_count", 0)),
        )
    except (KeyError, TypeError, ValueError) as error:
        raise NativeRunError(f"{where}: malformed player entry: {error}") from error


def _parse_room(raw: Any, where: str) -> RoomEntry:
    if not isinstance(raw, Mapping):
        raise NativeRunError(f"{where}: room must be an object")
    return RoomEntry(
        model_id=str(raw.get("model_id", "")),
        room_type=str(raw.get("room_type", "")),
        turns_taken=int(raw.get("turns_taken", 0)),
    )


def _parse_map_point(raw: Any, where: str) -> MapPoint:
    if not isinstance(raw, Mapping):
        raise NativeRunError(f"{where}: map point must be an object")
    return MapPoint(
        map_point_type=str(raw.get("map_point_type", "")),
        rooms=tuple(
            _parse_room(room, f"{where}.rooms") for room in raw.get("rooms", ())
        ),
        player_stats=tuple(raw.get("player_stats", ())),
    )


def parse_run_summary(raw: Any, *, source: str = "run file") -> RunSummary:
    """Validate a decoded .run JSON object into a RunSummary."""
    if not isinstance(raw, Mapping):
        raise NativeRunError(f"{source}: top level must be a JSON object")
    missing = [key for key in _REQUIRED_KEYS if key not in raw]
    if missing:
        raise NativeRunError(f"{source}: missing required keys: {missing}")
    schema_version = raw["schema_version"]
    if schema_version not in SUPPORTED_NATIVE_RUN_SCHEMA_VERSIONS:
        supported = ", ".join(
            str(version) for version in sorted(SUPPORTED_NATIVE_RUN_SCHEMA_VERSIONS)
        )
        raise NativeRunError(
            f"{source}: unsupported native schema_version {schema_version!r} "
            f"(this tool supports {supported})"
        )
    try:
        return RunSummary(
            schema_version=int(schema_version),
            seed=str(raw["seed"]),
            build_id=str(raw["build_id"]),
            win=bool(raw["win"]),
            was_abandoned=bool(raw["was_abandoned"]),
            ascension=int(raw["ascension"]),
            game_mode=str(raw["game_mode"]),
            platform_type=(
                str(raw["platform_type"]) if raw.get("platform_type") is not None else None
            ),
            start_time=int(raw["start_time"]),
            run_time=int(raw["run_time"]),
            acts=tuple(str(act) for act in raw["acts"]),
            killed_by_encounter=raw.get("killed_by_encounter"),
            killed_by_event=raw.get("killed_by_event"),
            modifiers=tuple(str(mod) for mod in raw.get("modifiers", ())),
            players=tuple(
                _parse_player(player, f"{source}.players[{index}]")
                for index, player in enumerate(raw["players"])
            ),
            map_point_history=tuple(
                tuple(
                    _parse_map_point(point, f"{source}.map_point_history[{act}]")
                    for point in act_points
                )
                for act, act_points in enumerate(raw["map_point_history"])
            ),
        )
    except (TypeError, ValueError) as error:
        raise NativeRunError(f"{source}: malformed field value: {error}") from error


def load_run_file(path: Path | str) -> RunSummary:
    """Load and validate a native .run history file."""
    path = Path(path)
    if not path.is_file():
        raise NativeRunError(f"run file not found: {path}")
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise NativeRunError(f"cannot read {path}: {error}") from error
    return parse_run_summary(raw, source=str(path))
