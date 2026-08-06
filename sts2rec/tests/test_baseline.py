import json

import pytest

from sts2rec.baseline import (
    BinaryWinValueModel,
    HashedStateActionFeatures,
    LinearBehaviorCloningPolicy,
    evaluate_behavior_cloning,
    evaluate_win_value,
)


def _actions():
    return [
        {"action_index": 0, "action": "strike", "target": "front"},
        {"action_index": 1, "action": "block"},
    ]


def _records():
    records = []
    for _ in range(12):
        records.append(
            {
                "observation": {"state_type": "combat", "mode": "attack"},
                "legal_actions": _actions(),
                "chosen_action_index": 0,
                "outcome": 1,
                "eligible": True,
            }
        )
        records.append(
            {
                "observation": {"state_type": "combat", "mode": "defend"},
                "legal_actions": _actions(),
                "chosen_action_index": 1,
                "outcome": 0,
                "eligible": True,
            }
        )
    records.append(
        {
            "observation": {},
            "legal_actions": [],
            "chosen_action_index": -1,
            "outcome": None,
            "eligible": False,
        }
    )
    return records


def _encoder():
    return HashedStateActionFeatures(dimension=4096, hash_seed="tiny-fixture")


def test_behavior_cloning_loss_decreases_and_tracks_skips() -> None:
    records = _records()
    model = LinearBehaviorCloningPolicy(encoder=_encoder())
    before = evaluate_behavior_cloning(model, records)
    after = model.fit(
        records,
        epochs=25,
        learning_rate=0.15,
        shuffle=True,
        seed=7,
    )

    assert before.nll == pytest.approx(0.6931471805599453)
    assert after.nll < before.nll * 0.25
    assert after.top1_accuracy == 1.0
    assert after.examples == 24
    assert after.skipped == 1


def test_candidate_permutation_reorders_probabilities_not_action_values() -> None:
    model = LinearBehaviorCloningPolicy(encoder=_encoder())
    model.fit(_records(), epochs=20, learning_rate=0.15, seed=3)
    observation = {"state_type": "combat", "mode": "attack"}
    original = _actions()
    reversed_actions = list(reversed(original))

    original_probabilities = model.predict_proba(observation, original)
    reversed_probabilities = model.predict_proba(observation, reversed_actions)

    assert original_probabilities[0] == pytest.approx(reversed_probabilities[1])
    assert original_probabilities[1] == pytest.approx(reversed_probabilities[0])
    assert model.choose_action(observation, original)["action"] == "strike"
    assert model.choose_action(observation, reversed_actions)["action"] == "strike"


def test_evaluator_annotations_do_not_change_action_features() -> None:
    encoder = _encoder()
    plain = {"action": "strike", "target": "front"}
    annotated = {
        **plain,
        "action_index": 99,
        "candidate_index": 4,
        "candidate": {"name": "Strike", "damage": 999},
        "target_candidate": {"hp": 1},
        "label": "best",
        "q_value": 0.999,
    }
    assert encoder.encode({}, plain) == encoder.encode({}, annotated)


def test_action_mask_applies_to_training_evaluation_and_inference() -> None:
    model = LinearBehaviorCloningPolicy(encoder=_encoder())
    observation = {"state_type": "combat"}
    actions = _actions()
    probabilities = model.predict_proba(
        observation, actions, action_mask=[1, 0]
    )
    assert probabilities == [1.0, 0.0]
    assert model.choose_action_index(
        observation, actions, action_mask=[1, 0]
    ) == 0
    with pytest.raises(ValueError, match="masked out"):
        model.update(observation, actions, 1, action_mask=[1, 0])
    with pytest.raises(ValueError, match="masked out"):
        evaluate_behavior_cloning(
            model,
            [
                {
                    "observation": observation,
                    "legal_actions": actions,
                    "action_mask": [1, 0],
                    "chosen_action_index": 1,
                    "eligible": True,
                }
            ],
        )
    with pytest.raises(ValueError, match="must be an integer"):
        model.update(observation, actions, True)


