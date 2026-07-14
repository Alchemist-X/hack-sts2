#!/usr/bin/env python3
"""Level-2 live-game driver: play STS2 programmatically via the STS2MCP HTTP API.

While the Sts2Recorder mod passively records, this driver injects actions
through STS2MCP (POST /api/v1/singleplayer, localhost:15526 by default) and
logs every injected action to <out>/injected_actions.jsonl — the ground-truth
stream that scripts/level2_diff.py later compares against the recorder's
actions.jsonl.

Stdlib-only (urllib). Coded against STS2MCP docs raw-full.md / raw-simplified.md
(see scripts/level2_diff.py for the doc-line references of every action).

Injected-action JSONL line format (one per POST, success or failure):
    {"seq": int, "t": float, "type": "injected_action",
     "endpoint": "/api/v1/singleplayer", "action": {"action": "...", ...},
     "http_status": int|null, "response": {...}|{"raw": "..."},
     "pre_state": {...screen-specific summary...}}

The STS2MCP API version may differ from the docs this was written against
(game updates broke it twice already); every response-shape assumption is
wrapped so a mismatch prints the raw payload with an actionable message
instead of a stack trace.
"""

from __future__ import annotations

import argparse
import json
import sys
import tempfile
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any

DEFAULT_PORT = 15526
DEFAULT_FLOORS = 3
DEFAULT_TIMEOUT_S = 600.0
HTTP_TIMEOUT_S = 10.0
POLL_INTERVAL_S = 0.75
STUCK_TIMEOUT_S = 60.0
MAX_RAW_ECHO = 1000
PREFERRED_CHARACTER = "IRONCLAD"
META_MENU_OPTIONS = frozenset(
    {"back", "confirm", "embark", "unready", "random", "quit", "settings"}
)


class ApiError(Exception):
    """HTTP/shape failure talking to STS2MCP, with actionable context."""


class PolicyStop(Exception):
    """Deterministic-policy stop signal (run end / unsupported screen)."""

    def __init__(self, reason: str, *, failure: bool = False) -> None:
        super().__init__(reason)
        self.reason = reason
        self.failure = failure


# --------------------------------------------------------------------------
# HTTP layer
# --------------------------------------------------------------------------


def _echo(raw: str) -> str:
    return raw[:MAX_RAW_ECHO] + ("..." if len(raw) > MAX_RAW_ECHO else "")


def _decode_json(raw: str, context: str) -> dict[str, Any]:
    try:
        decoded = json.loads(raw)
    except json.JSONDecodeError as error:
        raise ApiError(
            f"{context}: response is not JSON ({error}).\n"
            f"raw response: {_echo(raw)}\n"
            "Likely an STS2MCP version mismatch — compare against docs/raw-full.md."
        ) from error
    if not isinstance(decoded, dict):
        raise ApiError(
            f"{context}: expected a JSON object, got {type(decoded).__name__}.\n"
            f"raw response: {_echo(raw)}"
        )
    return decoded


def api_get_state(port: int) -> dict[str, Any]:
    """GET /api/v1/singleplayer -> decoded state dict."""
    url = f"http://127.0.0.1:{port}/api/v1/singleplayer"
    try:
        with urllib.request.urlopen(url, timeout=HTTP_TIMEOUT_S) as response:
            raw = response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise ApiError(
            f"GET {url} -> HTTP {error.code}.\nbody: {_echo(body)}\n"
            "HTTP 409 means a multiplayer run is active (this driver is SP-only)."
        ) from error
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise ApiError(
            f"GET {url} failed: {error}\n"
            "Is the game running with the STS2MCP mod loaded? "
            "Run scripts/level2_preflight.sh first."
        ) from error
    return _decode_json(raw, f"GET {url}")


def api_post_action(port: int, body: dict[str, Any]) -> tuple[int, dict[str, Any]]:
    """POST /api/v1/singleplayer -> (http_status, decoded response).

    HTTP-level errors with a JSON body are returned (not raised) so the
    caller can log them as ground truth; transport failures raise ApiError.
    """
    url = f"http://127.0.0.1:{port}/api/v1/singleplayer"
    payload = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        url, data=payload, headers={"Content-Type": "application/json"}, method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT_S) as response:
            raw = response.read().decode("utf-8", errors="replace")
            status = response.status
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8", errors="replace")
        status = error.code
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise ApiError(
            f"POST {url} {json.dumps(body)} failed at transport level: {error}"
        ) from error
    try:
        decoded = _decode_json(raw, f"POST {url}")
    except ApiError:
        decoded = {"raw": _echo(raw)}
    return status, decoded


