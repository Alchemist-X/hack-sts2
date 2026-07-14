"""Merge session streams into the hack-balatro-style canonical trajectory.

Output shape (docs/design.md, "Canonical trajectory"):

    {"meta": {...}, "prelude_events": [...],
     "steps": [{"step_idx", "ts", "state_before", "action",
                "state_after", "reward", "terminal", "info"}]}

Semantics:
- one step per hooked action, in seq order (seq is session-global, so it is a
  total order across streams);
- state_before  = payload of the snapshot the action references (state_seq);
- state_after   = payload of the first snapshot with seq > action.seq
                  (null when the run ended before another snapshot);
- info.events   = the events partitioned by action boundaries: step i holds
                  exactly the events with action_i.seq < event.seq <
                  action_{i+1}.seq (unbounded above for the final step);
                  events before the first action (e.g. combat-start draws)
                  land in the top-level "prelude_events" list. Invariant:
                  every event appears in exactly one place;
- meta.result   = the manifest result object (or null), so sessions with
                  zero recorded actions keep their outcome;
- terminal      = true only on the last step, and only when manifest.result
                  is set; the result object is also attached as info.result;
- reward        = null in v1: STS2 has no native scalar reward, and defining
                  one (win/loss shaping, HP delta, ...) is a benchmark-design
                  decision deferred to the analyzer layer.

Output is deterministic: same session bytes -> same trajectory dict.
"""

from __future__ import annotations

from bisect import bisect_right
from collections.abc import Mapping
from pathlib import Path
from typing import Any

from .errors import CanonicalError, JsonlError, RecordError
from .session import (
    ActionRecord,
    EventRecord,
    Manifest,
    RunResult,
    StateRecord,
    iter_jsonl,
    load_manifest,
    parse_action_record,
    parse_event_record,
    parse_state_record,
    stream_path,
)

REWARD_NOTE = (
    "reward is null in schema_version 1; derive rewards offline from "
    "info.result / state deltas"
)


def _load_stream(session_dir: Path, stream: str, parser: Any) -> tuple[Any, ...]:
    path = stream_path(session_dir, stream)
    try:
        return tuple(
            parser(raw, where=f"{path.name}:{line_no}")
            for line_no, raw in iter_jsonl(path)
        )
    except (JsonlError, RecordError) as error:
        raise CanonicalError(
            f"cannot build canonical trajectory: {error}"
        ) from error


def _result_payload(result: RunResult | None) -> dict[str, Any] | None:
    if result is None:
        return None
    return {
        "win": result.win,
        "abandoned": result.abandoned,
        "end_time": result.end_time,
    }


def _build_meta(manifest: Manifest, session_dir: Path) -> dict[str, Any]:
    return {
        "source": "sts2-real-client",
        "session_schema_version": manifest.schema_version,
        "run_id": session_dir.name,
        "seed": manifest.run.seed,
        "character": manifest.run.character,
        "ascension": manifest.run.ascension,
        "game_mode": manifest.run.game_mode,
        "start_time": manifest.run.start_time,
        "game_version": manifest.game.version,
        "game_build_id": manifest.game.build_id,
        "recorder_version": manifest.recorder_version,
        "platform": manifest.platform,
        "incomplete": manifest.incomplete,
        "part": manifest.part,
        "degraded_hooks": list(manifest.degraded_hooks),
        "result": _result_payload(manifest.result),
        "reward_note": REWARD_NOTE,
    }


def _event_payload(event: EventRecord) -> dict[str, Any]:
    return {"seq": event.seq, "t": event.t, "entry": event.entry, "data": event.data}


def _action_payload(action: ActionRecord) -> dict[str, Any]:
    extras = {key: value for key, value in action.action.items() if key != "kind"}
    # Recorder >= 0.1 nests parameters as action.params ({kind, params:{...}});
    # earlier synthetic sessions spread them inline ({kind, ...}). Accept both.
    if set(extras) == {"params"} and isinstance(extras["params"], Mapping):
        params = dict(extras["params"])
    else:
        params = extras
    return {"type": action.action["kind"], "params": params}


