"""``sts2rec export-bundles``: first-combat diff bundles from the recorder corpus.

Walks every resume lineage under the sessions root READ-ONLY, finds each
run's first combat (the floor-2 room right after the mandatory Neow event)
and every attempt at it (save&quit retries restart the same room-entry save),
and writes one bundle JSON per eligible attempt exactly per
``sim/specs/diff-bundle.md``. Every attempt, eligible or not, is listed in a
reasons index so coverage stays accountable.

Outputs (all under the output directory, never inside the corpus or the repo):

    bundles/<game_version>/<session>.attempt<k>.json   eligible bundles
    ineligible/<game_version>/<session>.attempt<k>.json  (--include-ineligible;
                                                        carry "ineligible_reasons")
    reasons.json                                        per-attempt verdicts
    name_map.json                                       zh name -> id table used
"""

from __future__ import annotations

import json
from bisect import bisect_left, bisect_right
from collections import Counter
from collections.abc import Callable, Iterable, Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .diff_bundle import (
    BUNDLE_VERSION,
    DECISION_ACTION_KINDS,
    EXCLUDED_ACTION_KINDS,
    NON_STATE_EVENT_ENTRIES,
    RELIC_WHITELIST,
    STICKY_CHOICE_CONTEXT_LABEL,
    TURN_END_FOLLOW_UP_KIND,
    CardNameMap,
    SimRegistry,
    base_deck,
    deck_multiset,
    event_creatures,
    extract_monster_ids,
    gap_keeps_hand,
    is_battle_state,
    multiset_delta,
    niche_offset,
    project_state,
    reason_class,
    reconstruct_deck,
    relic_reasons,
    resolve_hand_index,
    roll_forward,
    seeding_for,
    sim_coverage_reasons,
    snapshot_deck_multiset,
    strip_prefix,
    visible_card_total,
)
from .errors import JsonlError, RecordError
from .lineage import group_lineages
from .session import (
    ActionRecord,
    EventRecord,
    Manifest,
    StateRecord,
    iter_jsonl,
    parse_action_record,
    parse_event_record,
    parse_state_record,
    resolve_stream_path,
)

REASONS_INDEX_VERSION = 1
# pre_state of a decision whose state_seq snapshot was taken mid-resolution of
# the previous card: "settled" rolls it forward through the recorded events
# up to the action (diff_bundle.roll_forward) and falls back to null
# (unobserved, sim/specs/diff-harness.md section 5); "unobserved" always
# writes null; "snapshot" keeps the raw state_seq projection.
PRE_STATE_SETTLED = "settled"
PRE_STATE_UNOBSERVED = "unobserved"
PRE_STATE_SNAPSHOT = "snapshot"
PRE_STATE_MODES = (PRE_STATE_SETTLED, PRE_STATE_UNOBSERVED, PRE_STATE_SNAPSHOT)
COMBAT_ROOM_PREFIX = "ENCOUNTER."
NEOW_ROOM_ID = "EVENT.NEOW"
# Non-battle screens that close the combat, provided no battle snapshot of the room follows
# them. The map is not one: the player can open the map view mid-combat (corpus 2026-10-30:
# 61 map snapshots between battle snapshots of one room, e.g. 1792615807-4RHDW1LUA9 seq 95),
# and a won combat shows "Combat ended" / rewards before any map.
COMBAT_END_STATE_TYPES = frozenset(
    {"rewards", "card_reward", "game_over", "treasure", "event", "shop", "rest_site"}
)
# Screens that only show something and change no combat state: an action whose state_seq is
# one of them takes the latest battle snapshot before it as its pre-action snapshot.
COMBAT_VIEW_STATE_TYPES = frozenset({"map"})


class ExportError(Exception):
    """Unusable export configuration (bad output location, missing corpus)."""


# --------------------------------------------------------------------------
# Corpus reading (read-only)
# --------------------------------------------------------------------------


@dataclass
class PartStreams:
    path: Path
    manifest: Manifest
    states: list[StateRecord] = field(default_factory=list)
    actions: list[ActionRecord] = field(default_factory=list)
    events: list[EventRecord] = field(default_factory=list)
    errors: dict[str, str] = field(default_factory=dict)


def _read_stream(path: Path, stream: str, parser: Callable[..., Any], tolerate_torn: bool) -> tuple[list[Any], str | None]:
    """Parse one stream, keeping every record read before any failure."""
    stream_file = resolve_stream_path(path, stream)
    on_torn = (lambda _line, _error: None) if tolerate_torn else None
    records: list[Any] = []
    try:
        for line_no, raw in iter_jsonl(stream_file, on_torn_final=on_torn):
            records.append(parser(raw, where=f"{stream_file.name}:{line_no}"))
    except (JsonlError, RecordError) as error:
        return sorted(records, key=lambda r: r.seq), str(error)
    return sorted(records, key=lambda r: r.seq), None


