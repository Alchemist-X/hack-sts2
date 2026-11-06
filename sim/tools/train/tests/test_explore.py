"""Exploration for rare decision kinds (`sts2train.explore`, wave 9): kind / selector parsing and validation, the
mixture draw on synthetic candidate tables (draws only at decisions with an explore row, uniform over those rows,
never on masked / illegal rows, stored log-prob = log((1 - E) pi + E u) exactly, decoupled weights (the default), the
mixture start ratios / explore_mix_clip0, no host copy on steps without explore rows, policy mode),
the pure (update, t, slot) draw stream, the wave-10 selectors (Travel:<MapPointType> rows from the map pointer, checked
against the real env's travel; ProceedRewards:skipcard only next to a card row; unknown names refused), the config / CLI plumbing, that evaluation never explores, eps 0 = the
default path, --overlap double reproduces off under a greedy policy with the explorer on, and a resumed run is
deterministic."""

from __future__ import annotations

import inspect
import json
import math
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train import ppo
from sts2train.bench import load_spec, random_obs
from sts2train.explore import KindExplorer, format_kinds, parse_kinds
from sts2train.ppo import InferenceRunner, resolve_config, train
from sts2train.runlog import read_jsonl

DATA = Path(__file__).parent / "data"
EPS = 0.5


@pytest.fixture(scope="module")
def spec():
    return load_spec(str(DATA / "spec-fc2eb34c07bc8513.json"))[0]


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


# ------------------------------------------------------------------------------------------------ parsing / config


def test_parse_and_format():
    assert parse_kinds("") == () and parse_kinds(None) == () and parse_kinds("none") == ()
    assert parse_kinds(" ChooseRestOption , BuyEntry:remove ,ChooseRestOption") == \
        (("ChooseRestOption", None), ("BuyEntry", "card_removal"))
    assert format_kinds(parse_kinds("BuyEntry:macro,ChooseRestOption:SMITH")) == "BuyEntry:macro,ChooseRestOption:SMITH"
    for bad in ("BuyEntry:", ":x", "BuyEntry:a:b"):
        with pytest.raises(ValueError):
            parse_kinds(bad)


def test_resolve_config_rules():
    c = resolve_config({})
    assert c.explore_kinds == "" and c.explore_eps == 0.0 and c.explore_logp == "decoupled"
    c = resolve_config({"explore_kinds": "ChooseRestOption, BuyEntry:remove", "explore_eps": 0.3})
    assert c.explore_kinds == "ChooseRestOption,BuyEntry:card_removal" and c.explore_eps == 0.3
    for bad in ({"explore_eps": 0.3}, {"explore_kinds": "BuyEntry", "explore_eps": 1.0},
                {"explore_kinds": "BuyEntry", "explore_eps": -0.1}, {"explore_logp": "x"},
                {"explore_kinds": "BuyEntry"},                          # kinds without eps: the control arm
                {"explore_kinds": "BuyEntry", "explore_eps": 0.3, "explore_logp": "mixture"},  # >= clip / (1 + clip)
                {"explore_kinds": "BuyEntry", "explore_eps": 0.2 / 1.2, "explore_logp": "mixture"}):
        with pytest.raises(ValueError):
            resolve_config(bad)
    base = ppo.config_dict(resolve_config({"explore_kinds": "BuyEntry", "explore_eps": 0.2}))
    assert resolve_config({}, base).explore_eps == 0.2, "a resume inherits the explorer"
    off = resolve_config({"explore_kinds": ""}, base)
    assert off.explore_kinds == "" and off.explore_eps == 0.0, "--explore-kinds none turns it off on a resume"
    assert resolve_config({"explore_eps": 0.0}, base).explore_eps == 0.0
    off = resolve_config({"explore_kinds": "BuyEntry", "explore_eps": 0.0})
    assert off.explore_kinds == "" and off.explore_eps == 0.0, "an explicit eps 0 is off, kinds dropped"
    assert resolve_config({"explore_kinds": "BuyEntry", "explore_eps": 0.15, "explore_logp": "mixture"}).explore_eps \
        == 0.15, "mixture below clip / (1 + clip)"
    assert resolve_config({"explore_kinds": "BuyEntry", "explore_eps": 0.2, "explore_logp": "mixture",
                           "clip": 0.3}).explore_eps == 0.2, "the limit follows --clip"
    assert resolve_config({"explore_kinds": "BuyEntry", "explore_eps": 0.3}).explore_logp == "decoupled"


