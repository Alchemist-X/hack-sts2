"""Human-readable markdown walkthrough of a recorded session.

`sts2rec report <session>` renders a full-run summary from the three streams
plus the manifest: header, per-floor timeline, per-combat summaries,
acquisitions, deck evolution, and a counts/storage footprint. Works on
live/incomplete and crash-terminated sessions: every section degrades to
"(no ... recorded)" instead of failing, malformed trailing lines are
tolerated, and both plain and .gz streams are read transparently.

The report is heuristic by design — it recognizes the recorder's known entry
names / action kinds (docs/hook-map.md) but never requires them.
"""

from __future__ import annotations

import gzip
from collections.abc import Iterable, Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .errors import JsonlError, RecordError
from .session import (
    ActionRecord,
    EventRecord,
    Manifest,
    StateRecord,
    load_manifest,
    iter_jsonl,
    parse_action_record,
    parse_event_record,
    parse_state_record,
    resolve_stream_path,
)

_PLAY_KINDS = frozenset({"play_card"})
_TURN_KINDS = frozenset({"end_turn"})
_COMBAT_ACTION_KINDS = frozenset(
    {"play_card", "end_turn", "undo_end_turn", "use_potion", "discard_potion"}
)
_MAP_KINDS = frozenset({"map_choice", "vote_for_map_coord", "move_to_map_coord"})
_DAMAGE_DEALT = frozenset({"damage_dealt"})
_DAMAGE_TAKEN = frozenset({"damage_received", "damage_taken"})
_CARD_GAIN_MARKERS = ("card_obtained", "card_reward_obtained", "card_reward")
_CARD_LOSS_MARKERS = ("card_removal", "card_removed", "shop_card_removal")
_ITEM_NAME_KEYS = (
    "card", "card_name", "card_id", "card_model_id",
    "relic", "relic_id", "potion", "potion_id", "item", "item_id", "name",
)


@dataclass(slots=True)
class _SessionData:
    manifest: Manifest
    states: list[StateRecord] = field(default_factory=list)
    actions: list[ActionRecord] = field(default_factory=list)
    events: list[EventRecord] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def _load_lenient(session_dir: Path) -> _SessionData:
    data = _SessionData(manifest=load_manifest(session_dir))
    parsers = {
        "states": (parse_state_record, data.states),
        "actions": (parse_action_record, data.actions),
        "events": (parse_event_record, data.events),
    }
    # incomplete=true (crash-terminated): a malformed FINAL line is the
    # recorder's torn in-flight write at SIGKILL — skip it with a specific
    # note instead of aborting the stream read.
    incomplete = data.manifest.incomplete
    for stream, (parser, records) in parsers.items():
        path = resolve_stream_path(session_dir, stream)
        if not path.is_file():
            data.warnings.append(f"missing stream file: {path.name}")
            continue

        def _note_torn_final(
            line_no: int, _error: JsonlError, *, name: str = path.name
        ) -> None:
            data.warnings.append(
                f"{name}:{line_no}: torn final line "
                "(crash-terminated session); skipped"
            )

        try:
            for line_no, raw in iter_jsonl(
                path, on_torn_final=_note_torn_final if incomplete else None
            ):
                try:
                    records.append(parser(raw, where=f"{path.name}:{line_no}"))
                except RecordError as error:
                    data.warnings.append(str(error))
        except JsonlError as error:
            data.warnings.append(f"stream truncated/unreadable: {error}")
    data.states.sort(key=lambda r: r.seq)
    data.actions.sort(key=lambda r: r.seq)
    data.events.sort(key=lambda r: r.seq)
    return data


def _fmt_duration(seconds: float) -> str:
    seconds = max(0, int(seconds))
    minutes, secs = divmod(seconds, 60)
    hours, minutes = divmod(minutes, 60)
    if hours:
        return f"{hours}h {minutes:02d}m {secs:02d}s"
    return f"{minutes}m {secs:02d}s"


def _fmt_bytes(size: int) -> str:
    value = float(size)
    for unit in ("B", "KiB", "MiB", "GiB"):
        if value < 1024 or unit == "GiB":
            return f"{value:.1f} {unit}" if unit != "B" else f"{int(value)} B"
        value /= 1024
    return f"{int(size)} B"


def _result_label(manifest: Manifest) -> str:
    if manifest.result is None:
        return "in progress" if manifest.incomplete else "unknown (no result)"
    if manifest.result.abandoned:
        return "abandoned"
    return "WIN" if manifest.result.win else "LOSS"


