#!/usr/bin/env python3
"""Level-2 diff: injected ground truth vs the recorder's actions.jsonl.

scripts/level2_driver.py played the game through STS2MCP and logged every
injected action; the Sts2Recorder mod recorded passively. Because WE injected
the actions, ground truth is exact — this tool aligns the two streams and
fails on any injected action the recorder missed or mis-parameterized.

Run:
    uv run --project /Users/Aincrad/dev-proj/hack-sts2/sts2rec \
        python scripts/level2_diff.py --session <session dir> \
        --injected <out>/injected_actions.jsonl
(or plain python3 — a sys.path fallback finds the sts2rec package in-repo).

Exit 0 only if every successfully-injected recordable action appears in the
recording with matching params, source attribution shows a non-UI spine, and
`sts2rec validate` reports no errors.
"""

from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]

try:
    from sts2rec.session import iter_jsonl, load_manifest, parse_action_record
    from sts2rec.validate import validate_session
except ImportError:  # running outside `uv run --project sts2rec`
    sys.path.insert(0, str(REPO_ROOT / "sts2rec"))
    try:
        from sts2rec.session import iter_jsonl, load_manifest, parse_action_record
        from sts2rec.validate import validate_session
    except ImportError as error:
        print(
            f"error: cannot import sts2rec ({error}); expected the package at "
            f"{REPO_ROOT / 'sts2rec' / 'sts2rec'}",
            file=sys.stderr,
        )
        sys.exit(2)

FINAL_STATUSES = frozenset({"executed", "committed"})

# ---------------------------------------------------------------------------
# Mapping table: injected STS2MCP action -> recorder action kind.
#
# Sources:
#   * STS2MCP action names: sts2mcp-upstream/docs/raw-full.md (POST section,
#     lines 1048-1404) and raw-simplified.md (lines 53-204).
#   * Recorder kinds CONFIRMED in-repo: mod/tests/FakeGameScenario.cs —
#     "play_card" (:62), "end_turn" (:73), "map_choice" (:82),
#     "shop_purchase" (:89). Everything else is a BEST-EFFORT GUESS
#     (the hook agent has not finalized the vocabulary); guesses list several
#     plausible aliases and are marked UNCERTAIN so a miss is reported as a
#     possible naming mismatch, not silently as a recorder gap.
#   * kinds=None => expected NOT to be recorded:
#       - menu_select: sessions cover one run; menu time is not recorded
#         (docs/design.md "One session = one run").
#       - proceed: leaving a screen has no game command; the next decision is
#         the map click (docs/hook-map.md, shop row: "'Leave shop' has no
#         command").
#       - advance_dialogue / crystal_sphere_*: bespoke UIs are cosmetic-local
#         and uncaptured by design (docs/hook-map.md, risk #16).
# ---------------------------------------------------------------------------

CONFIRMED = "confirmed"
UNCERTAIN = "uncertain"


@dataclass(frozen=True)
class KindMapping:
    kinds: tuple[str, ...] | None  # None => expected not recorded
    confidence: str
    note: str = ""


