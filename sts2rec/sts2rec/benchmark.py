"""Pure, auditable statistics for STS2 policy benchmarks.

This module deliberately contains no environment or process runner.  It turns
already-recorded episode outcomes and action-space audits into JSON-serializable
benchmark summaries.  Keeping this layer pure makes the reported numbers easy
to reproduce without launching the game.
"""

from __future__ import annotations

import hashlib
import json
import math
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from types import MappingProxyType
from typing import Any, Iterable, Mapping, Sequence


_WILSON_95_Z = 1.959963984540054
_INFORMATION_MODES = frozenset({"limited", "omniscient"})
_SAVE_LOAD_MODES = frozenset({"nosl", "sl"})
_SHA256_PREFIX = "sha256:"


def _nonempty_text(value: object, name: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{name} must be a non-empty string")
    if value != value.strip():
        raise ValueError(f"{name} must not have leading or trailing whitespace")
    return value


def _integer(value: object, name: str, *, minimum: int = 0) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise ValueError(f"{name} must be an integer >= {minimum}")
    return value


def _positive_finite_number(value: object, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be a finite number > 0")
    normalized = float(value)
    if not math.isfinite(normalized) or normalized <= 0:
        raise ValueError(f"{name} must be a finite number > 0")
    return normalized


def _nonnegative_finite_number(value: object, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be a finite number >= 0")
    normalized = float(value)
    if not math.isfinite(normalized) or normalized < 0:
        raise ValueError(f"{name} must be a finite number >= 0")
    return normalized


def _sha256_text(value: object, name: str) -> str:
    text = _nonempty_text(value, name)
    digest = text.removeprefix(_SHA256_PREFIX)
    if not text.startswith(_SHA256_PREFIX) or len(digest) != 64 or any(
        character not in "0123456789abcdef" for character in digest
    ):
        raise ValueError(f"{name} must be sha256:<64 lowercase hex characters>")
    return text


def _ordered_unique_ints(
    values: Iterable[int], name: str, *, minimum: int
) -> tuple[int, ...]:
    if isinstance(values, (str, bytes)):
        raise ValueError(f"{name} must be an iterable of integers")
    try:
        normalized = tuple(_integer(value, name, minimum=minimum) for value in values)
    except TypeError as exc:
        raise ValueError(f"{name} must be an iterable of integers") from exc
    if not normalized:
        raise ValueError(f"{name} must not be empty")
    if tuple(sorted(set(normalized))) != normalized:
        raise ValueError(f"{name} must be strictly increasing and contain no duplicates")
    return normalized


def _freeze_json(value: Any, path: str = "metadata") -> Any:
    """Validate JSON data and make containers immutable.

    Benchmark fingerprints must not change because a caller mutates a metadata
    dictionary after constructing the specification.
    """
    if value is None or isinstance(value, (str, bool, int)):
        return value
    if isinstance(value, float):
        if not math.isfinite(value):
            raise ValueError(f"{path} contains a non-finite float")
        return value
    if isinstance(value, Mapping):
        frozen: dict[str, Any] = {}
        for key, item in value.items():
            if not isinstance(key, str):
                raise ValueError(f"{path} contains a non-string object key")
            frozen[key] = _freeze_json(item, f"{path}.{key}")
        return MappingProxyType(frozen)
    if isinstance(value, (list, tuple)):
        return tuple(_freeze_json(item, f"{path}[]") for item in value)
    raise ValueError(f"{path} contains a non-JSON value: {type(value).__name__}")


def _thaw_json(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {key: _thaw_json(item) for key, item in value.items()}
    if isinstance(value, tuple):
        return [_thaw_json(item) for item in value]
    return value


@dataclass(frozen=True)
class BenchmarkCase:
    """One immutable, predeclared case in a benchmark seed manifest."""

    case_id: str
    seed: str
    ascension: int
    sequence_index: int

    def __post_init__(self) -> None:
        _nonempty_text(self.case_id, "case_id")
        _nonempty_text(self.seed, "seed")
        _integer(self.ascension, "ascension", minimum=0)
        _integer(self.sequence_index, "sequence_index", minimum=0)

    def as_dict(self) -> dict[str, Any]:
        return {
            "case_id": self.case_id,
            "seed": self.seed,
            "ascension": self.ascension,
            "sequence_index": self.sequence_index,
        }


@dataclass(frozen=True)
class BenchmarkSpec:
    """Version-locked contract defining exactly which cases are measured."""

    name: str
    game_version: str
    character: str
    ascensions: tuple[int, ...]
    policy_id: str
    seed_suite_id: str
    cases: tuple[BenchmarkCase, ...]
    game_build: str
    environment_id: str
    victory_condition: str
    timeout_s: float
    max_steps: int
    mod_set_id: str
    unlock_state_id: str
    controller_version: str
    information_mode: str = "limited"
    save_load_mode: str = "nosl"
    sl_budgets: tuple[int, ...] = ()
    observation_schema: str = "v1"
    metadata: Mapping[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        for field_name in (
            "name",
            "game_version",
            "character",
            "policy_id",
            "seed_suite_id",
            "observation_schema",
            "game_build",
            "environment_id",
            "victory_condition",
            "mod_set_id",
            "unlock_state_id",
            "controller_version",
        ):
            _nonempty_text(getattr(self, field_name), field_name)

        object.__setattr__(
            self, "timeout_s", _positive_finite_number(self.timeout_s, "timeout_s")
        )
        _integer(self.max_steps, "max_steps", minimum=1)

        ascensions = _ordered_unique_ints(self.ascensions, "ascensions", minimum=0)
        object.__setattr__(self, "ascensions", ascensions)

        if isinstance(self.cases, (str, bytes)):
            raise ValueError("cases must be a non-empty iterable of BenchmarkCase objects")
        try:
            cases = tuple(self.cases)
        except TypeError as exc:
            raise ValueError(
                "cases must be a non-empty iterable of BenchmarkCase objects"
            ) from exc
        if not cases:
            raise ValueError("cases must not be empty")
        if not all(isinstance(case, BenchmarkCase) for case in cases):
            raise ValueError("cases must contain only BenchmarkCase objects")

        case_ids = [case.case_id for case in cases]
        seeds = [case.seed for case in cases]
        sequence_indices = [case.sequence_index for case in cases]
        if len(set(case_ids)) != len(case_ids):
            raise ValueError("cases must have unique case_id values")
        if len(set(seeds)) != len(seeds):
            raise ValueError("cases must have unique seed values")
        if len(set(sequence_indices)) != len(sequence_indices):
            raise ValueError("cases must have unique sequence_index values")
        if set(sequence_indices) != set(range(len(cases))):
            raise ValueError("case sequence_index values must be contiguous from 0")

        cases = tuple(sorted(cases, key=lambda case: case.sequence_index))
        case_ascensions = tuple(sorted({case.ascension for case in cases}))
        if case_ascensions != ascensions:
            raise ValueError(
                "ascensions must exactly match the ascensions represented by cases"
            )
        object.__setattr__(self, "cases", cases)

        if self.information_mode not in _INFORMATION_MODES:
            allowed = ", ".join(sorted(_INFORMATION_MODES))
            raise ValueError(f"information_mode must be one of: {allowed}")
        if self.save_load_mode not in _SAVE_LOAD_MODES:
            allowed = ", ".join(sorted(_SAVE_LOAD_MODES))
            raise ValueError(f"save_load_mode must be one of: {allowed}")

        if self.sl_budgets:
            budgets = _ordered_unique_ints(self.sl_budgets, "sl_budgets", minimum=1)
        else:
            budgets = ()
        if self.save_load_mode == "nosl" and budgets:
            raise ValueError("sl_budgets must be empty for a nosl benchmark")
        if self.save_load_mode == "sl" and not budgets:
            raise ValueError("an sl benchmark must declare at least one sl budget")
        object.__setattr__(self, "sl_budgets", budgets)

        if not isinstance(self.metadata, Mapping):
            raise ValueError("metadata must be a JSON object")
        object.__setattr__(self, "metadata", _freeze_json(self.metadata))

    def as_dict(self) -> dict[str, Any]:
        """Return a JSON-serializable representation of the complete contract."""
        return {
            "name": self.name,
            "game_version": self.game_version,
            "game_build": self.game_build,
            "character": self.character,
            "ascensions": list(self.ascensions),
            "policy_id": self.policy_id,
            "seed_suite_id": self.seed_suite_id,
            "cases": [case.as_dict() for case in self.cases],
            "victory_condition": self.victory_condition,
            "information_mode": self.information_mode,
            "save_load_mode": self.save_load_mode,
            "sl_budgets": list(self.sl_budgets),
            "observation_schema": self.observation_schema,
            "environment_id": self.environment_id,
            "timeout_s": self.timeout_s,
            "max_steps": self.max_steps,
            "mod_set_id": self.mod_set_id,
            "unlock_state_id": self.unlock_state_id,
            "controller_version": self.controller_version,
            "metadata": _thaw_json(self.metadata),
        }

    def canonical_json(self) -> str:
        """Return stable UTF-8 JSON used to identify the benchmark contract."""
        return json.dumps(
            self.as_dict(),
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )

    def fingerprint(self) -> str:
        """Return the SHA-256 fingerprint of :meth:`canonical_json`."""
        return hashlib.sha256(self.canonical_json().encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class EpisodeOutcome:
    """Terminal result of one NoSL run or one attempt in an SL seed group."""

    episode_id: str
    case_id: str
    seed: str
    ascension: int
    won: bool
    terminal_reason: str
    sequence_index: int
    attempt: int = 1
    reload_count: int | None = None
    trajectory_complete: bool | None = None
    trajectory_hash: str | None = None
    steps: int | None = None
    elapsed_s: float | None = None

    def __post_init__(self) -> None:
        _nonempty_text(self.episode_id, "episode_id")
        _nonempty_text(self.case_id, "case_id")
        _nonempty_text(self.seed, "seed")
        _integer(self.ascension, "ascension", minimum=0)
        if not isinstance(self.won, bool):
            raise ValueError("won must be a bool")
        _nonempty_text(self.terminal_reason, "terminal_reason")
        if self.won != (self.terminal_reason == "victory"):
            raise ValueError("won must be true if and only if terminal_reason is victory")
        _integer(self.sequence_index, "sequence_index", minimum=0)
        _integer(self.attempt, "attempt", minimum=1)
        if self.reload_count is not None:
            _integer(self.reload_count, "reload_count", minimum=0)
        if self.trajectory_complete is not None and not isinstance(
            self.trajectory_complete, bool
        ):
            raise ValueError("trajectory_complete must be a bool or None")
        if self.trajectory_hash is not None:
            _sha256_text(self.trajectory_hash, "trajectory_hash")
        if self.steps is not None:
            _integer(self.steps, "steps", minimum=0)
        if self.elapsed_s is not None:
            object.__setattr__(
                self,
                "elapsed_s",
                _nonnegative_finite_number(self.elapsed_s, "elapsed_s"),
            )

    @property
    def seed_group(self) -> tuple[str, int, str]:
        """SL attempts are grouped by declared case, ascension, and seed."""
        return (self.case_id, self.ascension, self.seed)

    def as_dict(self) -> dict[str, Any]:
        return {
            "episode_id": self.episode_id,
            "case_id": self.case_id,
            "seed": self.seed,
            "ascension": self.ascension,
            "won": self.won,
            "terminal_reason": self.terminal_reason,
            "sequence_index": self.sequence_index,
            "attempt": self.attempt,
            "reload_count": self.reload_count,
            "trajectory_complete": self.trajectory_complete,
            "trajectory_hash": self.trajectory_hash,
            "steps": self.steps,
            "elapsed_s": self.elapsed_s,
        }


@dataclass(frozen=True)
class WilsonInterval:
    """Two-sided 95% Wilson score interval for a binomial proportion."""

    lower: float
    upper: float
    confidence: float = 0.95

    def as_dict(self) -> dict[str, float]:
        return {
            "confidence": self.confidence,
            "lower": self.lower,
            "upper": self.upper,
        }


def wilson_interval(successes: int, total: int) -> WilsonInterval | None:
    """Return the 95% Wilson interval, or ``None`` when no trials exist."""
    successes = _integer(successes, "successes", minimum=0)
    total = _integer(total, "total", minimum=0)
    if successes > total:
        raise ValueError("successes must not exceed total")
    if total == 0:
        return None

    probability = successes / total
    z_squared = _WILSON_95_Z * _WILSON_95_Z
    denominator = 1.0 + z_squared / total
    center = (probability + z_squared / (2.0 * total)) / denominator
    radius = (
        _WILSON_95_Z
        * math.sqrt(
            probability * (1.0 - probability) / total
            + z_squared / (4.0 * total * total)
        )
        / denominator
    )
    return WilsonInterval(
        lower=max(0.0, center - radius),
        upper=min(1.0, center + radius),
    )


@dataclass(frozen=True)
class OutcomeSummary:
    episodes: int
    wins: int
    losses: int
    pass_rate: float | None
    wilson_95: WilsonInterval | None
    longest_win_streak: int
    terminal_reason_counts: Mapping[str, int]

    def as_dict(self) -> dict[str, Any]:
        return {
            "episodes": self.episodes,
            "wins": self.wins,
            "losses": self.losses,
            "pass_rate": self.pass_rate,
            "wilson_95": None if self.wilson_95 is None else self.wilson_95.as_dict(),
            "longest_win_streak": self.longest_win_streak,
            "terminal_reason_counts": dict(sorted(self.terminal_reason_counts.items())),
        }


@dataclass(frozen=True)
class NoSLSummary:
    overall: OutcomeSummary
    per_ascension: Mapping[int, OutcomeSummary]
    spec_fingerprint: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "mode": "nosl",
            "spec_fingerprint": self.spec_fingerprint,
            "overall": self.overall.as_dict(),
            "per_ascension": {
                str(ascension): summary.as_dict()
                for ascension, summary in sorted(self.per_ascension.items())
            },
        }


def _longest_win_streak(outcomes: Sequence[EpisodeOutcome]) -> int:
    longest = 0
    current = 0
    for outcome in outcomes:
        if outcome.won:
            current += 1
            longest = max(longest, current)
        else:
            current = 0
    return longest


def _summarize_outcomes(outcomes: Sequence[EpisodeOutcome]) -> OutcomeSummary:
    episodes = len(outcomes)
    wins = sum(outcome.won for outcome in outcomes)
    reasons = Counter(outcome.terminal_reason for outcome in outcomes)
    return OutcomeSummary(
        episodes=episodes,
        wins=wins,
        losses=episodes - wins,
        pass_rate=None if episodes == 0 else wins / episodes,
        wilson_95=wilson_interval(wins, episodes),
        longest_win_streak=_longest_win_streak(outcomes),
        terminal_reason_counts=MappingProxyType(dict(sorted(reasons.items()))),
    )


def _validate_unique_episode_ids(outcomes: Sequence[EpisodeOutcome]) -> None:
    seen: set[str] = set()
    for outcome in outcomes:
        if outcome.episode_id in seen:
            raise ValueError(f"duplicate episode_id: {outcome.episode_id}")
        seen.add(outcome.episode_id)


def _validate_spec_mode(spec: BenchmarkSpec, expected_mode: str) -> None:
    if not isinstance(spec, BenchmarkSpec):
        raise ValueError("spec must be a BenchmarkSpec")
    if spec.save_load_mode != expected_mode:
        raise ValueError(
            f"benchmark spec mode is {spec.save_load_mode!r}; expected {expected_mode!r}"
        )


def _validate_manifest_outcomes(
    outcomes: Sequence[EpisodeOutcome], spec: BenchmarkSpec
) -> None:
    expected = {case.case_id: case for case in spec.cases}
    actual_case_ids = {outcome.case_id for outcome in outcomes}
    expected_case_ids = set(expected)
    missing = sorted(expected_case_ids - actual_case_ids)
    extra = sorted(actual_case_ids - expected_case_ids)
    if missing or extra:
        details: list[str] = []
        if missing:
            details.append(f"missing cases: {missing}")
        if extra:
            details.append(f"extra cases: {extra}")
        raise ValueError("outcomes do not exactly match benchmark cases; " + "; ".join(details))

    for outcome in outcomes:
        case = expected[outcome.case_id]
        mismatches: list[str] = []
        if outcome.seed != case.seed:
            mismatches.append(f"seed={outcome.seed!r} (expected {case.seed!r})")
        if outcome.ascension != case.ascension:
            mismatches.append(
                f"ascension={outcome.ascension} (expected {case.ascension})"
            )
        if outcome.sequence_index != case.sequence_index:
            mismatches.append(
                "sequence_index="
                f"{outcome.sequence_index} (expected {case.sequence_index})"
            )
        if mismatches:
            raise ValueError(
                f"outcome for case {outcome.case_id!r} does not match benchmark "
                f"manifest: {', '.join(mismatches)}"
            )


def _validate_nosl_trajectory_proof(outcomes: Sequence[EpisodeOutcome]) -> None:
    """Require auditable evidence that every declared NoSL run is complete."""
    for outcome in outcomes:
        if outcome.reload_count != 0:
            raise ValueError(
                "spec-backed NoSL outcome "
                f"{outcome.episode_id!r} must explicitly declare reload_count=0"
            )
        if outcome.trajectory_complete is not True:
            raise ValueError(
                "spec-backed NoSL outcome "
                f"{outcome.episode_id!r} must explicitly declare trajectory_complete=true"
            )
        if outcome.trajectory_hash is None:
            raise ValueError(
                "spec-backed NoSL outcome "
                f"{outcome.episode_id!r} must declare a non-empty trajectory_hash"
            )


def _validate_sl_trajectory_proof(outcomes: Sequence[EpisodeOutcome]) -> None:
    """Require a complete, addressable trajectory for every formal SL attempt."""

    for outcome in outcomes:
        if outcome.trajectory_complete is not True:
            raise ValueError(
                "spec-backed SL outcome "
                f"{outcome.episode_id!r} must explicitly declare "
                "trajectory_complete=true"
            )
        if outcome.trajectory_hash is None:
            raise ValueError(
                "spec-backed SL outcome "
                f"{outcome.episode_id!r} must declare a non-empty trajectory_hash"
            )


def _validate_formal_resources(
    outcomes: Sequence[EpisodeOutcome], spec: BenchmarkSpec
) -> None:
    hashes: set[str] = set()
    for outcome in outcomes:
        if outcome.steps is None:
            raise ValueError(
                f"spec-backed outcome {outcome.episode_id!r} must declare steps"
            )
        if outcome.steps > spec.max_steps:
            raise ValueError(
                f"outcome {outcome.episode_id!r} exceeded max_steps="
                f"{spec.max_steps}: {outcome.steps}"
            )
        if outcome.elapsed_s is None:
            raise ValueError(
                f"spec-backed outcome {outcome.episode_id!r} must declare elapsed_s"
            )
        if outcome.elapsed_s > spec.timeout_s:
            raise ValueError(
                f"outcome {outcome.episode_id!r} exceeded timeout_s="
                f"{spec.timeout_s}: {outcome.elapsed_s}"
            )
        assert outcome.trajectory_hash is not None
        if outcome.trajectory_hash in hashes:
            raise ValueError(
                f"duplicate trajectory_hash in formal outcomes: "
                f"{outcome.trajectory_hash}"
            )
        hashes.add(outcome.trajectory_hash)


def summarize_nosl(
    outcomes: Iterable[EpisodeOutcome], *, spec: BenchmarkSpec | None = None
) -> NoSLSummary:
    """Summarize explicitly ordered, single-attempt NoSL episodes.

    ``sequence_index`` is the benchmark's predeclared order.  Input iteration
    order is intentionally ignored because concurrent workers may finish out of
    order.  A case may occur only once; repeated attempts belong in an SL summary
    instead of inflating a NoSL denominator.
    """
    recorded = tuple(outcomes)
    if not all(isinstance(outcome, EpisodeOutcome) for outcome in recorded):
        raise ValueError("outcomes must contain only EpisodeOutcome objects")
    if spec is not None:
        _validate_spec_mode(spec, "nosl")
    _validate_unique_episode_ids(recorded)

    seen_cases: set[str] = set()
    seen_sequence_indices: set[int] = set()
    for outcome in recorded:
        if outcome.attempt != 1:
            raise ValueError("NoSL outcomes must all have attempt=1")
        if outcome.case_id in seen_cases:
            raise ValueError(f"duplicate case_id in NoSL outcomes: {outcome.case_id}")
        if outcome.sequence_index in seen_sequence_indices:
            raise ValueError(
                f"duplicate sequence_index in NoSL outcomes: {outcome.sequence_index}"
            )
        seen_cases.add(outcome.case_id)
        seen_sequence_indices.add(outcome.sequence_index)

    if spec is not None:
        _validate_manifest_outcomes(recorded, spec)
        _validate_nosl_trajectory_proof(recorded)
        _validate_formal_resources(recorded, spec)
    if seen_sequence_indices and seen_sequence_indices != set(range(len(recorded))):
        raise ValueError("NoSL sequence_index values must be contiguous from 0")
    recorded = tuple(sorted(recorded, key=lambda outcome: outcome.sequence_index))

    ascensions: dict[int, list[EpisodeOutcome]] = defaultdict(list)
    for outcome in recorded:
        ascensions[outcome.ascension].append(outcome)
    return NoSLSummary(
        overall=_summarize_outcomes(recorded),
        per_ascension=MappingProxyType(
            {
                ascension: _summarize_outcomes(tuple(items))
                for ascension, items in sorted(ascensions.items())
            }
        ),
        spec_fingerprint=None if spec is None else spec.fingerprint(),
    )


@dataclass(frozen=True)
class SLBudgetSummary:
    budget: int
    seed_groups: int
    eligible_seed_groups: int
    solved_seed_groups: int
    unsolved_seed_groups: int
    incomplete_seed_groups: int
    solve_rate: float | None
    wilson_95: WilsonInterval | None

    def as_dict(self) -> dict[str, Any]:
        return {
            "budget": self.budget,
            "seed_groups": self.seed_groups,
            "eligible_seed_groups": self.eligible_seed_groups,
            "solved_seed_groups": self.solved_seed_groups,
            "unsolved_seed_groups": self.unsolved_seed_groups,
            "incomplete_seed_groups": self.incomplete_seed_groups,
            "solve_rate": self.solve_rate,
            "wilson_95": None if self.wilson_95 is None else self.wilson_95.as_dict(),
        }


@dataclass(frozen=True)
class SLSummary:
    budgets: tuple[int, ...]
    overall: Mapping[int, SLBudgetSummary]
    per_ascension: Mapping[int, Mapping[int, SLBudgetSummary]]
    spec_fingerprint: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "mode": "sl",
            "spec_fingerprint": self.spec_fingerprint,
            "budgets": list(self.budgets),
            "overall": {
                str(budget): summary.as_dict()
                for budget, summary in sorted(self.overall.items())
            },
            "per_ascension": {
                str(ascension): {
                    str(budget): summary.as_dict()
                    for budget, summary in sorted(summaries.items())
                }
                for ascension, summaries in sorted(self.per_ascension.items())
            },
        }


def _group_sl_attempts(
    outcomes: Sequence[EpisodeOutcome],
) -> dict[tuple[str, int, str], tuple[EpisodeOutcome, ...]]:
    grouped: dict[tuple[str, int, str], list[EpisodeOutcome]] = defaultdict(list)
    for outcome in outcomes:
        grouped[outcome.seed_group].append(outcome)

    case_identity: dict[str, tuple[int, str, int]] = {}
    for outcome in outcomes:
        identity = (outcome.ascension, outcome.seed, outcome.sequence_index)
        existing = case_identity.setdefault(outcome.case_id, identity)
        if existing != identity:
            raise ValueError(
                f"SL case_id {outcome.case_id} changed seed, ascension, or sequence_index"
            )

    normalized: dict[tuple[str, int, str], tuple[EpisodeOutcome, ...]] = {}
    for group, attempts in grouped.items():
        attempts.sort(key=lambda outcome: outcome.attempt)
        attempt_numbers = [outcome.attempt for outcome in attempts]
        expected = list(range(1, len(attempts) + 1))
        if attempt_numbers != expected:
            case_id, ascension, seed = group
            raise ValueError(
                f"SL attempts for {case_id}/A{ascension}/{seed} must be unique and contiguous "
                f"from 1; got {attempt_numbers}"
            )
        normalized[group] = tuple(attempts)
    return normalized


def _validate_spec_sl_attempts(
    groups: Mapping[tuple[str, int, str], Sequence[EpisodeOutcome]],
    spec: BenchmarkSpec,
) -> None:
    """Enforce the predeclared attempt cap and stop-on-victory rule."""
    maximum_attempt = max(spec.sl_budgets)
    for (case_id, ascension, seed), attempts in groups.items():
        final_attempt = attempts[-1].attempt
        if final_attempt > maximum_attempt:
            raise ValueError(
                f"SL attempts for {case_id}/A{ascension}/{seed} exceed the declared "
                f"maximum budget {maximum_attempt}; got attempt={final_attempt}"
            )
        first_win = next((outcome.attempt for outcome in attempts if outcome.won), None)
        if first_win is not None and final_attempt > first_win:
            raise ValueError(
                f"SL attempts for {case_id}/A{ascension}/{seed} continue after "
                f"victory at attempt={first_win}"
            )
        if first_win is None and final_attempt < maximum_attempt:
            raise ValueError(
                f"unsolved SL case {case_id}/A{ascension}/{seed} is incomplete: "
                f"declared maximum budget is {maximum_attempt}, got only "
                f"{final_attempt} attempt(s)"
            )


def _summarize_sl_groups(
    groups: Mapping[tuple[str, int, str], Sequence[EpisodeOutcome]], budget: int
) -> SLBudgetSummary:
    solved = 0
    unsolved = 0
    incomplete = 0
    for attempts in groups.values():
        first_win = next((outcome.attempt for outcome in attempts if outcome.won), None)
        if first_win is not None and first_win <= budget:
            solved += 1
        elif len(attempts) >= budget:
            unsolved += 1
        else:
            # An unsolved seed with fewer than B attempts cannot be included in
            # SL@B.  Reporting it separately prevents an overstated solve rate.
            incomplete += 1
    eligible = solved + unsolved
    # The public SL@B denominator is every declared case seen by this summary.
    # An incomplete case is a protocol failure, not a reason to shrink the
    # denominator and inflate the headline solve rate. ``eligible`` is retained
    # as a separate data-completeness diagnostic.
    denominator = len(groups)
    return SLBudgetSummary(
        budget=budget,
        seed_groups=len(groups),
        eligible_seed_groups=eligible,
        solved_seed_groups=solved,
        unsolved_seed_groups=unsolved,
        incomplete_seed_groups=incomplete,
        solve_rate=None if denominator == 0 else solved / denominator,
        wilson_95=wilson_interval(solved, denominator),
    )


def summarize_sl(
    outcomes: Iterable[EpisodeOutcome],
    *,
    budgets: Iterable[int] | None = None,
    spec: BenchmarkSpec | None = None,
) -> SLSummary:
    """Compute SL@B solve rates from attempts grouped by seed and ascension.

    A group that solved before ``B`` is complete even when no later attempts
    exist.  An unsolved group with fewer than ``B`` attempts is explicitly
    reported as incomplete and remains in the conservative SL@B denominator.
    """
    recorded = tuple(outcomes)
    if not all(isinstance(outcome, EpisodeOutcome) for outcome in recorded):
        raise ValueError("outcomes must contain only EpisodeOutcome objects")
    if spec is not None:
        _validate_spec_mode(spec, "sl")
    _validate_unique_episode_ids(recorded)
    if budgets is None:
        budgets = spec.sl_budgets if spec is not None else (1, 2, 4, 8)
    normalized_budgets = _ordered_unique_ints(budgets, "budgets", minimum=1)
    if spec is not None and normalized_budgets != spec.sl_budgets:
        raise ValueError(
            "SL summary budgets must exactly match the benchmark spec sl_budgets"
        )
    groups = _group_sl_attempts(recorded)
    if spec is not None:
        _validate_manifest_outcomes(recorded, spec)
        _validate_spec_sl_attempts(groups, spec)
        _validate_sl_trajectory_proof(recorded)
        _validate_formal_resources(recorded, spec)

    overall = {
        budget: _summarize_sl_groups(groups, budget)
        for budget in normalized_budgets
    }
    ascension_groups: dict[
        int, dict[tuple[str, int, str], tuple[EpisodeOutcome, ...]]
    ] = (
        defaultdict(dict)
    )
    for group, attempts in groups.items():
        ascension_groups[group[1]][group] = attempts
    per_ascension = {
        ascension: {
            budget: _summarize_sl_groups(grouped, budget)
            for budget in normalized_budgets
        }
        for ascension, grouped in sorted(ascension_groups.items())
    }
    return SLSummary(
        budgets=normalized_budgets,
        overall=MappingProxyType(overall),
        per_ascension=MappingProxyType(
            {
                ascension: MappingProxyType(summaries)
                for ascension, summaries in per_ascension.items()
            }
        ),
        spec_fingerprint=None if spec is None else spec.fingerprint(),
    )


@dataclass(frozen=True)
class ActionCoverageSlice:
    snapshots: int
    audited_snapshots: int
    missing_audit_snapshots: int
    supported_snapshots: int
    complete_snapshots: int
    actionable_snapshots: int
    total_actions: int
    candidate_actions: int
    chosen_action_checks: int
    chosen_actions_covered: int
    audit_rate: float | None
    supported_rate: float | None
    coverage_rate: float | None
    complete_audited_rate: float | None
    candidate_action_rate: float | None
    chosen_action_coverage_rate: float | None
    issue_counts: Mapping[str, int]

    def as_dict(self) -> dict[str, Any]:
        return {
            "snapshots": self.snapshots,
            "audited_snapshots": self.audited_snapshots,
            "missing_audit_snapshots": self.missing_audit_snapshots,
            "supported_snapshots": self.supported_snapshots,
            "complete_snapshots": self.complete_snapshots,
            "actionable_snapshots": self.actionable_snapshots,
            "total_actions": self.total_actions,
            "candidate_actions": self.candidate_actions,
            "chosen_action_checks": self.chosen_action_checks,
            "chosen_actions_covered": self.chosen_actions_covered,
            "audit_rate": self.audit_rate,
            "supported_rate": self.supported_rate,
            "coverage_rate": self.coverage_rate,
            "complete_audited_rate": self.complete_audited_rate,
            "candidate_action_rate": self.candidate_action_rate,
            "chosen_action_coverage_rate": self.chosen_action_coverage_rate,
            "issue_counts": dict(sorted(self.issue_counts.items())),
        }


@dataclass(frozen=True)
class ActionCoverageSummary:
    overall: ActionCoverageSlice
    per_state_type: Mapping[str, ActionCoverageSlice]

    def as_dict(self) -> dict[str, Any]:
        return {
            "overall": self.overall.as_dict(),
            "per_state_type": {
                state_type: summary.as_dict()
                for state_type, summary in sorted(self.per_state_type.items())
            },
        }


def _normalize_audit(raw: Mapping[str, Any] | None) -> dict[str, Any]:
    if raw is None:
        return {
            "state_type": "unknown",
            "audited": False,
            "supported": False,
            "complete": False,
            "action_count": 0,
            "candidate_action_count": 0,
            "issues": ("missing_action_space_audit",),
            "chosen_action_legal": None,
        }

    nested = raw.get("action_space_audit")
    if isinstance(nested, Mapping):
        audit = nested
    elif {"supported", "complete", "action_count"}.issubset(raw):
        audit = raw
    else:
        observation = raw.get("observation")
        state_type_value = raw.get("state_type")
        if state_type_value is None and isinstance(observation, Mapping):
            state_type_value = observation.get("state_type")
        return {
            "state_type": "unknown" if state_type_value is None else str(state_type_value),
            "audited": False,
            "supported": False,
            "complete": False,
            "action_count": 0,
            "candidate_action_count": 0,
            "issues": ("missing_action_space_audit",),
            "chosen_action_legal": None,
        }

    state_type_value = audit.get("state_type")
    state_type = "unknown" if state_type_value is None else str(state_type_value)
    supported = audit.get("supported")
    complete = audit.get("complete")
    if not isinstance(supported, bool) or not isinstance(complete, bool):
        raise ValueError("action-space audit supported/complete fields must be bools")
    if complete and not supported:
        raise ValueError("a complete action-space audit must also be supported")
    action_count = _integer(audit.get("action_count"), "action_count", minimum=0)
    candidate_count = _integer(
        audit.get("candidate_action_count", 0),
        "candidate_action_count",
        minimum=0,
    )
    if candidate_count > action_count:
        raise ValueError("candidate_action_count must not exceed action_count")

    issues_value = audit.get("issues", ())
    if not isinstance(issues_value, (list, tuple)):
        raise ValueError("action-space audit issues must be a list")
    issues = tuple(_nonempty_text(issue, "issue") for issue in issues_value)
    if complete and issues:
        raise ValueError("a complete action-space audit must not contain issues")

    legal_actions = raw.get("legal_actions", audit.get("legal_actions"))
    if legal_actions is not None:
        if not isinstance(legal_actions, list):
            raise ValueError("legal_actions must be a list when present")
        if len(legal_actions) != action_count:
            raise ValueError("action_count must match the legal_actions length")

    chosen_action_legal = raw.get("chosen_action_legal", audit.get("chosen_action_legal"))
    if chosen_action_legal is not None and not isinstance(chosen_action_legal, bool):
        raise ValueError("chosen_action_legal must be bool or null")
    if (
        raw.get("state_before_estimated") is True
        or raw.get("action_status") in {"cancelled", "automatic"}
        or raw.get("is_player_decision") is False
    ):
        chosen_action_legal = None
    return {
        "state_type": state_type,
        "audited": True,
        "supported": supported,
        "complete": complete,
        "action_count": action_count,
        "candidate_action_count": candidate_count,
        "issues": issues,
        "chosen_action_legal": chosen_action_legal,
    }


def _coverage_slice(audits: Sequence[Mapping[str, Any]]) -> ActionCoverageSlice:
    snapshots = len(audits)
    audited = sum(bool(audit["audited"]) for audit in audits)
    supported = sum(bool(audit["supported"]) for audit in audits)
    complete = sum(bool(audit["complete"]) for audit in audits)
    actionable = sum(int(audit["action_count"]) > 0 for audit in audits)
    total_actions = sum(int(audit["action_count"]) for audit in audits)
    candidate_actions = sum(int(audit["candidate_action_count"]) for audit in audits)
    checked = sum(audit["chosen_action_legal"] is not None for audit in audits)
    covered = sum(audit["chosen_action_legal"] is True for audit in audits)
    issues = Counter(
        issue for audit in audits for issue in audit["issues"]  # type: ignore[union-attr]
    )
    return ActionCoverageSlice(
        snapshots=snapshots,
        audited_snapshots=audited,
        missing_audit_snapshots=snapshots - audited,
        supported_snapshots=supported,
        complete_snapshots=complete,
        actionable_snapshots=actionable,
        total_actions=total_actions,
        candidate_actions=candidate_actions,
        chosen_action_checks=checked,
        chosen_actions_covered=covered,
        audit_rate=None if snapshots == 0 else audited / snapshots,
        supported_rate=None if snapshots == 0 else supported / snapshots,
        coverage_rate=None if snapshots == 0 else complete / snapshots,
        complete_audited_rate=None if audited == 0 else complete / audited,
        candidate_action_rate=(
            None if total_actions == 0 else candidate_actions / total_actions
        ),
        chosen_action_coverage_rate=None if checked == 0 else covered / checked,
        issue_counts=MappingProxyType(dict(sorted(issues.items()))),
    )


def summarize_action_coverage(
    audits: Iterable[Mapping[str, Any] | None],
) -> ActionCoverageSummary:
    """Aggregate :func:`legal_actions.audit_action_space` dictionaries.

    Inputs can be either the audit dictionaries themselves or wrapper mappings
    containing an ``action_space_audit`` field.  A wrapper may additionally set
    ``chosen_action_legal`` to audit whether the committed action was covered.
    """
    normalized: list[dict[str, Any]] = []
    for raw in audits:
        if raw is not None and not isinstance(raw, Mapping):
            raise ValueError("audits must contain only mappings or null")
        normalized.append(_normalize_audit(raw))

    by_state_type: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for audit in normalized:
        by_state_type[str(audit["state_type"])].append(audit)
    return ActionCoverageSummary(
        overall=_coverage_slice(normalized),
        per_state_type=MappingProxyType(
            {
                state_type: _coverage_slice(items)
                for state_type, items in sorted(by_state_type.items())
            }
        ),
    )
