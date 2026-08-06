"""Small, dependency-free learning baselines for recorded STS2 decisions.

The models in this module intentionally trade capacity for portability.  They
use deterministic feature hashing, keep only non-zero weights, and serialize
to ordinary JSON.  This makes them useful as smoke-test/trainability baselines
before introducing a tensor framework or a large replay dataset.

Both trainers consume records with this shape::

    {
        "observation": {...},
        "legal_actions": [{...}, ...],
        "chosen_action_index": 0,
        "outcome": 1,
        "eligible": True,
    }

``outcome`` is ignored by behavior cloning and is required by the value model.
Ineligible records are counted and skipped.  Legal actions are the mask: the
behavior policy normalizes its softmax over those candidates only.
"""

from __future__ import annotations

import hashlib
import json
import math
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Iterator, Mapping, Sequence

from .action_normalization import NON_SEMANTIC_ACTION_KEYS


CHECKPOINT_SCHEMA_VERSION = 1
MODEL_VERSION = 2
_NON_SEMANTIC_ACTION_KEYS = NON_SEMANTIC_ACTION_KEYS | frozenset(
    {"candidate_index"}
)
_MIN_PROBABILITY = 1e-15


@dataclass(frozen=True)
class BehaviorCloningMetrics:
    """Offline behavior-cloning metrics over eligible decisions."""

    examples: int
    skipped: int
    nll: float
    top1_accuracy: float


@dataclass(frozen=True)
class ValueMetrics:
    """Offline binary win-value metrics over eligible decisions."""

    examples: int
    skipped: int
    log_loss: float
    brier_score: float


@dataclass(frozen=True)
class _Decision:
    observation: Mapping[str, Any]
    privileged_observation: Mapping[str, Any] | None
    information_mode: str
    legal_actions: tuple[Mapping[str, Any], ...]
    action_mask: tuple[int, ...]
    chosen_action_index: int
    outcome: float | None


def _canonical_json(value: Any) -> str:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
        default=str,
    )


def _stable_key(key: Any) -> tuple[str, str]:
    return type(key).__name__, str(key)


def _number_value(value: float) -> float:
    """Compress large counters while preserving sign and useful ordering."""
    if not math.isfinite(value):
        return 0.0
    return math.copysign(math.log1p(abs(value)), value)


def _number_bucket(value: float) -> str:
    if not math.isfinite(value):
        return "nonfinite"
    if value == int(value) and abs(value) <= 32:
        return str(int(value))
    if value == 0.0:
        return "0"
    magnitude = int(math.floor(math.log2(abs(value))))
    return f"{'+' if value > 0 else '-'}2^{magnitude}"


def _atomic_features(
    value: Any,
    *,
    prefix: str,
    ignored_keys: frozenset[str] = frozenset(),
) -> Iterator[tuple[str, float]]:
    """Flatten JSON-like data into deterministic sparse feature atoms."""
    if isinstance(value, Mapping):
        for raw_key in sorted(value, key=_stable_key):
            key = str(raw_key)
            if key in ignored_keys:
                continue
            child = f"{prefix}.{key}" if prefix else key
            yield from _atomic_features(
                value[raw_key], prefix=child, ignored_keys=ignored_keys
            )
        return
    if isinstance(value, (list, tuple)):
        yield f"{prefix}.length={len(value)}", 1.0
        for index, item in enumerate(value):
            yield from _atomic_features(
                item,
                prefix=f"{prefix}[{index}]",
                ignored_keys=ignored_keys,
            )
        return
    if value is None:
        yield f"{prefix}=null", 1.0
        return
    if isinstance(value, bool):
        yield f"{prefix}=bool:{str(value).lower()}", 1.0
        return
    if isinstance(value, (int, float)):
        number = float(value)
        yield f"{prefix}=number", _number_value(number)
        yield f"{prefix}=bucket:{_number_bucket(number)}", 1.0
        return
    yield f"{prefix}=string:{value}", 1.0