def load_part(path: Path, manifest: Manifest, streams: Iterable[str] = ("states", "actions", "events")) -> PartStreams:
    part = PartStreams(path=path, manifest=manifest)
    parsers = {"states": parse_state_record, "actions": parse_action_record, "events": parse_event_record}
    for stream in streams:
        records, error = _read_stream(path, stream, parsers[stream], manifest.incomplete)
        setattr(part, stream, records)
        if error is not None:
            part.errors[stream] = error
    return part


@dataclass
class CorpusVocabulary:
    """Corpus-wide lookups: zh card names and the power ids snapshots show."""

    names: CardNameMap = field(default_factory=CardNameMap)
    visible_powers: set[str] = field(default_factory=set)
    errors: list[str] = field(default_factory=list)

    def observe_state(self, character: str, state: Any) -> None:
        if not isinstance(state, Mapping):
            return
        player = state.get("player")
        if isinstance(player, Mapping):
            hand = player.get("hand")
            if isinstance(hand, list) and hand:
                self.names.observe(character, (c for c in hand if isinstance(c, Mapping)))
            self._powers(player.get("status"))
        battle = state.get("battle")
        if isinstance(battle, Mapping):
            for enemy in battle.get("enemies") or ():
                if isinstance(enemy, Mapping):
                    self._powers(enemy.get("status"))

    def _powers(self, statuses: Any) -> None:
        for status in statuses or ():
            if isinstance(status, Mapping) and isinstance(status.get("id"), str):
                self.visible_powers.add(status["id"])


def build_vocabulary(lineages: Sequence[Sequence[tuple[Path, Manifest]]]) -> CorpusVocabulary:
    """Learn zh card names (from hand entries) and visible power ids from
    every snapshot in the corpus."""
    vocabulary = CorpusVocabulary()
    for lineage in lineages:
        for path, manifest in lineage:
            stream_file = resolve_stream_path(path, "states")
            on_torn = (lambda _line, _error: None) if manifest.incomplete else None
            try:
                for _line_no, raw in iter_jsonl(stream_file, on_torn_final=on_torn):
                    vocabulary.observe_state(manifest.run.character, raw.get("state"))
            except JsonlError as error:
                vocabulary.errors.append(f"{path.name}: {error}")
    return vocabulary


