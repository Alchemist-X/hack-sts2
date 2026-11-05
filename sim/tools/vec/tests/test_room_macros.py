"""Room macros (config ``macros``; sim/VEC.md "Room macros", W3 2026-11-04): with ``rest`` a rest site lists one
``ChooseRestOption(A, B = deck card)`` row per upgradable card instead of SMITH, with ``shop`` a shop lists one
``BuyEntry(A, B = deck card)`` row per removable card instead of the card removal; each runs open -> select -> confirm
in one step. Off (the default) the config and every output are those of a build without the option."""

from __future__ import annotations

import json

import numpy as np
import pytest

from conftest import SMALL_POOL
from sts2vec import PoolSpec, Sts2VecError, VecEnv, random_actions
from sts2vec.evaluate import evaluate
from sts2vec.pool import MACRO_NAMES, make_config, parse_macros

POOL = PoolSpec(characters=("SILENT",), ascension=10, count=256)


def test_macros_config_and_parse(lib):
    assert MACRO_NAMES == ("rest", "shop", "prompt")
    assert parse_macros("all") == ("rest", "shop", "prompt") and parse_macros("shop,ALL") == MACRO_NAMES
    assert parse_macros(None) == () and parse_macros("") == () and parse_macros("none") == ()
    assert parse_macros("shop,REST") == ("rest", "shop") and parse_macros(["shop", "shop"]) == ("shop",)
    with pytest.raises(ValueError, match="unknown macro"):
        parse_macros("rest,event")
    assert "macros" not in make_config(2), "off: the key is left out (the config of every earlier build)"
    assert "macros" not in make_config(2, macros="")
    assert make_config(2, macros="rest,shop")["macros"] == ["rest", "shop"]
    with VecEnv(2, threads=1, pool=SMALL_POOL) as env:
        assert env.macros == () and "macros" not in env.config
    with VecEnv(2, threads=1, pool=SMALL_POOL, macros=("rest", "shop")) as env:
        assert env.macros == ("rest", "shop") and env.config["macros"] == ["rest", "shop"]
    bad = json.dumps(dict(make_config(1), macros=["event"])).encode()
    assert lib.sts2vec_create(bad, len(bad)) == -1 and "event" in lib.last_error(0)


def _play(macros, steps: int = 300, seed: int = 0, pick_macro: bool = False):
    """Uniform-random play of 64 Silent A10 slots; with `pick_macro` a slot that lists a shop macro row takes it once
    (checked: gold - price, deck - 1). -> (schema, macro rows seen {kind: count}, checks done)."""
    env = VecEnv(64, threads=4, pool=POOL, macros=macros, seed_text="W3MACRO")
    try:
        S = env.schema
        C, G, SH = S.cols("cand"), S.cols("global_i"), S.cols("shop")
        K = S.vocab.enums["RunActionKind"]
        kinds = {K.index_of("ChooseRestOption"): "rest", K.index_of("BuyEntry"): "shop"}
        rng = np.random.default_rng(seed)
        obs = env.reset()
        seen = {"rest": 0, "shop": 0}
        checks = 0
        for _ in range(steps):
            a = random_actions(rng, obs["cand_n"])
            probe = []
            for i in range(env.num_envs):
                rows = obs["cand"][i, : obs["cand_n"][i]]
                for k, name in kinds.items():
                    m = (rows[:, C.kind] == k) & (rows[:, C.B] >= 0)
                    if m.any():
                        seen[name] += 1
                        j = np.flatnonzero(m)
                        deck = obs["deck"][i]
                        # card_id = the deck row B's card
                        assert (rows[j, C.card_id] == deck[rows[j, C.B], S.col("deck", "card_id")]).all()
                        # appended after the screen's other candidates
                        assert j.min() == len(rows) - len(j) and (np.diff(j) == 1).all()
                        if pick_macro and name == "shop" and len(probe) < 4:
                            r = int(j[0])
                            price = int(obs["shop"][i, rows[r, C.option_index], SH.price])
                            probe.append((i, int(obs["global_i"][i, G.gold]), int(obs["global_i"][i, G.deck_n]), price))
                            a[i] = r
            obs, _, term, trunc, status, _ = env.step(a)
            assert not (status & 8).any(), env.slot_errors() if hasattr(env, "slot_errors") else "SIM_ERROR"
            for i, gold, deck_n, price in probe:
                assert not term[i] and not trunc[i]
                assert int(obs["global_i"][i, G.gold]) == gold - price, "the removal price is paid"
                assert int(obs["global_i"][i, G.deck_n]) == deck_n - 1, "one card removed"
                assert int(obs["global_i"][i, G.decision_kind]) != S.vocab.enums["RunPhase"].index_of("Prompt")
                checks += 1
        return S, seen, checks
    finally:
        env.close()


def test_macro_rows_appear_and_pay(full):
    S_off, seen_off, _ = _play(None)
    assert seen_off == {"rest": 0, "shop": 0}, "macros off: no ChooseRestOption / BuyEntry row has B >= 0"
    S_on, seen_on, checks = _play("rest,shop", pick_macro=True)
    assert seen_on["shop"] > 0, seen_on
    assert checks > 0, "a shop macro was taken and checked"
    assert S_on.encoder_version == S_off.encoder_version, "the schema does not depend on the macros"


def test_eval_records_macros_and_replays(full, tmp_path):
    ss = {"name": "macro-mini", "ascension": 10, "content": "ported_only",
          "games": [{"character": "SILENT", "seed": f"W3MAC{i}"} for i in range(4)]}
    rep = evaluate(ss, "random", tmp_path / "on", threads=2, step_cap=200, macros="rest,shop", log=lambda m: None)
    cfg = json.loads((tmp_path / "on" / "config.json").read_text())
    assert cfg["limits"]["macros"] == ["rest", "shop"]
    assert rep["headline"]["replayable"] == 4, "the replay audit uses the same macros"
    evaluate(ss, "random", tmp_path / "off", threads=2, step_cap=200, log=lambda m: None)
    assert "macros" not in json.loads((tmp_path / "off" / "config.json").read_text())["limits"]
