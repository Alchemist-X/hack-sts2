"""Decision-value contracts shared by limited and omniscient evaluators.

This module deliberately separates the mathematical loss definition from the
model that estimates Q(s, a).  A rule-based diagnostic is not silently called
"win probability"; calibrated rollout or learned estimators can plug into the
same interface later.
"""

from __future__ import annotations

import json
from collections import defaultdict
from dataclasses import asdict, dataclass
from typing import Any, Callable, Protocol


class ActionValueEstimator(Protocol):
    """Estimate eventual run-win probability for a legal action."""

    def win_probability(self, view: dict[str, Any], action: dict[str, Any]) -> float: ...


@dataclass(frozen=True)
class DecisionEvaluation:
    chosen_action: dict[str, Any]
    chosen_win_probability: float
    best_action: dict[str, Any]
    best_win_probability: float
    decision_loss: float
    score: float

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


def evaluate_decision(
    view: dict[str, Any],
    chosen_action: dict[str, Any],
    estimator: ActionValueEstimator,
) -> DecisionEvaluation:
    """Evaluate L(s,a)=max_a' Q(s,a')-Q(s,a), with probabilities in [0,1]."""
    legal = view.get("legal_actions")
    if not isinstance(legal, list) or not legal:
        raise ValueError("view has no legal actions")
    valued: list[tuple[float, dict[str, Any]]] = []
    for action in legal:
        probability = float(estimator.win_probability(view, action))
        if not 0.0 <= probability <= 1.0:
            raise ValueError(f"estimator returned probability outside [0,1]: {probability}")
        valued.append((probability, action))
    chosen_probability = float(estimator.win_probability(view, chosen_action))
    if not 0.0 <= chosen_probability <= 1.0:
        raise ValueError(
            f"estimator returned chosen probability outside [0,1]: {chosen_probability}"
        )
    best_probability, best_action = max(valued, key=lambda item: item[0])
    loss = max(0.0, best_probability - chosen_probability)
    return DecisionEvaluation(
        chosen_action=chosen_action,
        chosen_win_probability=chosen_probability,
        best_action=best_action,
        best_win_probability=best_probability,
        decision_loss=loss,
        score=100.0 * (1.0 - loss),
    )


@dataclass(frozen=True)
class BetaWinRate:
    """Limited-information Bayesian empirical win-rate baseline."""

    wins: int
    losses: int
    prior_wins: float = 1.0
    prior_losses: float = 1.0

    @property
    def mean(self) -> float:
        return (self.wins + self.prior_wins) / (
            self.wins + self.losses + self.prior_wins + self.prior_losses
        )


def _action_key(action: dict[str, Any]) -> str:
    compact = {key: value for key, value in action.items() if key not in {"candidate", "target_candidate"}}
    return json.dumps(compact, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def _limited_state_key(view: dict[str, Any]) -> str:
    state = view.get("observation") if isinstance(view.get("observation"), dict) else {}
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    run = state.get("run") if isinstance(state.get("run"), dict) else {}
    max_hp = max(1, int(player.get("max_hp", 1) or 1))
    hp_bucket = round(10 * int(player.get("hp", 0) or 0) / max_hp)
    relics = sorted(
        str(relic.get("id"))
        for relic in player.get("relics", [])
        if isinstance(relic, dict) and relic.get("id")
    )
    return json.dumps(
        {
            "state_type": state.get("state_type"),
            "character": player.get("character"),
            "act": run.get("act"),
            "floor_bucket": int(run.get("floor", 0) or 0) // 4,
            "hp_bucket": hp_bucket,
            "relics": relics,
        },
        sort_keys=True,
        ensure_ascii=False,
    )


class LimitedExperienceEstimator:
    """Human-information estimator that learns Beta posteriors from gameplay."""

    def __init__(self, prior_wins: float = 1.0, prior_losses: float = 1.0) -> None:
        self.prior_wins = prior_wins
        self.prior_losses = prior_losses
        self._counts: dict[tuple[str, str], list[int]] = defaultdict(lambda: [0, 0])

    def observe(self, view: dict[str, Any], action: dict[str, Any], *, won: bool) -> None:
        if view.get("mode") != "limited" or view.get("privileged") is not None:
            raise ValueError("limited estimator accepts only leakage-free limited views")
        counts = self._counts[(_limited_state_key(view), _action_key(action))]
        counts[0 if won else 1] += 1

    def win_probability(self, view: dict[str, Any], action: dict[str, Any]) -> float:
        wins, losses = self._counts[(_limited_state_key(view), _action_key(action))]
        return BetaWinRate(
            wins=wins,
            losses=losses,
            prior_wins=self.prior_wins,
            prior_losses=self.prior_losses,
        ).mean


class OmniscientRolloutEstimator:
    """Full-information Monte Carlo estimator backed by a sandbox rollout callback."""

    def __init__(
        self,
        rollout: Callable[[dict[str, Any], dict[str, Any], int], bool],
        *,
        rollouts: int = 32,
    ) -> None:
        if rollouts < 1:
            raise ValueError("rollouts must be >= 1")
        self.rollout = rollout
        self.rollouts = rollouts

    def win_probability(self, view: dict[str, Any], action: dict[str, Any]) -> float:
        if view.get("mode") != "omniscient" or view.get("privileged") is None:
            raise ValueError("omniscient rollouts require an omniscient privileged view")
        wins = sum(bool(self.rollout(view, action, seed)) for seed in range(self.rollouts))
        return BetaWinRate(wins=wins, losses=self.rollouts - wins).mean
