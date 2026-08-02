import pytest

from sts2rec.evaluation import (
    BetaWinRate,
    LimitedExperienceEstimator,
    OmniscientRolloutEstimator,
    evaluate_decision,
)


class Estimator:
    def win_probability(self, view, action):
        return {"a": 0.4, "b": 0.7}[action["action"]]


def test_counterfactual_decision_loss_formula() -> None:
    view = {"legal_actions": [{"action": "a"}, {"action": "b"}]}
    result = evaluate_decision(view, {"action": "a"}, Estimator())
    assert result.best_action == {"action": "b"}
    assert result.decision_loss == pytest.approx(0.3)
    assert result.score == pytest.approx(70.0)


def test_beta_win_rate_has_smoothing() -> None:
    assert BetaWinRate(wins=0, losses=0).mean == 0.5
    assert BetaWinRate(wins=8, losses=2).mean == pytest.approx(0.75)


def test_limited_estimator_learns_without_privileged_channel() -> None:
    view = {
        "mode": "limited",
        "privileged": None,
        "observation": {"state_type": "map", "run": {"act": 1, "floor": 2}},
    }
    action = {"action": "choose_map_node", "index": 0}
    model = LimitedExperienceEstimator()
    model.observe(view, action, won=True)
    assert model.win_probability(view, action) == pytest.approx(2 / 3)


def test_omniscient_estimator_runs_seeded_sandbox_rollouts() -> None:
    view = {"mode": "omniscient", "privileged": {"seed": "X"}}
    model = OmniscientRolloutEstimator(lambda _v, _a, seed: seed < 3, rollouts=4)
    assert model.win_probability(view, {"action": "a"}) == pytest.approx(4 / 6)