# --------------------------------------------------------------------------
# State summaries (persisted with every injected action for the diff tool)
# --------------------------------------------------------------------------


def _pick(mapping: Any, keys: tuple[str, ...]) -> dict[str, Any]:
    if not isinstance(mapping, dict):
        return {}
    return {key: mapping[key] for key in keys if key in mapping}


def _summarize_list(items: Any, keys: tuple[str, ...]) -> list[dict[str, Any]]:
    if not isinstance(items, list):
        return []
    return [_pick(item, keys) for item in items]


def summarize_state(state: dict[str, Any]) -> dict[str, Any]:
    """Compact, diff-relevant snapshot of the pre-action screen."""
    state_type = state.get("state_type", "ABSENT")
    summary: dict[str, Any] = {"state_type": state_type}
    summary.update(_pick(state.get("run"), ("act", "floor", "ascension")))
    player = state.get("player")
    if isinstance(player, dict):
        summary.update(_pick(player, ("hp", "gold", "energy")))
        summary["hand"] = _summarize_list(
            player.get("hand"), ("index", "id", "cost", "can_play", "target_type")
        )
        summary["potions"] = _summarize_list(player.get("potions"), ("slot", "id"))
    battle = state.get("battle")
    if isinstance(battle, dict):
        summary["enemies"] = _summarize_list(
            battle.get("enemies"), ("entity_id", "name", "hp")
        )
        summary.update(_pick(battle, ("round", "turn", "is_play_phase")))
    section_keys: dict[str, tuple[str, tuple[str, ...], tuple[str, ...]]] = {
        "map": ("next_options", ("index", "col", "row", "type"), ()),
        "rewards": ("items", ("index", "type", "potion_id"), ()),
        "card_reward": ("cards", ("index", "id", "name"), ("can_skip",)),
        "shop": ("items", ("index", "category", "card_id", "relic_id", "potion_id", "price"), ()),
        "event": ("options", ("index", "title", "is_locked", "is_proceed"), ("event_id", "in_dialogue")),
        "rest_site": ("options", ("index", "id", "is_enabled"), ("can_proceed",)),
        "treasure": ("relics", ("index", "id"), ("can_proceed",)),
        "card_select": ("cards", ("index", "id"), ("screen_type", "can_confirm", "can_cancel")),
        "bundle_select": ("bundles", ("index", "card_count"), ("can_confirm",)),
        "relic_select": ("relics", ("index", "id"), ("can_skip",)),
        "hand_select": ("cards", ("index", "id"), ("mode", "can_confirm")),
    }
    for section, (list_key, item_keys, scalar_keys) in section_keys.items():
        block = state.get(section)
        if isinstance(block, dict):
            summary[section] = {
                list_key: _summarize_list(block.get(list_key), item_keys),
                **_pick(block, scalar_keys),
            }
    if state_type == "menu":
        summary["menu_screen"] = state.get("menu_screen")
        summary["options"] = state.get("options")
    return summary


# --------------------------------------------------------------------------
# Injected-action log
# --------------------------------------------------------------------------


@dataclass
class ActionLog:
    path: Path
    seq: int = 0

    def append(
        self,
        action: dict[str, Any],
        http_status: int | None,
        response: dict[str, Any],
        pre_state: dict[str, Any],
    ) -> None:
        self.seq += 1
        record = {
            "seq": self.seq,
            "t": time.time(),
            "type": "injected_action",
            "endpoint": "/api/v1/singleplayer",
            "action": action,
            "http_status": http_status,
            "response": response,
            "pre_state": summarize_state(pre_state),
        }
        try:
            with self.path.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(record, separators=(",", ":")) + "\n")
        except OSError as error:
            raise PolicyStop(
                f"cannot append to {self.path}: {error}", failure=True
            ) from error


# --------------------------------------------------------------------------
# Deterministic policy: state -> next action body (or None to wait)
# --------------------------------------------------------------------------


@dataclass(frozen=True)
class DriverContext:
    """Immutable per-loop policy context (replaced, never mutated)."""

    character_picked: bool = False
    start_floor: int | None = None
    floors_target: int = DEFAULT_FLOORS
    last_failed_action: str = ""