def test_unknown_names_fail(spec, live):
    with pytest.raises(ValueError, match="RunActionKind"):
        KindExplorer(spec, "ChooseRest", 0.3, 1)
    with pytest.raises(ValueError, match="selector"):
        KindExplorer(live, "BuyEntry:SMITH", 0.3, 1)
    with pytest.raises(ValueError, match="selector"):
        KindExplorer(live, "EndTurn:card_removal", 0.3, 1)
    with pytest.raises(ValueError):
        KindExplorer(spec, "BuyEntry", 0.0, 1)
    assert "SMITH" in live.enum_names["RestOption"] and "card_removal" in live.enum_names["ShopCategory"]
    KindExplorer(live, "ChooseRestOption:SMITH,BuyEntry:remove,BuyEntry:macro,ChooseRestOption:plain", 0.3, 1)


def test_cli_flags(monkeypatch, tmp_path):
    from sts2train import cli

    seen = {}

    def fake_plan(given, resume, log=print):
        seen.update(given)
        raise ValueError("stop")

    monkeypatch.setattr(ppo, "plan_run", fake_plan)
    rc = cli.main(["train", "--out", str(tmp_path / "r"), "--explore-kinds", "ChooseRestOption,BuyEntry:remove",
                   "--explore-eps", "0.3", "--explore-logp", "decoupled"])
    assert rc == 2 and seen["explore_kinds"] == "ChooseRestOption,BuyEntry:remove" and seen["explore_eps"] == 0.3
    assert seen["explore_logp"] == "decoupled"
    seen.clear()
    cli.main(["train", "--out", str(tmp_path / "r"), "--explore-kinds", "none"])
    assert seen["explore_kinds"] == ""


# ------------------------------------------------------------------------------------------------ the mixture draw


def _step(ex, obs, logits, *, update=1, t=0, lo=0, n_total=None, seed=0):
    torch.manual_seed(seed)
    dist = torch.distributions.Categorical(logits=logits, validate_args=False)
    a = dist.sample()
    lp = dist.log_prob(a)
    a2, lp2, w = ex(obs, logits, dist, a, lp, update=update, t=t, lo=lo, n_total=n_total)
    return a, lp, a2, lp2, w, dist


def _table(spec, n=64, seed=0):
    """Even slots: a rest site (HEAL-like row 0 + 4 'macro' rows of ChooseRestOption + LeaveRestSite); odd slots: a
    combat (PlayCard x 3 + EndTurn), no explore row."""
    obs = _obs(spec, n, seed)
    for s in range(n):
        if s % 2 == 0:
            _set_rows(spec, obs, s, ["ChooseRestOption"] * 5 + ["LeaveRestSite"], B=[-1, 0, 1, 2, 3, -1])
        else:
            _set_rows(spec, obs, s, ["PlayCard"] * 3 + ["EndTurn"])
    w = 8
    lg = torch.full((n, w), -1e9)
    g = torch.Generator().manual_seed(seed)
    for s in range(n):
        k = int(obs["cand_n"][s])
        lg[s, :k] = torch.randn(k, generator=g) * 3
        if s % 2 == 0:
            lg[s, 1:5] -= 60.0          # the macro rows at ~0 probability (the audit's P(SMITH) = 0.000)
    return obs, lg


def test_mixture_draws_and_logp_exact(spec):
    obs, lg = _table(spec)
    ex = KindExplorer(spec, "ChooseRestOption", EPS, 123, logp_mode="mixture")
    n = lg.shape[0]
    fired_total, picks = 0, []
    for t in range(40):
        a, lp, a2, lp2, w, dist = _step(ex, obs, lg, t=t, seed=t)
        assert w is None
        u = ex.uniforms(1, t, n)
        p = torch.softmax(lg.double(), -1)
        for s in range(n):
            if s % 2 == 1:          # no explore row: the policy's draw and log-prob, untouched
                assert int(a2[s]) == int(a[s]) and float(lp2[s]) == float(lp[s])
                continue
            fire = u[0, s] < EPS
            if fire:
                assert int(a2[s]) == min(int(u[1, s] * 5), 4), "the k-th explore row, uniform over the 5"
                fired_total += 1
                picks.append(int(a2[s]))
            else:
                assert int(a2[s]) == int(a[s])
            j = int(a2[s])
            pi = float(p[s, j])
            uu = 1.0 / 5 if j < 5 else 0.0
            want = math.log((1 - EPS) * pi + EPS * uu)
            assert float(lp2[s]) == pytest.approx(want, abs=1e-5, rel=1e-6), (s, j)
            assert int(a2[s]) < int(obs["cand_n"][s])
    st = ex.pop_stats()
    assert st["explore_steps"] == fired_total > 0
    assert st["explore_decisions"] == 40 * n // 2 and st["explore_share"] == pytest.approx(0.5)
    assert st["explore_drawn_macro.ChooseRestOption"] == sum(1 for j in picks if 1 <= j <= 4)
    assert st["explore_taken_macro.ChooseRestOption"] >= st["explore_drawn_macro.ChooseRestOption"] > 0
    assert st["explore_pmass_macro.ChooseRestOption"] < 1e-9, "pi on the macro rows stays ~0 (only exploration)"
    counts = np.bincount(picks, minlength=5)
    assert counts.min() > 0.5 * counts.mean(), counts      # uniform over the 5 explore rows
    assert ex.pop_stats()["explore_steps"] == 0             # popped


