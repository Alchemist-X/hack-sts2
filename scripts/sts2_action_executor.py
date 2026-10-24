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


# Ports whose MCP has no /api/v1/eval endpoint (older build) or is unreachable;
# the eval busy gate is skipped for them for the rest of this process.
_EVAL_UNAVAILABLE: set[int] = set()


def eval_probe(port: int, wait_settled_ms: int = 0, stable_ms: int = 0) -> dict[str, Any] | None:
    """The eval API's settled-state probe (no game state), or None without that endpoint.

    The mod reports ``busy`` from the game's own action queue/executor, combat
    phase, scene transitions and save tasks, which the harness cannot see in
    the public state.  Read-only; never sends a game action.
    """
    if port in _EVAL_UNAVAILABLE:
        return None
    url = (f"http://127.0.0.1:{port}/api/v1/eval?include_state=false"
           f"&wait_settled_ms={int(wait_settled_ms)}&stable_ms={int(stable_ms)}")
    try:
        with urllib.request.urlopen(url, timeout=15 + wait_settled_ms / 1000) as response:
            body = json.load(response)
    except (urllib.error.URLError, OSError, ValueError):
        _EVAL_UNAVAILABLE.add(port)
        return None
    if not isinstance(body, dict) or body.get("schema") != "sts2-eval-snapshot":
        _EVAL_UNAVAILABLE.add(port)
        return None
    return body


