import hashlib
import json

import pytest

from sts2rec.benchmark import (
    BenchmarkCase,
    BenchmarkSpec,
    EpisodeOutcome,
    summarize_action_coverage,
    summarize_nosl,
    summarize_sl,
    wilson_interval,
)


def _spec(**changes):
    values = {
        "name": "limited-nosl-a0-a1",
        "game_version": "0.107.1",
        "game_build": "build-abc",
        "character": "necrobinder",
        "ascensions": (0, 1),
        "policy_id": "sha256:policy",
        "seed_suite_id": "sha256:seeds",
        "cases": (
            BenchmarkCase("case-a0", "seed-a0", 0, 0),
            BenchmarkCase("case-a1", "seed-a1", 1, 1),
        ),
        "information_mode": "limited",
        "save_load_mode": "nosl",
        "observation_schema": "v1",
        "environment_id": "sha256:environment",
        "victory_condition": "defeat_final_boss",
        "timeout_s": 900,
        "max_steps": 100_000,
        "mod_set_id": "sha256:mods",
        "unlock_state_id": "sha256:unlocks",
        "controller_version": "sts2rec-controller-v1",
        "metadata": {"中文": "保留", "nested": {"b": 2, "a": [1, True]}},
    }
    values.update(changes)
    return BenchmarkSpec(**values)


_AUTO_TRAJECTORY_HASH = object()


def _outcome(
    episode_id: str,
    seed: str,
    won: bool,
    *,
    case_id: str | None = None,
    ascension: int = 1,
    attempt: int = 1,
    sequence_index: int = 0,
    reason: str | None = None,
    reload_count: int | None = 0,
    trajectory_complete: bool | None = True,
    trajectory_hash: str | None | object = _AUTO_TRAJECTORY_HASH,
    steps: int | None = 10,
    elapsed_s: float | None = 1.0,
) -> EpisodeOutcome:
    if trajectory_hash is _AUTO_TRAJECTORY_HASH:
        trajectory_hash = "sha256:" + hashlib.sha256(
            episode_id.encode("utf-8")
        ).hexdigest()
    return EpisodeOutcome(
        episode_id=episode_id,
        case_id=case_id or episode_id,
        seed=seed,
        ascension=ascension,
        won=won,
        terminal_reason=reason or ("victory" if won else "death"),
        sequence_index=sequence_index,
        attempt=attempt,
        reload_count=reload_count,
        trajectory_complete=trajectory_complete,
        trajectory_hash=trajectory_hash,  # type: ignore[arg-type]
        steps=steps,
        elapsed_s=elapsed_s,
    )


def test_benchmark_spec_has_stable_canonical_json_and_fingerprint() -> None:
    first_metadata = {"z": 3, "nested": {"b": 2, "a": [1, True]}}
    second_metadata = {"nested": {"a": [1, True], "b": 2}, "z": 3}
    first = _spec(metadata=first_metadata)
    second = _spec(metadata=second_metadata, ascensions=[0, 1])

    assert first.canonical_json() == second.canonical_json()
    assert first.fingerprint() == second.fingerprint()
    assert len(first.fingerprint()) == 64
    assert json.loads(first.canonical_json()) == first.as_dict()
    json.dumps(first.as_dict(), allow_nan=False)
    assert first.as_dict()["timeout_s"] == 900.0

    first_metadata["z"] = 99
    assert first.as_dict()["metadata"]["z"] == 3

    for field_name, value in (
        ("timeout_s", 901),
        ("max_steps", 100_001),
        ("mod_set_id", "sha256:other-mods"),
        ("unlock_state_id", "sha256:other-unlocks"),
        ("controller_version", "sts2rec-controller-v2"),
    ):
        assert _spec(**{field_name: value}).fingerprint() != first.fingerprint()