class HashedStateActionFeatures:
    """Deterministic sparse feature encoder for a state/action candidate.

    Candidate positions are deliberately excluded.  Reordering a legal-action
    list therefore reorders probabilities without changing the probability of
    the underlying action.
    """

    def __init__(
        self,
        *,
        dimension: int = 1 << 15,
        hash_seed: str = "sts2rec-baseline-v1",
        max_observation_atoms: int = 256,
        max_action_atoms: int = 64,
        max_interactions: int = 2048,
    ) -> None:
        if dimension < 2:
            raise ValueError("dimension must be >= 2")
        for name, value in (
            ("max_observation_atoms", max_observation_atoms),
            ("max_action_atoms", max_action_atoms),
            ("max_interactions", max_interactions),
        ):
            if value < 1:
                raise ValueError(f"{name} must be >= 1")
        self.dimension = int(dimension)
        self.hash_seed = str(hash_seed)
        self.max_observation_atoms = int(max_observation_atoms)
        self.max_action_atoms = int(max_action_atoms)
        self.max_interactions = int(max_interactions)
        self._hash_key = hashlib.sha256(self.hash_seed.encode("utf-8")).digest()

    def config(self) -> dict[str, Any]:
        return {
            "dimension": self.dimension,
            "hash_seed": self.hash_seed,
            "max_observation_atoms": self.max_observation_atoms,
            "max_action_atoms": self.max_action_atoms,
            "max_interactions": self.max_interactions,
        }

    @classmethod
    def from_config(cls, config: Mapping[str, Any]) -> HashedStateActionFeatures:
        return cls(
            dimension=int(config["dimension"]),
            hash_seed=str(config["hash_seed"]),
            max_observation_atoms=int(config["max_observation_atoms"]),
            max_action_atoms=int(config["max_action_atoms"]),
            max_interactions=int(config["max_interactions"]),
        )

    def _hash(self, token: str) -> tuple[int, float]:
        digest = hashlib.blake2b(
            token.encode("utf-8"), digest_size=16, key=self._hash_key
        ).digest()
        index = int.from_bytes(digest[:8], "little") % self.dimension
        sign = 1.0 if digest[8] & 1 else -1.0
        return index, sign

    def encode(
        self, observation: Mapping[str, Any], action: Mapping[str, Any]
    ) -> dict[int, float]:
        if not isinstance(observation, Mapping):
            raise TypeError("observation must be a mapping")
        if not isinstance(action, Mapping):
            raise TypeError("action must be a mapping")
        observation_atoms = list(
            _atomic_features(observation, prefix="observation")
        )[: self.max_observation_atoms]
        action_atoms = list(
            _atomic_features(
                action,
                prefix="action",
                ignored_keys=_NON_SEMANTIC_ACTION_KEYS,
            )
        )[: self.max_action_atoms]
        sparse: dict[int, float] = {}

        def add(token: str, value: float) -> None:
            if value == 0.0:
                return
            index, sign = self._hash(token)
            sparse[index] = sparse.get(index, 0.0) + sign * value

        # Direct action features learn useful global priors.  State-only
        # features would cancel in a candidate softmax, so state information is
        # introduced through state/action interactions instead.
        add("action:bias", 1.0)
        for token, value in action_atoms:
            add(f"direct|{token}", value)

        interactions = 0
        for action_token, action_value in action_atoms or [("action:empty", 1.0)]:
            for observation_token, observation_value in observation_atoms:
                add(
                    f"interaction|{observation_token}|{action_token}",
                    observation_value * action_value,
                )
                interactions += 1
                if interactions >= self.max_interactions:
                    return {key: value for key, value in sparse.items() if value != 0.0}
        return {key: value for key, value in sparse.items() if value != 0.0}


def _dot(weights: Mapping[int, float], features: Mapping[int, float]) -> float:
    return sum(weights.get(index, 0.0) * value for index, value in features.items())


def _softmax(scores: Sequence[float]) -> list[float]:
    if not scores:
        raise ValueError("cannot compute a softmax without legal actions")
    maximum = max(scores)
    exponents = [math.exp(score - maximum) for score in scores]
    total = sum(exponents)
    return [value / total for value in exponents]