INJECTED_TO_RECORDER: dict[str, KindMapping] = {
    "play_card": KindMapping(("play_card",), CONFIRMED),
    "end_turn": KindMapping(("end_turn",), CONFIRMED),
    "choose_map_node": KindMapping(("map_choice", "choose_map_node"), CONFIRMED,
                                   "'map_choice' confirmed in FakeGameScenario"),
    "shop_purchase": KindMapping(("shop_purchase",), CONFIRMED),
    "use_potion": KindMapping(("use_potion", "potion_use"), UNCERTAIN),
    "discard_potion": KindMapping(("discard_potion", "potion_discard"), UNCERTAIN),
    "choose_event_option": KindMapping(
        ("event_choice", "choose_event_option", "event_option"), UNCERTAIN),
    "choose_rest_option": KindMapping(
        ("rest_choice", "choose_rest_option", "rest_option"), UNCERTAIN),
    "claim_reward": KindMapping(("reward_claim", "claim_reward", "reward"), UNCERTAIN),
    "select_card_reward": KindMapping(
        ("card_reward_pick", "select_card_reward", "card_obtained", "card_pick"),
        UNCERTAIN),
    "skip_card_reward": KindMapping(
        ("card_reward_skip", "skip_card_reward", "card_skip"), UNCERTAIN),
    "claim_treasure_relic": KindMapping(
        ("treasure_relic", "claim_treasure_relic", "pick_relic", "relic_pick"),
        UNCERTAIN),
    "select_card": KindMapping(
        ("player_choice", "card_select", "select_card"), UNCERTAIN),
    "confirm_selection": KindMapping(
        ("player_choice", "card_select_confirm", "confirm_selection"), UNCERTAIN,
        "grid confirm may be folded into one player_choice record"),
    "cancel_selection": KindMapping(
        ("player_choice", "card_select_cancel", "cancel_selection"), UNCERTAIN),
    "combat_select_card": KindMapping(
        ("player_choice", "hand_select", "combat_select_card"), UNCERTAIN),
    "combat_confirm_selection": KindMapping(
        ("player_choice", "hand_select_confirm", "combat_confirm_selection"),
        UNCERTAIN),
    "select_bundle": KindMapping(("player_choice", "bundle_select"), UNCERTAIN),
    "confirm_bundle_selection": KindMapping(
        ("player_choice", "bundle_confirm"), UNCERTAIN),
    "cancel_bundle_selection": KindMapping(
        ("player_choice", "bundle_cancel"), UNCERTAIN),
    "select_relic": KindMapping(
        ("relic_choice", "select_relic", "player_choice"), UNCERTAIN),
    "skip_relic_selection": KindMapping(
        ("relic_choice", "skip_relic", "player_choice"), UNCERTAIN),
    "menu_select": KindMapping(None, CONFIRMED, "menu time is outside the session"),
    "proceed": KindMapping(None, CONFIRMED, "screen-leave has no game command"),
    "advance_dialogue": KindMapping(None, CONFIRMED, "cosmetic-local, uncaptured"),
    "crystal_sphere_set_tool": KindMapping(None, UNCERTAIN, "bespoke UI"),
    "crystal_sphere_click_cell": KindMapping(None, UNCERTAIN, "bespoke UI"),
    "crystal_sphere_proceed": KindMapping(None, UNCERTAIN, "bespoke UI"),
}

# Reverse lookup: recorder kind -> canonical injected action name.
RECORDER_KIND_TO_CANONICAL: dict[str, str] = {
    kind: injected
    for injected, mapping in INJECTED_TO_RECORDER.items()
    if mapping.kinds
    for kind in mapping.kinds
}

# Recorded sources matching these (case-insensitive substrings) are HUMAN UI
# funnels (docs/hook-map.md "Per-decision hooks": TryManualPlay,
# NEndTurnButton.CallReleaseLogic, NMapScreen.OnMapPointSelectedLocally,
# NEventRoom.OptionButtonClicked, NPotionPopup, NTreasureRoom chest button).
# STS2MCP bypasses all of them (hook-map.md "STS2MCP cross-check"), so any hit
# on an MCP-injected action means source attribution is broken. Best-effort
# patterns — the hook agent has not finalized patch IDs.
HUMAN_FUNNEL_PATTERNS: tuple[str, ...] = (
    "manualplay", "tryplay", "endturnbutton", "releaselogic", "mapscreen",
    "mappointselected", "eventroom", "optionbutton", "potionpopup",
    "treasureroom", "chestbutton", "ncardplay", "ui:",
)

# Param aliases: canonical dimension -> keys accepted in recorder params.
PARAM_ALIASES: dict[str, tuple[str, ...]] = {
    "card": ("card", "card_id", "card_model_id", "cardid", "card_name"),
    "card_index": ("card_index", "hand_index", "combat_card_index"),
    "target": ("target", "target_id", "targetid"),
    "node": ("node", "coord", "map_node", "map_coord"),
    "index": ("index", "option", "option_index", "choice", "choice_index"),
    "item": ("item", "item_id", "relic", "relic_id", "potion", "potion_id"),
    "slot": ("slot", "potion_slot"),
}


# ---------------------------------------------------------------------------
# Loading + normalization
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class InjectedAction:
    seq: int
    name: str
    params: dict[str, Any]  # normalized salient params
    mapping: KindMapping
    raw: dict[str, Any]


@dataclass(frozen=True)
class RecordedAction:
    seq: int
    kind: str
    canonical: str  # canonical injected-action name, or the kind itself
    source: str
    status: str | None
    params: dict[str, Any]


def _norm_id(value: Any) -> str:
    """Normalize model ids for loose comparison: 'CARD.ZAP' ~ 'ZAP' ~ 'Zap'."""
    text = re.sub(r"[^A-Z0-9]+", "_", str(value).upper()).strip("_")
    return text.split("_", 1)[1] if text.startswith(("CARD_", "RELIC_", "POTION_")) else text