def test_benchmark_case_validates_and_serializes() -> None:
    case = BenchmarkCase("case-1", "seed-1", 10, 0)
    assert case.as_dict() == {
        "case_id": "case-1",
        "seed": "seed-1",
        "ascension": 10,
        "sequence_index": 0,
    }
    with pytest.raises(ValueError, match="case_id"):
        BenchmarkCase("", "seed", 0, 0)
    with pytest.raises(ValueError, match="seed"):
        BenchmarkCase("case", " ", 0, 0)
    with pytest.raises(ValueError, match="ascension"):
        BenchmarkCase("case", "seed", -1, 0)
    with pytest.raises(ValueError, match="0 through 10"):
        BenchmarkCase("case", "seed", 11, 0)
    with pytest.raises(ValueError, match="sequence_index"):
        BenchmarkCase("case", "seed", 0, True)  # type: ignore[arg-type]


def test_benchmark_spec_normalizes_cases_into_predeclared_sequence() -> None:
    spec = _spec(
        cases=(
            BenchmarkCase("case-a1", "seed-a1", 1, 1),
            BenchmarkCase("case-a0", "seed-a0", 0, 0),
        )
    )
    assert [case.case_id for case in spec.cases] == ["case-a0", "case-a1"]
    assert [case["sequence_index"] for case in spec.as_dict()["cases"]] == [0, 1]


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"name": " "}, "whitespace"),
        ({"ascensions": ()}, "must not be empty"),
        ({"ascensions": (1, 1)}, "strictly increasing"),
        ({"ascensions": (2, 1)}, "strictly increasing"),
        ({"ascensions": (False,)}, "integer"),
        ({"ascensions": (11,)}, "0 through 10"),
        ({"information_mode": "cheat"}, "information_mode"),
        ({"save_load_mode": "maybe"}, "save_load_mode"),
        ({"sl_budgets": (2,)}, "empty for a nosl"),
        ({"game_build": ""}, "game_build"),
        ({"environment_id": None}, "environment_id"),
        ({"victory_condition": " "}, "victory_condition"),
        ({"timeout_s": 0}, "timeout_s"),
        ({"timeout_s": float("inf")}, "timeout_s"),
        ({"timeout_s": True}, "timeout_s"),
        ({"max_steps": 0}, "max_steps"),
        ({"max_steps": True}, "max_steps"),
        ({"mod_set_id": ""}, "mod_set_id"),
        ({"unlock_state_id": " "}, "unlock_state_id"),
        ({"controller_version": None}, "controller_version"),
        ({"cases": ()}, "must not be empty"),
        ({"cases": ({"case_id": "not-an-object"},)}, "BenchmarkCase"),
        (
            {
                "cases": (
                    BenchmarkCase("same", "seed-a0", 0, 0),
                    BenchmarkCase("same", "seed-a1", 1, 1),
                )
            },
            "unique case_id",
        ),
        (
            {
                "cases": (
                    BenchmarkCase("case-a0", "same", 0, 0),
                    BenchmarkCase("case-a1", "same", 1, 1),
                )
            },
            "unique seed",
        ),
        (
            {
                "cases": (
                    BenchmarkCase("case-a0", "seed-a0", 0, 0),
                    BenchmarkCase("case-a1", "seed-a1", 1, 0),
                )
            },
            "unique sequence_index",
        ),
        (
            {
                "cases": (
                    BenchmarkCase("case-a0", "seed-a0", 0, 0),
                    BenchmarkCase("case-a1", "seed-a1", 1, 2),
                )
            },
            "contiguous from 0",
        ),
        (
            {
                "cases": (
                    BenchmarkCase("case-a0", "seed-a0", 0, 0),
                    BenchmarkCase("case-a1", "seed-a1", 0, 1),
                )
            },
            "ascensions must exactly match",
        ),
        ({"metadata": {"bad": float("nan")}}, "non-finite"),
        ({"metadata": {1: "bad"}}, "non-string"),
    ],
)
def test_benchmark_spec_rejects_ambiguous_contracts(changes, message) -> None:
    with pytest.raises(ValueError, match=message):
        _spec(**changes)