def _option_names(options: Any) -> list[str]:
    """Normalize menu options: list of strings or {name, enabled} objects."""
    names: list[str] = []
    for option in options if isinstance(options, list) else []:
        if isinstance(option, str):
            names.append(option)
        elif isinstance(option, dict) and option.get("enabled", True):
            name = option.get("name")
            if isinstance(name, str):
                names.append(name)
    return names


def _menu_select(option: str) -> dict[str, Any]:
    return {"action": "menu_select", "option": option}


def decide_menu(state: dict[str, Any], ctx: DriverContext) -> tuple[dict[str, Any] | None, DriverContext]:
    screen = state.get("menu_screen", "main")
    options = _option_names(state.get("options"))
    lowered = [name.lower() for name in options]
    if screen == "main":
        for choice in ("singleplayer", "continue"):
            if choice in lowered:
                return _menu_select(choice), ctx
    if screen == "singleplayer" and "standard" in lowered:
        return _menu_select("standard"), ctx
    if screen == "character_select":
        if not ctx.character_picked:
            pickable = [
                name for name in options if name.lower() not in META_MENU_OPTIONS
            ]
            preferred = [
                name for name in pickable if PREFERRED_CHARACTER in name.upper()
            ]
            if preferred or pickable:
                pick = (preferred or pickable)[0]
                return _menu_select(pick), replace(ctx, character_picked=True)
        for choice in ("embark", "confirm"):
            if choice in lowered:
                return _menu_select(choice), ctx
    if screen == "tutorial_prompt" and "no" in lowered:
        return _menu_select("no"), ctx
    if screen == "popup":
        preferred = "ignore" if "ignore" in lowered else (lowered[0] if lowered else None)
        if preferred is not None:
            return _menu_select(preferred), ctx
    if screen in ("multiplayer", "multiplayer_host", "multiplayer_join", "multiplayer_load_lobby"):
        raise PolicyStop(
            f"menu_screen {screen!r} is multiplayer — this driver is SP-only", failure=True
        )
    # Unhandled submenu: back out if possible, else let the stuck timer fire.
    if "back" in lowered:
        return _menu_select("back"), ctx
    return None, ctx


def _first_living_enemy(state: dict[str, Any]) -> str | None:
    battle = state.get("battle")
    enemies = battle.get("enemies") if isinstance(battle, dict) else None
    for enemy in enemies if isinstance(enemies, list) else []:
        if isinstance(enemy, dict) and enemy.get("hp", 0) > 0:
            entity_id = enemy.get("entity_id")
            if isinstance(entity_id, str):
                return entity_id
    return None


def _needs_enemy_target(card: dict[str, Any]) -> bool:
    return card.get("target_type") == "AnyEnemy"


def decide_combat(state: dict[str, Any]) -> dict[str, Any] | None:
    battle = state.get("battle")
    if not isinstance(battle, dict):
        raise ApiError(
            "combat state has no 'battle' object — API shape mismatch.\n"
            f"state keys: {sorted(state)}"
        )
    if battle.get("turn") != "player" or not battle.get("is_play_phase", False):
        return None  # enemy turn / animation — wait and re-poll
    player = state.get("player")
    hand = player.get("hand") if isinstance(player, dict) else None
    energy = player.get("energy", 0) if isinstance(player, dict) else 0
    if energy > 0:
        for card in hand if isinstance(hand, list) else []:
            if not (isinstance(card, dict) and card.get("can_play")):
                continue
            action: dict[str, Any] = {
                "action": "play_card",
                "card_index": card.get("index"),
            }
            if _needs_enemy_target(card):
                target = _first_living_enemy(state)
                if target is None:
                    continue  # no living target for this card; try the next
                action["target"] = target
            return action
    return {"action": "end_turn"}