def read_native_modifiers(lineage: Sequence[tuple[Path, Manifest]]) -> list[Any] | None:
    """``modifiers`` from any archived native run_history.run in the lineage."""
    for path, _manifest in reversed(lineage):
        native = path / "native" / "run_history.run"
        if not native.is_file():
            continue
        try:
            data = json.loads(native.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        modifiers = data.get("modifiers") if isinstance(data, Mapping) else None
        if isinstance(modifiers, list):
            return modifiers
    return None


# --------------------------------------------------------------------------
# First combat discovery
# --------------------------------------------------------------------------


@dataclass
class AttemptCandidate:
    part_index: int
    room: EventRecord
    end_seq: int | None


@dataclass
class FirstCombat:
    act: int
    floor: int
    encounter: str
    candidates: list[AttemptCandidate]
    neow_stats: Mapping[str, Any] | None
    neow_seen: bool


def _is_neow_summary(event: EventRecord) -> bool:
    if event.entry != "floor_summary" or not isinstance(event.data, Mapping):
        return False
    rooms = event.data.get("rooms") or ()
    return any(isinstance(room, Mapping) and room.get("model_id") == NEOW_ROOM_ID for room in rooms)


def find_first_combat(event_parts: Sequence[Sequence[EventRecord]]) -> FirstCombat | None:
    """Locate the lineage's first combat room and every entry into it.

    ``event_parts`` are the event streams of the lineage parts in part order.
    Entries stop being attempts once the run has moved past the combat floor.
    """
    first: tuple[int, int, str] | None = None
    candidates: list[AttemptCandidate] = []
    neow_stats: Mapping[str, Any] | None = None
    neow_seen = False
    for part_index, events in enumerate(event_parts):
        rooms = [e for e in events if e.entry == "room_entered" and isinstance(e.data, Mapping)]
        for event in events:
            if first is None and _is_neow_summary(event):
                stats = event.data.get("player_stats") or ()
                neow_stats = stats[0] if stats and isinstance(stats[0], Mapping) else None
            if (
                first is None
                and event.entry == "room_entered"
                and isinstance(event.data, Mapping)
                and event.data.get("room_model_id") == NEOW_ROOM_ID
            ):
                neow_seen = True
        for position, room in enumerate(rooms):
            data = room.data
            model = str(data.get("room_model_id") or "")
            act = data.get("act")
            floor = data.get("floor")
            if not isinstance(act, int) or not isinstance(floor, int):
                continue
            end_seq = rooms[position + 1].seq if position + 1 < len(rooms) else None
            if first is None:
                if model.startswith(COMBAT_ROOM_PREFIX):
                    first = (act, floor, model)
                    candidates.append(AttemptCandidate(part_index, room, end_seq))
                continue
            if (act, floor, model) == first:
                candidates.append(AttemptCandidate(part_index, room, end_seq))
            elif (act, floor) > first[:2]:
                return FirstCombat(first[0], first[1], first[2][len(COMBAT_ROOM_PREFIX) :], candidates, neow_stats, neow_seen)
    if first is None:
        return None
    return FirstCombat(first[0], first[1], first[2][len(COMBAT_ROOM_PREFIX) :], candidates, neow_stats, neow_seen)


# --------------------------------------------------------------------------
# One attempt -> bundle
# --------------------------------------------------------------------------


@dataclass
class AttemptResult:
    record: dict[str, Any]
    bundle: dict[str, Any] | None


def _window(records: Sequence[Any], start: int, end: int | None) -> list[Any]:
    return [r for r in records if r.seq > start and (end is None or r.seq < end)]


def _combat_end(states: Sequence[StateRecord], events: Sequence[EventRecord]) -> tuple[int | None, str | None, str]:
    """(end seq, outcome, how) of the combat inside an attempt window."""
    candidates: list[tuple[int, str | None, str]] = []
    battle_seqs = [record.seq for record in states if is_battle_state(record.state)]
    last_battle_seq = max(battle_seqs) if battle_seqs else None
    seen_battle = False
    for record in states:
        state = record.state
        if is_battle_state(state):
            seen_battle = True
            continue
        if not seen_battle or not isinstance(state, Mapping):
            continue
        if last_battle_seq is not None and record.seq < last_battle_seq:
            # A screen between battle snapshots of this room (card selection, the map view):
            # the combat goes on.
            continue
        state_type = state.get("state_type")
        message = state.get("message")
        if state_type == "game_over":
            candidates.append((record.seq, "loss", "game_over_state"))
            break
        if isinstance(message, str) and "Combat ended" in message:
            candidates.append((record.seq, "win", "combat_ended_message"))
            break
        if state_type in COMBAT_END_STATE_TYPES:
            candidates.append((record.seq, "win", f"{state_type}_state"))
            break
    for event in events:
        if event.entry == "run_ended" and isinstance(event.data, Mapping):
            if event.data.get("abandoned"):
                outcome = "abandoned"
            elif event.data.get("win"):
                outcome = "win"
            else:
                outcome = "loss"
            candidates.append((event.seq, outcome, "run_ended"))
            break
    if not candidates:
        return None, None, "open"
    candidates.sort(key=lambda c: c[0])
    end_seq, outcome, how = candidates[0]
    # A game_over / defeat screen and run_ended may both fire; run_ended knows
    # whether the run was abandoned rather than lost.
    for seq, other_outcome, other_how in candidates[1:]:
        if other_how == "run_ended" and outcome == "loss":
            outcome = other_outcome
    return end_seq, outcome, how


def _final_player_hp(last_battle: StateRecord, events: Sequence[EventRecord]) -> int | None:
    """Player hp at combat end, before any post-victory heal.

    The last in-combat snapshot can predate the final resolution; every
    event's creature references carry hp after the event, so a later event
    naming the player supersedes the snapshot. Post-victory heals (Burning
    Blood) are recorded by no event inside the combat window.
    """
    player = last_battle.state.get("player")
    hp = player.get("hp") if isinstance(player, Mapping) and isinstance(player.get("hp"), int) else None
    for event in events:
        if event.seq <= last_battle.seq:
            continue
        for creature in event_creatures(event.data):
            if creature.get("is_player") is True and isinstance(creature.get("hp"), int):
                hp = creature["hp"]
    return hp


def _card_play_finish(event: EventRecord, card_id: str) -> bool:
    data = event.data if isinstance(event.data, Mapping) else {}
    card_play = data.get("card_play") if isinstance(data.get("card_play"), Mapping) else {}
    card = card_play.get("card") if isinstance(card_play.get("card"), Mapping) else {}
    return (
        event.entry == "card_play_finished"
        and not card_play.get("is_auto_play")
        and strip_prefix(card.get("id"), "CARD.") == card_id
    )


def extract_decisions(
    *,
    states: Sequence[StateRecord],
    actions: Sequence[ActionRecord],
    events: Sequence[EventRecord],
    character: str,
    vocabulary: CorpusVocabulary,
    monster_ids: Mapping[int, str],
    pre_state_mode: str = PRE_STATE_SETTLED,
) -> tuple[list[dict[str, Any]], list[str], list[str], Counter]:
    """Decisions (play_card / end_turn) of one combat, with their pre_states.

    ``actions`` must already be limited to the combat. Returns (decisions,
    reasons, notes, counters); a decision whose action cannot be resolved
    has ``action: None`` and a reason.
    """
    states_by_seq = {s.seq: s for s in states}
    state_seqs = [s.seq for s in states]
    battle_states = [s for s in states if is_battle_state(s.state)]
    battle_seqs = [s.seq for s in battle_states]
    decisions: list[dict[str, Any]] = []
    reasons: list[str] = []
    notes: list[str] = []
    counts: Counter = Counter()
    previous: dict[str, Any] | None = None
    turn_end_recorded = False
    for action in actions:
        kind = action.action.get("kind")
        raw_params = action.action.get("params")
        params = raw_params if isinstance(raw_params, Mapping) else {
            k: v for k, v in action.action.items() if k != "kind"
        }
        if kind == TURN_END_FOLLOW_UP_KIND:
            # Logged after every player turn end; the decision is the end_turn recorded since
            # the previous one. Recorder 0.2.0 parts driven through STS2_MCP log only this
            # follow-up (1784043137-CPSVMWJPUL: 16 play_card, 5 of these, no end_turn), and a
            # bundle without its end_turn decisions would never advance the sim's turns.
            if not turn_end_recorded:
                reasons.append(f"turn_end_unrecorded:seq{action.seq}")
            turn_end_recorded = False
            continue
        if kind in EXCLUDED_ACTION_KINDS:
            continue
        if kind == "player_choice":
            # Every in-combat choice (a discard, a card to put on top, ...) changes the combat in
            # a way the bundle cannot express. The context label proves nothing about who asked
            # (STICKY_CHOICE_CONTEXT_LABEL), so no choice is skipped as automatic.
            reasons.append("unsupported_action:player_choice")
            label = params.get("context_model")
            sticky = " (sticky recorder label, not the requester)" if label == STICKY_CHOICE_CONTEXT_LABEL else ""
            notes.append(
                f"player_choice at seq {action.seq}: {params.get('choice_type')}, "
                f"context {label}{sticky}, picked={params.get('picked')}"
            )
            continue
        if kind == "use_potion":
            reasons.append("potion_used")
            continue
        if kind == "discard_potion":
            notes.append(f"discard_potion at seq {action.seq} (no combat effect)")
            continue
        if kind not in DECISION_ACTION_KINDS:
            reasons.append(f"unsupported_action:{kind}")
            continue
        if action.status == "cancelled":
            notes.append(f"cancelled {kind} at seq {action.seq} skipped")
            continue
        if kind == "end_turn":
            turn_end_recorded = True
        if (
            kind == "end_turn"
            and previous is not None
            and previous["kind"] == "end_turn"
            and params.get("turn_number") is not None
            and params.get("turn_number") == previous["turn_number"]
        ):
            notes.append(f"duplicate end_turn for turn {previous['turn_number']} at seq {action.seq} skipped")
            continue

        index = len(decisions)
        pre = states_by_seq.get(action.state_seq) if action.state_seq is not None else None
        view_seq = None
        if (
            pre is not None
            and not is_battle_state(pre.state)
            and isinstance(pre.state, Mapping)
            and pre.state.get("state_type") in COMBAT_VIEW_STATE_TYPES
        ):
            # state_seq is the map view opened mid-combat (corpus: 24 play_card actions point at
            # one): nothing changed since the latest battle snapshot before it; the events in
            # between are pending events as usual.
            view_seq = pre.seq
            position = bisect_left(battle_seqs, pre.seq)
            pre = battle_states[position - 1] if position > 0 else None
        if pre is None or not is_battle_state(pre.state):
            reasons.append(f"pre_state_missing:decision{index}")
            decisions.append({"index": index, "pre_state": None, "action": None, "oracle": {"action_seq": action.seq}})
            previous = None
            continue
        if previous is not None and previous["kind"] == "play_card" and pre.seq == previous["state_seq"]:
            reasons.append(f"pre_state_shared:decision{index}")
        snapshot_projection, issues = project_state(
            pre.state, character=character, names=vocabulary.names, monster_ids=monster_ids
        )
        reasons += issues

        gap = [e for e in events if pre.seq < e.seq < action.seq and e.entry not in NON_STATE_EVENT_ENTRIES]
        played = previous["played"] if previous is not None else None
        # Where the previous decision's card is at this snapshot: decided by
        # card counts (hand + piles drop by one while it sits in the play
        # pile), never by event order.
        finish = None
        resolving_at_hook = False
        in_limbo: bool | None = None
        if played is not None:
            finish = next(
                (e for e in events if e.seq > previous["action_seq"] and _card_play_finish(e, played[0])),
                None,
            )
            if finish is not None and finish.seq > action.seq:
                own_start = any(
                    e.entry == "card_play_started" and action.seq < e.seq < finish.seq for e in events
                )
                resolving_at_hook = not own_start
            generated = any(
                e.entry == "card_generated" and previous["state_seq"] < e.seq < action.seq for e in events
            )
            snapshot_total = visible_card_total(snapshot_projection)
            if previous["total"] is not None and snapshot_total is not None and not generated:
                if snapshot_total == previous["total"] - 1:
                    in_limbo = True
                elif snapshot_total == previous["total"]:
                    in_limbo = False
        destination = None
        if finish is not None and isinstance(finish.data, Mapping):
            card_play = finish.data.get("card_play")
            destination = card_play.get("result_pile") if isinstance(card_play, Mapping) else None
        pending = [e.entry for e in gap]
        move_pending = bool(in_limbo) and destination not in (None, "None")
        settled = not pending and not move_pending and not resolving_at_hook
        projection: dict[str, Any] | None = snapshot_projection
        oracle: dict[str, Any] = {
            "action_seq": action.seq,
            "state_seq": pre.seq,
            "settled": settled,
            "pending_events": pending,
            "combat_card_index": params.get("combat_card_index"),
        }
        if view_seq is not None:
            oracle["view_state_seq"] = view_seq
        if not settled:
            counts["unsettled"] += 1
            gap_pairs = [(e.entry, e.data) for e in gap]
            rolled, problem = None, "mid_resolution_snapshot"
            if resolving_at_hook:
                problem = "previous_card_resolving_at_hook"
            elif pre_state_mode == PRE_STATE_SETTLED:
                rolled, problem = roll_forward(
                    snapshot_projection,
                    gap_pairs,
                    played_card=played,
                    visible_powers=vocabulary.visible_powers,
                    played_in_limbo=in_limbo,
                    played_destination=destination,
                )
            if pre_state_mode == PRE_STATE_SNAPSHOT:
                pass
            elif rolled is not None:
                projection = rolled
                counts["rolled_forward"] += 1
                oracle["rolled_forward"] = True
                oracle["snapshot_pre_state"] = snapshot_projection
            else:
                # Contract extension (sim/specs/diff-harness.md section 5):
                # pre_state null = replay the action, compare nothing.
                projection = None
                counts["unobserved"] += 1
                oracle["unobserved_reason"] = problem
                oracle["snapshot_pre_state"] = snapshot_projection
                hand_kept = (
                    not resolving_at_hook
                    and not (in_limbo and destination == "Hand")
                    and gap_keeps_hand(gap_pairs, played_card=played)
                )
                if kind == "play_card" and not hand_kept:
                    reasons.append(f"pre_state_hand_unsettled:{problem}:decision{index}")

        bundle_action: dict[str, Any] | None
        if kind == "end_turn":
            bundle_action = {"type": "end_turn"}
        else:
            card_id = strip_prefix(params.get("card_model_id") or params.get("card_name") or "", "CARD.")
            pre_hand = pre.state["player"].get("hand") or []
            # The recorder's post-action snapshot is taken once the played
            # card has left the hand; it disambiguates identical copies.
            after = bisect_right(state_seqs, action.seq)
            post_state = states[after].state if after < len(states) else None
            post_hand = (post_state["player"].get("hand") or []) if is_battle_state(post_state) else None
            hand_index, problem = resolve_hand_index(pre_hand, card_id, post_hand)
            if problem is not None:
                reasons.append(f"{problem}:decision{index}")
                bundle_action = None
            else:
                target = params.get("target_id")
                target_id = target if isinstance(target, int) and not isinstance(target, bool) else None
                if target_id is not None and target_id not in {e["combat_id"] for e in snapshot_projection["enemies"]}:
                    reasons.append(f"target_not_in_pre_state:decision{index}")
                chosen = pre_hand[hand_index]
                bundle_action = {
                    "type": "play_card",
                    "hand_index": hand_index,
                    "card_id": chosen.get("id"),
                    "upgraded": bool(chosen.get("is_upgraded")),
                    "target_combat_id": target_id,
                }
        decisions.append({"index": index, "pre_state": projection, "action": bundle_action, "oracle": oracle})
        trusted = projection is not None and (settled or oracle.get("rolled_forward"))
        previous = {
            "kind": kind,
            "action_seq": action.seq,
            "state_seq": pre.seq,
            "played": (
                (bundle_action["card_id"], bundle_action["upgraded"])
                if bundle_action is not None and bundle_action["type"] == "play_card"
                else None
            ),
            "total": visible_card_total(projection) if trusted else None,
            "turn_number": params.get("turn_number") if kind == "end_turn" else None,
        }
    return decisions, list(dict.fromkeys(reasons)), notes, counts


def build_attempt(
    *,
    session: str,
    part: PartStreams,
    lineage_versions: set[str],
    attempt: int,
    first: FirstCombat,
    candidate: AttemptCandidate,
    vocabulary: CorpusVocabulary,
    registry: SimRegistry,
    modifiers: list[Any] | None,
    pre_state_mode: str = "settled",
) -> AttemptResult:
    manifest = part.manifest
    run = manifest.run
    version = manifest.game.version
    reasons: list[str] = []
    notes: list[str] = []

    record: dict[str, Any] = {
        "session": session,
        "part": part.path.name,
        "attempt": attempt,
        "game_version": version,
        "seed": run.seed,
        "character": run.character,
        "ascension": run.ascension,
        "game_mode": run.game_mode,
        "act": first.act + 1,
        "floor": first.floor,
        "encounter_id": first.encounter,
    }

    seeding, seed_reasons = seeding_for(version, run.seed)
    reasons += seed_reasons
    record["seeding"] = seeding
    if len(lineage_versions) > 1:
        reasons.append("lineage_version_mixed:" + ",".join(sorted(lineage_versions)))
    if run.game_mode != "standard":
        if modifiers is None:
            reasons.append(f"game_mode_modifiers_unknown:{run.game_mode}")
        elif modifiers:
            reasons.append(f"game_mode_modifiers:{run.game_mode}")
        else:
            notes.append(f"game_mode {run.game_mode} with no modifiers (native run_history)")
    if (first.act, first.floor) != (0, 2):
        reasons.append(f"first_combat_not_floor_2:act{first.act + 1}_floor{first.floor}")
    if not first.neow_seen:
        notes.append("neow room not recorded in this lineage")
    for stream, error in part.errors.items():
        reasons.append(f"stream_unreadable:{stream}")
        notes.append(f"{stream}: {error}")

    start = candidate.room.seq
    states = _window(part.states, start, candidate.end_seq)
    actions = _window(part.actions, start, candidate.end_seq)
    events = _window(part.events, start, candidate.end_seq)
    battle_states = [s for s in states if is_battle_state(s.state)]
    if not battle_states:
        record.update({"eligible": False, "reasons": ["no_combat_snapshot"], "notes": notes, "bundle": None})
        return AttemptResult(record, None)

    end_seq, outcome, end_how = _combat_end(states, events)
    if outcome is None:
        if candidate.end_seq is not None:
            outcome, end_how = "win", "next_room_entered"
        else:
            outcome, end_how = "abandoned", "part_ended"
    record["outcome"] = outcome
    record["outcome_evidence"] = end_how

    def in_combat(item: Any) -> bool:
        return end_seq is None or item.seq < end_seq

    battle_states = [s for s in battle_states if in_combat(s)]
    first_state = battle_states[0].state
    round_number = first_state["battle"].get("round")
    if round_number != 1:
        reasons.append(f"attempt_not_from_combat_start:round{round_number}")

    monster_ids = extract_monster_ids(e.data for e in events)
    relics = [
        str(r.get("id"))
        for r in first_state["player"].get("relics") or ()
        if isinstance(r, Mapping) and r.get("id") is not None
    ]
    reasons += relic_reasons(relics)
    offsets = {"Niche": niche_offset(relics)}

    deck, deck_issues = reconstruct_deck(run.character, run.ascension, first.neow_stats)
    observed, snapshot_issues = snapshot_deck_multiset(first_state, character=run.character, names=vocabulary.names)
    reasons += snapshot_issues
    if "neow_summary_missing" in deck_issues and deck is not None:
        base = base_deck(run.character, run.ascension) or []
        if not snapshot_issues and deck_multiset(base) == observed:
            deck_issues = [i for i in deck_issues if i != "neow_summary_missing"]
            notes.append("neow summary missing; combat deck equals the starter deck, so its order is known")
    reasons += deck_issues
    if deck is not None and not snapshot_issues:
        delta = multiset_delta(deck_multiset(deck), observed)
        if delta:
            reasons.append(f"deck_mismatch:{delta}")
    record["deck_size"] = len(deck) if deck is not None else None
    record["neow_picks"] = [
        strip_prefix(c.get("choice"), "RELIC.")
        for c in (first.neow_stats or {}).get("relic_choices") or ()
        if isinstance(c, Mapping) and c.get("was_picked")
    ]
    record["relics"] = relics

    reasons += sim_coverage_reasons(registry, character=run.character, encounter_id=first.encounter, deck=deck)

    # Decisions --------------------------------------------------------------
    if any(e.entry == "potion_used" and in_combat(e) for e in events):
        reasons.append("potion_used")
    decisions, decision_reasons, decision_notes, counts = extract_decisions(
        states=states,
        actions=[a for a in actions if in_combat(a)],
        events=events,
        character=run.character,
        vocabulary=vocabulary,
        monster_ids=monster_ids,
        pre_state_mode=pre_state_mode,
    )
    reasons += decision_reasons
    notes += decision_notes
    unsettled = counts["unsettled"]
    rolled_forward = counts["rolled_forward"]
    unobserved = counts["unobserved"]

    if not decisions:
        reasons.append("no_decisions")
    final_hp = _final_player_hp(battle_states[-1], [e for e in events if in_combat(e)])
    record["decisions"] = len(decisions)
    record["unsettled_pre_states"] = unsettled
    record["rolled_forward_pre_states"] = rolled_forward
    record["unobserved_pre_states"] = unobserved
    record["final_hp"] = final_hp

    reasons = list(dict.fromkeys(reasons))
    eligible = not reasons
    record["eligible"] = eligible
    record["reasons"] = reasons
    record["notes"] = list(dict.fromkeys(notes))

    bundle = None
    if deck is not None and seeding is not None and all(d["action"] is not None for d in decisions):
        player = first_state["player"]
        bundle = {
            "bundle_version": BUNDLE_VERSION,
            "source": {
                "session": session,
                "part": part.path.name,
                "attempt": attempt,
                "act": first.act + 1,
                "floor": first.floor,
            },
            "game_version": version,
            "seeding": seeding,
            "seed": run.seed,
            "character": run.character,
            "ascension": run.ascension,
            "encounter_id": first.encounter,
            "player": {"hp": player.get("hp"), "max_hp": player.get("max_hp")},
            "deck": [{"id": card_id, "upgraded": upgraded} for card_id, upgraded in deck],
            "relics": relics,
            "rng_offsets": offsets,
            "decisions": decisions,
            "outcome": outcome,
            "final_hp": final_hp,
        }
        if not eligible:
            # Additive key (sim/specs/diff-bundle-exporter.md §2): the bundle body cannot show every
            # reason (deck_mismatch, potion_used, unsupported_action, ...), so an ineligible bundle
            # carries the verdict and the diff harness skips it unless run with --include-ineligible.
            bundle["ineligible_reasons"] = list(reasons)
    return AttemptResult(record, bundle)


# --------------------------------------------------------------------------
# Corpus export
# --------------------------------------------------------------------------


def _inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def check_output_dir(out_dir: Path, *, corpus_root: Path, forbidden: Iterable[Path] = ()) -> None:
    if _inside(out_dir, corpus_root) or _inside(corpus_root, out_dir):
        raise ExportError(f"refusing to write {out_dir}: it overlaps the corpus {corpus_root}")
    for root in forbidden:
        if _inside(out_dir, root):
            raise ExportError(f"refusing to write {out_dir}: it is inside {root}")


def export_bundles(
    corpus_root: Path,
    out_dir: Path,
    registry: SimRegistry,
    *,
    include_ineligible: bool = False,
    pre_state_mode: str = PRE_STATE_SETTLED,
    forbidden_roots: Iterable[Path] = (),
    log: Callable[[str], None] | None = None,
) -> dict[str, Any]:
    """Export every first-combat attempt under ``corpus_root``; returns the index."""
    corpus_root = Path(corpus_root)
    out_dir = Path(out_dir)
    if pre_state_mode not in PRE_STATE_MODES:
        raise ExportError(f"unknown pre_state mode {pre_state_mode!r} (expected one of {PRE_STATE_MODES})")
    if not corpus_root.is_dir():
        raise ExportError(f"no sessions directory: {corpus_root}")
    check_output_dir(out_dir, corpus_root=corpus_root, forbidden=forbidden_roots)
    say = log or (lambda _msg: None)

    lineages = group_lineages(corpus_root)
    say(f"{len(lineages)} lineages under {corpus_root}")
    vocabulary = build_vocabulary(lineages)
    say(
        f"vocabulary: {len(vocabulary.names.global_names)} card names, "
        f"{len(vocabulary.visible_powers)} visible powers "
        f"({len(vocabulary.errors)} unreadable state streams)"
    )

    records: list[dict[str, Any]] = []
    lineage_records: list[dict[str, Any]] = []
    bundles: list[tuple[dict[str, Any], dict[str, Any]]] = []
    for lineage in lineages:
        session = lineage[0][0].name
        first_manifest = lineage[0][1]
        lineage_record: dict[str, Any] = {
            "session": session,
            "parts": len(lineage),
            "game_version": first_manifest.game.version,
            "character": first_manifest.run.character,
            "ascension": first_manifest.run.ascension,
        }
        event_parts: list[list[EventRecord]] = []
        part_cache: dict[int, PartStreams] = {}
        event_errors: dict[int, str] = {}
        for index, (path, manifest) in enumerate(lineage):
            records_, error = _read_stream(path, "events", parse_event_record, manifest.incomplete)
            event_parts.append(records_)
            if error is not None:
                event_errors[index] = error
            if any(
                e.entry == "room_entered"
                and isinstance(e.data, Mapping)
                and isinstance(e.data.get("floor"), int)
                and e.data["floor"] > 2
                for e in records_
            ):
                break
        if event_errors:
            lineage_record["event_stream_errors"] = {lineage[i][0].name: e for i, e in event_errors.items()}
        first = find_first_combat(event_parts)
        if first is None:
            lineage_record["status"] = "no_combat_recorded"
            lineage_records.append(lineage_record)
            continue
        lineage_record["first_combat"] = {"act": first.act + 1, "floor": first.floor, "encounter_id": first.encounter}
        versions = {manifest.game.version for _path, manifest in lineage[: max(c.part_index for c in first.candidates) + 1]}
        modifiers = read_native_modifiers(lineage)
        attempt = 0
        skipped_resumes = 0
        for candidate in first.candidates:
            if candidate.part_index not in part_cache:
                path, manifest = lineage[candidate.part_index]
                part_cache[candidate.part_index] = load_part(path, manifest)
            part = part_cache[candidate.part_index]
            window_states = _window(part.states, candidate.room.seq, candidate.end_seq)
            if not any(is_battle_state(s.state) for s in window_states):
                skipped_resumes += 1
                continue
            attempt += 1
            result = build_attempt(
                session=session,
                part=part,
                lineage_versions=versions,
                attempt=attempt,
                first=first,
                candidate=candidate,
                vocabulary=vocabulary,
                registry=registry,
                modifiers=modifiers,
                pre_state_mode=pre_state_mode,
            )
            records.append(result.record)
            if result.bundle is not None:
                bundles.append((result.record, result.bundle))
        lineage_record["status"] = "ok" if attempt else "no_combat_snapshot"
        lineage_record["attempts"] = attempt
        lineage_record["post_combat_resumes_skipped"] = skipped_resumes
        lineage_records.append(lineage_record)

    out_dir.mkdir(parents=True, exist_ok=True)
    for record, bundle in bundles:
        if not record["eligible"] and not include_ineligible:
            record["bundle"] = None
            continue
        folder = "bundles" if record["eligible"] else "ineligible"
        target = out_dir / folder / record["game_version"] / f"{record['session']}.attempt{record['attempt']}.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(bundle, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        record["bundle"] = str(target)
    for record in records:
        record.setdefault("bundle", None)

    index = {
        "index_version": REASONS_INDEX_VERSION,
        # Not "bundle_version": the diff harness treats any JSON object with a
        # top-level bundle_version as a bundle (sim/specs/diff-harness.md).
        "bundle_contract_version": BUNDLE_VERSION,
        "corpus_root": str(corpus_root),
        "out_dir": str(out_dir),
        "pre_state_mode": pre_state_mode,
        "sim_registry": registry.to_json(),
        "relic_whitelist": sorted(RELIC_WHITELIST),
        "summary": summarize(records, lineage_records),
        "vocabulary_errors": vocabulary.errors,
        "visible_powers": sorted(vocabulary.visible_powers),
        "lineages": lineage_records,
        "combats": records,
    }
    (out_dir / "reasons.json").write_text(json.dumps(index, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    (out_dir / "name_map.json").write_text(
        json.dumps(vocabulary.names.to_json(), ensure_ascii=False, indent=1) + "\n", encoding="utf-8"
    )
    return index


# Reason classes that only say the simulator lacks content (not oracle-side
# problems): an attempt failing on these alone becomes eligible as content lands.
SIM_COVERAGE_REASON_CLASSES = frozenset(
    {"character_not_ported", "encounter_not_ported", "card_not_ported", "card_upgrade_not_ported"}
)


def summarize(records: Sequence[Mapping[str, Any]], lineages: Sequence[Mapping[str, Any]]) -> dict[str, Any]:
    by_version: Counter = Counter()
    eligible_by_version: Counter = Counter()
    reason_hist: Counter = Counter()
    sole_reason: Counter = Counter()
    outcomes: Counter = Counter()
    coverage_only: Counter = Counter()
    for record in records:
        version = record["game_version"]
        by_version[version] += 1
        outcomes[record.get("outcome")] += 1
        if record["eligible"]:
            eligible_by_version[version] += 1
        classes = sorted({reason_class(r) for r in record["reasons"]})
        for cls in classes:
            reason_hist[cls] += 1
        if len(classes) == 1:
            sole_reason[classes[0]] += 1
        if classes and set(classes) <= SIM_COVERAGE_REASON_CLASSES:
            coverage_only[version] += 1
    lineage_status = Counter(str(lineage.get("status")) for lineage in lineages)
    return {
        "blocked_only_by_sim_coverage": sum(coverage_only.values()),
        "blocked_only_by_sim_coverage_by_version": dict(sorted(coverage_only.items())),
        "rolled_forward_pre_states": sum(int(r.get("rolled_forward_pre_states") or 0) for r in records),
        "unobserved_pre_states": sum(int(r.get("unobserved_pre_states") or 0) for r in records),
        "lineages": len(lineages),
        "lineage_status": dict(sorted(lineage_status.items())),
        "attempts": len(records),
        "attempts_by_version": dict(sorted(by_version.items())),
        "eligible": sum(eligible_by_version.values()),
        "eligible_by_version": dict(sorted(eligible_by_version.items())),
        "outcomes": dict(sorted((str(k), v) for k, v in outcomes.items())),
        "reason_classes": dict(sorted(reason_hist.items(), key=lambda kv: (-kv[1], kv[0]))),
        "sole_reason_classes": dict(sorted(sole_reason.items(), key=lambda kv: (-kv[1], kv[0]))),
        "unsettled_pre_states": sum(int(r.get("unsettled_pre_states") or 0) for r in records),
        "decisions": sum(int(r.get("decisions") or 0) for r in records),
    }