def test_sl_spec_requires_ordered_budgets() -> None:
    with pytest.raises(ValueError, match="declare at least one"):
        _spec(save_load_mode="sl")
    spec = _spec(save_load_mode="sl", sl_budgets=[1, 2, 8])
    assert spec.sl_budgets == (1, 2, 8)


def test_episode_outcome_validates_and_serializes() -> None:
    outcome = _outcome("episode-1", "seed-a", True, ascension=0)
    assert outcome.seed_group == ("episode-1", 0, "seed-a")
    assert json.loads(json.dumps(outcome.as_dict())) == outcome.as_dict()
    with pytest.raises(ValueError, match="won must be a bool"):
        _outcome("episode-2", "seed-b", 1)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="attempt"):
        _outcome("episode-3", "seed-c", False, attempt=0)
    with pytest.raises(ValueError, match="if and only if"):
        _outcome("episode-4", "seed-d", True, reason="timeout")
    with pytest.raises(ValueError, match="reload_count"):
        _outcome("episode-5", "seed-e", False, reload_count=True)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="reload_count"):
        _outcome("episode-6", "seed-f", False, reload_count=-1)
    with pytest.raises(ValueError, match="trajectory_complete"):
        _outcome("episode-7", "seed-g", False, trajectory_complete=1)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="trajectory_hash"):
        _outcome("episode-8", "seed-h", False, trajectory_hash=" ")
    with pytest.raises(ValueError, match="0 through 10"):
        _outcome("episode-9", "seed-i", False, ascension=11)


@pytest.mark.parametrize(
    ("wins", "total", "lower", "upper"),
    [
        (0, 10, 0.0, 0.2775328),
        (5, 10, 0.2365931, 0.7634069),
        (10, 10, 0.7224672, 1.0),
    ],
)
def test_wilson_interval_known_values(wins, total, lower, upper) -> None:
    interval = wilson_interval(wins, total)
    assert interval is not None
    assert interval.lower == pytest.approx(lower, abs=1e-7)
    assert interval.upper == pytest.approx(upper, abs=1e-7)


def test_wilson_interval_empty_and_invalid_counts() -> None:
    assert wilson_interval(0, 0) is None
    with pytest.raises(ValueError, match="must not exceed"):
        wilson_interval(2, 1)
    with pytest.raises(ValueError, match="integer"):
        wilson_interval(True, 1)


def test_nosl_summary_pass_rate_streak_reasons_and_ascension_slices() -> None:
    outcomes = [
        _outcome("e1", "s1", True, ascension=1),
        _outcome("e2", "s2", True, ascension=2, sequence_index=1),
        _outcome(
            "e3", "s3", False, ascension=1, reason="timeout", sequence_index=2
        ),
        _outcome("e4", "s4", True, ascension=1, sequence_index=3),
        _outcome("e5", "s5", True, ascension=2, sequence_index=4),
        _outcome("e6", "s6", True, ascension=1, sequence_index=5),
    ]

    summary = summarize_nosl(outcomes)
    assert summary.overall.episodes == 6
    assert summary.overall.wins == 5
    assert summary.overall.pass_rate == pytest.approx(5 / 6)
    assert summary.overall.longest_win_streak == 3
    assert summary.overall.terminal_reason_counts == {"timeout": 1, "victory": 5}
    assert summary.per_ascension[1].pass_rate == pytest.approx(3 / 4)
    assert summary.per_ascension[1].longest_win_streak == 2
    assert summary.per_ascension[2].pass_rate == 1.0
    json.dumps(summary.as_dict(), allow_nan=False)


def test_nosl_empty_is_not_misreported_as_zero_percent() -> None:
    summary = summarize_nosl([])
    assert summary.overall.episodes == 0
    assert summary.overall.pass_rate is None
    assert summary.overall.wilson_95 is None
    assert summary.overall.longest_win_streak == 0
    assert summary.as_dict()["per_ascension"] == {}
    assert summary.as_dict()["spec_fingerprint"] is None