def _duration_label(data: _SessionData) -> str:
    manifest = data.manifest
    end: float | None = manifest.result.end_time if manifest.result else None
    if end is None:
        timestamps = [r.t for r in (*data.states, *data.actions, *data.events)]
        end = max(timestamps) if timestamps else None
    if end is None or end < manifest.run.start_time:
        return "unknown"
    return _fmt_duration(end - manifest.run.start_time)


def _item_name(data: Mapping[str, Any]) -> str | None:
    for key in _ITEM_NAME_KEYS:
        value = data.get(key)
        if isinstance(value, (str, int)) and str(value):
            return str(value)
    return None


def _actor_name(value: Any) -> str | None:
    if isinstance(value, str) and value:
        return value
    if isinstance(value, Mapping):
        for key in ("name", "id", "model_id"):
            inner = value.get(key)
            if isinstance(inner, str) and inner:
                return inner
    return None


def _action_params(action: ActionRecord) -> Mapping[str, Any]:
    params = action.action.get("params")
    if isinstance(params, Mapping):
        return params
    return {k: v for k, v in action.action.items() if k != "kind"}


def _find_scalar(state: Mapping[str, Any], keys: tuple[str, ...], depth: int = 3) -> Any:
    """Shallow recursive search for the first int/float under any of `keys`."""
    for key in keys:
        value = state.get(key)
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            return value
    if depth <= 1:
        return None
    for value in state.values():
        if isinstance(value, Mapping):
            found = _find_scalar(value, keys, depth - 1)
            if found is not None:
                return found
    return None


# --- floors -----------------------------------------------------------------


@dataclass(slots=True)
class _Floor:
    floor: Any  # int when known, None on legacy data
    act: Any
    room_type: str | None
    start_seq: int
    notes: list[str] = field(default_factory=list)
    actions: list[ActionRecord] = field(default_factory=list)
    events: list[EventRecord] = field(default_factory=list)


def _segment_floors(data: _SessionData) -> list[_Floor]:
    """Partition actions/events into floor segments at room_entered events."""
    floors: list[_Floor] = []
    for event in data.events:
        if event.entry != "room_entered":
            continue
        floors.append(
            _Floor(
                floor=event.data.get("floor"),
                act=event.data.get("act"),
                room_type=_actor_name(event.data.get("room_type")),
                start_seq=event.seq,
            )
        )
    if not floors:
        # No room events (synthetic/legacy): one catch-all segment.
        floors = [_Floor(floor=None, act=None, room_type=None, start_seq=0)]
    boundaries = [f.start_seq for f in floors]

    def bucket(seq: int) -> _Floor:
        index = 0
        for i, start in enumerate(boundaries):
            if seq >= start:
                index = i
        return floors[index]

    for action in data.actions:
        bucket(action.seq).actions.append(action)
    for event in data.events:
        bucket(event.seq).events.append(event)
    return floors


def _floor_notes(floor: _Floor) -> str:
    notes: list[str] = []
    for action in floor.actions:
        kind = str(action.action.get("kind"))
        if kind in _MAP_KINDS:
            params = _action_params(action)
            target = (
                _actor_name(params.get("destination"))
                or _item_name(params)
                or params.get("node")
                or params.get("coord")
            )
            notes.append(f"map: {target}" if target else "map choice")
        elif kind == "shop_purchase":
            item = _item_name(_action_params(action))
            notes.append(f"bought {item}" if item else "shop purchase")
        elif kind == "rest_site_option":
            option = _item_name(_action_params(action)) or _action_params(action).get(
                "rest_site_option"
            )
            notes.append(f"rest: {option}" if option else "rest site")
    if any(event.entry == "floor_summary" for event in floor.events):
        notes.append("floor summary recorded")
    return "; ".join(dict.fromkeys(notes)) if notes else "-"


# --- combats ----------------------------------------------------------------


@dataclass(slots=True)
class _Combat:
    floor: _Floor
    cards: list[str]
    turns: int
    damage_dealt: int
    damage_taken: int
    enemies: list[str]
    end_seq: int