def test_masked_and_illegal_rows_are_never_explored(spec):
    obs, lg = _table(spec, n=16)
    lg[0, 2] = -1e9                     # a loop-breaker ban on a macro row
    obs["cand_n"][2] = 3                # rows 3, 4 of slot 2 are past cand_n (the model masks them)
    lg[2, 3:] = -1e9
    ex = KindExplorer(spec, "ChooseRestOption", 0.999, 5)
    for t in range(60):
        _, _, a2, lp2, _, _ = _step(ex, obs, lg, t=t, seed=t)
        assert int(a2[0]) != 2 and int(a2[2]) < 3
        assert torch.isfinite(lp2).all()


def test_decoupled_and_policy_modes(spec):
    obs, lg = _table(spec, n=32)
    for mode in ("decoupled", "policy"):
        ex = KindExplorer(spec, "ChooseRestOption", EPS, 9, logp_mode=mode)
        ref = KindExplorer(spec, "ChooseRestOption", EPS, 9, logp_mode="mixture")
        a, lp, a2, lp2, w, dist = _step(ex, obs, lg, t=3, seed=1)
        _, _, a3, lpb, _, _ = _step(ref, obs, lg, t=3, seed=1)
        assert torch.equal(a2, a3), "the same draws in every mode"
        want_pi = dist.logits.gather(1, a2[:, None])[:, 0]
        assert torch.allclose(lp2, want_pi, atol=1e-6)
        if mode == "decoupled":
            assert np.allclose(w, np.exp((want_pi - lpb).numpy()), rtol=1e-5)
            assert np.all(w[1::2] == 1.0)
        else:
            assert w is None


def test_start_ratios_and_clip0(spec):
    """The mixture mode's start ratio pi / b: 1 / (1 - E) on a policy-drawn non-explore row (outside the 0.2 clip at
    E = 0.5), ~0 on an explored ~0-probability row; explore_mix_clip0 counts both. Decoupled: w = that ratio, stored
    log-prob = log pi_old (start ratio 1)."""
    obs, lg = _table(spec, n=32)
    ex = KindExplorer(spec, "ChooseRestOption", EPS, 9)
    assert ex.logp_mode == "decoupled"
    out, n_out, n_pres = [], 0, 0
    for t in range(20):
        _, _, a2, lp2, w, dist = _step(ex, obs, lg, t=t, seed=t)
        pi = torch.softmax(lg.double(), -1).gather(1, a2[:, None].long())[:, 0].numpy()
        for s in range(0, 32, 2):
            j = int(a2[s])
            if j == 5:              # LeaveRestSite: not an explore row
                assert w[s] == pytest.approx(1 / (1 - EPS), rel=1e-5)
            elif 1 <= j <= 4:       # an explored macro row at ~0 probability
                assert w[s] < 1e-6
            out.append(abs(float(w[s]) - 1) > 0.2)
        assert np.all(w[1::2] == 1.0) and torch.allclose(lp2.double(), torch.from_numpy(np.log(pi)), atol=1e-4)
    st = ex.pop_stats()
    assert st["explore_mix_clip0"] == pytest.approx(sum(out) / len(out)) and st["t_explore_s"] >= 0