def test_nosl_spec_requires_exact_manifest_and_carries_fingerprint() -> None:
    spec = _spec()
    outcomes = [
        _outcome(
            "e1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
        ),
        _outcome(
            "e0",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
        ),
    ]

    summary = summarize_nosl(outcomes, spec=spec)
    assert summary.overall.episodes == 2
    assert summary.overall.longest_win_streak == 1
    assert summary.spec_fingerprint == spec.fingerprint()
    assert summary.as_dict()["spec_fingerprint"] == spec.fingerprint()

    with pytest.raises(ValueError, match="missing cases.*case-a1"):
        summarize_nosl(outcomes[1:], spec=spec)
    with pytest.raises(ValueError, match="extra cases.*unexpected"):
        summarize_nosl(
            outcomes
            + [
                _outcome(
                    "extra",
                    "extra-seed",
                    False,
                    case_id="unexpected",
                    ascension=0,
                    sequence_index=2,
                )
            ],
            spec=spec,
        )


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"reload_count": None}, "reload_count=0"),
        ({"reload_count": 1}, "reload_count=0"),
        ({"trajectory_complete": None}, "trajectory_complete=true"),
        ({"trajectory_complete": False}, "trajectory_complete=true"),
        ({"trajectory_hash": None}, "non-empty trajectory_hash"),
    ],
)
def test_nosl_spec_requires_reload_and_complete_trajectory_proof(
    changes, message
) -> None:
    spec = _spec()
    first_values = {
        "episode_id": "e0",
        "seed": "seed-a0",
        "won": False,
        "case_id": "case-a0",
        "ascension": 0,
        "sequence_index": 0,
    }
    first_values.update(changes)
    outcomes = [
        _outcome(**first_values),
        _outcome(
            "e1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
        ),
    ]
    with pytest.raises(ValueError, match=message):
        summarize_nosl(outcomes, spec=spec)


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"seed": "wrong"}, "seed=.*expected"),
        ({"ascension": 9}, "ascension=9.*expected"),
        ({"sequence_index": 9}, "sequence_index=9.*expected"),
    ],
)
def test_nosl_spec_rejects_case_identity_drift(changes, message) -> None:
    spec = _spec()
    values = {
        "episode_id": "e0",
        "case_id": "case-a0",
        "seed": "seed-a0",
        "ascension": 0,
        "won": False,
        "terminal_reason": "death",
        "sequence_index": 0,
        "reload_count": 0,
        "trajectory_complete": True,
        "trajectory_hash": "sha256:" + "e" * 64,
        "steps": 10,
        "elapsed_s": 1.0,
    }
    values.update(changes)
    first = EpisodeOutcome(**values)
    second = _outcome(
        "e1",
        "seed-a1",
        True,
        case_id="case-a1",
        ascension=1,
        sequence_index=1,
    )
    with pytest.raises(ValueError, match=message):
        summarize_nosl([first, second], spec=spec)


def test_nosl_rejects_sl_spec_mode() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    with pytest.raises(ValueError, match="expected 'nosl'"):
        summarize_nosl([], spec=spec)


def test_nosl_rejects_retries_duplicate_cases_and_episode_ids() -> None:
    with pytest.raises(ValueError, match="attempt=1"):
        summarize_nosl([_outcome("e1", "s1", False, attempt=2)])
    with pytest.raises(ValueError, match="duplicate case_id"):
        summarize_nosl(
            [
                _outcome("e1", "s1", False, case_id="case"),
                _outcome(
                    "e2", "s1", True, case_id="case", sequence_index=1
                ),
            ]
        )
    with pytest.raises(ValueError, match="duplicate episode_id"):
        summarize_nosl(
            [
                _outcome("e1", "s1", False),
                _outcome("e1", "s2", True, sequence_index=1),
            ]
        )