def test_privileged_features_are_opt_in_and_cannot_mix_with_limited() -> None:
    record = {
        "observation": {"state_type": "combat"},
        "privileged_observation": {"rng_state": "A"},
        "information_mode": "omniscient",
        "legal_actions": _actions(),
        "action_mask": [1, 1],
        "chosen_action_index": 0,
        "outcome": 1,
        "eligible": True,
    }
    public_model = LinearBehaviorCloningPolicy(encoder=_encoder())
    public_model.fit([record], epochs=1)

    privileged_model = LinearBehaviorCloningPolicy(
        encoder=_encoder(), use_privileged_observation=True
    )
    privileged_model.fit([record], epochs=1)
    assert privileged_model.use_privileged_observation is True

    contaminated_limited = {
        **record,
        "information_mode": "limited",
    }
    with pytest.raises(ValueError, match="limited records"):
        public_model.fit([contaminated_limited])
    with pytest.raises(ValueError, match="requires omniscient"):
        privileged_model.fit(
            [{**record, "information_mode": "limited", "privileged_observation": None}]
        )


def test_empty_metrics_raise_instead_of_reporting_perfect_zero() -> None:
    skipped = [
        {
            "observation": {},
            "legal_actions": [],
            "chosen_action_index": -1,
            "eligible": False,
        }
    ]
    with pytest.raises(ValueError, match="without eligible examples"):
        evaluate_behavior_cloning(LinearBehaviorCloningPolicy(), skipped)
    with pytest.raises(ValueError, match="without eligible examples"):
        evaluate_win_value(BinaryWinValueModel(), skipped)


def test_value_loss_decreases_and_both_checkpoints_roundtrip(tmp_path) -> None:
    records = _records()
    value = BinaryWinValueModel(encoder=_encoder(), metadata={"game": "sts2"})
    before = evaluate_win_value(value, records)
    after = value.fit(records, epochs=30, learning_rate=0.12, seed=11)

    assert after.log_loss < before.log_loss * 0.25
    assert after.brier_score < before.brier_score
    assert after.skipped == 1

    value_path = value.save(tmp_path / "value.json", metadata={"split": "tiny"})
    raw = json.loads(value_path.read_text(encoding="utf-8"))
    assert raw["schema_version"] == 1
    assert raw["model"]["type"] == "hashed_linear_win_value"
    assert raw["metadata"] == {"game": "sts2", "split": "tiny"}
    restored_value = BinaryWinValueModel.load(value_path)
    observation = {"state_type": "combat", "mode": "attack"}
    action = _actions()[0]
    assert restored_value.predict_proba(observation, action) == pytest.approx(
        value.predict_proba(observation, action)
    )

    policy = LinearBehaviorCloningPolicy(encoder=_encoder())
    policy.fit(records, epochs=5, seed=2)
    policy_path = policy.save(tmp_path / "policy.json", metadata={"run": 5})
    restored_policy = LinearBehaviorCloningPolicy.load(policy_path)
    assert restored_policy.predict_proba(observation, _actions()) == pytest.approx(
        policy.predict_proba(observation, _actions())
    )
    assert restored_policy.updates == policy.updates


def test_incomplete_human_rows_train_bc_but_are_skipped_for_value() -> None:
    record = {
        **_records()[0],
        "bc_eligible": True,
        "value_eligible": False,
        "outcome": None,
    }
    assert LinearBehaviorCloningPolicy().fit([record]).examples == 1
    with pytest.raises(ValueError, match="without eligible examples"):
        BinaryWinValueModel().fit([record])


def test_value_eligibility_cannot_bypass_bc_quality_gate() -> None:
    record = {
        **_records()[0],
        "eligible": False,
        "bc_eligible": False,
        "value_eligible": True,
    }
    with pytest.raises(ValueError, match="value_eligible requires bc_eligible"):
        BinaryWinValueModel().fit([record])
