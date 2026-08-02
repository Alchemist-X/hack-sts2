"""Text-only, Gym-style environment over the real STS2 transition engine.

The game process runs with Godot ``--headless`` and exposes structured state
through STS2MCP.  This module is the renderer boundary: agents receive JSON and
legal actions, never pixels or screenshots.  Keeping the official engine as
the transition function avoids training against a subtly incorrect rewrite of
cards, relics, enemies, events, and RNG.
"""

from __future__ import annotations

import json
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Sequence

from .env import Sts2Env
from .information import InformationMode, build_information_view


Action = int | dict[str, Any]
PrivilegedProvider = Callable[[dict[str, Any]], dict[str, Any]]
RewardFunction = Callable[[dict[str, Any], dict[str, Any]], float]

_NON_WIRE_KEYS = frozenset(
    {"action_index", "candidate", "target_candidate", "label", "q_value"}
)


@dataclass(frozen=True)
class TextEnvConfig:
    """Observation and transition-wait policy for a text worker."""

    information_mode: InformationMode = InformationMode.LIMITED
    settle_timeout_s: float = 12.0
    poll_interval_s: float = 0.08
    max_episode_steps: int | None = None
    strict_action_validation: bool = True


@dataclass(frozen=True)
class TextTimeStep:
    """JSON-serializable Gym-style result.

    ``observation`` is the mode-filtered public state. ``legal_actions`` is an
    indexed full candidate set; pass an integer index back to ``step``.  Reward
    is sparse by default: 1 only on a confirmed win, otherwise 0.
    """

    observation: dict[str, Any]
    legal_actions: list[dict[str, Any]]
    action_mask: list[int]
    reward: float
    terminated: bool
    truncated: bool
    info: dict[str, Any] = field(default_factory=dict)

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