def test_nosl_streak_uses_sequence_index_not_worker_completion_order() -> None:
    outcomes = [
        _outcome("third", "s3", False, sequence_index=2),
        _outcome("first", "s1", True, sequence_index=0),
        _outcome("fourth", "s4", True, sequence_index=3),
        _outcome("second", "s2", True, sequence_index=1),
    ]
    summary = summarize_nosl(outcomes)
    assert summary.overall.longest_win_streak == 2

    with pytest.raises(ValueError, match="contiguous from 0"):
        summarize_nosl(
            [
                _outcome("first", "s1", True, sequence_index=0),
                _outcome("third", "s3", False, sequence_index=2),
            ]
        )


def test_sl_at_b_groups_attempts_by_seed_and_reports_solve_rates() -> None:
    outcomes = [
        _outcome("a1", "alpha", False, case_id="alpha", attempt=1),
        _outcome("a2", "alpha", True, case_id="alpha", attempt=2),
        _outcome("b1", "beta", False, case_id="beta", attempt=1),
        _outcome("b2", "beta", False, case_id="beta", attempt=2),
        _outcome("b3", "beta", False, case_id="beta", attempt=3),
        _outcome("c1", "gamma", True, case_id="gamma", attempt=1),
    ]

    summary = summarize_sl(outcomes, budgets=[1, 2, 3])
    assert summary.overall[1].eligible_seed_groups == 3
    assert summary.overall[1].solved_seed_groups == 1
    assert summary.overall[1].solve_rate == pytest.approx(1 / 3)
    assert summary.overall[2].solved_seed_groups == 2
    assert summary.overall[2].unsolved_seed_groups == 1
    assert summary.overall[2].solve_rate == pytest.approx(2 / 3)
    assert summary.overall[3].solved_seed_groups == 2
    assert summary.overall[3].incomplete_seed_groups == 0
    assert summary.per_ascension[1][2].solve_rate == pytest.approx(2 / 3)
    json.dumps(summary.as_dict(), allow_nan=False)

    reversed_summary = summarize_sl(reversed(outcomes), budgets=[1, 2, 3])
    assert reversed_summary.as_dict() == summary.as_dict()


def test_sl_spec_requires_exact_manifest_and_declared_budgets() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    outcomes = [
        _outcome(
            "a0-1",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=1,
        ),
        _outcome(
            "a0-2",
            "seed-a0",
            True,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=2,
        ),
        _outcome(
            "a1-1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
            attempt=1,
        ),
    ]

    summary = summarize_sl(outcomes, spec=spec)
    assert summary.budgets == (1, 2)
    assert summary.overall[2].solved_seed_groups == 2
    assert summary.spec_fingerprint == spec.fingerprint()
    assert summary.as_dict()["spec_fingerprint"] == spec.fingerprint()

    with pytest.raises(ValueError, match="budgets must exactly match"):
        summarize_sl(outcomes, budgets=(1,), spec=spec)
    with pytest.raises(ValueError, match="missing cases.*case-a1"):
        summarize_sl(outcomes[:2], spec=spec)
    with pytest.raises(ValueError, match="extra cases.*unexpected"):
        summarize_sl(
            outcomes
            + [
                _outcome(
                    "extra",
                    "extra-seed",
                    False,
                    case_id="unexpected",
                    ascension=0,
                    sequence_index=2,
                )
            ],
            spec=spec,
        )


def test_sl_spec_enforces_maximum_attempt_budget() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    outcomes = [
        _outcome(
            f"a0-{attempt}",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=attempt,
        )
        for attempt in (1, 2, 3)
    ]
    outcomes.append(
        _outcome(
            "a1-1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
        )
    )
    with pytest.raises(ValueError, match="exceed.*maximum budget 2"):
        summarize_sl(outcomes, spec=spec)

    # Without a locked specification, exploratory summaries retain their
    # historical permissive behavior even when more attempts were recorded.
    summary = summarize_sl(outcomes, budgets=(1, 2))
    assert summary.overall[2].seed_groups == 2