def test_no_host_copy_without_explore_rows(spec):
    """A step with no explore row in any slot (every combat step) never touches the distribution / device."""
    obs = _obs(spec, 8)
    for s in range(8):
        _set_rows(spec, obs, s, ["PlayCard"] * 3 + ["EndTurn"])

    class NoTouch:
        def __getattr__(self, k):
            raise AssertionError(f"dist.{k} read on a step without explore rows")

    for mode in ("decoupled", "mixture", "policy"):
        ex = KindExplorer(spec, "ChooseRestOption,BuyEntry", EPS, 1, logp_mode=mode)
        a, lp = torch.zeros(8, dtype=torch.long), torch.zeros(8)
        a2, lp2, w = ex(obs, torch.zeros(8, 8), NoTouch(), a, lp, update=1, t=0)
        assert a2 is a and lp2 is lp and (w is None) == (mode != "decoupled")
        st = ex.pop_stats()
        assert st["explore_decisions"] == 0 and st["explore_share"] == 0 and st["explore_mix_clip0"] is None


def test_selectors(live):
    obs = _obs(live, 4, 1)
    cat = list(live.enum_names["ShopCategory"])
    rest = list(live.enum_names["RestOption"])
    S, R = live.cols["shop"], live.cols["rest"]
    obs["shop"][:, :, S["category"]] = cat.index("card")
    obs["shop"][:, 3, S["category"]] = cat.index("card_removal")
    obs["rest"][:, :, R["option_id"]] = rest.index("HEAL")
    obs["rest"][:, 1, R["option_id"]] = rest.index("SMITH")
    # slot 0: shop: buy entries 0..2, removal macro rows (A = 3, B = deck rows), leave
    _set_rows(live, obs, 0, ["BuyEntry"] * 5 + ["LeaveShop"], B=[-1, -1, -1, 0, 1, -1], oi=[0, 1, 2, 3, 3, -1])
    # slot 1: shop without macros: the plain removal row (A = 3)
    _set_rows(live, obs, 1, ["BuyEntry"] * 4 + ["LeaveShop"], oi=[0, 1, 2, 3, -1])
    # slot 2: rest site: HEAL (A = 0), SMITH macro rows (A = 1, B = deck rows)
    _set_rows(live, obs, 2, ["ChooseRestOption"] * 4 + ["LeaveRestSite"], B=[-1, 0, 1, 2, -1], oi=[0, 1, 1, 1, -1])
    # slot 3: rest site without macros: HEAL, plain SMITH
    _set_rows(live, obs, 3, ["ChooseRestOption"] * 2 + ["LeaveRestSite"], oi=[0, 1, -1])
    ex = KindExplorer(live, "BuyEntry:remove,BuyEntry:macro,ChooseRestOption:SMITH,ChooseRestOption:plain", 0.3, 1)
    rows, macro = ex.item_rows(obs, 8)
    got = [[np.flatnonzero(rows[i, s]).tolist() for s in range(4)] for i in range(4)]
    assert got[0] == [[3, 4], [3], [], []]                 # BuyEntry:remove (macro or plain)
    assert got[1] == [[3, 4], [], [], []]                  # BuyEntry:macro
    assert got[2] == [[], [], [1, 2, 3], [1]]              # ChooseRestOption:SMITH
    assert got[3] == [[], [], [0], [0, 1]]                 # ChooseRestOption:plain
    assert macro[0].tolist()[:6] == [False, False, False, True, True, False]


# ------------------------------------------------------------------------------------------------ wave 10 selectors


def _map(live, obs, s, cells):
    """Slot s's map: `cells` = [(row, col, MapPointType name, legal_next)], every other cell empty."""
    M, pts = live.cols["map_nodes"], list(live.enum_names["MapPointType"])
    obs["map_nodes"][s] = 0
    for r, c, name, legal in cells:
        obs["map_nodes"][s, r, c, M["point_type"]] = pts.index(name)
        obs["map_nodes"][s, r, c, M["legal_next"]] = int(legal)


