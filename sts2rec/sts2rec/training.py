"""Policy-agnostic rollout helpers for text-only STS2 environments."""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Callable, Sequence

from .text_env import Action, JsonlTrajectoryWriter, Sts2TextEnv, TextTimeStep


Policy = Callable[[TextTimeStep], Action]


@dataclass(frozen=True)
class EpisodeResult:
    episode_id: int | str
    worker_id: int | str | None
    steps: int
    total_reward: float
    terminated: bool
    truncated: bool
    win: bool | None
    terminal_reason: str
    elapsed_s: float
    policy_time_s: float
    settle_failures: int
    incomplete_action_spaces: int


def _terminal_reason(step: TextTimeStep) -> str:
    if step.terminated:
        if step.info.get("win") is True:
            return "victory"
        if step.info.get("win") is False:
            return "defeat"
        return "unknown_outcome"
    if step.truncated:
        return "step_limit"
    return "incomplete"


def run_episode(
    env: Sts2TextEnv,
    policy: Policy,
    *,
    episode_id: int | str,
    character: str | None = None,
    writer: JsonlTrajectoryWriter | None = None,
) -> EpisodeResult:
    """Reset, execute one policy episode, and optionally stream JSONL."""
    started = time.monotonic()
    previous = env.reset(character=character)
    total_reward = 0.0
    steps = 0
    policy_time_s = 0.0
    settle_failures = 0
    incomplete_action_spaces = 0
    while not previous.terminated and not previous.truncated:
        if not previous.legal_actions:
            raise RuntimeError(
                "environment did not expose an actionable state before settle timeout"
            )
        audit = previous.info.get("action_space_audit")
        if isinstance(audit, dict) and not audit.get("complete", False):
            incomplete_action_spaces += 1
        policy_started = time.monotonic()
        action = policy(previous)
        policy_time_s += time.monotonic() - policy_started
        current = env.step(action)
        if not current.info.get("settled", True):
            settle_failures += 1
        if writer is not None:
            writer.append(
                worker_id=env.worker_id,
                episode_id=episode_id,
                previous=previous,
                action=action,
                current=current,
            )
        total_reward += current.reward
        steps += 1
        previous = current
    return EpisodeResult(
        episode_id=episode_id,
        worker_id=env.worker_id,
        steps=steps,
        total_reward=total_reward,
        terminated=previous.terminated,
        truncated=previous.truncated,
        win=previous.info.get("win"),
        terminal_reason=_terminal_reason(previous),
        elapsed_s=time.monotonic() - started,
        policy_time_s=policy_time_s,
        settle_failures=settle_failures,
        incomplete_action_spaces=incomplete_action_spaces,
    )


def run_episodes_concurrently(
    envs: Sequence[Sts2TextEnv],
    policies: Sequence[Policy],
    *,
    episode_ids: Sequence[int | str],
    character: str | None = None,
    writer: JsonlTrajectoryWriter | None = None,
) -> list[EpisodeResult]:
    """Run one independent episode per worker in parallel."""
    if not (len(envs) == len(policies) == len(episode_ids)):
        raise ValueError("envs, policies and episode_ids must have equal length")
    from concurrent.futures import ThreadPoolExecutor

    with ThreadPoolExecutor(max_workers=len(envs), thread_name_prefix="sts2-rollout") as pool:
        futures = [
            pool.submit(
                run_episode,
                env,
                policy,
                episode_id=episode_id,
                character=character,
                writer=writer,
            )
            for env, policy, episode_id in zip(envs, policies, episode_ids)
        ]
        return [future.result() for future in futures]
