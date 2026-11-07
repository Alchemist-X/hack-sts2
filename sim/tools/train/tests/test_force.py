"""Forced-choice what-if evaluation (`sts2train.force`, wave 16): on synthetic candidate tables the forced action is
among the selector rows (greedy = the max-logit forced row, uniform = seeded and batch-independent), a decision without
selector rows is untouched (the same tensor), masked / dead slots are never forced, the counts; on the real env an
eval without --force (omitted, None, "none") plays bit-identical actions (8 games x 50 steps), a forced eval replays
and writes consistent counts; the CLI flags."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train.bench import random_obs
from sts2train.force import NEG, ForcedChoice, parse_force


@pytest.fixture(scope="module")
def live():
    from sts2vec import PoolSpec, VecEnv

    from sts2train.features import ModelSpec

    with VecEnv(2, threads=1, pool=PoolSpec(characters=("SILENT",), ascension=10, count=2)) as env:
        return ModelSpec.from_schema(env.schema)


def _obs(spec, n, seed=0):
    return {k: np.ascontiguousarray(v).copy() for k, v in random_obs(spec, n, np.random.default_rng(seed)).items()}


def _set_rows(spec, obs, s, kinds, B=None, oi=None):
    names, C = spec.enum_names["RunActionKind"], spec.cols["cand"]
    obs["cand_n"][s] = len(kinds)
    for j, k in enumerate(kinds):
        obs["cand"][s, j, C["kind"]] = names.index(k)
        obs["cand"][s, j, C["B"]] = -1 if B is None else B[j]
        obs["cand"][s, j, C["option_index"]] = -1 if oi is None else oi[j]


def _rest_obs(live, n=4):
    """slot 0: rest site, HEAL (row 0) + SMITH macro rows 1..3 + leave; slot 1: combat; slot 2: shop with a removal
    row (3); slot 3: rest site again (another logit order)."""
    obs = _obs(live, n, 1)
    cat, rest = list(live.enum_names["ShopCategory"]), list(live.enum_names["RestOption"])
    S, R = live.cols["shop"], live.cols["rest"]
    obs["shop"][:, :, S["category"]] = cat.index("card")
    obs["shop"][:, 3, S["category"]] = cat.index("card_removal")
    obs["rest"][:, :, R["option_id"]] = rest.index("HEAL")
    obs["rest"][:, 1, R["option_id"]] = rest.index("SMITH")
    for s in (0, 3):
        _set_rows(live, obs, s, ["ChooseRestOption"] * 4 + ["LeaveRestSite"], B=[-1, 0, 1, 2, -1], oi=[0, 1, 1, 1, -1])
    _set_rows(live, obs, 1, ["PlayCard"] * 3 + ["EndTurn"])
    _set_rows(live, obs, 2, ["BuyEntry"] * 4 + ["LeaveShop"], oi=[0, 1, 2, 3, -1])
    return obs


def _logits(n=4, w=8):
    z = torch.full((n, w), NEG)
    z[0, :5] = torch.tensor([9.0, 1.0, 3.0, 2.0, 0.5])       # HEAL best; SMITH row 2 the best forced row
    z[1, :4] = torch.tensor([1.0, 4.0, 2.0, 3.0])
    z[2, :5] = torch.tensor([5.0, 4.0, 3.0, 2.0, 1.0])       # removal row 3 not the argmax
    z[3, :5] = torch.tensor([0.0, 7.0, 8.0, 6.0, 1.0])       # policy already smiths (row 2)
    return z


def test_parse_force():
    assert parse_force(None) == "" and parse_force("none") == "" and parse_force("") == ""
    assert parse_force("ChooseRestOption:SMITH, Travel:Elite") == "ChooseRestOption:SMITH,Travel:Elite"
    assert parse_force("BuyEntry:remove") == "BuyEntry:card_removal"


def test_unknown_selectors_fail(live):
    with pytest.raises(ValueError):
        ForcedChoice(live, "ChooseRest")
    with pytest.raises(ValueError):
        ForcedChoice(live, "BuyEntry:SMITH")
    with pytest.raises(ValueError):
        ForcedChoice(live, "")
    with pytest.raises(ValueError):
        ForcedChoice(live, "ChooseRestOption:SMITH", mode="random")


def test_greedy_forces_the_max_logit_selector_row(live):
    obs, z = _rest_obs(live), _logits()
    fc = ForcedChoice(live, "ChooseRestOption:SMITH")
    out = fc.apply(obs, z)
    a = out.argmax(-1).tolist()
    assert a[0] == 2, "the max-logit SMITH row, not HEAL"
    assert a[3] == 2 and a[1] == 1 and a[2] == 0, "unforced slots keep the policy's argmax"
    assert torch.equal(out[1], z[1]) and torch.equal(out[2], z[2])
    assert (out[0] > NEG / 2).sum() == 1, "a forced slot keeps one live row"
    t = fc.totals()
    lb = "ChooseRestOption:SMITH"
    assert t["available"][lb] == 2 and t["forced_decisions"][lb] == 2 and t["changed"][lb] == 1
    assert t["policy_took"][lb] == 1 and t["forced_total"] == 2 and t["changed_total"] == 1
    assert t[f"forced_decisions.{lb}"] == 2 and t[f"available.{lb}"] == 2 and t["games_forced"] == 2
    assert fc.per_game(0)["changed_total"] == 1 and fc.per_game(1)["forced_total"] == 0


def test_union_of_selectors_and_removal(live):
    obs, z = _rest_obs(live), _logits()
    fc = ForcedChoice(live, "ChooseRestOption:SMITH,BuyEntry:remove")
    a = fc.apply(obs, z).argmax(-1).tolist()
    assert a == [2, 1, 3, 2]
    t = fc.totals()
    assert t["forced_decisions"] == {"ChooseRestOption:SMITH": 2, "BuyEntry:card_removal": 1}
    assert t["forced_total"] == 3 and t["changed_total"] == 2


def test_absent_is_untouched_and_dead_slots_are_not_forced(live):
    obs, z = _rest_obs(live), _logits()
    fc = ForcedChoice(live, "Travel:Elite")
    assert fc.apply(obs, z) is z and fc.totals()["forced_total"] == 0
    fc = ForcedChoice(live, "ChooseRestOption:SMITH")
    out = fc.apply(obs, z, live=np.array([False, True, True, False]))
    assert out is z and fc.totals()["available"]["ChooseRestOption:SMITH"] == 0
    zm = z.clone()
    zm[0, 1:4] = NEG                       # the model masks every SMITH row of slot 0: nothing to force there
    fc = ForcedChoice(live, "ChooseRestOption:SMITH")
    a = fc.apply(obs, zm).argmax(-1).tolist()
    assert a[0] == 0 and fc.totals()["forced_total"] == 1


def test_uniform_is_seeded_and_among_the_rows(live):
    obs, z = _rest_obs(live), _logits()

    def picks(seed, keys=("a", "b", "c", "d"), reps=60):
        fc = ForcedChoice(live, "ChooseRestOption:SMITH", mode="uniform", seed=seed)
        return [fc.apply(obs, z, keys=list(keys)).argmax(-1).tolist() for _ in range(reps)]

    p = picks(1)
    assert p == picks(1), "same seed, same draws"
    assert p != picks(2)
    s0 = [r[0] for r in p]
    assert set(s0) == {1, 2, 3}, "uniform over every SMITH row"
    assert all(r[1] == 1 and r[2] == 0 for r in p), "unforced slots untouched"
    # a play's draws depend on its key, not its slot: slot 3 with key "a" draws what slot 0 with key "a" drew
    q = picks(1, keys=("x", "b", "c", "a"))
    assert [r[3] for r in q] == s0


def test_observe_counts_only(live):
    obs, z = _rest_obs(live), _logits()
    fc = ForcedChoice(live, "ChooseRestOption:SMITH", mode="observe")
    assert fc.apply(obs, z) is z
    t = fc.totals()
    assert t["available"]["ChooseRestOption:SMITH"] == 2 and t["policy_took"]["ChooseRestOption:SMITH"] == 1
    assert t["forced_total"] == 0


# ------------------------------------------------------------------------------------------------ real env eval


EIGHT = {"name": "f8", "ascension": 10, "content": "ported_only",
         "games": [{"seed": f"W16A10SI0{i}", "character": "SILENT"} for i in range(1, 9)]}


@pytest.fixture(scope="module")
def model_spec():
    from sts2vec import load, schema_of

    from sts2train.features import ModelSpec, bind_spec
    from sts2train.model import Policy

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(3)
    return Policy(spec), spec


def _actions(d: Path):
    return {p.name: json.loads(p.read_text())["history_actions"] for p in sorted((d / "games").glob("*.summary.json"))}


def test_no_force_is_bit_identical(tmp_path, model_spec):
    from sts2train.evaluate import evaluate_model

    model, spec = model_spec
    ss = tmp_path / "f8.json"
    ss.write_text(json.dumps(EIGHT))
    kw = dict(weights_hash="test", threads=2, step_cap=50, loop_breaker=False, log=lambda _: None)
    base = evaluate_model(model, spec, str(ss), str(tmp_path / "base"), **kw)
    runs = {"none": evaluate_model(model, spec, str(ss), str(tmp_path / "none"), force="none", **kw),
            "None": evaluate_model(model, spec, str(ss), str(tmp_path / "None"), force=None, **kw)}
    a0 = _actions(tmp_path / "base")
    assert len(a0) == 8 and all(0 < len(v) <= 50 for v in a0.values()) and sum(map(len, a0.values())) > 200
    for k, rep in runs.items():
        assert _actions(tmp_path / k) == a0, k
        assert "force" not in rep and rep["headline"]["progress_mean"] == base["headline"]["progress_mean"]
        assert "force" not in json.loads((tmp_path / k / "config.json").read_text())


def test_forced_eval_counts_and_replays(tmp_path, model_spec):
    from sts2train.evaluate import evaluate_model

    model, spec = model_spec
    ss = tmp_path / "f8.json"
    ss.write_text(json.dumps(EIGHT))
    kw = dict(weights_hash="test", threads=2, step_cap=50, loop_breaker=False, log=lambda _: None)
    for mode in ("greedy", "uniform"):
        out = tmp_path / mode
        rep = evaluate_model(model, spec, str(ss), str(out), force="Travel", force_mode=mode, force_seed=5, **kw)
        assert rep["headline"]["replayable"] == 8
        fo = rep["force"]
        assert json.loads((out / "report.json").read_text())["force"] == fo
        assert json.loads((out / "config.json").read_text())["force"]["mode"] == mode
        games = [json.loads(p.read_text())["force"] for p in sorted((out / "games").glob("*.summary.json"))]
        assert fo["forced_total"] == sum(g["forced_total"] for g in games) > 0, "every game travels in 50 steps"
        assert fo["forced_decisions"]["Travel"] == fo["available"]["Travel"] == fo["forced_total"]
        assert fo["changed_total"] <= fo["forced_total"]
    again = evaluate_model(model, spec, str(ss), str(tmp_path / "u2"), force="Travel", force_mode="uniform",
                           force_seed=5, **kw)
    assert _actions(tmp_path / "u2") == _actions(tmp_path / "uniform") and again["force"]["forced_total"] > 0


def test_cli_flags(monkeypatch, tmp_path):
    import sts2train.evaluate as E
    from sts2train import cli

    seen = {}

    def fake(*a, **kw):
        seen.update(kw)
        raise SystemExit(0)

    monkeypatch.setattr(E, "evaluate", fake)
    with pytest.raises(SystemExit):
        cli.main(["eval", "--seedset", "x", "--checkpoint", "c.pt", "--out", str(tmp_path / "o"),
                  "--force", "ChooseRestOption:SMITH,Travel:Elite", "--force-mode", "uniform"])
    assert seen["force"] == "ChooseRestOption:SMITH,Travel:Elite" and seen["force_mode"] == "uniform"
    seen.clear()
    with pytest.raises(SystemExit):
        cli.main(["eval", "--seedset", "x", "--checkpoint", "c.pt", "--out", str(tmp_path / "o")])
    assert seen["force"] is None and seen["force_mode"] == "greedy"