def _travel_obs(live, n=3):
    obs = _obs(live, n, 2)
    # slot 0: legal (col, row) order = (0, 5) RestSite, (1, 2) Elite, (3, 2) Monster, (5, 2) Elite; an Elite at (2, 3)
    # that is not legal; Travel A = the rank in that order
    _map(live, obs, 0, [(2, 3, "Monster", 1), (2, 1, "Elite", 1), (5, 0, "RestSite", 1), (2, 5, "Elite", 1),
                        (3, 2, "Elite", 0)])
    _set_rows(live, obs, 0, ["Travel"] * 4, oi=[0, 1, 2, 3])
    # slot 1: no Elite destination (Monster, Unknown); an Elite elsewhere on the map
    _map(live, obs, 1, [(1, 0, "Monster", 1), (1, 2, "Unknown", 1), (2, 2, "Elite", 0)])
    _set_rows(live, obs, 1, ["Travel"] * 2, oi=[0, 1])
    # slot 2: a combat (the map has a legal-looking Elite cell, but no Travel row)
    _map(live, obs, 2, [(1, 1, "Elite", 1)])
    _set_rows(live, obs, 2, ["PlayCard"] * 3 + ["EndTurn"])
    return obs


def test_travel_selector_rows(live):
    obs = _travel_obs(live)
    ex = KindExplorer(live, "Travel:Elite,Travel:RestSite,Travel:Unknown,Travel:Monster,Travel", 0.3, 1)
    rows, _ = ex.item_rows(obs, 8)
    got = [[np.flatnonzero(rows[i, s]).tolist() for s in range(3)] for i in range(5)]
    assert got[0] == [[1, 3], [], []]                  # Travel:Elite
    assert got[1] == [[0], [], []]                     # Travel:RestSite
    assert got[2] == [[], [1], []]                     # Travel:Unknown
    assert got[3] == [[2], [0], []]                    # Travel:Monster
    assert got[4] == [[0, 1, 2, 3], [0, 1], []]        # Travel (every row)
    pts = list(live.enum_names["MapPointType"])
    oi = obs["cand"][:, :8, live.cols["cand"]["option_index"]].astype(np.int64)
    tr = (obs["cand"][:, :8, live.cols["cand"]["kind"]] == ex.travel_id) & (np.arange(8)[None] < obs["cand_n"][:, None])
    d = ex.travel_dest(obs, oi, tr)
    assert [pts[int(v)] for v in d[0, :4]] == ["RestSite", "Elite", "Monster", "Elite"] and (d[2] == -1).all()