def _partition_events(
    events: tuple[EventRecord, ...], action_seqs: list[int]
) -> tuple[list[dict[str, Any]], list[list[dict[str, Any]]]]:
    """Partition events by action boundaries (every event lands exactly once).

    Returns (prelude, buckets): prelude holds events with seq < action_0.seq;
    buckets[i] holds events with action_i.seq < seq < action_{i+1}.seq
    (unbounded above for the last bucket). With zero actions every event is
    prelude.
    """
    prelude: list[dict[str, Any]] = []
    buckets: list[list[dict[str, Any]]] = [[] for _ in action_seqs]
    for event in events:
        index = bisect_right(action_seqs, event.seq)
        target = prelude if index == 0 else buckets[index - 1]
        target.append(_event_payload(event))
    return prelude, buckets


def _build_step(
    step_idx: int,
    action: ActionRecord,
    states: tuple[StateRecord, ...],
    states_by_seq: dict[int, StateRecord],
    state_seqs: list[int],
    step_events: list[dict[str, Any]],
    is_last: bool,
    manifest: Manifest,
) -> dict[str, Any]:
    state_before = states_by_seq.get(action.state_seq)
    if state_before is None:
        raise CanonicalError(
            f"action seq={action.seq} references state_seq={action.state_seq}, "
            "which does not exist in states.jsonl (run `sts2rec validate` "
            "for a full report)"
        )
    after_index = bisect_right(state_seqs, action.seq)
    state_after = states[after_index] if after_index < len(states) else None
    info: dict[str, Any] = {
        "events": step_events,
        "action_source": action.source,
        "state_before_seq": state_before.seq,
        "state_after_seq": state_after.seq if state_after is not None else None,
    }
    if action.status is not None:
        info["action_status"] = action.status
    terminal = False
    if is_last and manifest.result is not None:
        terminal = True
        info["result"] = _result_payload(manifest.result)
    return {
        "step_idx": step_idx,
        "ts": action.t,
        "state_before": state_before.state,
        "action": _action_payload(action),
        "state_after": state_after.state if state_after is not None else None,
        "reward": None,
        "terminal": terminal,
        "info": info,
    }


def build_canonical(session_dir: Path | str) -> dict[str, Any]:
    """Build the canonical {meta, prelude_events, steps[]} trajectory."""
    session_dir = Path(session_dir)
    manifest = load_manifest(session_dir)
    states: tuple[StateRecord, ...] = _load_stream(
        session_dir, "states", parse_state_record
    )
    actions: tuple[ActionRecord, ...] = _load_stream(
        session_dir, "actions", parse_action_record
    )
    events: tuple[EventRecord, ...] = _load_stream(
        session_dir, "events", parse_event_record
    )

    ordered_states = tuple(sorted(states, key=lambda record: record.seq))
    ordered_actions = tuple(sorted(actions, key=lambda record: record.seq))
    ordered_events = tuple(sorted(events, key=lambda record: record.seq))
    states_by_seq = {record.seq: record for record in ordered_states}
    state_seqs = [record.seq for record in ordered_states]
    action_seqs = [record.seq for record in ordered_actions]

    prelude_events, step_events = _partition_events(ordered_events, action_seqs)
    steps = [
        _build_step(
            step_idx=index,
            action=action,
            states=ordered_states,
            states_by_seq=states_by_seq,
            state_seqs=state_seqs,
            step_events=step_events[index],
            is_last=(index == len(ordered_actions) - 1),
            manifest=manifest,
        )
        for index, action in enumerate(ordered_actions)
    ]
    return {
        "meta": _build_meta(manifest, session_dir),
        "prelude_events": prelude_events,
        "steps": steps,
    }