def decide_hand_select(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("hand_select")
    if not isinstance(block, dict):
        return None
    if block.get("can_confirm"):
        return {"action": "combat_confirm_selection"}
    cards = block.get("cards")
    selected = block.get("selected_cards") or []
    if isinstance(cards, list) and cards:
        selected_indices = {
            card.get("index") for card in selected if isinstance(card, dict)
        }
        for card in cards:
            if isinstance(card, dict) and card.get("index") not in selected_indices:
                return {"action": "combat_select_card", "card_index": card.get("index")}
    return {"action": "combat_confirm_selection"}


def decide_rewards(state: dict[str, Any]) -> dict[str, Any]:
    block = state.get("rewards")
    items = block.get("items") if isinstance(block, dict) else None
    if isinstance(items, list) and items:
        first = items[0] if isinstance(items[0], dict) else {}
        return {"action": "claim_reward", "index": first.get("index", 0)}
    return {"action": "proceed"}


def decide_card_reward(state: dict[str, Any]) -> dict[str, Any]:
    block = state.get("card_reward")
    cards = block.get("cards") if isinstance(block, dict) else None
    if isinstance(cards, list) and cards:
        first = cards[0] if isinstance(cards[0], dict) else {}
        return {"action": "select_card_reward", "card_index": first.get("index", 0)}
    return {"action": "skip_card_reward"}


def decide_map(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("map")
    options = block.get("next_options") if isinstance(block, dict) else None
    if isinstance(options, list) and options:
        first = options[0] if isinstance(options[0], dict) else {}
        return {"action": "choose_map_node", "index": first.get("index", 0)}
    return None  # map still animating / regenerating — wait


def decide_event(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("event")
    if not isinstance(block, dict):
        return None
    if block.get("in_dialogue"):
        return {"action": "advance_dialogue"}
    options = block.get("options")
    for option in options if isinstance(options, list) else []:
        if isinstance(option, dict) and not option.get("is_locked", False):
            return {"action": "choose_event_option", "index": option.get("index", 0)}
    return None


def decide_rest_site(state: dict[str, Any]) -> dict[str, Any]:
    block = state.get("rest_site")
    options = block.get("options") if isinstance(block, dict) else None
    for option in options if isinstance(options, list) else []:
        if isinstance(option, dict) and option.get("is_enabled", True):
            return {"action": "choose_rest_option", "index": option.get("index", 0)}
    return {"action": "proceed"}


def decide_shop(_state: dict[str, Any]) -> dict[str, Any]:
    return {"action": "proceed"}  # policy: buy nothing, leave


def decide_treasure(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("treasure")
    if not isinstance(block, dict):
        return None
    relics = block.get("relics")
    if isinstance(relics, list) and relics:
        first = relics[0] if isinstance(relics[0], dict) else {}
        return {"action": "claim_treasure_relic", "index": first.get("index", 0)}
    if block.get("can_proceed"):
        return {"action": "proceed"}
    return None  # "Opening chest..." transitional state — wait


def decide_card_select(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("card_select")
    if not isinstance(block, dict):
        return None
    if block.get("can_confirm"):
        return {"action": "confirm_selection"}
    cards = block.get("cards")
    if isinstance(cards, list) and cards:
        first = cards[0] if isinstance(cards[0], dict) else {}
        return {"action": "select_card", "index": first.get("index", 0)}
    if block.get("can_cancel"):
        return {"action": "cancel_selection"}
    return None


def decide_bundle_select(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("bundle_select")
    if not isinstance(block, dict):
        return None
    if block.get("preview_showing") or block.get("can_confirm"):
        return {"action": "confirm_bundle_selection"}
    bundles = block.get("bundles")
    if isinstance(bundles, list) and bundles:
        first = bundles[0] if isinstance(bundles[0], dict) else {}
        return {"action": "select_bundle", "index": first.get("index", 0)}
    return None


def decide_relic_select(state: dict[str, Any]) -> dict[str, Any]:
    block = state.get("relic_select")
    relics = block.get("relics") if isinstance(block, dict) else None
    if isinstance(relics, list) and relics:
        first = relics[0] if isinstance(relics[0], dict) else {}
        return {"action": "select_relic", "index": first.get("index", 0)}
    return {"action": "skip_relic_selection"}


def decide_crystal_sphere(state: dict[str, Any]) -> dict[str, Any] | None:
    block = state.get("crystal_sphere")
    if not isinstance(block, dict):
        return None
    if block.get("can_proceed"):
        return {"action": "crystal_sphere_proceed"}
    cells = block.get("clickable_cells")
    if isinstance(cells, list) and cells and isinstance(cells[0], dict):
        return {
            "action": "crystal_sphere_click_cell",
            "x": cells[0].get("x"),
            "y": cells[0].get("y"),
        }
    return {"action": "crystal_sphere_proceed"}


def decide_unknown(state: dict[str, Any]) -> dict[str, Any]:
    # Docs advertise no action for unknown/overlay; try the generic proceed
    # once per stuck-window — harmless error response if unsupported.
    print(
        f"warning: unrecognized state_type={state.get('state_type')!r}; "
        "trying generic 'proceed'",
        file=sys.stderr,
    )
    return {"action": "proceed"}


def decide_action(
    state: dict[str, Any], ctx: DriverContext
) -> tuple[dict[str, Any] | None, DriverContext]:
    """Dispatch to the per-screen policy. Returns (action|None, new ctx)."""
    state_type = state.get("state_type")
    if state_type is None:
        raise ApiError(
            "state response has no 'state_type' — API shape mismatch.\n"
            f"raw state: {_echo(json.dumps(state))}"
        )
    if state_type == "menu":
        return decide_menu(state, ctx)
    if state_type == "game_over":
        raise PolicyStop("run end detected (state_type=game_over)")
    simple = {
        "monster": decide_combat,
        "elite": decide_combat,
        "boss": decide_combat,
        "hand_select": decide_hand_select,
        "rewards": decide_rewards,
        "card_reward": decide_card_reward,
        "map": decide_map,
        "event": decide_event,
        "rest_site": decide_rest_site,
        "shop": decide_shop,
        "fake_merchant": decide_shop,
        "treasure": decide_treasure,
        "card_select": decide_card_select,
        "bundle_select": decide_bundle_select,
        "relic_select": decide_relic_select,
        "crystal_sphere": decide_crystal_sphere,
        "unknown": decide_unknown,
        "overlay": decide_unknown,
    }
    decider = simple.get(str(state_type))
    if decider is None:
        return decide_unknown(state), ctx
    return decider(state), ctx


# --------------------------------------------------------------------------
# Main loop
# --------------------------------------------------------------------------


def _dump_state(out_dir: Path, state: dict[str, Any], reason: str) -> Path:
    dump_path = out_dir / "stuck_state.json"
    try:
        dump_path.write_text(
            json.dumps({"reason": reason, "state": state}, indent=2), encoding="utf-8"
        )
    except OSError as error:
        print(f"warning: could not write state dump: {error}", file=sys.stderr)
    return dump_path


def _floors_completed(state: dict[str, Any], ctx: DriverContext) -> tuple[int, DriverContext]:
    run = state.get("run")
    floor = run.get("floor") if isinstance(run, dict) else None
    if not isinstance(floor, int):
        return 0, ctx
    if ctx.start_floor is None:
        return 0, replace(ctx, start_floor=floor)
    return floor - ctx.start_floor, ctx


def run_driver(args: argparse.Namespace) -> int:
    out_dir = Path(args.out)
    try:
        out_dir.mkdir(parents=True, exist_ok=True)
    except OSError as error:
        print(f"error: cannot create --out dir {out_dir}: {error}", file=sys.stderr)
        return 2
    log = ActionLog(out_dir / "injected_actions.jsonl")
    print(f"level2 driver: port={args.port} floors={args.floors} out={out_dir}")
    print(f"injected-action log: {log.path}")

    ctx = DriverContext(floors_target=args.floors)
    deadline = time.monotonic() + args.timeout_s
    last_progress = time.monotonic()
    last_state: dict[str, Any] = {}
    stop_reason, exit_code = "", 0

    while True:
        if time.monotonic() > deadline:
            stop_reason, exit_code = (
                f"--timeout-s {args.timeout_s} reached (partial log is still diffable)",
                3,
            )
            break
        try:
            state = api_get_state(args.port)
            last_state = state
            floors_done, ctx = _floors_completed(state, ctx)
            if floors_done >= ctx.floors_target:
                # The SP API exposes no abandon action (docs raw-full.md:
                # abandon exists only in the multiplayer submenu), so a
                # mid-run stop is the clean exit — fine for diffing.
                stop_reason = (
                    f"{floors_done} floor(s) completed (target {ctx.floors_target}); "
                    "no SP abandon action in the API — leaving the run in place"
                )
                break
            action, ctx = decide_action(state, ctx)
            if action is None:
                if time.monotonic() - last_progress > STUCK_TIMEOUT_S:
                    dump = _dump_state(out_dir, state, "no action decided for >60s")
                    stop_reason, exit_code = (
                        f"stuck on state_type={state.get('state_type')!r} for "
                        f">{STUCK_TIMEOUT_S:.0f}s; state dumped to {dump}",
                        2,
                    )
                    break
                time.sleep(args.poll_interval)
                continue
            action_json = json.dumps(action, sort_keys=True)
            if action_json == ctx.last_failed_action:
                # Same action already failed: don't spam it — wait for the
                # screen to change, and give up after the stuck window.
                if time.monotonic() - last_progress > STUCK_TIMEOUT_S:
                    dump = _dump_state(
                        out_dir, state, f"action failing repeatedly: {action_json}"
                    )
                    stop_reason, exit_code = (
                        f"action {action_json} kept failing for "
                        f">{STUCK_TIMEOUT_S:.0f}s; state dumped to {dump}",
                        2,
                    )
                    break
                time.sleep(args.poll_interval)
                continue
            status, response = api_post_action(args.port, action)
            log.append(action, status, response, state)
            ok = status == 200 and response.get("status") == "ok"
            print(
                f"[{log.seq:4d}] {state.get('state_type', '?'):<12} "
                f"{action_json}  -> HTTP {status} "
                f"{response.get('status', response.get('raw', ''))!s:.60}"
            )
            if ok:
                last_progress = time.monotonic()
                ctx = replace(ctx, last_failed_action="")
            else:
                ctx = replace(ctx, last_failed_action=action_json)
                if time.monotonic() - last_progress > STUCK_TIMEOUT_S:
                    dump = _dump_state(out_dir, state, f"errors for >60s; last: {response}")
                    stop_reason, exit_code = (
                        f"no successful action for >{STUCK_TIMEOUT_S:.0f}s; "
                        f"last error: {response} (state dumped to {dump})",
                        2,
                    )
                    break
            time.sleep(args.poll_interval)
        except PolicyStop as stop:
            stop_reason, exit_code = stop.reason, 2 if stop.failure else 0
            break
        except ApiError as error:
            if time.monotonic() - last_progress > STUCK_TIMEOUT_S:
                dump = _dump_state(out_dir, last_state, f"ApiError persisted: {error}")
                stop_reason, exit_code = (
                    f"API errors for >{STUCK_TIMEOUT_S:.0f}s: {error} "
                    f"(last good state dumped to {dump})",
                    2,
                )
                break
            print(f"transient API error (will retry): {error}", file=sys.stderr)
            time.sleep(args.poll_interval * 2)

    summary = {
        "stop_reason": stop_reason,
        "exit_code": exit_code,
        "injected_actions": log.seq,
        "out_dir": str(out_dir),
    }
    try:
        (out_dir / "driver_summary.json").write_text(
            json.dumps(summary, indent=2), encoding="utf-8"
        )
    except OSError as error:
        print(f"warning: could not write driver_summary.json: {error}", file=sys.stderr)
    print(f"driver stopped: {stop_reason}")
    print(f"injected {log.seq} action(s); next: scripts/level2_diff.py "
          f"--session <recorded session dir> --injected {log.path}")
    return exit_code


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Drive an STS2 run through the STS2MCP HTTP API while the "
        "Sts2Recorder mod records; logs every injected action for level2_diff.py.",
    )
    parser.add_argument(
        "--port", type=int, default=DEFAULT_PORT,
        help=f"STS2MCP HTTP port (default {DEFAULT_PORT})",
    )
    parser.add_argument(
        "--floors", type=int, default=DEFAULT_FLOORS,
        help=f"stop after this many completed floors (default {DEFAULT_FLOORS})",
    )
    parser.add_argument(
        "--timeout-s", type=float, default=DEFAULT_TIMEOUT_S,
        help=f"overall wall-clock budget in seconds (default {DEFAULT_TIMEOUT_S:.0f})",
    )
    parser.add_argument(
        "--out",
        default=str(
            Path(tempfile.gettempdir())
            / "sts2-level2"
            / time.strftime("%Y%m%dT%H%M%S")
        ),
        help="output dir for injected_actions.jsonl and diagnostics "
        "(default: a fresh scratch runs dir under the system temp dir)",
    )
    parser.add_argument(
        "--poll-interval", type=float, default=POLL_INTERVAL_S,
        help=f"seconds between state polls (default {POLL_INTERVAL_S})",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.floors < 1:
        print("error: --floors must be >= 1", file=sys.stderr)
        return 2
    try:
        return run_driver(args)
    except KeyboardInterrupt:
        print("\ninterrupted — partial injected_actions.jsonl is still diffable")
        return 130


if __name__ == "__main__":
    sys.exit(main())