def _combat_for_floor(floor: _Floor) -> _Combat | None:
    combat_actions = [
        a
        for a in floor.actions
        if str(a.action.get("kind")) in _COMBAT_ACTION_KINDS
        # enqueued-then-backed-out plays never resolved: not "played"
        and a.status != "cancelled"
    ]
    damage_events = [
        e for e in floor.events if e.entry in (_DAMAGE_DEALT | _DAMAGE_TAKEN)
    ]
    if not combat_actions and not damage_events:
        return None
    cards = []
    for action in combat_actions:
        if str(action.action.get("kind")) in _PLAY_KINDS:
            name = _item_name(_action_params(action))
            cards.append(name or "?")
    turns = sum(
        1 for a in combat_actions if str(a.action.get("kind")) in _TURN_KINDS
    )
    dealt = sum(
        int(e.data.get("amount") or 0) for e in floor.events if e.entry in _DAMAGE_DEALT
    )
    taken = sum(
        int(e.data.get("amount") or 0) for e in floor.events if e.entry in _DAMAGE_TAKEN
    )
    enemies: list[str] = []
    for event in floor.events:
        candidate = None
        if event.entry in _DAMAGE_DEALT:
            candidate = _actor_name(event.data.get("target"))
        elif event.entry in _DAMAGE_TAKEN:
            candidate = _actor_name(event.data.get("actor")) or _actor_name(
                event.data.get("source")
            )
        if candidate and candidate not in enemies:
            enemies.append(candidate)
    last_seq = max(
        [a.seq for a in combat_actions] + [e.seq for e in damage_events]
    )
    return _Combat(
        floor=floor,
        cards=cards,
        turns=turns + 1 if combat_actions else turns,
        damage_dealt=dealt,
        damage_taken=taken,
        enemies=enemies,
        end_seq=last_seq,
    )


def _state_after(states: list[StateRecord], seq: int) -> StateRecord | None:
    for state in states:
        if state.seq > seq:
            return state
    return None


def _hp_gold_after(states: list[StateRecord], seq: int) -> str:
    state = _state_after(states, seq)
    if state is None:
        return "n/a"
    hp = _find_scalar(state.state, ("hp", "current_hp"))
    gold = _find_scalar(state.state, ("gold",))
    parts = []
    if hp is not None:
        parts.append(f"HP {hp}")
    if gold is not None:
        parts.append(f"gold {gold}")
    return ", ".join(parts) if parts else "n/a"


# --- acquisitions / deck ----------------------------------------------------


def _acquisitions(floors: list[_Floor]) -> list[tuple[Any, str, str]]:
    rows: list[tuple[Any, str, str]] = []
    for floor in floors:
        for event in floor.events:
            if event.entry.endswith("_obtained") or event.entry == "reward_taken":
                kind = event.entry.replace("_obtained", "").replace("_", " ")
                item = _item_name(event.data) or event.entry
                rows.append((floor.floor, kind, item))
        for action in floor.actions:
            kind = str(action.action.get("kind"))
            if kind == "shop_purchase":
                item = _item_name(_action_params(action)) or "?"
                rows.append((floor.floor, "shop purchase", item))
    return rows


def _deck_evolution(floors: list[_Floor]) -> list[str]:
    changes: list[str] = []
    for floor in floors:
        where = f"floor {floor.floor}" if floor.floor is not None else "floor ?"
        for event in floor.events:
            if any(marker in event.entry for marker in _CARD_GAIN_MARKERS):
                changes.append(f"+ {_item_name(event.data) or '?'} ({where})")
        for action in floor.actions:
            kind = str(action.action.get("kind"))
            params = _action_params(action)
            if any(marker in kind for marker in _CARD_LOSS_MARKERS):
                changes.append(f"- {_item_name(params) or '?'} ({where})")
            elif any(marker in kind for marker in _CARD_GAIN_MARKERS):
                changes.append(f"+ {_item_name(params) or '?'} ({where})")
    return changes


# --- storage ----------------------------------------------------------------


def _raw_size(path: Path) -> int:
    if not path.name.endswith(".gz"):
        return path.stat().st_size
    total = 0
    with gzip.open(path, "rb") as handle:
        while chunk := handle.read(1 << 16):
            total += len(chunk)
    return total


def _storage_rows(session_dir: Path, data: _SessionData) -> list[tuple[str, ...]]:
    rows: list[tuple[str, ...]] = []
    counts = {"states": len(data.states), "actions": len(data.actions), "events": len(data.events)}
    for stream in ("states", "actions", "events"):
        path = resolve_stream_path(session_dir, stream)
        if not path.is_file():
            rows.append((stream, str(counts[stream]), "missing", "-", "-"))
            continue
        on_disk = path.stat().st_size
        compressed = path.name.endswith(".gz")
        try:
            raw = _raw_size(path)
        except OSError:
            raw = on_disk
        ratio = f"{on_disk / raw:.2f}" if compressed and raw else "-"
        rows.append(
            (
                stream,
                str(counts[stream]),
                _fmt_bytes(on_disk),
                _fmt_bytes(raw),
                f"gz (x{ratio})" if compressed else "plain",
            )
        )
    return rows