def test_travel_elite_draw(live):
    """Travel:Elite at E: decisions with an Elite destination take one with P >= E (uniform among them) even when the
    policy gives them ~0; decisions without one keep the policy's draw."""
    base = _travel_obs(live)
    n = 64
    obs = {k: np.ascontiguousarray(np.concatenate([v[i % 2: i % 2 + 1] for i in range(n)])) for k, v in base.items()}
    lg = torch.full((n, 8), -1e9)
    lg[0::2, :4] = torch.tensor([0.0, -60.0, 0.0, -60.0])      # slot-0 pattern: the elites at ~0
    lg[1::2, :2] = 0.0
    ex = KindExplorer(live, "Travel:Elite", 0.3, 11)
    elite, picks = 0, []
    for t in range(60):
        a, lp, a2, lp2, w, _ = _step(ex, obs, lg, t=t, seed=t)
        assert torch.equal(a2[1::2], a[1::2]) and torch.equal(lp2[1::2], lp[1::2])
        e = [int(j) for j in a2[0::2] if int(j) in (1, 3)]
        elite += len(e)
        picks += e
    share = elite / (60 * n // 2)
    assert 0.25 < share < 0.35, share
    assert abs(picks.count(1) - picks.count(3)) < 0.3 * len(picks)
    st = ex.pop_stats()
    assert st["explore_present.Travel:Elite"] == 60 * n // 2 and st["explore_taken.Travel:Elite"] == elite
    assert st["explore_drawn.Travel:Elite"] == elite and st["explore_pmass.Travel:Elite"] < 1e-9


def test_proceed_skipcard_rows(spec, live):
    obs = _obs(live, 4, 3)
    _set_rows(live, obs, 0, ["TakeRewardCard"] * 3 + ["ClaimReward", "ProceedRewards"], B=[0, 1, 2, -1, -1],
              oi=[0, 1, 2, 1, -1])
    _set_rows(live, obs, 1, ["ClaimReward", "ProceedRewards"], oi=[0, -1])       # only gold left: proceed is no skip
    _set_rows(live, obs, 2, ["PickRewardCard"] * 3 + ["RewardAlternative"], oi=[0, 1, 2, 0])   # card screen
    _set_rows(live, obs, 3, ["PickRewardCard"] * 2 + ["ProceedRewards"], oi=[0, 1, -1])
    ex = KindExplorer(live, "ProceedRewards:skipcard,ProceedRewards", 0.3, 1)
    rows, _ = ex.item_rows(obs, 8)
    got = [[np.flatnonzero(rows[i, s]).tolist() for s in range(4)] for i in range(2)]
    assert got[0] == [[4], [], [], [2]]                # ProceedRewards:skipcard
    assert got[1] == [[4], [1], [], [2]]               # ProceedRewards (also the no-skip decision)
    assert parse_kinds("ProceedRewards:skipcard") == (("ProceedRewards", "skipcard"),)
    KindExplorer(spec, "ProceedRewards:skipcard", 0.3, 1)      # pre-macro library: PickRewardCard only
    # the draw: skips the card with P = E at the skipcard decisions (pi gives ProceedRewards ~0), slot 1 untouched
    lg = torch.full((4, 8), -1e9)
    lg[0, :5] = torch.tensor([0.0, 0.0, 0.0, 0.0, -60.0])
    lg[1, :2] = torch.tensor([0.0, -60.0])
    lg[2, :4] = 0.0
    lg[3, :3] = torch.tensor([0.0, 0.0, -60.0])
    ex = KindExplorer(live, "ProceedRewards:skipcard", 0.3, 4)
    skips = 0
    for t in range(400):
        a, _, a2, _, _, _ = _step(ex, obs, lg, t=t, seed=t)
        assert int(a2[1]) == int(a[1]) == 0 and int(a2[2]) == int(a[2])
        skips += int(a2[0] == 4) + int(a2[3] == 2)
    assert 0.25 < skips / 800 < 0.35, skips


def test_wave10_unknown_names_fail(spec, live):
    for bad in ("Travel:Elit", "Travel:Unassigned", "Travel:elite", "Travel:SMITH"):
        with pytest.raises(ValueError, match="MapPointType"):
            KindExplorer(live, bad, 0.3, 1)
    with pytest.raises(ValueError, match="selector"):
        KindExplorer(spec, "Travel:Elite", 0.3, 1)       # a spec without MapPointType names
    for bad in ("ProceedRewards:skip", "BuyEntry:skipcard", "Travel:skipcard", "ChooseRestOption:Elite"):
        with pytest.raises(ValueError, match="selector"):
            KindExplorer(live, bad, 0.3, 1)
    assert resolve_config({"explore_kinds": "Travel:Elite,ProceedRewards:skipcard", "explore_eps": 0.3}).explore_kinds \
        == "Travel:Elite,ProceedRewards:skipcard"


def test_travel_destination_matches_the_env():
    """Heuristic play with uniform travel on the real env: every Travel row maps to a live node, after taking a
    Travel row the slot's current node has the point type `travel_dest` predicted (the A -> (col, row) rank mapping
    is the env's), and ProceedRewards:skipcard rows appear on real rewards screens with a card."""
    from sts2vec import PoolSpec, VecEnv
    from sts2vec.policies import HeuristicPolicy, PolicyContext

    from sts2train.features import ModelSpec

    n = 32
    with VecEnv(n, threads=2, pool=PoolSpec(characters=("SILENT",), ascension=10, count=16, seed_prefix="TEXTR"),
                seed_text="TEXTR") as env:
        live = ModelSpec.from_schema(env.schema)
        ex = KindExplorer(live, "Travel:Elite,ProceedRewards:skipcard", 0.3, 1)
        C, M = live.cols["cand"], live.cols["map_nodes"]
        pol = HeuristicPolicy()
        ctx = PolicyContext(schema=env.schema, num_envs=n, live=np.ones(n, bool))
        proceed = live.enum_names["RunActionKind"].index("ProceedRewards")
        obs = env.reset()
        rng = np.random.default_rng(7)
        checked = skip_decisions = 0
        seen = set()
        ar = np.arange(n)
        for _ in range(1200):
            w = obs["cand"].shape[1]
            kind = obs["cand"][:, :, C["kind"]]
            tr = (kind == ex.travel_id) & (np.arange(w)[None] < obs["cand_n"][:, None])
            d = ex.travel_dest(obs, obs["cand"][:, :, C["option_index"]].astype(np.int64), tr)
            assert (d[tr] > 0).all(), "every Travel row points at a node"
            seen.update(int(v) for v in d[tr])
            rows, _ = ex.item_rows(obs, w)
            sk = rows[1].any(1)
            assert (kind[rows[1]] == proceed).all()
            skip_decisions += int(sk.sum())
            act = np.asarray(pol(obs, ctx)).astype(np.int64)
            ntr = tr.sum(1)
            for s in np.flatnonzero(ntr > 0):              # uniform over the destinations (not the first column)
                act[s] = int(np.flatnonzero(tr[s])[rng.integers(0, ntr[s])])
            took = (ntr > 0) & (act >= 0)
            want = np.where(took, d[ar, np.clip(act, 0, w - 1)], -1)
            obs, _, term, trunc, _, _ = env.step(act.astype(np.int32))
            done = (term | trunc).astype(bool)
            for s in np.flatnonzero(took & ~done):
                cur = obs["map_nodes"][s][..., M["current"]] != 0
                assert cur.sum() == 1
                assert int(obs["map_nodes"][s][..., M["point_type"]][cur][0]) == int(want[s]), s
                checked += 1
        pts = live.enum_names["MapPointType"]
        assert checked > 50 and len(seen) >= 3, (checked, sorted(pts[v] for v in seen))
        assert skip_decisions > 0


def test_draw_stream_is_a_pure_function_of_update_t_slot(spec):
    ex = KindExplorer(spec, "ChooseRestOption", EPS, 77)
    a = ex.uniforms(5, 3, 12)
    assert np.array_equal(a, KindExplorer(spec, "BuyEntry", 0.1, 77).uniforms(5, 3, 12))
    assert not np.array_equal(a, ex.uniforms(5, 4, 12)) and not np.array_equal(a, ex.uniforms(6, 3, 12))
    obs, lg = _table(spec, n=12)
    full = _step(ex, obs, lg, update=5, t=3)[2]
    halves = []
    for lo, hi in ((0, 6), (6, 12)):
        o = {k: v[lo:hi] for k, v in obs.items()}
        d = torch.distributions.Categorical(logits=lg[lo:hi], validate_args=False)
        a0 = torch.distributions.Categorical(logits=lg, validate_args=False)
        torch.manual_seed(0)
        a_full = a0.sample()[lo:hi]
        halves.append(ex(o, lg[lo:hi], d, a_full, d.log_prob(a_full), update=5, t=3, lo=lo, n_total=12)[0])
    assert torch.equal(torch.cat(halves), full), "lanes draw slot s exactly as the N-slot batch does"


def test_evaluation_never_explores():
    from sts2train import evaluate, loopbreak

    for mod in (evaluate, loopbreak):
        assert "explore" not in inspect.getsource(mod)
    assert inspect.signature(InferenceRunner.sample).parameters["explore"].default is None


# ------------------------------------------------------------------------------------------------ train()


TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 12, "threads": 2, "rollout": 16, "minibatch": 64, "epochs": 1, "max_updates": 3, "ckpt_every": 1,
       "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2, "log_every": 100,
       "characters": ("SILENT",), "ascension": 10, "step_cap": 24, **TINY}
