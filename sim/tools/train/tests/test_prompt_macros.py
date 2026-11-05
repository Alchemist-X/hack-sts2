"""Prompt macros v2 in the trainer (W4 2026-11-04; sim/VEC.md "Prompt macros v2"): ``--macros all`` (= rest, shop,
prompt) reaches the VecEnv; the model spec is identical with the macros on or off (any checkpoint resumes with them);
with them a uniform-random policy on 64 slots x 2,000 steps closes no UI revisit cycle at ANY screen or prompt kind
(``CycleDetector(scope="all")``, counting only), while without them the same detector finds the cancel loops."""

from __future__ import annotations

import json
import os
from pathlib import Path

import numpy as np
import pytest

from sts2train.cycles import COUNT_KEYS, CycleDetector
from sts2train.features import ModelSpec, check_spec_compat
from sts2train.ppo import TrainConfig, env_kwargs
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions

POOL = PoolSpec(characters=("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER"), ascension=10, count=512)
STEPS_ON = int(os.environ.get("STS2_PROMPT_CYCLE_STEPS", "2000"))


def test_config_and_spec():
    assert env_kwargs(TrainConfig(macros="rest,shop,prompt"), "X")["macros"] == "rest,shop,prompt"
    with VecEnv(2, threads=1, pool=POOL) as off, VecEnv(2, threads=1, pool=POOL, macros="all") as on:
        assert on.macros == ("rest", "shop", "prompt")
        a, b = ModelSpec.from_schema(off.schema), ModelSpec.from_schema(on.schema)
    assert a.to_json() == b.to_json(), "prompt macros change no model input, enum or encoder_version"
    compat = check_spec_compat(a, b, what="test")
    assert not compat.get("widened_enums") and not compat["other_differences"]


def test_scope_validation_and_room_keys():
    with VecEnv(2, threads=1, pool=POOL) as env:
        spec = ModelSpec.from_schema(env.schema)
    with pytest.raises(ValueError, match="scope"):
        CycleDetector(spec, scope="everything")
    assert tuple(CycleDetector(spec).totals_dict()) == COUNT_KEYS, "room scope: the counters of earlier builds"
    assert "cycles_other" in CycleDetector(spec, scope="all").totals_dict()


def _run(macros, steps: int, seed: int = 0) -> dict:
    env = VecEnv(64, threads=4, pool=POOL, macros=macros, seed_text="W4CYCLE")
    try:
        spec = ModelSpec.from_schema(env.schema)
        det_all = CycleDetector(spec, scope="all")
        det_room = CycleDetector(spec)
        rng = np.random.default_rng(seed)
        obs = env.reset()
        det_all.observe(obs)
        det_room.observe(obs)
        S = env.schema
        G = S.cols("global_i")
        prompt_phase = S.vocab.enums["RunPhase"].index_of("Prompt")
        pk_names = [S.vocab.enums["ChoicePromptKind"].name(i) for i in range(len(S.vocab.enums["ChoicePromptKind"]))]
        kinds: dict[str, int] = {}
        fallback = sim_err = over = episodes = 0
        for _ in range(steps):
            n = obs["cand_n"]
            at = (obs["global_i"][:, G.decision_kind] == prompt_phase) & (n > 0)
            for k in obs["global_i"][at, G.prompt_kind].tolist():
                kinds[pk_names[k]] = kinds.get(pk_names[k], 0) + 1
            over += int(np.count_nonzero(n > 64))
            a = random_actions(rng, n)
            det_all.commit(a, obs)
            det_room.commit(a, obs)
            obs, _, term, trunc, status, _ = env.step(a)
            done = term | trunc
            episodes += int(np.count_nonzero(done))
            det_all.after_step(obs, done)
            det_room.after_step(obs, done)
            fallback += int(np.count_nonzero(status & STATUS["MACRO_FALLBACK"]))
            sim_err += int(np.count_nonzero(status & STATUS["SIM_ERROR"]))
        return {"macros": macros, "steps": steps, "slots": 64, "all": det_all.totals_dict(),
                "room": det_room.totals_dict(), "prompt_decisions": kinds, "macro_fallback": fallback,
                "cand_over_max_rows": over, "sim_error": sim_err, "episodes_done": episodes,
                "events_all": det_all.events[:5]}
    finally:
        env.close()


def test_no_ui_cycles_of_any_kind_with_macros_all():
    off = _run(None, 600)
    on = _run("all", STEPS_ON)
    rec = json.dumps({"off": off, "on": on}, default=str)
    print("PROMPT_CYCLE_CHECK " + rec)
    out = Path(__file__).resolve().parents[4] / "tmp"
    if out.is_dir():                        # the testbox's persisted tmp/ (scripts/test_remote.sh)
        (out / "prompt-macros").mkdir(exist_ok=True)
        (out / "prompt-macros" / "cycle_check.json").write_text(rec)
    assert off["all"]["cycles"] >= 1, f"the all-scope detector finds the cancel loops without macros: {off['all']}"
    assert off["all"]["cycles"] >= off["room"]["cycles"], "scope all is a superset of the room scope"
    assert on["all"]["cycles"] == 0, on["all"]
    assert on["room"]["cycles"] == 0, on["room"]
    assert on["sim_error"] == 0
    assert sum(on["prompt_decisions"].values()) > 0, "prompts were decided"
