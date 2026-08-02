"""Auditable baseline review for recorded human decisions.

The output loss is a *diagnostic proxy*, not a calibrated counterfactual win
probability.  Each non-zero value is accompanied by a rule and confidence;
the true Q-loss contract lives in :mod:`sts2rec.evaluation`.
"""

from __future__ import annotations

import csv
import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

from .lineage import build_lineage


@dataclass(frozen=True)
class ReviewRow:
    step_idx: int
    part: int
    action_seq: int | None
    act: int | None
    floor: int | None
    state_type: str | None
    action_type: str
    hp: int | None
    loss: float
    score: float
    confidence: str
    labels: tuple[str, ...]


def _is_attack_intent(enemy: dict[str, Any]) -> bool:
    return any(
        isinstance(intent, dict) and str(intent.get("type", "")).lower() == "attack"
        for intent in enemy.get("intents", [])
    )


def _player_damage(events: list[dict[str, Any]]) -> int:
    total = 0
    for event in events:
        if event.get("entry") != "damage_received":
            continue
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        result = data.get("result") if isinstance(data.get("result"), dict) else {}
        receiver = result.get("receiver") if isinstance(result.get("receiver"), dict) else {}
        if receiver.get("is_player"):
            total += int(result.get("unblocked", 0) or 0)
    return total


def _review_step(step: dict[str, Any]) -> ReviewRow:
    state = step.get("state_before") if isinstance(step.get("state_before"), dict) else {}
    action = step.get("action") if isinstance(step.get("action"), dict) else {}
    params = action.get("params") if isinstance(action.get("params"), dict) else {}
    info = step.get("info") if isinstance(step.get("info"), dict) else {}
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    run = state.get("run") if isinstance(state.get("run"), dict) else {}
    battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
    enemies = [enemy for enemy in battle.get("enemies", []) if isinstance(enemy, dict)]
    action_type = str(action.get("type", "unknown"))
    labels: list[str] = []
    loss = 0.0
    confidence = "low"

    if action_type == "end_turn":
        if not enemies:
            labels.append("combat_transition_not_scored")
            return ReviewRow(
                step_idx=int(step.get("step_idx", 0)),
                part=int(info.get("part", 1)),
                action_seq=info.get("action_seq"),
                act=run.get("act"),
                floor=run.get("floor"),
                state_type=state.get("state_type"),
                action_type=action_type,
                hp=player.get("hp"),
                loss=0.0,
                score=100.0,
                confidence="high",
                labels=tuple(labels),
            )
        energy = int(player.get("energy", 0) or 0)
        playable = [
            card
            for card in player.get("hand", [])
            if isinstance(card, dict) and card.get("can_play", False)
        ]
        if energy > 0 and playable:
            attacks = [card for card in playable if str(card.get("type")) in {"Attack", "Power"}]
            loss += min(0.35, 0.06 + 0.05 * energy + 0.025 * len(playable))
            labels.append("end_turn_with_playable_cards")
            if attacks:
                loss += 0.05
                labels.append("unused_attack_or_power")
            confidence = "high"
        else:
            labels.append("energy_discipline")
            confidence = "high"
        damage = _player_damage(info.get("events", []))
        if damage == 0 and any(_is_attack_intent(enemy) for enemy in enemies):
            labels.append("fully_covered_incoming_damage")
        elif damage > 0:
            labels.append(f"unblocked_damage:{damage}")

    if action_type == "play_card" and len(enemies) > 1:
        target_id = params.get("target_id")
        target = next((enemy for enemy in enemies if enemy.get("combat_id") == target_id), None)
        other_attackers = [
            enemy for enemy in enemies if enemy is not target and _is_attack_intent(enemy)
        ]
        if target and other_attackers:
            weakest = min(other_attackers, key=lambda enemy: int(enemy.get("hp", 0) or 0))
            if int(target.get("hp", 0) or 0) >= int(weakest.get("hp", 0) or 0) + 8:
                loss += 0.10
                labels.append("possible_target_focus_error")
                confidence = "medium"

    if action_type == "vote_for_map_coord":
        options = state.get("map", {}).get("next_options", []) if isinstance(state.get("map"), dict) else []
        chosen = next(
            (
                item
                for item in options
                if isinstance(item, dict)
                and item.get("col") == params.get("col")
                and item.get("row") == params.get("row")
            ),
            None,
        )
        hp = int(player.get("hp", 0) or 0)
        max_hp = int(player.get("max_hp", 0) or 0)
        has_elite = any(str(item.get("type", "")).lower() == "elite" for item in options if isinstance(item, dict))
        if chosen and str(chosen.get("type", "")).lower() == "restsite" and has_elite and max_hp and hp / max_hp >= 0.85:
            loss += 0.12
            labels.append("high_hp_rest_over_available_elite")
            confidence = "medium"
        elif chosen and str(chosen.get("type", "")).lower() == "elite":
            labels.append("took_growth_opportunity")
            confidence = "medium"

    if action_type in {"ready_to_begin_enemy_turn", "move_to_map_coord"}:
        labels.append("system_followup_not_scored")

    if not labels:
        labels.append("no_rule_based_regret_detected")
    loss = min(1.0, round(loss, 4))
    return ReviewRow(
        step_idx=int(step.get("step_idx", 0)),
        part=int(info.get("part", 1)),
        action_seq=info.get("action_seq"),
        act=run.get("act"),
        floor=run.get("floor"),
        state_type=state.get("state_type"),
        action_type=action_type,
        hp=player.get("hp"),
        loss=loss,
        score=round(100.0 * (1.0 - loss), 2),
        confidence=confidence,
        labels=tuple(labels),
    )


def review_lineage(session_dir: str | Path) -> dict[str, Any]:
    trajectory = build_lineage(session_dir)
    rows = [_review_step(step) for step in trajectory["steps"]]
    scored = [
        row
        for row in rows
        if "system_followup_not_scored" not in row.labels
        and "combat_transition_not_scored" not in row.labels
    ]
    return {
        "meta": trajectory["meta"],
        "method": {
            "name": "auditable_rule_based_proxy_v1",
            "warning": "loss is a diagnostic proxy, not calibrated Q(s,a) win-probability regret",
            "true_loss_formula": "max_a Q(s,a) - Q(s,a_human)",
        },
        "summary": {
            "steps": len(rows),
            "scored_steps": len(scored),
            "mean_proxy_loss": round(sum(row.loss for row in scored) / max(1, len(scored)), 6),
            "mean_score": round(sum(row.score for row in scored) / max(1, len(scored)), 3),
            "flagged_steps": sum(row.loss > 0 for row in rows),
        },
        "rows": [asdict(row) for row in rows],
    }


def write_review(report: dict[str, Any], json_path: str | Path, csv_path: str | Path) -> None:
    Path(json_path).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    rows = report["rows"]
    with Path(csv_path).open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]) if rows else [])
        if rows:
            writer.writeheader()
            for row in rows:
                writer.writerow({**row, "labels": "|".join(row["labels"])})