def _lookup(items: Any, index: Any, *keys: str, index_key: str = "index") -> Any:
    """First present key of the pre_state list entry whose index_key matches."""
    for item in items if isinstance(items, list) else []:
        if isinstance(item, dict) and item.get(index_key) == index:
            for key in keys:
                if item.get(key) is not None:
                    return item[key]
    return None


def _injected_salient(name: str, action: dict[str, Any], pre: dict[str, Any]) -> dict[str, Any]:
    """Salient params for one injected action, enriched from its pre-state."""
    params: dict[str, Any] = {}
    if name == "play_card":
        params["card_index"] = action.get("card_index")
        params["card"] = _lookup(pre.get("hand"), action.get("card_index"), "id")
        if action.get("target") is not None:
            params["target"] = action["target"]
    elif name == "choose_map_node":
        params["index"] = action.get("index")
        options = pre.get("map", {}).get("next_options") if isinstance(pre.get("map"), dict) else None
        for option in options if isinstance(options, list) else []:
            if isinstance(option, dict) and option.get("index") == action.get("index"):
                params["node"] = f"x{option.get('col')}y{option.get('row')}"
    elif name == "shop_purchase":
        params["index"] = action.get("index")
        items = pre.get("shop", {}).get("items") if isinstance(pre.get("shop"), dict) else None
        params["item"] = _lookup(items, action.get("index"), "card_id", "relic_id", "potion_id")
    elif name in ("use_potion", "discard_potion"):
        params["slot"] = action.get("slot")
        params["item"] = _lookup(
            pre.get("potions"), action.get("slot"), "id", index_key="slot")
        if action.get("target") is not None:
            params["target"] = action["target"]
    elif name == "select_card_reward":
        params["index"] = action.get("card_index")
        block = pre.get("card_reward")
        cards = block.get("cards") if isinstance(block, dict) else None
        params["card"] = _lookup(cards, action.get("card_index"), "id")
    elif name == "claim_treasure_relic":
        params["index"] = action.get("index")
        block = pre.get("treasure")
        params["item"] = _lookup(
            block.get("relics") if isinstance(block, dict) else None,
            action.get("index"), "id")
    elif name == "select_relic":
        params["index"] = action.get("index")
        block = pre.get("relic_select")
        params["item"] = _lookup(
            block.get("relics") if isinstance(block, dict) else None,
            action.get("index"), "id")
    else:
        for key in ("index", "card_index", "slot", "x", "y", "tool", "option"):
            if action.get(key) is not None:
                params["index" if key == "card_index" else key] = action[key]
    return {key: value for key, value in params.items() if value is not None}


def load_injected(path: Path) -> tuple[list[InjectedAction], int]:
    """Load successful injections; returns (actions, failed_post_count)."""
    actions: list[InjectedAction] = []
    failed = 0
    for line_no, record in iter_jsonl(path):
        if record.get("type") != "injected_action":
            continue
        body = record.get("action")
        if not isinstance(body, dict) or "action" not in body:
            print(f"warning: {path.name}:{line_no}: no action body; skipped",
                  file=sys.stderr)
            continue
        response = record.get("response")
        ok = (record.get("http_status") == 200
              and isinstance(response, dict) and response.get("status") == "ok")
        if not ok:
            failed += 1
            continue
        name = str(body["action"])
        mapping = INJECTED_TO_RECORDER.get(
            name, KindMapping((name,), UNCERTAIN, "action not in mapping table"))
        pre = record.get("pre_state") if isinstance(record.get("pre_state"), dict) else {}
        actions.append(InjectedAction(
            seq=int(record.get("seq", line_no)),
            name=name,
            params=_injected_salient(name, body, pre),
            mapping=mapping,
            raw=record,
        ))
    return actions, failed


def _recorded_params(action: dict[str, Any]) -> dict[str, Any]:
    """Recorder >=0.1 nests params; older sessions spread them inline."""
    params = action.get("params")
    if isinstance(params, dict):
        return dict(params)
    return {key: value for key, value in action.items() if key != "kind"}