COMMON = "PlayCard,EndTurn"       # frequent kinds: every update has explore decisions


def _run(tmp_path, name, **kw):
    s = train(resolve_config({**RUN, "out": str(tmp_path / name), **kw}), log=lambda m: None)
    return s, read_jsonl(tmp_path / name / "metrics.jsonl")


def _strip(rows):
    return [{k: v for k, v in r.items() if not k.startswith("t_") and k not in
             ("time", "wall_s", "session_s", "env_steps_per_s", "env_steps_per_s_session")} for r in rows]


def test_eps_zero_is_the_default_path(tmp_path):
    kw = {"start_mode": "stream", "seed": 3, "init_seed": 3, "sample_seed": 3}
    s0, r0 = _run(tmp_path, "default", **kw)
    s1, r1 = _run(tmp_path, "eps0", explore_kinds="ChooseRestOption,BuyEntry", explore_eps=0.0, **kw)
    assert s0["weights_hash"] == s1["weights_hash"] and _strip(r0) == _strip(r1)
    assert "explore" not in s1 and not any(k.startswith("explore") for r in r1 for k in r)


def test_eps_zero_with_wave10_items_is_the_default_path(tmp_path):
    kw = {"start_mode": "stream", "seed": 3, "init_seed": 3, "sample_seed": 3}
    s0, r0 = _run(tmp_path, "default", **kw)
    s1, r1 = _run(tmp_path, "eps0", explore_kinds="Travel:Elite,ProceedRewards:skipcard", explore_eps=0.0, **kw)
    assert s0["weights_hash"] == s1["weights_hash"] and _strip(r0) == _strip(r1)
    assert "explore" not in s1 and not any(k.startswith("explore") for r in r1 for k in r)


