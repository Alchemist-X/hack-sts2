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
recording with matching params, source attribution shows no human-exclusive
funnel, and `sts2rec validate` reports no errors. Known-and-already-fixed
recorder gaps (see EXPECTED_FIXED_NOTE) are reported as warnings, not
failures — they can only be re-verified with a fresh recording.
"""

from __future__ import annotations

import argparse
import difflib
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
#   * Recorder kinds: VERIFIED 2026-07-14 against the recorder source
#     (mod/src/ActionPipeline.cs Describe(), mod/src/HumanFunnels.cs) and the
#     live session 1784043137-CPSVMWJPUL (actions.jsonl). The earlier
#     FakeGameScenario-derived guesses ("map_choice", "reward_claim", ...) were
#     test-fixture names, not the recorder vocabulary.
#   * kinds=None => expected NOT to be recorded:
#       - menu_select: sessions cover one run; menu time is not recorded
#         (docs/design.md "One session = one run").
#       - proceed: leaving a screen has no game command; the next decision is
#         the map click (docs/hook-map.md, shop row: "'Leave shop' has no
#         command").
#       - advance_dialogue / crystal_sphere_*: bespoke UIs are cosmetic-local
#         and uncaptured by design (docs/hook-map.md, risk #16).
#
# The FIRST kind in each tuple is the canonical alignment token; later kinds
# are aliases that group to the same token (see _KIND_GROUP).
# ---------------------------------------------------------------------------

CONFIRMED = "confirmed"
UNCERTAIN = "uncertain"

# Dated label for the end_turn recorder gap found in session
# 1784043137-CPSVMWJPUL and fixed the same day (2026-07-14) in
# mod/src/ActionPipeline.cs: programmatic end-turns (STS2MCP's PlayerCmd.EndTurn)
# bypass ActionQueueSynchronizer.RequestEnqueue, so recorder <= 0.2.0 wrote NO
# end_turn record — only the automatic ready_to_begin_enemy_turn transition
# marker. The fix taps the native CombatManager.PlayerEndedTurn event.
EXPECTED_FIXED_NOTE = (
    "recorder <= 0.2.0 could not see PlayerCmd.EndTurn end-turns "
    "(no EndPlayerTurnAction is enqueued on that path); fixed 2026-07-14 via "
    "event:CombatManager.PlayerEndedTurn in mod/src/ActionPipeline.cs — "
    "needs a FRESH recording with the patched mod to verify"
)

# Salvage/marker time window (seconds): recorded t may precede the injected
# log t by a few ms (the driver logs after the POST returns); card-reward
# claims commit only after the subsequent pick, tens of seconds at most.
T_BEFORE = 5.0
T_AFTER = 60.0


@dataclass(frozen=True)
class KindMapping:
    kinds: tuple[str, ...] | None  # None => expected not recorded
    confidence: str
    note: str = ""


INJECTED_TO_RECORDER: dict[str, KindMapping] = {
    "play_card": KindMapping(("play_card",), CONFIRMED),
    "end_turn": KindMapping(("end_turn",), CONFIRMED,
                            "human button -> RequestEnqueue funnel record; "
                            "programmatic -> event:CombatManager.PlayerEndedTurn "
                            "(recorder > 0.2.0)"),
    "choose_map_node": KindMapping(("vote_for_map_coord",), CONFIRMED,
                                   "the decision record; move_to_map_coord is the "
                                   "movement commit for the same click (info)"),
    "shop_purchase": KindMapping(("shop_purchase",), CONFIRMED),
    "use_potion": KindMapping(("use_potion",), CONFIRMED),
    "discard_potion": KindMapping(("discard_potion",), CONFIRMED),
    "choose_event_option": KindMapping(
        ("event_option", "event_proceed"), CONFIRMED,
        "real choices -> event_option (SaveEventOptionToHistory); IsProceed "
        "options -> event_proceed (OptionButtonClicked)"),
    "choose_rest_option": KindMapping(("rest_site_option",), CONFIRMED),
    "claim_reward": KindMapping(("reward_taken",), CONFIRMED,
                                "commits when the claim task completes; a "
                                "card-reward claim only commits after the card "
                                "pick, i.e. AFTER the pick's player_choice "
                                "(matched out-of-order)"),
    "select_card_reward": KindMapping(
        ("player_choice",), CONFIRMED,
        "card pick commits via SyncLocalChoice -> player_choice (index)"),
    "skip_card_reward": KindMapping(
        ("player_choice", "rewards_skipped"), CONFIRMED,
        "skip is a CardRewardAlternative index via SyncLocalChoice; whole-set "
        "skip -> rewards_skipped"),
    "claim_treasure_relic": KindMapping(("pick_relic",), CONFIRMED),
    "select_card": KindMapping(("player_choice",), CONFIRMED),
    "confirm_selection": KindMapping(
        ("player_choice",), CONFIRMED,
        "grid confirm is folded into one player_choice record"),
    "cancel_selection": KindMapping(("player_choice",), UNCERTAIN),
    "combat_select_card": KindMapping(("player_choice",), CONFIRMED),
    "combat_confirm_selection": KindMapping(("player_choice",), CONFIRMED),
    "select_bundle": KindMapping(("player_choice",), UNCERTAIN),
    "confirm_bundle_selection": KindMapping(("player_choice",), UNCERTAIN),
    "cancel_bundle_selection": KindMapping(("player_choice",), UNCERTAIN),
    "select_relic": KindMapping(("player_choice",), CONFIRMED),
    "skip_relic_selection": KindMapping(("player_choice",), UNCERTAIN),
    "menu_select": KindMapping(None, CONFIRMED, "menu time is outside the session"),
    "proceed": KindMapping(None, CONFIRMED, "screen-leave has no game command"),
    "advance_dialogue": KindMapping(None, CONFIRMED, "cosmetic-local, uncaptured"),
    "crystal_sphere_set_tool": KindMapping(None, UNCERTAIN, "bespoke UI"),
    "crystal_sphere_click_cell": KindMapping(None, UNCERTAIN, "bespoke UI"),
    "crystal_sphere_proceed": KindMapping(None, UNCERTAIN, "bespoke UI"),
}

# Recorded kind -> alignment group token (the FIRST kind of the first mapping
# that lists it). Kinds the table does not know keep themselves as the token,
# so they can only ever land in "extra".
_KIND_GROUP: dict[str, str] = {}
for _mapping in INJECTED_TO_RECORDER.values():
    for _kind in _mapping.kinds or ():
        _KIND_GROUP.setdefault(_kind, _mapping.kinds[0])

# Recorded kinds that are known CONSEQUENCES of an already-matched decision —
# annotated in the "extra" section instead of looking like noise.
KNOWN_CONSEQUENCE_KINDS: dict[str, str] = {
    "move_to_map_coord": "movement commit for the matched vote_for_map_coord",
    "ready_to_begin_enemy_turn": "automatic turn transition (not player-driven)",
}

# ---------------------------------------------------------------------------
# Source attribution tiers.
#
# STRICT patterns are human-EXCLUSIVE funnels STS2MCP genuinely cannot reach
# (it plays cards via RequestEnqueue(new PlayCardAction(...)) and ends turns
# via PlayerCmd.EndTurn) — a hit on an MCP-injected action means attribution
# is broken.
#
# SHARED patterns are UI-level funnels MCP drives through the SAME code path
# as a human: sts2mcp-upstream/McpMod.Actions.cs calls
# NMapScreen.OnMapPointSelectedLocally directly (:448) and ForceClicks
# NEventOptionButton / NRewardButton (:292, :485). A hit there is EXPECTED for
# MCP injections and means "came through the local UI code path", NOT "human
# at the keyboard" — see docs/hook-map.md attribution note (2026-07-14).
# ---------------------------------------------------------------------------
STRICT_HUMAN_FUNNEL_PATTERNS: tuple[str, ...] = (
    "manualplay", "tryplay", "endturnbutton", "releaselogic", "potionpopup",
    "treasureroom", "chestbutton", "ncardplay", "ui:",
)
SHARED_UI_FUNNEL_PATTERNS: tuple[str, ...] = (
    "mapscreen", "mappointselected", "eventroom", "optionbutton",
)

# Param aliases: canonical dimension -> keys accepted in recorder params.
# NOTE deliberately absent: the injected card_index (hand position at inject
# time) and the recorder's combat_card_index (per-combat stable card id) are
# DIFFERENT numbering schemes — never compare them. Card identity is compared
# via the "card" dimension instead (injected pre-state hand[i].id vs recorded
# card_model_id).
PARAM_ALIASES: dict[str, tuple[str, ...]] = {
    "card": ("card", "card_id", "card_model_id", "cardid", "card_name"),
    "target": ("target", "target_id", "targetid"),
    "node": ("node", "coord", "map_node", "map_coord", "destination"),
    "index": ("index", "option", "option_index", "choice", "choice_index"),
    "item": ("item", "item_id", "relic", "relic_id", "potion", "potion_id"),
    "slot": ("slot", "potion_slot", "potion_index"),
    "reward_type": ("reward_type",),
}

# Dimensions compared with normalized-id containment (CARD.BASH ~ BASH)
# rather than strict string equality. reward_type has its own normalizer —
# _norm_id's model-prefix stripping would mangle 'card_reward' to 'REWARD'.
_CONTAINMENT_DIMENSIONS = frozenset({"card", "item", "node"})


# ---------------------------------------------------------------------------
# Loading + normalization
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class InjectedAction:
    seq: int
    t: float
    name: str
    token: str  # alignment token in the RECORDED vocabulary
    params: dict[str, Any]  # normalized salient params
    mapping: KindMapping
    raw: dict[str, Any]


@dataclass(frozen=True)
class RecordedAction:
    seq: int
    t: float
    kind: str
    token: str  # alignment group token
    source: str
    status: str | None
    params: dict[str, Any]


def _norm_id(value: Any) -> str:
    """Normalize model ids for loose comparison: 'CARD.ZAP' ~ 'ZAP' ~ 'Zap'."""
    text = re.sub(r"[^A-Z0-9]+", "_", str(value).upper()).strip("_")
    if text.startswith(("CARD_", "RELIC_", "POTION_", "MONSTER_")):
        return text.split("_", 1)[1]
    return text


def _norm_reward_type(value: Any) -> str:
    """'card' ~ 'card_reward' ~ 'CardReward' -> 'CARD' (no model-prefix strip)."""
    text = re.sub(r"[^A-Z0-9]+", "_", str(value).upper()).strip("_")
    return text.removesuffix("_REWARD")


def _entity_base(entity_id: Any) -> str:
    """Injected entity ids carry an occurrence suffix: 'TWIG_SLIME_S_0' -> 'TWIG_SLIME_S'."""
    return re.sub(r"_\d+$", "", str(entity_id))


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
        # card_index itself is NOT comparable (hand position vs the recorder's
        # per-combat combat_card_index) — resolve it to the card id instead.
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
    elif name == "claim_reward":
        params["index"] = action.get("index")
        block = pre.get("rewards")
        params["reward_type"] = _lookup(
            block.get("items") if isinstance(block, dict) else None,
            action.get("index"), "type")
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


def _injected_token(name: str, mapping: KindMapping, params: dict[str, Any]) -> str:
    primary = mapping.kinds[0] if mapping.kinds else name
    if name == "claim_reward" and params.get("reward_type"):
        # Sub-token by reward type: a card-reward claim commits AFTER the pick
        # (out of injected order) — typed tokens keep difflib from pairing the
        # gold claim with the card claim's record.
        return f"{primary}:{_norm_reward_type(params['reward_type'])}"
    return primary


def _recorded_token(kind: str, params: dict[str, Any]) -> str:
    group = _KIND_GROUP.get(kind, kind)
    if kind == "reward_taken" and params.get("reward_type"):
        return f"{group}:{_norm_reward_type(params['reward_type'])}"
    return group


def _tokens_compatible(left: str, right: str) -> bool:
    """Exact token match, or same group when only one side carries a subtype."""
    if left == right:
        return True
    lg, _, ls = left.partition(":")
    rg, _, rs = right.partition(":")
    return lg == rg and (not ls or not rs)


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
        params = _injected_salient(name, body, pre)
        actions.append(InjectedAction(
            seq=int(record.get("seq", line_no)),
            t=float(record.get("t", 0.0)),
            name=name,
            token=_injected_token(name, mapping, params),
            params=params,
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
        params = _recorded_params(dict(parsed.action))
        records.append(RecordedAction(
            seq=parsed.seq,
            t=parsed.t,
            kind=kind,
            token=_recorded_token(kind, params),
            source=parsed.source,
            status=parsed.status,
            params=params,
        ))
    return records


# ---------------------------------------------------------------------------
# Alignment + comparison
# ---------------------------------------------------------------------------


def align(
    injected: list[InjectedAction], recorded: list[RecordedAction]
) -> tuple[list[tuple[InjectedAction, RecordedAction]], list[InjectedAction], list[RecordedAction]]:
    """Order-preserving alignment over alignment-token sequences."""
    left = [item.token for item in injected]
    right = [item.token for item in recorded]
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


def salvage_out_of_order(
    missing: list[InjectedAction], extra: list[RecordedAction]
) -> tuple[list[tuple[InjectedAction, RecordedAction]], list[InjectedAction], list[RecordedAction]]:
    """Pair leftover injected/recorded items whose token matches within a time
    window. Covers legitimately reordered commits (a card-reward claim commits
    only after the pick that follows it)."""
    pairs: list[tuple[InjectedAction, RecordedAction]] = []
    used: set[int] = set()
    still_missing: list[InjectedAction] = []
    for item in missing:
        found = None
        for idx, record in enumerate(extra):
            if idx in used:
                continue
            if (_tokens_compatible(item.token, record.token)
                    and item.t - T_BEFORE <= record.t <= item.t + T_AFTER):
                found = idx
                break
        if found is None:
            still_missing.append(item)
        else:
            used.add(found)
            pairs.append((item, extra[found]))
    remaining_extra = [record for idx, record in enumerate(extra) if idx not in used]
    return pairs, still_missing, remaining_extra


def classify_expected_fixed(
    missing: list[InjectedAction],
    extra: list[RecordedAction],
    recorded: list[RecordedAction],
) -> tuple[list[tuple[InjectedAction, RecordedAction]], list[InjectedAction], list[RecordedAction]]:
    """The known pre-fix end_turn gap: classify a missing end_turn as
    expected-fixed when (a) the session has NO end_turn records at all (i.e. it
    was recorded before the 2026-07-14 PlayerEndedTurn tap) and (b) the
    automatic ready_to_begin_enemy_turn marker for that same turn exists. A
    post-fix session has end_turn records, so a genuinely missing one keeps
    failing loudly."""
    if any(record.kind == "end_turn" for record in recorded):
        return [], missing, extra
    expected: list[tuple[InjectedAction, RecordedAction]] = []
    used: set[int] = set()
    still_missing: list[InjectedAction] = []
    for item in missing:
        found = None
        if item.name == "end_turn":
            for idx, record in enumerate(extra):
                if idx in used or record.kind != "ready_to_begin_enemy_turn":
                    continue
                if item.t - T_BEFORE <= record.t <= item.t + T_AFTER:
                    found = idx
                    break
        if found is None:
            still_missing.append(item)
        else:
            used.add(found)
            expected.append((item, extra[found]))
    remaining_extra = [record for idx, record in enumerate(extra) if idx not in used]
    return expected, still_missing, remaining_extra


def _values_match(dimension: str, injected_value: Any, recorded_value: Any) -> bool:
    if dimension == "target":
        return _target_match(injected_value, recorded_value)
    if dimension == "reward_type":
        return _norm_reward_type(injected_value) == _norm_reward_type(recorded_value)
    if dimension == "node":
        recorded_value = _coord_text(recorded_value)
    if dimension in _CONTAINMENT_DIMENSIONS:
        left, right = _norm_id(injected_value), _norm_id(recorded_value)
        return left == right or left in right or right in left
    return str(injected_value) == str(recorded_value)


def _coord_text(value: Any) -> Any:
    """Recorded map coords are {'col': c, 'row': r}; normalize to 'xCyR'."""
    if isinstance(value, dict) and "col" in value and "row" in value:
        return f"x{value['col']}y{value['row']}"
    return value


def _target_match(injected_value: Any, recorded_value: Any) -> bool:
    """Injected targets are entity ids ('TWIG_SLIME_S_0', occurrence-indexed);
    the recorder describes the target object ({'model_id': 'MONSTER.TWIG_SLIME_S',
    ...}). Compare at model level: strip the occurrence suffix on the injected
    side and the MONSTER. prefix on the recorded model_id."""
    if isinstance(recorded_value, dict):
        model = recorded_value.get("model_id")
        if model is None:
            return False
        return _norm_id(_entity_base(injected_value)) == _norm_id(model)
    left, right = str(injected_value), str(recorded_value)
    if left == right:
        return True
    # injected entity_id "JAW_WORM_0" vs recorded int index 0
    suffix = left.rsplit("_", 1)[-1]
    return suffix.isdigit() and suffix == right


def _target_ambiguity(injected: InjectedAction) -> int:
    """How many pre-state enemies share the injected target's model? >1 means a
    model-level target match cannot pin the exact instance."""
    target = injected.params.get("target")
    pre = injected.raw.get("pre_state")
    enemies = pre.get("enemies") if isinstance(pre, dict) else None
    if target is None or not isinstance(enemies, list):
        return 1
    base = _entity_base(target)
    return sum(
        1 for enemy in enemies
        if isinstance(enemy, dict) and _entity_base(enemy.get("entity_id", "")) == base
    ) or 1


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
        label = dimension
        if dimension == "target" and isinstance(recorded_value, dict):
            same_model = _target_ambiguity(injected)
            if same_model > 1:
                # Model-level match only: the injected occurrence index and the
                # recorder's combat_id are different instance-numbering schemes.
                label = f"target(model-level, {same_model} same-model enemies)"
        compared.append(label)
        if not _values_match(dimension, injected_value, recorded_value):
            mismatches.append(
                f"{dimension}: injected={injected_value!r} "
                f"recorded[{present[0]}]={recorded_value!r}"
            )
    return mismatches, compared


def check_attribution(recorded: RecordedAction) -> tuple[str, str] | None:
    """Returns (tier, pattern) for a human-funnel source hit, else None."""
    source = recorded.source.lower()
    for pattern in STRICT_HUMAN_FUNNEL_PATTERNS:
        if pattern in source:
            return ("strict", pattern)
    for pattern in SHARED_UI_FUNNEL_PATTERNS:
        if pattern in source:
            return ("shared", pattern)
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
    salvaged, missing, extra = salvage_out_of_order(missing, extra)
    expected_fixed, missing, extra = classify_expected_fixed(missing, extra, recorded)
    out_of_order = {id(recorded_item) for _, recorded_item in salvaged}
    pairs = sorted(pairs + salvaged, key=lambda pair: pair[0].seq)

    failures: list[str] = []
    warnings: list[str] = []
    print(f"\ncounts: injected ok={len(injected_all)} (failed POSTs skipped={failed_posts}) "
          f"recordable={len(recordable)} expected-unrecorded={len(unrecordable)} | "
          f"recorded final actions={len(recorded)}")
    print(f"alignment: matched={len(pairs)} (out-of-order={len(salvaged)}) "
          f"expected-fixed={len(expected_fixed)} missing={len(missing)} extra={len(extra)}")

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
        if id(recorded_item) in out_of_order:
            verdict += (" [matched out-of-order: record-stream order differs from "
                        "inject order (e.g. a card-reward claim commits after the pick)]")
        print(f"{injected_item.seq:>5} {injected_item.name:<26} "
              f"{recorded_item.seq:>7} {recorded_item.kind:<22} {verdict}")

    if expected_fixed:
        print("\n-- EXPECTED-FIXED recorder gaps (warning, not failure) --")
        print(f"  {EXPECTED_FIXED_NOTE}")
        for injected_item, marker in expected_fixed:
            print(f"  #{injected_item.seq} {injected_item.name}: no end_turn record; "
                  f"downstream marker seq={marker.seq} kind={marker.kind} confirms the turn ended")
            warnings.append(
                f"expected-fixed: injected #{injected_item.seq} end_turn unrecorded "
                f"(pre-fix session; marker seq={marker.seq})")

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
            note = KNOWN_CONSEQUENCE_KINDS.get(item.kind)
            annotation = f"  [{note}]" if note else ""
            print(f"  seq={item.seq} kind={item.kind} source={item.source} "
                  f"params={item.params}{annotation}")

    if unrecordable:
        print("\n-- injected actions expected to be unrecorded (info) --")
        by_name: dict[str, int] = {}
        for item in unrecordable:
            by_name[item.name] = by_name.get(item.name, 0) + 1
        for name, count in sorted(by_name.items()):
            print(f"  {name} x{count} ({INJECTED_TO_RECORDER[name].note})")

    print("\n-- source attribution --")
    print("  note: 'human' from shared UI funnels (NMapScreen / NEventRoom) means")
    print("  'came through the local UI code path' — STS2MCP drives the same path")
    print("  (OnMapPointSelectedLocally / ForceClick), see docs/hook-map.md.")
    strict_hits = []
    shared_hits = []
    for injected_item, recorded_item in pairs:
        hit = check_attribution(recorded_item)
        if hit is None:
            continue
        tier, pattern = hit
        (strict_hits if tier == "strict" else shared_hits).append(
            (injected_item, recorded_item, pattern))
    sources = sorted({recorded_item.source for _, recorded_item in pairs})
    print(f"  checked {len(pairs)} matched records; sources seen: {sources or ['-']}")
    for injected_item, recorded_item, pattern in strict_hits:
        failures.append(
            f"attribution: recorded seq={recorded_item.seq} "
            f"source={recorded_item.source!r} matches human-EXCLUSIVE funnel "
            f"pattern {pattern!r} for MCP-injected #{injected_item.seq}"
        )
        print(f"  FAIL seq={recorded_item.seq} source={recorded_item.source!r} "
              f"(human-exclusive funnel pattern {pattern!r})")
    for injected_item, recorded_item, pattern in shared_hits:
        print(f"  info seq={recorded_item.seq} source={recorded_item.source!r} "
              f"(shared UI funnel {pattern!r}; expected for MCP map/event clicks)")
    if not strict_hits:
        print("  PASS: no human-exclusive funnel sources on injected actions")

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
        if warnings:
            print(f"  (+{len(warnings)} expected-fixed warning(s), see above)")
        return 1
    if warnings:
        print(f"PASS with {len(warnings)} expected-fixed warning(s) — every other "
              "injected recordable action was recorded with matching params, "
              "attribution shows no human-exclusive funnel, session validates.")
        for warning in warnings:
            print(f"  ~ {warning}")
        print(f"  {EXPECTED_FIXED_NOTE}")
        return 0
    print("PASS — every injected recordable action was recorded with matching "
          "params, attribution shows no human-exclusive funnel, session validates")
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