def load_recorded(session_dir: Path) -> list[RecordedAction]:
    path = session_dir / "actions.jsonl"
    records: list[RecordedAction] = []
    for line_no, raw in iter_jsonl(path):
        parsed = parse_action_record(raw, where=f"{path.name}:{line_no}")
        if parsed.status is not None and parsed.status not in FINAL_STATUSES:
            continue  # cancelled enqueues are not ground truth
        kind = str(parsed.action["kind"])
        records.append(RecordedAction(
            seq=parsed.seq,
            kind=kind,
            canonical=RECORDER_KIND_TO_CANONICAL.get(kind, kind),
            source=parsed.source,
            status=parsed.status,
            params=_recorded_params(dict(parsed.action)),
        ))
    return records


# ---------------------------------------------------------------------------
# Alignment + comparison
# ---------------------------------------------------------------------------


def align(
    injected: list[InjectedAction], recorded: list[RecordedAction]
) -> tuple[list[tuple[InjectedAction, RecordedAction]], list[InjectedAction], list[RecordedAction]]:
    """Order-preserving alignment over canonical kind sequences."""
    left = [item.name for item in injected]
    right = [item.canonical for item in recorded]
    matcher = difflib.SequenceMatcher(a=left, b=right, autojunk=False)
    pairs: list[tuple[InjectedAction, RecordedAction]] = []
    missing: list[InjectedAction] = []
    extra: list[RecordedAction] = []
    for tag, a_lo, a_hi, b_lo, b_hi in matcher.get_opcodes():
        if tag == "equal":
            pairs.extend(zip(injected[a_lo:a_hi], recorded[b_lo:b_hi]))
        else:
            missing.extend(injected[a_lo:a_hi])
            extra.extend(recorded[b_lo:b_hi])
    return pairs, missing, extra


def _values_match(dimension: str, injected_value: Any, recorded_value: Any) -> bool:
    if dimension in ("card", "item", "node"):
        left, right = _norm_id(injected_value), _norm_id(recorded_value)
        return left == right or left in right or right in left
    if dimension == "target":
        left, right = str(injected_value), str(recorded_value)
        if left == right:
            return True
        # injected entity_id "JAW_WORM_0" vs recorded int index 0
        suffix = left.rsplit("_", 1)[-1]
        return suffix.isdigit() and suffix == right
    return str(injected_value) == str(recorded_value)


def compare_params(
    injected: InjectedAction, recorded: RecordedAction
) -> tuple[list[str], list[str]]:
    """Returns (mismatches, comparisons_made) over aliased dimensions."""
    mismatches: list[str] = []
    compared: list[str] = []
    recorded_lower = {key.lower(): value for key, value in recorded.params.items()}
    for dimension, injected_value in injected.params.items():
        aliases = PARAM_ALIASES.get(dimension, (dimension,))
        present = [alias for alias in aliases if alias in recorded_lower]
        if not present:
            continue  # recorder naming unknown here — uncomparable, not a failure
        recorded_value = recorded_lower[present[0]]
        compared.append(dimension)
        if not _values_match(dimension, injected_value, recorded_value):
            mismatches.append(
                f"{dimension}: injected={injected_value!r} "
                f"recorded[{present[0]}]={recorded_value!r}"
            )
    return mismatches, compared


def check_attribution(recorded: RecordedAction) -> str | None:
    source = recorded.source.lower()
    for pattern in HUMAN_FUNNEL_PATTERNS:
        if pattern in source:
            return pattern
    return None


# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------


def _print_manifest(session_dir: Path) -> None:
    try:
        manifest = load_manifest(session_dir)
        print(f"session manifest: seed={manifest.run.seed} "
              f"character={manifest.run.character} game={manifest.game.version} "
              f"recorder={manifest.recorder_version} incomplete={manifest.incomplete}")
    except Exception as error:  # noqa: BLE001 - report and continue diffing
        print(f"warning: could not load manifest: {error}", file=sys.stderr)