def test_train_logs_wave10_items(tmp_path):
    """The per-item metrics of the wave-10 selectors appear in metrics.jsonl (counts may be 0 in 3 short updates)."""
    items = "Travel:Elite,Travel:Monster,ProceedRewards:skipcard"
    s, rows = _run(tmp_path, "w10", start_mode="stream", seed=6, explore_kinds=items, explore_eps=0.3,
                   max_updates=2, rollout=48)
    for it in items.split(","):
        assert all(f"explore_present.{it}" in r and f"explore_taken.{it}" in r and f"explore_pmass.{it}" in r
                   for r in rows), it
    assert s["explore"]["kinds"] == items


def test_train_logs_explore_metrics(tmp_path):
    for mode, eps in (("mixture", 0.15), ("decoupled", 0.3)):
        s, rows = _run(tmp_path, mode, start_mode="stream", seed=2, explore_kinds=COMMON, explore_eps=eps,
                       explore_logp=mode)
        assert len(rows) == 3 and all(r["explore_steps"] > 0 and 0 < r["explore_share"] <= 1 for r in rows)
        assert all(0 <= r["explore_policy_mass"] <= 1 + 1e-6 for r in rows)
        assert s["explore"]["explore_steps"] == sum(r["explore_steps"] for r in rows)
        assert all(r["explore_drawn.EndTurn"] <= r["explore_taken.EndTurn"] for r in rows)
        assert all(0 <= r["explore_mix_clip0"] <= 1 and r["t_explore_s"] >= 0 for r in rows)
        cfg = json.loads((tmp_path / mode / "config.json").read_text())["config"]
        assert cfg["explore_kinds"] == COMMON and cfg["explore_eps"] == eps and cfg["explore_logp"] == mode


def _greedy(self, x, *, compiled=True, adjust=None, explore=None):
    logits, value = self.forward(x, compiled=compiled)
    if adjust is not None:
        logits = adjust(logits)
    dist = torch.distributions.Categorical(logits=logits.float(), validate_args=False)
    a = logits.argmax(-1)
    if explore is not None:
        a, lp, self.explore_w = explore(logits, dist, a, dist.log_prob(a))
        return a, lp, value
    return a, dist.log_prob(a), value


@pytest.mark.parametrize("mode", ["mixture", "decoupled"])
def test_overlap_double_reproduces_off_with_explore(tmp_path, monkeypatch, mode):
    monkeypatch.setattr(ppo.InferenceRunner, "sample", _greedy)
    kw = {"start_mode": "stream", "seed": 4, "init_seed": 4, "sample_seed": 4, "explore_kinds": COMMON,
          "explore_eps": 0.15 if mode == "mixture" else 0.4, "explore_logp": mode}
    s0, r0 = _run(tmp_path, "off", overlap="off", **kw)
    s1, r1 = _run(tmp_path, "double", overlap="double", **kw)
    assert sum(r["explore_steps"] for r in r0) > 0
    for a, b in zip(_strip(r0), _strip(r1)):
        assert sorted(a) == sorted(b)
        for k, v in a.items():
            if isinstance(v, float):
                assert b[k] == pytest.approx(v, rel=1e-4, abs=1e-5), k
            else:
                assert b[k] == v, k
    assert s0["explore"] == s1["explore"]


def test_resume_is_deterministic_and_keeps_the_explorer(tmp_path):
    kw = {"start_mode": "stream", "seed": 5, "explore_kinds": COMMON, "explore_eps": 0.3}
    s, _ = _run(tmp_path, "base", max_updates=2, **kw)
    ck = s["last_checkpoint"]
    outs = []
    for name in ("r1", "r2"):
        cfg, res, path = ppo.plan_run({"out": str(tmp_path / name), "max_updates": 4}, ck, log=lambda m: None)
        assert cfg.explore_kinds == COMMON and cfg.explore_eps == 0.3
        outs.append((train(cfg, log=lambda m: None, resume=res, resume_path=path),
                     read_jsonl(tmp_path / name / "metrics.jsonl")))
    (a, ra), (b, rb) = outs
    assert a["weights_hash"] == b["weights_hash"] and _strip(ra) == _strip(rb)
    assert [r["update"] for r in ra][-2:] == [3, 4] and all(r["explore_steps"] > 0 for r in ra[-2:])


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
