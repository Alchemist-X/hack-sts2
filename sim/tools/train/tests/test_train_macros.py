"""Room macros in the trainer (W3 2026-11-04): ``--macros rest,shop`` reaches the VecEnv, the evals and the checkpoint;
the model spec (and so any checkpoint's compatibility) is the same with macros on or off; with macros on a random
policy closes no rest-site / shop UI revisit cycle (the SMITH / removal prompt is never shown, so never cancelled)."""

from __future__ import annotations

import numpy as np
import pytest

from sts2train.cycles import CycleDetector
from sts2train.evaluate import checkpoint_macros
from sts2train.features import ModelSpec, check_spec_compat
from sts2train.ppo import TrainConfig, env_kwargs
from sts2vec import PoolSpec, VecEnv, random_actions

POOL = PoolSpec(characters=("SILENT",), ascension=10, count=256)


def test_config_plumbing():
    assert TrainConfig().macros == ""
    assert "macros" not in env_kwargs(TrainConfig(), "X"), "default: the VecEnv kwargs of earlier builds"
    assert env_kwargs(TrainConfig(macros="rest,shop"), "X")["macros"] == "rest,shop"
    assert env_kwargs(TrainConfig(macros="rest,shop", start_mode="stream"), "X")["macros"] == "rest,shop"

    class OldEnv:
        def __init__(self, n, *, threads=8):
            pass

    with pytest.raises(ValueError, match="macros"):
        env_kwargs(TrainConfig(macros="rest"), "X", env_cls=OldEnv)
    assert checkpoint_macros(None) == "" and checkpoint_macros({"config": {}}) == ""
    assert checkpoint_macros({"config": {"macros": "rest,shop"}}) == "rest,shop"


def test_spec_identical_with_macros():
    with VecEnv(2, threads=1, pool=POOL) as off, VecEnv(2, threads=1, pool=POOL, macros="rest,shop") as on:
        a, b = ModelSpec.from_schema(off.schema), ModelSpec.from_schema(on.schema)
    assert a.to_json() == b.to_json(), "macros change no model input, enum or encoder_version"
    compat = check_spec_compat(a, b, what="test")
    assert not compat.get("widened_enums") and not compat["other_differences"]


def _cycles(macros, steps: int = 300) -> dict:
    env = VecEnv(64, threads=4, pool=POOL, macros=macros, seed_text="W3PROBE")
    try:
        det = CycleDetector(ModelSpec.from_schema(env.schema))
        rng = np.random.default_rng(0)
        obs = env.reset()
        det.observe(obs)
        for _ in range(steps):
            a = random_actions(rng, obs["cand_n"])
            det.commit(a, obs)
            obs, _, term, trunc, _, _ = env.step(a)
            det.after_step(obs, term | trunc)
        return det.totals_dict()
    finally:
        env.close()


def test_no_rest_or_shop_cycles_with_macros():
    off = _cycles(None)
    on = _cycles("rest,shop")
    assert off["cycles"] >= 1, f"the probe reaches a removal cancel loop without macros: {off}"
    assert on["cycles_rest"] == 0 and on["cycles_shop"] == 0 and on["cycles"] == 0, on