def run_diff(session_dir: Path, injected_path: Path) -> int:
    print("== level2 diff ==")
    print(f"session:  {session_dir}")
    print(f"injected: {injected_path}")
    _print_manifest(session_dir)

    injected_all, failed_posts = load_injected(injected_path)
    recordable = [item for item in injected_all if item.mapping.kinds is not None]
    unrecordable = [item for item in injected_all if item.mapping.kinds is None]
    recorded = load_recorded(session_dir)

    pairs, missing, extra = align(recordable, recorded)

    failures: list[str] = []
    print(f"\ncounts: injected ok={len(injected_all)} (failed POSTs skipped={failed_posts}) "
          f"recordable={len(recordable)} expected-unrecorded={len(unrecordable)} | "
          f"recorded final actions={len(recorded)}")
    print(f"alignment: matched={len(pairs)} missing={len(missing)} extra={len(extra)}")

    print("\n-- matched pairs --")
    print(f"{'inj#':>5} {'injected action':<26} {'rec seq':>7} {'recorded kind':<22} params")
    for injected_item, recorded_item in pairs:
        mismatches, compared = compare_params(injected_item, recorded_item)
        if mismatches:
            verdict = "MISMATCH: " + "; ".join(mismatches)
            failures.append(
                f"param mismatch on injected #{injected_item.seq} "
                f"({injected_item.name}) vs recorded seq={recorded_item.seq}: "
                + "; ".join(mismatches)
            )
        elif compared:
            verdict = f"ok ({', '.join(compared)})"
        elif not injected_item.params:
            verdict = "ok (no params)"
        else:
            verdict = "no comparable keys (recorder naming unknown) [warn]"
        print(f"{injected_item.seq:>5} {injected_item.name:<26} "
              f"{recorded_item.seq:>7} {recorded_item.kind:<22} {verdict}")

    if missing:
        print("\n-- injected but MISSING from recording (FAIL) --")
        for item in missing:
            hint = (" [mapping UNCERTAIN — possibly a kind-name mismatch, "
                    f"expected one of {list(item.mapping.kinds or ())}]"
                    if item.mapping.confidence == UNCERTAIN else "")
            print(f"  #{item.seq} {item.name} params={item.params}{hint}")
            failures.append(f"injected #{item.seq} {item.name} not recorded{hint}")

    if extra:
        print("\n-- extra recorded actions (info, may be legitimate auto-claims) --")
        for item in extra:
            print(f"  seq={item.seq} kind={item.kind} source={item.source} "
                  f"params={item.params}")

    if unrecordable:
        print("\n-- injected actions expected to be unrecorded (info) --")
        by_name: dict[str, int] = {}
        for item in unrecordable:
            by_name[item.name] = by_name.get(item.name, 0) + 1
        for name, count in sorted(by_name.items()):
            print(f"  {name} x{count} ({INJECTED_TO_RECORDER[name].note})")

    print("\n-- source attribution (MCP injections must bypass human-UI hooks) --")
    hits = [(injected_item, recorded_item, pattern)
            for injected_item, recorded_item in pairs
            if (pattern := check_attribution(recorded_item)) is not None]
    sources = sorted({recorded_item.source for _, recorded_item in pairs})
    print(f"  checked {len(pairs)} matched records; sources seen: {sources or ['-']}")
    if hits:
        for injected_item, recorded_item, pattern in hits:
            failures.append(
                f"attribution: recorded seq={recorded_item.seq} "
                f"source={recorded_item.source!r} matches human-funnel "
                f"pattern {pattern!r} for MCP-injected #{injected_item.seq}"
            )
            print(f"  FAIL seq={recorded_item.seq} source={recorded_item.source!r} "
                  f"(human-funnel pattern {pattern!r})")
    else:
        print("  PASS: no human-funnel sources on injected actions")

    print("\n-- sts2rec validate --")
    report = validate_session(session_dir)
    print(f"  errors={len(report.errors)} warnings={len(report.warnings)} "
          f"counts={dict(report.counts)}")
    for error in report.errors:
        print(f"  error: {error}")
        failures.append(f"validate: {error}")
    for warning in report.warnings:
        print(f"  warning: {warning}")

    print("\n== result ==")
    if failures:
        print(f"FAIL — {len(failures)} problem(s):")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("PASS — every injected recordable action was recorded with matching "
          "params, attribution is non-UI, session validates")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Diff level2_driver.py injected actions against a recorded "
        "Sts2Recorder session (see module docstring).")
    parser.add_argument("--session", required=True,
                        help="recorded session directory (contains actions.jsonl)")
    parser.add_argument("--injected", required=True,
                        help="injected_actions.jsonl written by level2_driver.py")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    session_dir = Path(args.session)
    injected_path = Path(args.injected)
    if not session_dir.is_dir():
        print(f"error: --session {session_dir} is not a directory", file=sys.stderr)
        return 2
    if not injected_path.is_file():
        print(f"error: --injected {injected_path} not found", file=sys.stderr)
        return 2
    try:
        return run_diff(session_dir, injected_path)
    except Exception as error:  # noqa: BLE001 - top-level diagnostics
        print(f"error: diff failed: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