def test_sl_spec_rejects_attempts_after_victory_but_exploration_allows_them() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    outcomes = [
        _outcome(
            "a0-1",
            "seed-a0",
            True,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=1,
        ),
        _outcome(
            "a0-2",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=2,
        ),
        _outcome(
            "a1-1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
        ),
    ]
    with pytest.raises(ValueError, match="continue after victory at attempt=1"):
        summarize_sl(outcomes, spec=spec)

    summary = summarize_sl(outcomes, budgets=(1, 2))
    assert summary.overall[2].solved_seed_groups == 2


def test_sl_spec_rejects_an_unsolved_case_that_did_not_exhaust_budget() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    outcomes = [
        _outcome(
            "a0-1",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=1,
        ),
        _outcome(
            "a1-1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
            attempt=1,
        ),
    ]

    with pytest.raises(ValueError, match="unsolved SL case.*incomplete"):
        summarize_sl(outcomes, spec=spec)


def test_sl_spec_requires_complete_hashed_trajectory_for_every_attempt() -> None:
    spec = _spec(save_load_mode="sl", sl_budgets=(1, 2))
    outcomes = [
        _outcome(
            "a0-1",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=1,
        ),
        _outcome(
            "a0-2",
            "seed-a0",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
            attempt=2,
            trajectory_hash=None,
        ),
        _outcome(
            "a1-1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
            attempt=1,
        ),
    ]

    with pytest.raises(ValueError, match="SL outcome.*trajectory_hash"):
        summarize_sl(outcomes, spec=spec)


def test_sl_spec_rejects_case_identity_drift_and_nosl_mode() -> None:
    sl_spec = _spec(save_load_mode="sl", sl_budgets=(1,))
    mismatched = [
        _outcome(
            "a0",
            "wrong-seed",
            False,
            case_id="case-a0",
            ascension=0,
            sequence_index=0,
        ),
        _outcome(
            "a1",
            "seed-a1",
            True,
            case_id="case-a1",
            ascension=1,
            sequence_index=1,
        ),
    ]
    with pytest.raises(ValueError, match="seed=.*expected"):
        summarize_sl(mismatched, spec=sl_spec)

    with pytest.raises(ValueError, match="expected 'sl'"):
        summarize_sl([], spec=_spec())


def test_sl_at_b_does_not_treat_missing_attempts_as_failures() -> None:
    summary = summarize_sl(
        [
            _outcome("a1", "alpha", False, case_id="alpha", attempt=1),
            _outcome("b1", "beta", True, case_id="beta", attempt=1),
        ],
        budgets=[2],
    )
    at_two = summary.overall[2]
    assert at_two.seed_groups == 2
    assert at_two.eligible_seed_groups == 1
    assert at_two.solved_seed_groups == 1
    assert at_two.incomplete_seed_groups == 1
    assert at_two.solve_rate == 0.5


def test_sl_groups_same_seed_separately_by_ascension() -> None:
    summary = summarize_sl(
        [
            _outcome("a0", "same", False, case_id="a0", ascension=0),
            _outcome("a1", "same", True, case_id="a1", ascension=1),
        ],
        budgets=[1],
    )
    assert summary.overall[1].seed_groups == 2
    assert summary.per_ascension[0][1].solve_rate == 0.0
    assert summary.per_ascension[1][1].solve_rate == 1.0


def test_sl_rejects_duplicate_or_gapped_attempt_numbers() -> None:
    with pytest.raises(ValueError, match="unique and contiguous"):
        summarize_sl(
            [
                _outcome("e1", "seed", False, case_id="case", attempt=1),
                _outcome("e2", "seed", False, case_id="case", attempt=1),
            ],
            budgets=[1],
        )
    with pytest.raises(ValueError, match="unique and contiguous"):
        summarize_sl(
            [
                _outcome("e1", "seed", False, case_id="case", attempt=1),
                _outcome("e3", "seed", False, case_id="case", attempt=3),
            ],
            budgets=[1],
        )


