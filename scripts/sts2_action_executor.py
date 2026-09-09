#!/usr/bin/env python3
"""Execute one externally chosen STS2MCP action with an auditable record.

This module deliberately contains no gameplay policy.  It never chooses a card,
target, path, reward, or end turn on behalf of its caller.  An external reasoner
must first inspect the complete public state and then provide exactly one action
plus its concise decision reason.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def request(port: int, action: dict[str, Any] | None = None) -> dict[str, Any]:
    url = f"http://127.0.0.1:{port}/api/v1/singleplayer"
    kwargs: dict[str, Any] = {"method": "GET"}
    if action is not None:
        kwargs = {
            "data": json.dumps(action).encode(),
            "headers": {"Content-Type": "application/json"},
            "method": "POST",
        }
    req = urllib.request.Request(url, **kwargs)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            body = json.load(response)
    except urllib.error.HTTPError as error:
        raw = error.read().decode(errors="replace")
        raise SystemExit(f"HTTP {error.code}: {raw}") from error
    if not isinstance(body, dict):
        raise SystemExit(f"expected JSON object, got {type(body).__name__}")
    if action is not None and body.get("status") != "ok":
        raise SystemExit(f"action rejected: {body}")
    return body


def digest(state: dict[str, Any]) -> str:
    raw = json.dumps(state, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(raw.encode()).hexdigest()


def decision_digest(state: dict[str, Any]) -> str:
    """Exclude animated transform examples, retaining the selected originals.

    NTransformPreview cycles replacement examples every 0.2 seconds. They are
    presentation, not the committed random outcome. Raw audit hashes use digest.
    """
    selection = state.get("card_select", {})
    if selection.get("screen_type") == "transform" and selection.get("preview_showing"):
        state = dict(state)
        selection = dict(selection)
        # Explicit original/selected fields from the patched MCP are retained.
        # Legacy preview_cards has no trustworthy role tags: exclude it all,
        # rather than guessing that the first half are the original cards.
        selection.pop("preview_cards", None)
        selection.pop("preview_examples", None)
        state["card_select"] = selection
    return digest(state)


def summary(state: dict[str, Any]) -> dict[str, Any]:
    result: dict[str, Any] = {"state_type": state.get("state_type")}
    for key in ("run", "player", "battle", "rewards", "card_reward", "map", "event",
                "rest_site", "shop", "treasure", "card_select", "hand_select", "game_over", "menu_screen", "options"):
        if key in state:
            result[key] = state[key]
    return result


def _player(state: dict[str, Any]) -> dict[str, Any]:
    player = state.get("player")
    return player if isinstance(player, dict) else {}


def _battle(state: dict[str, Any]) -> dict[str, Any]:
    battle = state.get("battle")
    return battle if isinstance(battle, dict) else {}


def _hand(state: dict[str, Any]) -> list[dict[str, Any]]:
    hand = _player(state).get("hand")
    return [card for card in hand if isinstance(card, dict)] if isinstance(hand, list) else []


def _card_at(state: dict[str, Any], index: Any) -> dict[str, Any] | None:
    for card in _hand(state):
        if card.get("index") == index:
            return card
    return None


def _card_count(state: dict[str, Any], card_id: Any) -> int:
    return sum(card.get("id") == card_id for card in _hand(state))


def _play_card_committed(
    before: dict[str, Any], current: dict[str, Any], action: dict[str, Any]
) -> bool:
    """Return true only after the requested card left the hand/action queue.

    The MCP acknowledges a click before Godot has necessarily committed the
    card play. A generic state change is therefore insufficient: it may be the
    tail of the previous animation. Card-count reduction ties the observation
    to the exact card ID that this request selected, including when hand
    indices shift after earlier plays.
    """
    if current.get("state_type") != before.get("state_type"):
        return True
    before_battle, current_battle = _battle(before), _battle(current)
    if current_battle.get("round") != before_battle.get("round"):
        return True
    selected = _card_at(before, action.get("card_index"))
    if selected is None:
        return False
    card_id = selected.get("id")
    if card_id is not None and _card_count(current, card_id) < _card_count(before, card_id):
        return True
    # Enemy HP is deliberately not a commitment witness. Damage from the tail
    # of an earlier animation can hit the same target after the MCP acknowledges
    # this request. Only movement/transformation of the selected card is tied to
    # the requested play strongly enough to permit a following action.
    before_player, current_player = _player(before), _player(current)
    description = str(selected.get("description") or "")
    keyword_names = {
        str(keyword.get("name") or "")
        for keyword in selected.get("keywords", [])
        if isinstance(keyword, dict)
    }
    # A played draw token can immediately draw another copy of itself, leaving
    # the same card-ID count in hand. Pile movement is the semantic witness in
    # that case: Exhaust cards increase exhaust_pile_count; ordinary cards
    # increase discard_pile_count. This specifically prevents false negatives
    # for Soul -> draw another Soul.
    if ("消耗" in description or "消耗" in keyword_names) and int(
        current_player.get("exhaust_pile_count", 0) or 0
    ) > int(before_player.get("exhaust_pile_count", 0) or 0):
        return True
    if "消耗" not in description and "消耗" not in keyword_names and int(
        current_player.get("discard_pile_count", 0) or 0
    ) > int(before_player.get("discard_pile_count", 0) or 0):
        return True
    # Some cards replace/transform themselves in hand. In that case require
    # both the selected slot to change and an observable energy change.
    current_selected = _card_at(current, action.get("card_index"))
    return (
        current_selected is not None
        and current_selected.get("id") != card_id
        and current_player.get("energy") != before_player.get("energy")
    )


def _action_committed(
    before: dict[str, Any], current: dict[str, Any], action: dict[str, Any]
) -> bool:
    name = action.get("action")
    if name == "select_card" and before.get("state_type") == "card_select":
        if current.get("state_type") != "card_select":
            return True
        pre, post = before.get("card_select", {}), current.get("card_select", {})
        if pre.get("screen_type") != post.get("screen_type"):
            return True
        if pre.get("selection_tracking") == "available" and post.get("selection_tracking") == "available":
            # A toggle of this exact index is the witness, not another card or animation.
            index = action.get("index")
            return (index in pre.get("selected_indices", [])) != (index in post.get("selected_indices", []))
        return not pre.get("preview_showing") and bool(post.get("preview_showing"))
    if name == "confirm_selection" and before.get("state_type") == "card_select":
        pre, post = before.get("card_select", {}), current.get("card_select", {})
        return (current.get("state_type") != "card_select"
                or pre.get("screen_type") != post.get("screen_type")
                or (bool(pre.get("preview_showing")) and not post.get("preview_showing")))
    if name == "play_card":
        return _play_card_committed(before, current, action)
    if name == "end_turn":
        before_battle, current_battle = _battle(before), _battle(current)
        return current.get("state_type") != before.get("state_type") or (
            current_battle.get("is_play_phase") is True
            and current_battle.get("round") != before_battle.get("round")
        )
    if name == "choose_map_node":
        before_run = before.get("run") if isinstance(before.get("run"), dict) else {}
        current_run = current.get("run") if isinstance(current.get("run"), dict) else {}
        return current.get("state_type") != "map" or (
            current_run.get("floor") != before_run.get("floor")
        )
    if name == "menu_select" and before.get("menu_screen") == "character_select":
        if action.get("option") in {"embark", "confirm"}:
            return current.get("state_type") != "menu"
        # STS2MCP exposes the character list but not the currently highlighted
        # character. Selecting one is therefore intentionally state-silent;
        # embark is the subsequent semantic verification of character and
        # ascension. Accept only actual character IDs here, never meta buttons.
        return action.get("option") in {
            "IRONCLAD", "SILENT", "REGENT", "NECROBINDER", "DEFECT",
            "RANDOM_CHARACTER",
        }
    return decision_digest(current) != decision_digest(before)


def settled_state(
    port: int,
    before: dict[str, Any],
    action: dict[str, Any],
    timeout: float = 30.0,
    quiet_window: float = 2.5,
) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    latest = before
    committed = False
    stable_since: float | None = None
    latest_hash = decision_digest(latest)
    while time.monotonic() < deadline:
        time.sleep(0.1)
        newer = request(port)
        newer_hash = decision_digest(newer)
        now = time.monotonic()
        if not committed and _action_committed(before, newer, action):
            committed = True
            stable_since = now
        elif committed:
            if newer_hash != latest_hash:
                stable_since = now
            elif stable_since is not None and now - stable_since >= (
                0.2 if action.get("action") == "select_card"
                and newer.get("card_select", {}).get("selection_tracking") == "available"
                and (not newer.get("card_select", {}).get("preview_showing")
                     or newer.get("card_select", {}).get("can_confirm")) else quiet_window
            ):
                return newer
        latest, latest_hash = newer, newer_hash
    if not committed:
        raise SystemExit(
            f"action acknowledged but not committed within {timeout:.1f}s: {action}"
        )
    raise SystemExit(
        f"action committed but did not settle for {quiet_window:.1f}s "
        f"within {timeout:.1f}s: {action}"
    )


def append_jsonl(path: Path, record: dict[str, Any]) -> None:
    """Durably append one audit event before another game action can run."""

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(
            json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n"
        )
        handle.flush()
        os.fsync(handle.fileno())


def append_live(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(f"\n[{datetime.now().astimezone().isoformat(timespec='seconds')}] {text}\n")
        handle.flush()
        os.fsync(handle.fileno())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=15601)
    sub = parser.add_subparsers(dest="command", required=True)
    observe = sub.add_parser("observe")
    observe.add_argument("--full", action="store_true")
    act = sub.add_parser("act")
    act.add_argument("--json", required=True, dest="action_json")
    act.add_argument("--reason", required=True)
    act.add_argument("--log", type=Path, required=True)
    act.add_argument("--expected-hash")
    act.add_argument(
        "--controller",
        choices=("llm", "human", "policy", "test"),
        default="llm",
        help="decision provenance; this executor itself is never the controller",
    )
    act.add_argument("--decision-id")
    args = parser.parse_args()

    pre = request(args.port)
    pre_hash = digest(pre)
    decision_hash = decision_digest(pre)
    if args.command == "observe":
        print(json.dumps(pre if args.full else summary(pre), ensure_ascii=False, indent=2))
        print(f"state_sha256={pre_hash}")
        print(f"decision_hash={decision_hash}")
        return

    if args.expected_hash and args.expected_hash not in {pre_hash, decision_hash}:
        raise SystemExit(
            f"stale decision: expected {args.expected_hash}, observed {pre_hash}; re-observe"
        )
    try:
        action = json.loads(args.action_json)
    except json.JSONDecodeError as error:
        raise SystemExit(f"invalid action JSON: {error}") from error
    if not isinstance(action, dict) or not isinstance(action.get("action"), str):
        raise SystemExit("action JSON must be an object with a string 'action'")

    decision_id = args.decision_id or uuid.uuid4().hex
    append_live(args.log.parent / "live.log", f"{decision_id} | {args.reason}\n执行：{json.dumps(action, ensure_ascii=False)}")
    append_jsonl(
        args.log,
        {
            "type": "decision_intent",
            "decision_id": decision_id,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "controller": args.controller,
            "state": pre,
            "state_sha256": pre_hash,
            "action": action,
            "decision_reason": args.reason,
        },
    )
    action_response = request(args.port, action)
    post = settled_state(args.port, pre, action)
    append_live(args.log.parent / "live.log", f"{decision_id} | 结算：{post.get('state_type')}，HP {post.get('player', {}).get('hp')}")
    append_jsonl(
        args.log,
        {
            "type": "decision_outcome",
            "decision_id": decision_id,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "controller": args.controller,
            "action_response": action_response,
            "post_state": post,
            "post_state_sha256": digest(post),
        },
    )
    print(json.dumps(summary(post), ensure_ascii=False, indent=2))
    print(f"state_sha256={digest(post)}")


if __name__ == "__main__":
    main()
