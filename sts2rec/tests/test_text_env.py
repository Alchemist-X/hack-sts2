from __future__ import annotations

import json
import time
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest

from sts2rec.information import InformationMode
from sts2rec.text_env import (
    JsonlTrajectoryWriter,
    Sts2TextEnv,
    TextEnvConfig,
    VectorSts2TextEnv,
    render_text,
)
from sts2rec.training import run_episode, run_episodes_concurrently


def combat_state(*, seed: str = "SECRET") -> dict[str, Any]:
    return {
        "state_type": "monster",
        "seed": seed,
        "run": {"act": 1, "floor": 2},
        "player": {
            "character": "IRONCLAD",
            "hp": 70,
            "max_hp": 80,
            "energy": 3,
            "gold": 99,
            "hand": [
                {
                    "index": 0,
                    "id": "STRIKE",
                    "name": "Strike",
                    "cost": 1,
                    "can_play": True,
                    "target_type": "AnyEnemy",
                }
            ],
        },
        "battle": {
            "is_play_phase": True,
            "enemies": [
                {"entity_id": "LOUSE_0", "name": "Louse", "hp": 12, "max_hp": 12}
            ],
        },
    }


def map_state() -> dict[str, Any]:
    return {
        "state_type": "map",
        "run": {"act": 1, "floor": 3},
        "player": {"hp": 70, "max_hp": 80, "gold": 99},
        "map": {"next_options": [{"index": 4, "room_type": "monster"}]},
    }


def terminal_state(win: bool = True) -> dict[str, Any]:
    return {"state_type": "game_over", "game_over": {"win": win}}


class ScriptedEnv:
    def __init__(self, initial: dict[str, Any], next_states: list[dict[str, Any]]) -> None:
        self.initial = deepcopy(initial)
        self.current = deepcopy(initial)
        self.next_states = [deepcopy(state) for state in next_states]
        self.posted: list[dict[str, Any]] = []

    def observe(self) -> dict[str, Any]:
        return deepcopy(self.current)

    def reset(self, character: str | None = None) -> dict[str, Any]:
        del character
        self.current = deepcopy(self.initial)
        return self.observe()

    def step(self, action: dict[str, Any]) -> dict[str, Any]:
        self.posted.append(deepcopy(action))
        if self.next_states:
            self.current = self.next_states.pop(0)
        return self.observe()


def text_env(scripted: ScriptedEnv, worker_id: int = 1) -> Sts2TextEnv:
    return Sts2TextEnv(
        scripted,  # type: ignore[arg-type]
        config=TextEnvConfig(settle_timeout_s=0.05, poll_interval_s=0.001),
        worker_id=worker_id,
    )


def test_limited_observation_is_text_only_and_leakage_free() -> None:
    env = text_env(ScriptedEnv(combat_state(), []))
    step = env.observe()
    assert "seed" not in step.observation
    assert step.info["information_mode"] == "limited"
    assert step.info["privileged"] is None
    assert step.action_mask == [1, 1]
    assert [action["action"] for action in step.legal_actions] == ["play_card", "end_turn"]


def test_indexed_action_posts_only_wire_fields() -> None:
    scripted = ScriptedEnv(combat_state(), [map_state()])
    env = text_env(scripted)
    env.observe()
    result = env.step(0)
    assert scripted.posted == [
        {"action": "play_card", "card_index": 0, "target": "LOUSE_0"}
    ]
    assert result.observation["state_type"] == "map"
    assert result.info["settled"] is True


def test_strict_mode_rejects_action_not_in_public_set() -> None:
    scripted = ScriptedEnv(combat_state(), [map_state()])
    env = text_env(scripted)
    env.observe()
    with pytest.raises(ValueError, match="not in the current public legal-action set"):
        env.step({"action": "claim_reward", "index": 99})
    assert scripted.posted == []


def test_omniscient_privileged_channel_is_separate() -> None:
    scripted = ScriptedEnv(combat_state(), [])
    env = Sts2TextEnv(
        scripted,  # type: ignore[arg-type]
        config=TextEnvConfig(information_mode=InformationMode.OMNISCIENT),
        privileged_provider=lambda raw: {"engine_seed": raw["seed"]},
    )
    step = env.observe()
    assert "seed" not in step.observation
    assert step.info["privileged"] == {"engine_seed": "SECRET"}


def test_terminal_sparse_reward_and_render() -> None:
    env = text_env(ScriptedEnv(combat_state(), [terminal_state(True)]))
    env.observe()
    result = env.step(1)
    assert result.terminated is True
    assert result.reward == 1.0
    assert result.info["win"] is True
    rendered = render_text(result)
    assert "TERMINAL win=True reward=1.0" in rendered


def test_vector_steps_workers_concurrently() -> None:
    class SlowScriptedEnv(ScriptedEnv):
        def step(self, action: dict[str, Any]) -> dict[str, Any]:
            time.sleep(0.05)
            return super().step(action)

    envs = [
        text_env(SlowScriptedEnv(combat_state(seed=str(i)), [map_state()]), i)
        for i in (1, 2)
    ]
    for env in envs:
        env.observe()
    with VectorSts2TextEnv(envs) as vector:
        started = time.monotonic()
        results = vector.step([1, 1])
        elapsed = time.monotonic() - started
    assert elapsed < 0.09
    assert [result.info["worker_id"] for result in results] == [1, 2]


def test_jsonl_writer_and_single_episode(tmp_path: Path) -> None:
    env = text_env(ScriptedEnv(combat_state(), [terminal_state(True)]))
    output = tmp_path / "trajectory.jsonl"
    writer = JsonlTrajectoryWriter(output)
    result = run_episode(env, lambda step: 1, episode_id="ep-1", writer=writer)
    assert result.steps == 1
    assert result.win is True
    record = json.loads(output.read_text(encoding="utf-8"))
    assert record["episode_id"] == "ep-1"
    assert record["terminated"] is True
    assert record["reward"] == 1.0


def test_concurrent_episode_helper_preserves_worker_order() -> None:
    envs = [
        text_env(ScriptedEnv(combat_state(seed=str(i)), [terminal_state(i == 1)]), i)
        for i in (1, 2)
    ]
    results = run_episodes_concurrently(
        envs,
        [lambda step: 1, lambda step: 1],
        episode_ids=["one", "two"],
    )
    assert [result.episode_id for result in results] == ["one", "two"]
    assert [result.win for result in results] == [True, False]


def test_vector_rejects_wrong_action_count() -> None:
    env = text_env(ScriptedEnv(combat_state(), [map_state()]))
    with VectorSts2TextEnv([env]) as vector:
        with pytest.raises(ValueError, match="expected 1 actions"):
            vector.step([])