def _sigmoid(score: float) -> float:
    if score >= 0.0:
        decay = math.exp(-score)
        return 1.0 / (1.0 + decay)
    growth = math.exp(score)
    return growth / (1.0 + growth)


def _information_inputs(
    observation: Mapping[str, Any],
    *,
    use_privileged_observation: bool,
    privileged_observation: Mapping[str, Any] | None,
    information_mode: str,
) -> Mapping[str, Any]:
    """Build an explicit, auditable feature namespace for one information mode."""

    mode = str(information_mode).casefold()
    if mode not in {"limited", "omniscient"}:
        raise ValueError(f"unknown information_mode: {information_mode!r}")
    if privileged_observation is not None and not isinstance(
        privileged_observation, Mapping
    ):
        raise ValueError("privileged_observation must be an object or null")
    if mode == "limited" and privileged_observation is not None:
        raise ValueError(
            "limited records must not contain privileged_observation"
        )
    if not use_privileged_observation:
        return observation
    if mode != "omniscient" or privileged_observation is None:
        raise ValueError(
            "a privileged baseline requires omniscient information_mode and "
            "privileged_observation"
        )
    # Separate namespaces make it impossible for a privileged field to shadow
    # or silently alter a public observation field.
    return {
        "public_observation": observation,
        "privileged_observation": privileged_observation,
    }


def _active_action_indices(
    legal_actions: Sequence[Mapping[str, Any]],
    action_mask: Sequence[int] | None,
) -> tuple[int, ...]:
    if not legal_actions:
        raise ValueError("legal_actions must not be empty")
    if action_mask is None:
        return tuple(range(len(legal_actions)))
    if isinstance(action_mask, (str, bytes, bytearray)) or len(action_mask) != len(
        legal_actions
    ):
        raise ValueError("action_mask length must match legal_actions")
    if any(
        isinstance(value, bool) or not isinstance(value, int) or value not in (0, 1)
        for value in action_mask
    ):
        raise ValueError("action_mask values must be integer 0 or 1")
    active = tuple(index for index, value in enumerate(action_mask) if value == 1)
    if not active:
        raise ValueError("action_mask must enable at least one legal action")
    return active


def _parse_decision(
    record: Mapping[str, Any],
    *,
    require_outcome: bool,
    use_privileged_observation: bool,
) -> _Decision | None:
    if not isinstance(record, Mapping):
        raise TypeError("each training record must be a mapping")
    eligibility_key = "value_eligible" if require_outcome else "bc_eligible"
    eligible = record.get(eligibility_key, record.get("eligible"))
    if not isinstance(eligible, bool):
        raise ValueError(
            f"{eligibility_key} (or eligible fallback) must be an explicit bool"
        )
    if not eligible:
        return None
    observation = record.get("observation")
    privileged_observation = record.get("privileged_observation")
    information_mode = record.get("information_mode", "limited")
    legal = record.get("legal_actions")
    action_mask = record.get("action_mask")
    chosen = record.get("chosen_action_index")
    if not isinstance(observation, Mapping):
        raise ValueError("eligible record has no mapping observation")
    if not isinstance(legal, Sequence) or isinstance(legal, (str, bytes)) or not legal:
        raise ValueError("eligible record has no legal actions")
    if any(not isinstance(action, Mapping) for action in legal):
        raise ValueError("every legal action must be a mapping")
    if isinstance(chosen, bool) or not isinstance(chosen, int):
        raise ValueError("chosen_action_index must be an integer")
    if chosen < 0 or chosen >= len(legal):
        raise ValueError("chosen_action_index is outside the legal action list")
    active_indices = _active_action_indices(legal, action_mask)
    if chosen not in active_indices:
        raise ValueError("chosen_action_index is masked out")
    if not isinstance(information_mode, str):
        raise ValueError("information_mode must be a string")
    if privileged_observation is not None and not isinstance(
        privileged_observation, Mapping
    ):
        raise ValueError("privileged_observation must be an object or null")
    # Validate the boundary now.  Models receive public and privileged fields
    # separately below instead of trusting callers to merge them correctly.
    _information_inputs(
        observation,
        use_privileged_observation=use_privileged_observation,
        privileged_observation=privileged_observation,
        information_mode=information_mode,
    )
    outcome: float | None = None
    if require_outcome:
        raw_outcome = record.get("outcome")
        if isinstance(raw_outcome, bool):
            outcome = float(raw_outcome)
        elif isinstance(raw_outcome, (int, float)) and float(raw_outcome) in (0.0, 1.0):
            outcome = float(raw_outcome)
        else:
            raise ValueError("outcome must be 0, 1, or bool")
    return _Decision(
        observation=observation,
        privileged_observation=privileged_observation,
        information_mode=information_mode.casefold(),
        legal_actions=tuple(legal),
        action_mask=tuple(
            [1] * len(legal) if action_mask is None else action_mask
        ),
        chosen_action_index=chosen,
        outcome=outcome,
    )