# --- rendering ---------------------------------------------------------------


def _table(header: tuple[str, ...], rows: Iterable[tuple[Any, ...]]) -> list[str]:
    lines = [
        "| " + " | ".join(header) + " |",
        "|" + "|".join(" --- " for _ in header) + "|",
    ]
    for row in rows:
        lines.append("| " + " | ".join("-" if c is None else str(c) for c in row) + " |")
    return lines


def build_report(session_dir: Path | str) -> str:
    """Render the markdown report for a session directory."""
    session_dir = Path(session_dir)
    data = _load_lenient(session_dir)
    manifest = data.manifest
    floors = _segment_floors(data)

    lines: list[str] = [f"# STS2 run report — {session_dir.name}", ""]
    lines += _table(
        ("Field", "Value"),
        [
            ("Character", manifest.run.character),
            ("Seed", manifest.run.seed),
            ("Ascension", manifest.run.ascension),
            ("Game mode", manifest.run.game_mode),
            ("Result", _result_label(manifest)),
            ("Duration", _duration_label(data)),
            ("Game version", manifest.game.version),
            ("Recorder", manifest.recorder_version),
            ("Part", manifest.part),
            ("Session status", "incomplete" if manifest.incomplete else "finalized"),
        ],
    )
    if manifest.degraded_hooks:
        lines += ["", f"Degraded hooks: {', '.join(manifest.degraded_hooks)}"]
    if data.warnings:
        lines += ["", "> Partial data: " + "; ".join(data.warnings[:5])]

    lines += ["", "## Floor timeline", ""]
    timeline_rows = [
        (
            floor.floor if floor.floor is not None else "?",
            floor.act if floor.act is not None else "?",
            floor.room_type or "?",
            _floor_notes(floor),
        )
        for floor in floors
        if floor.room_type is not None or floor.floor is not None
    ]
    if timeline_rows:
        lines += _table(("Floor", "Act", "Room", "Notes"), timeline_rows)
    else:
        lines.append("(no floor events recorded)")

    lines += ["", "## Combats", ""]
    combats = [c for c in (_combat_for_floor(f) for f in floors) if c is not None]
    if combats:
        for index, combat in enumerate(combats, start=1):
            where = (
                f"floor {combat.floor.floor}"
                if combat.floor.floor is not None
                else "floor ?"
            )
            lines.append(f"### Combat {index} ({where})")
            lines.append("")
            lines.append(
                f"- Enemies: {', '.join(combat.enemies) if combat.enemies else 'unknown'}"
            )
            lines.append(f"- Turns: {combat.turns if combat.turns else '?'}")
            lines.append(
                "- Cards played (in order): "
                + (" -> ".join(combat.cards) if combat.cards else "none recorded")
            )
            lines.append(
                f"- Damage dealt/taken: {combat.damage_dealt}/{combat.damage_taken}"
            )
            lines.append(f"- After combat: {_hp_gold_after(data.states, combat.end_seq)}")
            lines.append("")
    else:
        lines += ["(no combat activity recorded)", ""]

    lines += ["## Acquisitions", ""]
    acquisition_rows = [
        (floor if floor is not None else "?", kind, item)
        for floor, kind, item in _acquisitions(floors)
    ]
    if acquisition_rows:
        lines += _table(("Floor", "Type", "Item"), acquisition_rows)
    else:
        lines.append("(no acquisitions recorded)")

    lines += ["", "## Deck evolution", ""]
    changes = _deck_evolution(floors)
    if changes:
        lines += [f"- {change}" for change in changes]
    else:
        lines.append("(no deck change events recorded)")

    lines += ["", "## Counts & storage", ""]
    lines += _table(
        ("Stream", "Records", "On disk", "Raw", "Format"),
        _storage_rows(session_dir, data),
    )
    manifest_counts = data.manifest.counts
    lines += [
        "",
        f"Manifest counts: states={manifest_counts.states}, "
        f"actions={manifest_counts.actions}, events={manifest_counts.events}"
        + ("" if not manifest.incomplete else " (checkpoint; session incomplete)"),
        "",
    ]
    return "\n".join(lines)
