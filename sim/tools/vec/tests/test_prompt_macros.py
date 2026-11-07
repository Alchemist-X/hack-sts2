"""Prompt macros v2 (config ``macros`` ∋ ``prompt`` / ``"all"``; sim/VEC.md "Prompt macros v2", W4 2026-11-04): no
``PromptCancel`` row at any prompt, a pick that completes the selection confirms in the same step, the rest / shop
macro rows fall back to the plain open row past 64 candidates (status ``MACRO_FALLBACK``). Off (the default) the config
and every output are those of a build without the option."""

from __future__ import annotations

import json

import numpy as np

from sts2vec import STATUS, PoolSpec, VecEnv, random_actions
from sts2vec.pool import make_config

POOL = PoolSpec(characters=("IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT"), ascension=10, count=256)


def test_prompt_macro_config(lib):
    assert STATUS["MACRO_FALLBACK"] == 512
    assert make_config(2, macros="all")["macros"] == ["rest", "shop", "prompt"]
    assert make_config(2, macros="prompt")["macros"] == ["prompt"]
    with VecEnv(2, threads=1, macros="all") as env:
        assert env.macros == ("rest", "shop", "prompt")
    bad = json.dumps(dict(make_config(1), macros=["cancel"])).encode()
    assert lib.sts2vec_create(bad, len(bad)) == -1 and "cancel" in lib.last_error(0)


def _play(macros, steps: int, seed: int = 0) -> dict:
    """Uniform-random play of 64 slots: prompt observations, cancel rows listed, MACRO_FALLBACK, SIM_ERROR counts."""
    env = VecEnv(64, threads=4, pool=POOL, macros=macros, seed_text="W4PROMPT")
    try:
        S = env.schema
        C, G = S.cols("cand"), S.cols("global_i")
        K = S.vocab.enums["RunActionKind"]
        cancel = K.index_of("PromptCancel")
        prompt_phase = S.vocab.enums["RunPhase"].index_of("Prompt")
        rng = np.random.default_rng(seed)
        obs = env.reset()
        out = {"prompt_obs": 0, "cancel_rows": 0, "fallback": 0, "sim_error": 0}
        for _ in range(steps):
            n = obs["cand_n"]
            rows = obs["cand"][:, :, C.kind]
            listed = np.arange(rows.shape[1])[None, :] < n[:, None]
            out["cancel_rows"] += int(((rows == cancel) & listed).sum())
            out["prompt_obs"] += int(((obs["global_i"][:, G.decision_kind] == prompt_phase) & (n > 0)).sum())
            obs, _, _, _, status, _ = env.step(random_actions(rng, n))
            out["fallback"] += int(np.count_nonzero(status & STATUS["MACRO_FALLBACK"]))
            out["sim_error"] += int(np.count_nonzero(status & STATUS["SIM_ERROR"]))
        return out
    finally:
        env.close()


def test_no_cancel_rows_with_prompt_macros(full):
    off = _play(None, 400)
    on = _play("all", 400)
    assert off["cancel_rows"] > 0, f"random play reaches a cancelable prompt without macros: {off}"
    assert off["fallback"] == 0
    assert on["prompt_obs"] > 0 and on["cancel_rows"] == 0, on
    assert on["sim_error"] == 0, on