def _prepare(
    records: Iterable[Mapping[str, Any]],
    *,
    require_outcome: bool,
    use_privileged_observation: bool,
) -> tuple[list[_Decision], int]:
    decisions: list[_Decision] = []
    skipped = 0
    for record in records:
        decision = _parse_decision(
            record,
            require_outcome=require_outcome,
            use_privileged_observation=use_privileged_observation,
        )
        if decision is None:
            skipped += 1
        else:
            decisions.append(decision)
    return decisions, skipped


class LinearBehaviorCloningPolicy:
    """Dynamic-candidate linear policy trained with masked-softmax SGD."""

    model_type = "hashed_linear_behavior_cloning"

    def __init__(
        self,
        *,
        encoder: HashedStateActionFeatures | None = None,
        metadata: Mapping[str, Any] | None = None,
        use_privileged_observation: bool = False,
    ) -> None:
        self.encoder = encoder or HashedStateActionFeatures()
        self.weights: dict[int, float] = {}
        self.metadata = dict(metadata or {})
        self.use_privileged_observation = bool(use_privileged_observation)
        self.updates = 0

    def _features_observation(
        self,
        observation: Mapping[str, Any],
        *,
        privileged_observation: Mapping[str, Any] | None,
        information_mode: str,
    ) -> Mapping[str, Any]:
        return _information_inputs(
            observation,
            use_privileged_observation=self.use_privileged_observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )

    def scores(
        self,
        observation: Mapping[str, Any],
        legal_actions: Sequence[Mapping[str, Any]],
        *,
        action_mask: Sequence[int] | None = None,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
    ) -> list[float]:
        active = frozenset(_active_action_indices(legal_actions, action_mask))
        features_observation = self._features_observation(
            observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        return [
            _dot(
                self.weights,
                self.encoder.encode(features_observation, action),
            )
            if index in active
            else -math.inf
            for index, action in enumerate(legal_actions)
        ]

    def predict_proba(
        self,
        observation: Mapping[str, Any],
        legal_actions: Sequence[Mapping[str, Any]],
        *,
        action_mask: Sequence[int] | None = None,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
    ) -> list[float]:
        active = _active_action_indices(legal_actions, action_mask)
        scores = self.scores(
            observation,
            legal_actions,
            action_mask=action_mask,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        active_probabilities = _softmax([scores[index] for index in active])
        probabilities = [0.0] * len(legal_actions)
        for index, probability in zip(active, active_probabilities):
            probabilities[index] = probability
        return probabilities

    def choose_action_index(
        self,
        observation: Mapping[str, Any],
        legal_actions: Sequence[Mapping[str, Any]],
        *,
        action_mask: Sequence[int] | None = None,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
    ) -> int:
        active = _active_action_indices(legal_actions, action_mask)
        probabilities = self.predict_proba(
            observation,
            legal_actions,
            action_mask=action_mask,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        # Stable content-based tie breaking avoids an accidental candidate-index
        # policy when all weights (or two scores) are equal.
        return max(
            active,
            key=lambda index: (
                probabilities[index],
                _canonical_json(
                    {
                        key: value
                        for key, value in legal_actions[index].items()
                        if str(key) not in _NON_SEMANTIC_ACTION_KEYS
                    }
                ),
            ),
        )

    def choose_action(
        self,
        observation: Mapping[str, Any],
        legal_actions: Sequence[Mapping[str, Any]],
        *,
        action_mask: Sequence[int] | None = None,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
    ) -> Mapping[str, Any]:
        return legal_actions[
            self.choose_action_index(
                observation,
                legal_actions,
                action_mask=action_mask,
                privileged_observation=privileged_observation,
                information_mode=information_mode,
            )
        ]

    def __call__(self, step: Any) -> int:
        """Allow direct use as a ``training.Policy`` over ``TextTimeStep``."""
        return self.choose_action_index(
            step.observation,
            step.legal_actions,
            action_mask=step.action_mask,
            privileged_observation=getattr(step, "privileged_observation", None),
            information_mode=step.info.get("information_mode", "limited"),
        )

    def update(
        self,
        observation: Mapping[str, Any],
        legal_actions: Sequence[Mapping[str, Any]],
        chosen_action_index: int,
        *,
        action_mask: Sequence[int] | None = None,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
        learning_rate: float = 0.1,
        l2: float = 0.0,
    ) -> float:
        if learning_rate <= 0.0:
            raise ValueError("learning_rate must be > 0")
        if l2 < 0.0:
            raise ValueError("l2 must be >= 0")
        if isinstance(chosen_action_index, bool) or not isinstance(
            chosen_action_index, int
        ):
            raise ValueError("chosen_action_index must be an integer")
        if chosen_action_index < 0 or chosen_action_index >= len(legal_actions):
            raise ValueError("chosen_action_index is outside the legal action list")
        active = _active_action_indices(legal_actions, action_mask)
        if chosen_action_index not in active:
            raise ValueError("chosen_action_index is masked out")
        features_observation = self._features_observation(
            observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        features = {
            index: self.encoder.encode(features_observation, legal_actions[index])
            for index in active
        }
        active_probabilities = _softmax(
            [_dot(self.weights, features[index]) for index in active]
        )
        probabilities = dict(zip(active, active_probabilities))
        gradient: dict[int, float] = {}
        for candidate_index in active:
            sparse = features[candidate_index]
            coefficient = probabilities[candidate_index] - float(
                candidate_index == chosen_action_index
            )
            for feature_index, value in sparse.items():
                gradient[feature_index] = gradient.get(feature_index, 0.0) + coefficient * value
        for feature_index, value in gradient.items():
            old_weight = self.weights.get(feature_index, 0.0)
            new_weight = old_weight - learning_rate * (value + l2 * old_weight)
            if abs(new_weight) < 1e-15:
                self.weights.pop(feature_index, None)
            else:
                self.weights[feature_index] = new_weight
        self.updates += 1
        return -math.log(max(_MIN_PROBABILITY, probabilities[chosen_action_index]))

    def fit(
        self,
        records: Iterable[Mapping[str, Any]],
        *,
        epochs: int = 1,
        learning_rate: float = 0.1,
        l2: float = 0.0,
        shuffle: bool = True,
        seed: int = 0,
    ) -> BehaviorCloningMetrics:
        if epochs < 1:
            raise ValueError("epochs must be >= 1")
        decisions, skipped = _prepare(
            records,
            require_outcome=False,
            use_privileged_observation=self.use_privileged_observation,
        )
        order = list(range(len(decisions)))
        generator = random.Random(seed)
        for _ in range(epochs):
            if shuffle:
                generator.shuffle(order)
            for index in order:
                decision = decisions[index]
                self.update(
                    decision.observation,
                    decision.legal_actions,
                    decision.chosen_action_index,
                    action_mask=decision.action_mask,
                    privileged_observation=decision.privileged_observation,
                    information_mode=decision.information_mode,
                    learning_rate=learning_rate,
                    l2=l2,
                )
        return _behavior_metrics(self, decisions, skipped)

    def to_checkpoint(self, *, metadata: Mapping[str, Any] | None = None) -> dict[str, Any]:
        resolved_metadata = {**self.metadata, **dict(metadata or {})}
        return {
            "schema_version": CHECKPOINT_SCHEMA_VERSION,
            "model": {
                "type": self.model_type,
                "version": MODEL_VERSION,
                "encoder": self.encoder.config(),
                "updates": self.updates,
                "use_privileged_observation": self.use_privileged_observation,
            },
            "metadata": resolved_metadata,
            "weights": [[index, self.weights[index]] for index in sorted(self.weights)],
        }

    def save(
        self, path: str | Path, *, metadata: Mapping[str, Any] | None = None
    ) -> Path:
        target = Path(path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            json.dumps(
                self.to_checkpoint(metadata=metadata),
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + "\n",
            encoding="utf-8",
        )
        return target

    @classmethod
    def load(cls, path: str | Path) -> LinearBehaviorCloningPolicy:
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        model, weights, metadata = _validate_checkpoint(payload, cls.model_type)
        result = cls(
            encoder=HashedStateActionFeatures.from_config(model["encoder"]),
            metadata=metadata,
            use_privileged_observation=bool(
                model.get("use_privileged_observation", False)
            ),
        )
        result.weights = weights
        result.updates = int(model.get("updates", 0))
        return result


class BinaryWinValueModel:
    """Linear logistic estimate of run outcome for a chosen state/action."""

    model_type = "hashed_linear_win_value"

    def __init__(
        self,
        *,
        encoder: HashedStateActionFeatures | None = None,
        metadata: Mapping[str, Any] | None = None,
        use_privileged_observation: bool = False,
    ) -> None:
        self.encoder = encoder or HashedStateActionFeatures()
        self.weights: dict[int, float] = {}
        self.metadata = dict(metadata or {})
        self.use_privileged_observation = bool(use_privileged_observation)
        self.updates = 0

    def _features_observation(
        self,
        observation: Mapping[str, Any],
        *,
        privileged_observation: Mapping[str, Any] | None,
        information_mode: str,
    ) -> Mapping[str, Any]:
        return _information_inputs(
            observation,
            use_privileged_observation=self.use_privileged_observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )

    def predict_proba(
        self,
        observation: Mapping[str, Any],
        action: Mapping[str, Any],
        *,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
    ) -> float:
        features_observation = self._features_observation(
            observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        return _sigmoid(
            _dot(self.weights, self.encoder.encode(features_observation, action))
        )

    def win_probability(
        self, view: Mapping[str, Any], action: Mapping[str, Any]
    ) -> float:
        """Implement ``evaluation.ActionValueEstimator`` without an adapter."""
        nested = view.get("observation")
        observation = nested if isinstance(nested, Mapping) else view
        privileged = view.get("privileged_observation", view.get("privileged"))
        return self.predict_proba(
            observation,
            action,
            privileged_observation=(
                privileged if isinstance(privileged, Mapping) else None
            ),
            information_mode=str(
                view.get("information_mode", view.get("mode", "limited"))
            ),
        )

    def update(
        self,
        observation: Mapping[str, Any],
        action: Mapping[str, Any],
        outcome: float | int | bool,
        *,
        privileged_observation: Mapping[str, Any] | None = None,
        information_mode: str = "limited",
        learning_rate: float = 0.1,
        l2: float = 0.0,
    ) -> float:
        if learning_rate <= 0.0:
            raise ValueError("learning_rate must be > 0")
        if l2 < 0.0:
            raise ValueError("l2 must be >= 0")
        target = float(outcome)
        if target not in (0.0, 1.0):
            raise ValueError("outcome must be 0, 1, or bool")
        features_observation = self._features_observation(
            observation,
            privileged_observation=privileged_observation,
            information_mode=information_mode,
        )
        features = self.encoder.encode(features_observation, action)
        probability = _sigmoid(_dot(self.weights, features))
        coefficient = probability - target
        for feature_index, value in features.items():
            old_weight = self.weights.get(feature_index, 0.0)
            gradient = coefficient * value + l2 * old_weight
            new_weight = old_weight - learning_rate * gradient
            if abs(new_weight) < 1e-15:
                self.weights.pop(feature_index, None)
            else:
                self.weights[feature_index] = new_weight
        self.updates += 1
        clipped = min(1.0 - _MIN_PROBABILITY, max(_MIN_PROBABILITY, probability))
        return -(target * math.log(clipped) + (1.0 - target) * math.log(1.0 - clipped))

    def fit(
        self,
        records: Iterable[Mapping[str, Any]],
        *,
        epochs: int = 1,
        learning_rate: float = 0.1,
        l2: float = 0.0,
        shuffle: bool = True,
        seed: int = 0,
    ) -> ValueMetrics:
        if epochs < 1:
            raise ValueError("epochs must be >= 1")
        decisions, skipped = _prepare(
            records,
            require_outcome=True,
            use_privileged_observation=self.use_privileged_observation,
        )
        order = list(range(len(decisions)))
        generator = random.Random(seed)
        for _ in range(epochs):
            if shuffle:
                generator.shuffle(order)
            for index in order:
                decision = decisions[index]
                assert decision.outcome is not None
                self.update(
                    decision.observation,
                    decision.legal_actions[decision.chosen_action_index],
                    decision.outcome,
                    privileged_observation=decision.privileged_observation,
                    information_mode=decision.information_mode,
                    learning_rate=learning_rate,
                    l2=l2,
                )
        return _value_metrics(self, decisions, skipped)

    def to_checkpoint(self, *, metadata: Mapping[str, Any] | None = None) -> dict[str, Any]:
        resolved_metadata = {**self.metadata, **dict(metadata or {})}
        return {
            "schema_version": CHECKPOINT_SCHEMA_VERSION,
            "model": {
                "type": self.model_type,
                "version": MODEL_VERSION,
                "encoder": self.encoder.config(),
                "updates": self.updates,
                "use_privileged_observation": self.use_privileged_observation,
            },
            "metadata": resolved_metadata,
            "weights": [[index, self.weights[index]] for index in sorted(self.weights)],
        }

    def save(
        self, path: str | Path, *, metadata: Mapping[str, Any] | None = None
    ) -> Path:
        target = Path(path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            json.dumps(
                self.to_checkpoint(metadata=metadata),
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + "\n",
            encoding="utf-8",
        )
        return target

    @classmethod
    def load(cls, path: str | Path) -> BinaryWinValueModel:
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        model, weights, metadata = _validate_checkpoint(payload, cls.model_type)
        result = cls(
            encoder=HashedStateActionFeatures.from_config(model["encoder"]),
            metadata=metadata,
            use_privileged_observation=bool(
                model.get("use_privileged_observation", False)
            ),
        )
        result.weights = weights
        result.updates = int(model.get("updates", 0))
        return result


def _behavior_metrics(
    model: LinearBehaviorCloningPolicy,
    decisions: Sequence[_Decision],
    skipped: int,
) -> BehaviorCloningMetrics:
    if not decisions:
        raise ValueError("cannot compute behavior-cloning metrics without eligible examples")
    nll = 0.0
    correct = 0
    for decision in decisions:
        probabilities = model.predict_proba(
            decision.observation,
            decision.legal_actions,
            action_mask=decision.action_mask,
            privileged_observation=decision.privileged_observation,
            information_mode=decision.information_mode,
        )
        nll -= math.log(
            max(_MIN_PROBABILITY, probabilities[decision.chosen_action_index])
        )
        correct += int(
            model.choose_action_index(
                decision.observation,
                decision.legal_actions,
                action_mask=decision.action_mask,
                privileged_observation=decision.privileged_observation,
                information_mode=decision.information_mode,
            )
            == decision.chosen_action_index
        )
    return BehaviorCloningMetrics(
        examples=len(decisions),
        skipped=skipped,
        nll=nll / len(decisions),
        top1_accuracy=correct / len(decisions),
    )


def evaluate_behavior_cloning(
    model: LinearBehaviorCloningPolicy,
    records: Iterable[Mapping[str, Any]],
) -> BehaviorCloningMetrics:
    """Compute average NLL and top-1 accuracy without modifying the model."""
    decisions, skipped = _prepare(
        records,
        require_outcome=False,
        use_privileged_observation=model.use_privileged_observation,
    )
    return _behavior_metrics(model, decisions, skipped)


def _value_metrics(
    model: BinaryWinValueModel,
    decisions: Sequence[_Decision],
    skipped: int,
) -> ValueMetrics:
    if not decisions:
        raise ValueError("cannot compute value metrics without eligible examples")
    log_loss = 0.0
    brier = 0.0
    for decision in decisions:
        assert decision.outcome is not None
        probability = model.predict_proba(
            decision.observation,
            decision.legal_actions[decision.chosen_action_index],
            privileged_observation=decision.privileged_observation,
            information_mode=decision.information_mode,
        )
        clipped = min(1.0 - _MIN_PROBABILITY, max(_MIN_PROBABILITY, probability))
        log_loss -= decision.outcome * math.log(clipped) + (
            1.0 - decision.outcome
        ) * math.log(1.0 - clipped)
        brier += (probability - decision.outcome) ** 2
    return ValueMetrics(
        examples=len(decisions),
        skipped=skipped,
        log_loss=log_loss / len(decisions),
        brier_score=brier / len(decisions),
    )


def evaluate_win_value(
    model: BinaryWinValueModel,
    records: Iterable[Mapping[str, Any]],
) -> ValueMetrics:
    """Compute binary log loss and Brier score without modifying the model."""
    decisions, skipped = _prepare(
        records,
        require_outcome=True,
        use_privileged_observation=model.use_privileged_observation,
    )
    return _value_metrics(model, decisions, skipped)


def _validate_checkpoint(
    payload: Any, expected_type: str
) -> tuple[Mapping[str, Any], dict[int, float], dict[str, Any]]:
    if not isinstance(payload, Mapping):
        raise ValueError("checkpoint root must be an object")
    if payload.get("schema_version") != CHECKPOINT_SCHEMA_VERSION:
        raise ValueError("unsupported checkpoint schema_version")
    model = payload.get("model")
    if not isinstance(model, Mapping):
        raise ValueError("checkpoint has no model metadata")
    if model.get("type") != expected_type:
        raise ValueError(f"checkpoint model type is not {expected_type}")
    if model.get("version") != MODEL_VERSION:
        raise ValueError("unsupported model version")
    if not isinstance(model.get("encoder"), Mapping):
        raise ValueError("checkpoint has no encoder metadata")
    raw_weights = payload.get("weights")
    if not isinstance(raw_weights, list):
        raise ValueError("checkpoint weights must be a list")
    dimension = int(model["encoder"]["dimension"])
    weights: dict[int, float] = {}
    for item in raw_weights:
        if not isinstance(item, list) or len(item) != 2:
            raise ValueError("checkpoint weight entries must be [index, value]")
        index = int(item[0])
        value = float(item[1])
        if index < 0 or index >= dimension or not math.isfinite(value):
            raise ValueError("checkpoint contains an invalid weight")
        weights[index] = value
    metadata = payload.get("metadata", {})
    if not isinstance(metadata, Mapping):
        raise ValueError("checkpoint metadata must be an object")
    return model, weights, dict(metadata)


__all__ = [
    "BehaviorCloningMetrics",
    "BinaryWinValueModel",
    "CHECKPOINT_SCHEMA_VERSION",
    "HashedStateActionFeatures",
    "LinearBehaviorCloningPolicy",
    "MODEL_VERSION",
    "ValueMetrics",
    "evaluate_behavior_cloning",
    "evaluate_win_value",
]