def test_action_coverage_aggregates_audits_and_optional_chosen_action_checks() -> None:
    summary = summarize_action_coverage(
        [
            {
                "state_type": "combat",
                "supported": True,
                "complete": True,
                "action_count": 4,
                "candidate_action_count": 3,
                "issues": [],
                "chosen_action_legal": True,
            },
            {
                "action_space_audit": {
                    "state_type": "combat",
                    "supported": True,
                    "complete": False,
                    "action_count": 0,
                    "candidate_action_count": 0,
                    "issues": ["no_visible_action"],
                },
                "chosen_action_legal": False,
            },
            {
                "state_type": "future_screen",
                "supported": False,
                "complete": False,
                "action_count": 0,
                "candidate_action_count": 0,
                "issues": ["unsupported_state_type:future_screen"],
            },
        ]
    )

    overall = summary.overall
    assert overall.snapshots == 3
    assert overall.supported_snapshots == 2
    assert overall.complete_snapshots == 1
    assert overall.coverage_rate == pytest.approx(1 / 3)
    assert overall.candidate_action_rate == pytest.approx(3 / 4)
    assert overall.chosen_action_checks == 2
    assert overall.chosen_actions_covered == 1
    assert overall.chosen_action_coverage_rate == 0.5
    assert summary.per_state_type["combat"].snapshots == 2
    assert overall.issue_counts == {
        "no_visible_action": 1,
        "unsupported_state_type:future_screen": 1,
    }
    json.dumps(summary.as_dict(), allow_nan=False)


def test_action_coverage_empty_summary_uses_null_rates() -> None:
    summary = summarize_action_coverage([])
    assert summary.overall.snapshots == 0
    assert summary.overall.supported_rate is None
    assert summary.overall.coverage_rate is None
    assert summary.as_dict()["per_state_type"] == {}


def test_action_coverage_reports_missing_audits_and_ignores_untrusted_matches() -> None:
    summary = summarize_action_coverage(
        [
            None,
            {"observation": {"state_type": "shop"}},
            {
                "action_space_audit": {
                    "state_type": "combat",
                    "supported": True,
                    "complete": True,
                    "action_count": 1,
                    "candidate_action_count": 1,
                    "issues": [],
                },
                "legal_actions": [{"action": "end_turn"}],
                "chosen_action_legal": False,
                "state_before_estimated": True,
            },
        ]
    )
    overall = summary.overall
    assert overall.snapshots == 3
    assert overall.audited_snapshots == 1
    assert overall.missing_audit_snapshots == 2
    assert overall.audit_rate == pytest.approx(1 / 3)
    assert overall.coverage_rate == pytest.approx(1 / 3)
    assert overall.complete_audited_rate == 1.0
    assert overall.chosen_action_checks == 0
    assert overall.issue_counts == {"missing_action_space_audit": 2}


@pytest.mark.parametrize(
    "audit",
    [
        {"supported": "yes", "complete": False, "action_count": 0},
        {"supported": False, "complete": True, "action_count": 0},
        {
            "supported": True,
            "complete": True,
            "action_count": 1,
            "candidate_action_count": 2,
        },
        {
            "supported": True,
            "complete": True,
            "action_count": 1,
            "issues": "not-a-list",
        },
        {
            "supported": True,
            "complete": True,
            "action_count": 1,
            "issues": ["contradiction"],
        },
        {
            "supported": True,
            "complete": True,
            "action_count": 2,
            "issues": [],
            "legal_actions": [{"action": "end_turn"}],
        },
    ],
)
def test_action_coverage_rejects_malformed_audits(audit) -> None:
    with pytest.raises(ValueError):
        summarize_action_coverage([audit])