def _fingerprint(state: dict[str, Any]) -> str:
    return json.dumps(state, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def _wire_action(action: dict[str, Any]) -> dict[str, Any]:
    """Remove evaluator metadata before POSTing to STS2MCP."""
    return {
        key: deepcopy(value)
        for key, value in action.items()
        if key not in _NON_WIRE_KEYS
    }


def _result_is_win(state: dict[str, Any]) -> bool:
    blocks = [state]
    for key in ("game_over", "result", "run"):
        value = state.get(key)
        if isinstance(value, dict):
            blocks.append(value)
    for block in blocks:
        for key in ("win", "won", "victory", "is_victory"):
            value = block.get(key)
            if isinstance(value, bool):
                return value
        result = block.get("result")
        if isinstance(result, str) and result.lower() in {"win", "victory", "won"}:
            return True
    return False


def _default_reward(previous: dict[str, Any], current: dict[str, Any]) -> float:
    del previous
    return 1.0 if current.get("state_type") == "game_over" and _result_is_win(current) else 0.0


def _indexed(actions: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {"action_index": index, **deepcopy(action)}
        for index, action in enumerate(actions)
    ]


class Sts2TextEnv:
    """One text-only environment backed by one isolated game process."""

    def __init__(
        self,
        env: Sts2Env,
        *,
        config: TextEnvConfig | None = None,
        privileged_provider: PrivilegedProvider | None = None,
        reward_function: RewardFunction | None = None,
        worker_id: int | str | None = None,
    ) -> None:
        self.env = env
        self.config = config or TextEnvConfig()
        self.privileged_provider = privileged_provider
        self.reward_function = reward_function or _default_reward
        self.worker_id = worker_id
        self.episode_steps = 0
        self._last_raw: dict[str, Any] | None = None

    def _view(self, raw: dict[str, Any]) -> dict[str, Any]:
        privileged = None
        if self.config.information_mode is InformationMode.OMNISCIENT:
            privileged = (
                self.privileged_provider(raw)
                if self.privileged_provider is not None
                else {}
            )
        return build_information_view(
            raw,
            self.config.information_mode,
            privileged=privileged,
        )

    def _time_step(
        self,
        raw: dict[str, Any],
        *,
        reward: float = 0.0,
        settled: bool = True,
        extra_info: dict[str, Any] | None = None,
    ) -> TextTimeStep:
        view = self._view(raw)
        actions = _indexed(view["legal_actions"])
        terminated = raw.get("state_type") == "game_over"
        truncated = (
            self.config.max_episode_steps is not None
            and self.episode_steps >= self.config.max_episode_steps
            and not terminated
        )
        info = {
            "worker_id": self.worker_id,
            "episode_step": self.episode_steps,
            "information_mode": self.config.information_mode.value,
            "action_space_audit": view["action_space_audit"],
            "privileged": view["privileged"],
            "settled": settled,
            "win": _result_is_win(raw) if terminated else None,
        }
        info.update(extra_info or {})
        return TextTimeStep(
            observation=view["observation"],
            legal_actions=actions,
            action_mask=[1] * len(actions),
            reward=float(reward),
            terminated=terminated,
            truncated=truncated,
            info=info,
        )

    def observe(self, *, wait_actionable: bool = True) -> TextTimeStep:
        raw = self.env.observe()
        if wait_actionable:
            raw, settled = self._wait_until_actionable(raw, require_change=False)
        else:
            settled = True
        self._last_raw = raw
        return self._time_step(raw, settled=settled)

    def reset(self, character: str | None = None) -> TextTimeStep:
        self.episode_steps = 0
        raw = self.env.reset(character=character)
        raw, settled = self._wait_until_actionable(raw, require_change=False)
        self._last_raw = raw
        return self._time_step(raw, settled=settled)

    def _resolve_action(
        self, action: Action, current: TextTimeStep
    ) -> tuple[dict[str, Any], dict[str, Any]]:
        if isinstance(action, int):
            if action < 0 or action >= len(current.legal_actions):
                raise IndexError(
                    f"action index {action} outside [0, {len(current.legal_actions)})"
                )
            selected = current.legal_actions[action]
        elif isinstance(action, dict):
            selected = action
        else:
            raise TypeError(f"action must be int or dict, got {type(action).__name__}")
        wire = _wire_action(selected)
        if not isinstance(wire.get("action"), str):
            raise ValueError(f"action has no string action name: {selected!r}")
        if self.config.strict_action_validation:
            legal_wire_actions = [_wire_action(item) for item in current.legal_actions]
            if wire not in legal_wire_actions:
                raise ValueError(
                    "action is not in the current public legal-action set: "
                    f"{wire!r}"
                )
        return deepcopy(selected), wire

    def step(self, action: Action) -> TextTimeStep:
        if self._last_raw is None:
            current = self.observe()
        else:
            current = self._time_step(self._last_raw)
        if current.terminated or current.truncated:
            raise RuntimeError("step() called after episode termination; call reset()")
        selected, wire = self._resolve_action(action, current)
        previous = self._last_raw or current.observation
        before = _fingerprint(previous)
        raw = self.env.step(wire)
        raw, settled = self._wait_until_actionable(
            raw, require_change=True, previous_fingerprint=before
        )
        self.episode_steps += 1
        reward = self.reward_function(previous, raw)
        self._last_raw = raw
        return self._time_step(
            raw,
            reward=reward,
            settled=settled,
            extra_info={"selected_action": selected, "wire_action": wire},
        )

    def _wait_until_actionable(
        self,
        raw: dict[str, Any],
        *,
        require_change: bool,
        previous_fingerprint: str | None = None,
    ) -> tuple[dict[str, Any], bool]:
        deadline = time.monotonic() + self.config.settle_timeout_s
        last = raw
        while True:
            fingerprint = _fingerprint(last)
            changed = not require_change or fingerprint != previous_fingerprint
            view = self._view(last)
            terminal = last.get("state_type") == "game_over"
            if changed and (terminal or bool(view["legal_actions"])):
                return last, True
            if time.monotonic() >= deadline:
                return last, False
            time.sleep(self.config.poll_interval_s)
            last = self.env.observe()


class VectorSts2TextEnv:
    """Synchronous vector facade; network calls execute concurrently."""

    def __init__(
        self,
        envs: Sequence[Sts2TextEnv],
        *,
        max_workers: int | None = None,
    ) -> None:
        if not envs:
            raise ValueError("VectorSts2TextEnv needs at least one environment")
        self.envs = list(envs)
        self._executor = ThreadPoolExecutor(
            max_workers=max_workers or len(self.envs),
            thread_name_prefix="sts2-text-worker",
        )

    @property
    def num_envs(self) -> int:
        return len(self.envs)

    def _map(self, func: Callable[[Sts2TextEnv], TextTimeStep]) -> list[TextTimeStep]:
        return list(self._executor.map(func, self.envs))

    def observe(self) -> list[TextTimeStep]:
        return self._map(lambda env: env.observe())

    def reset(self, character: str | None = None) -> list[TextTimeStep]:
        return self._map(lambda env: env.reset(character=character))

    def step(self, actions: Sequence[Action]) -> list[TextTimeStep]:
        if len(actions) != len(self.envs):
            raise ValueError(
                f"expected {len(self.envs)} actions, received {len(actions)}"
            )
        futures = [
            self._executor.submit(env.step, action)
            for env, action in zip(self.envs, actions)
        ]
        return [future.result() for future in futures]

    def close(self) -> None:
        self._executor.shutdown(wait=True, cancel_futures=False)

    def __enter__(self) -> "VectorSts2TextEnv":
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()


class JsonlTrajectoryWriter:
    """Thread-safe compact transition sink for training/evaluation pipelines."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()
        self._sequence = 0

    def append(
        self,
        *,
        worker_id: int | str | None,
        episode_id: int | str,
        previous: TextTimeStep,
        action: Action,
        current: TextTimeStep,
    ) -> None:
        with self._lock:
            self._sequence += 1
            record = {
                "schema_version": 1,
                "sequence": self._sequence,
                "time": time.time(),
                "worker_id": worker_id,
                "episode_id": episode_id,
                "observation": previous.observation,
                "legal_actions": previous.legal_actions,
                "action": action,
                "reward": current.reward,
                "next_observation": current.observation,
                "terminated": current.terminated,
                "truncated": current.truncated,
                "info": current.info,
            }
            with self.path.open("a", encoding="utf-8") as handle:
                handle.write(
                    json.dumps(record, ensure_ascii=False, separators=(",", ":"))
                    + "\n"
                )


def render_text(step: TextTimeStep) -> str:
    """Render one structured observation as a compact terminal screen."""
    state = step.observation
    run = state.get("run") if isinstance(state.get("run"), dict) else {}
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
    lines = [
        "=" * 78,
        f"STATE {state.get('state_type', 'unknown')}  "
        f"Act {run.get('act', '-')} Floor {run.get('floor', '-')}  "
        f"worker={step.info.get('worker_id', '-')}",
    ]
    if player:
        lines.append(
            f"PLAYER {player.get('character', '')}  HP {player.get('hp', '-')}/"
            f"{player.get('max_hp', '-')}  Energy {player.get('energy', '-')}  "
            f"Gold {player.get('gold', '-')}  Block {player.get('block', 0)}"
        )
    enemies = battle.get("enemies") if isinstance(battle.get("enemies"), list) else []
    if enemies:
        lines.append("ENEMIES")
        for enemy in enemies:
            if not isinstance(enemy, dict):
                continue
            intent = enemy.get("intent")
            if isinstance(intent, dict):
                intent = intent.get("type") or intent.get("name") or intent
            lines.append(
                f"  {enemy.get('entity_id', enemy.get('name', '?'))}: "
                f"HP {enemy.get('hp', '-')}/{enemy.get('max_hp', '-')}  "
                f"Block {enemy.get('block', 0)}  Intent {intent or '-'}"
            )
    hand = player.get("hand") if isinstance(player.get("hand"), list) else []
    if hand:
        lines.append("HAND")
        for card in hand:
            if isinstance(card, dict):
                lines.append(
                    f"  [{card.get('index', '?')}] {card.get('name', card.get('id', '?'))} "
                    f"cost={card.get('cost', card.get('energy_cost', '?'))} "
                    f"playable={card.get('can_play', False)}"
                )
    lines.append("ACTIONS")
    if step.legal_actions:
        for action in step.legal_actions:
            label = action.get("action", "?")
            candidate = action.get("candidate")
            if isinstance(candidate, dict):
                name = candidate.get("name") or candidate.get("id")
                if name:
                    label += f" — {name}"
                if candidate.get("price") is not None:
                    label += f" ({candidate['price']} gold)"
            if action.get("target"):
                label += f" -> {action['target']}"
            lines.append(f"  [{action['action_index']}] {label}")
    else:
        lines.append("  (transition frame: no public action yet)")
    if step.terminated:
        lines.append(f"TERMINAL win={step.info.get('win')} reward={step.reward}")
    elif step.truncated:
        lines.append(f"TRUNCATED reward={step.reward}")
    return "\n".join(lines)