def digest(state: dict[str, Any]) -> str:
    raw = json.dumps(state, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(raw.encode()).hexdigest()


def decision_digest(state: dict[str, Any]) -> str:
    """Exclude volatile execution metadata and animated transform examples.

    NTransformPreview cycles replacement examples every 0.2 seconds. They are
    presentation, not the committed random outcome. Raw audit hashes use digest.
    """
    if "execution" in state:
        # Per-request bookkeeping (request_id, last action, readiness flags)
        # changes on every call without changing the decision. Hashing it made
        # observe->act hashes go stale for no gameplay reason.
        state = {k: v for k, v in state.items() if k != "execution"}
    treasure = state.get("treasure")
    if state.get("state_type") == "treasure" and isinstance(treasure, dict) and treasure.get("relics"):
        # The chest-open animation flips can_proceed false->true a moment after
        # the room loads while the same relics stay offered; that made the
        # observe->act hash go stale for no decision reason.
        state = dict(state)
        state["treasure"] = {k: v for k, v in treasure.items() if k != "can_proceed"}
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


_EXHAUST_WORDS = ("exhaust", "消耗")


def _mentions_exhaust(description: str, keyword_names: set[str]) -> bool:
    """Exhaust keyword in any supported game language (was Chinese-only)."""
    text = " ".join([description, *keyword_names]).casefold()
    return any(word in text for word in _EXHAUST_WORDS)


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
    exhausts = _mentions_exhaust(description, keyword_names)
    if exhausts and int(
        current_player.get("exhaust_pile_count", 0) or 0
    ) > int(before_player.get("exhaust_pile_count", 0) or 0):
        return True
    if not exhausts and int(
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
        # Floor increments *before* the room loads; a map frame with the new
        # floor is the stale-map race seen live. Only leaving the map commits.
        return current.get("state_type") != "map"
    if name == "claim_treasure_relic":
        if current.get("state_type") != "treasure":
            return True
        claimed = _relic_id_at(before, "treasure", action.get("index"))
        return claimed is not None and claimed not in _relic_ids(current, "treasure")
    if name == "combat_select_card" and before.get("state_type") == "hand_select":
        if current.get("state_type") != "hand_select":
            return True
        pre, post = before.get("hand_select", {}), current.get("hand_select", {})
        return (len(post.get("selected_cards") or []) != len(pre.get("selected_cards") or [])
                or len(post.get("cards") or []) != len(pre.get("cards") or []))
    if name == "combat_confirm_selection":
        return current.get("state_type") != "hand_select"
    if name in {"select_card_reward", "skip_card_reward"}:
        return current.get("state_type") != "card_reward"
    if name == "claim_reward" and before.get("state_type") == "rewards":
        return current.get("state_type") != "rewards" or len(
            (current.get("rewards") or {}).get("items") or []) < len((before.get("rewards") or {}).get("items") or [])
    if name == "proceed":
        return current.get("state_type") != before.get("state_type")
    if name in {"use_potion", "discard_potion"}:
        slots = lambda state: {p.get("slot"): p.get("id") for p in _player(state).get("potions") or [] if isinstance(p, dict)}
        slot = action.get("slot")
        return slots(current).get(slot) != slots(before).get(slot) or current.get("state_type") != before.get("state_type")
    if name == "shop_purchase":
        return (current.get("state_type") != before.get("state_type")
                or _player(current).get("gold") != _player(before).get("gold"))
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


def _relic_ids(state: dict[str, Any], key: str) -> list[Any]:
    block = state.get(key) if isinstance(state.get(key), dict) else {}
    return [r.get("id") for r in block.get("relics") or [] if isinstance(r, dict)]


def _relic_id_at(state: dict[str, Any], key: str, index: Any) -> Any:
    block = state.get(key) if isinstance(state.get(key), dict) else {}
    for relic in block.get("relics") or []:
        if isinstance(relic, dict) and relic.get("index") == index:
            return relic.get("id")
    return None


COMBAT_TYPES = frozenset({"monster", "elite", "boss"})


def actionable(state: dict[str, Any]) -> bool:
    """True when the frame is a decision point rather than a transition frame.

    Combat frames during the enemy turn / card animations, empty maps and
    screens with nothing to click are not decision points; returning them is
    how stale frames reached the controller.
    """
    st = state.get("state_type")
    if st == "game_over":
        return True
    if st in COMBAT_TYPES:
        battle = _battle(state)
        execution = state.get("execution") if isinstance(state.get("execution"), dict) else {}
        if battle.get("is_over_or_ending"):
            return False
        return battle.get("is_play_phase") is True and execution.get("queue_idle", True) is not False
    if st == "map":
        block = state.get("map") if isinstance(state.get("map"), dict) else {}
        return bool(block.get("next_options"))
    if st in {None, "unknown", "loading", "transition"}:
        return False
    try:
        from sts2rec.legal_actions import derive_legal_actions
    except ImportError:  # pragma: no cover - sts2rec is on sys.path for the live harness
        return True
    return bool(derive_legal_actions(state))


def wait_settled(
    port: int,
    timeout: float = 20.0,
    quiet_window: float = 0.5,
    *,
    initial: dict[str, Any] | None = None,
    poll: float = 0.1,
) -> tuple[dict[str, Any], bool]:
    """Poll until the decision hash is stable for ``quiet_window`` on an actionable frame.

    Returns ``(state, settled)``; ``settled`` is False when ``timeout`` expired
    first, so callers can report that explicitly instead of acting on it.
    """
    deadline = time.monotonic() + timeout
    latest = initial if initial is not None else request(port)
    latest_hash = decision_digest(latest)
    stable_since = time.monotonic()
    while True:
        now = time.monotonic()
        if actionable(latest) and now - stable_since >= quiet_window:
            probe = eval_probe(port)
            if probe is None or not probe.get("busy"):
                return latest, True
            # The game is still resolving (e.g. combat ending before rewards):
            # restart the quiet window instead of returning a transition frame.
            stable_since = time.monotonic()
        if now >= deadline:
            return latest, False
        time.sleep(poll)
        newer = request(port)
        newer_hash = decision_digest(newer)
        if newer_hash != latest_hash:
            stable_since = time.monotonic()
        latest, latest_hash = newer, newer_hash


def quiet_window_for(action: dict[str, Any], state: dict[str, Any]) -> float:
    name = action.get("action")
    if name == "select_card" and state.get("card_select", {}).get("selection_tracking") == "available" and (
        not state.get("card_select", {}).get("preview_showing") or state.get("card_select", {}).get("can_confirm")
    ):
        return 0.2
    if state.get("state_type") in COMBAT_TYPES:
        # Damage numbers / block / draw animations keep mutating for a moment.
        return 1.0
    return 0.6


def settled_state(
    port: int,
    before: dict[str, Any],
    action: dict[str, Any],
    timeout: float = 30.0,
    quiet_window: float | None = None,
) -> dict[str, Any]:
    """Wait for this action's own commitment witness, then for an actionable quiet frame.

    Raises SystemExit with an explicit reason on timeout; never returns a
    pre-commit or transition frame as the outcome.
    """
    deadline = time.monotonic() + timeout
    committed_state: dict[str, Any] | None = None
    while time.monotonic() < deadline:
        time.sleep(0.1)
        newer = request(port)
        if _action_committed(before, newer, action):
            committed_state = newer
            break
    if committed_state is None:
        raise SystemExit(
            f"action acknowledged but not committed within {timeout:.1f}s: {action}"
        )
    window = quiet_window if quiet_window is not None else quiet_window_for(action, committed_state)
    remaining = max(0.5, deadline - time.monotonic())
    post, ok = wait_settled(port, remaining, window, initial=committed_state)
    if not ok:
        raise SystemExit(
            f"action committed but did not reach a stable actionable frame "
            f"({window:.1f}s quiet) within {timeout:.1f}s: {action}; last state_type={post.get('state_type')}"
        )
    return post


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
