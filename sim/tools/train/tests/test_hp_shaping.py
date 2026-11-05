"""--hp-shaping-coef (objective.HpShaping): phi = KAPPA * hp / max_hp, 0 after an episode end, F = gamma phi(s') -
phi(s) per step. Coef 0 leaves the trainer's rewards bit-identical; the shaping telescopes per episode (death, win,
truncation); a harvest-anchor start begins from the restored state's ratio; a session start (resume) reads the
potential from its first observations and injects no step; a run logs it apart and a checkpoint trained without it
resumes with the coefficient taken from the command line."""

from __future__ import annotations

import json

import numpy as np
import pytest
import torch
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions

from sts2train.harvest import Harvester
from sts2train.objective import ACQ_DEFAULTS, AcqShaping, HpShaping, annotate_shaping, make_shaper
from sts2train.ppo import resolve_config, train
from sts2train.runlog import read_jsonl

AUX = ["total_floor", "deck_n", "hp", "max_hp", "relic_n"]
DECK = {"card_id": 0, "upgrade": 1, "enchant_id": 2}
K = 0.1


def _obs(states):
    """states: per slot (hp, max_hp, deck card count, relics) -> aux, deck arrays."""
    n = len(states)
    aux = np.zeros((n, len(AUX)), np.float32)
    deck = np.zeros((n, 96, 3), np.int32)
    for i, (hp, mx, cards, relics) in enumerate(states):
        aux[i, AUX.index("hp")], aux[i, AUX.index("max_hp")] = hp, mx
        aux[i, AUX.index("deck_n")], aux[i, AUX.index("relic_n")] = cards, relics
        deck[i, :cards, DECK["card_id"]] = 7
    return aux, deck


def _phi(hp, mx, *_):
    return K * hp / mx if mx > 0 else 0.0


# Per slot and step: the observation after the step and whether the step ended the episode (the observation is then
# the next episode's first one). Slot 0: loses hp, heals, dies at step 4 (its last live state 9/70), the next episode
# starts fresh at 70/70. Slot 1: a max-HP relic (70 -> 77), a win at step 2 (last live 50/77), the next episode is a
# harvest-anchor start at 41/75 (a restored mid-run state). Slot 2: starts with max_hp 0 (potential 0), truncated at
# step 3.
START = [(70, 70, 10, 1), (60, 70, 10, 1), (0, 0, 10, 1)]
TIMELINE = [
    [((52, 70, 10, 1), False), ((40, 70, 11, 1), False), ((58, 70, 11, 1), False), ((9, 70, 11, 1), False),
     ((70, 70, 10, 1), True), ((61, 70, 10, 1), False)],
    [((60, 77, 10, 2), False), ((50, 77, 10, 2), False), ((41, 75, 18, 5), True), ((30, 75, 18, 5), False),
     ((30, 75, 19, 5), False), ((75, 75, 19, 5), False)],
    [((70, 70, 10, 1), False), ((66, 70, 10, 1), False), ((66, 70, 10, 1), False), ((70, 70, 10, 1), True),
     ((55, 70, 10, 1), False), ((55, 70, 10, 1), False)],
]


def test_potential_and_refusal():
    sh = HpShaping(AUX, 4, coef=K)
    aux, deck = _obs([(70, 70, 10, 1), (14, 70, 10, 1), (0, 80, 10, 1), (5, 0, 10, 1)])
    np.testing.assert_allclose(sh.potential(aux, deck), [K, K * 0.2, 0.0, 0.0], atol=1e-12)
    with pytest.raises(ValueError, match="max_hp"):
        HpShaping(["hp", "deck_n"], 1, coef=K)


@pytest.mark.parametrize("gamma", [1.0, 0.9])
def test_per_step_and_telescoping(gamma):
    """(b) F = gamma phi(s') - phi(s) (phi(s') = 0 at an end); with gamma = 1 every finished episode's shaping sums to
    phi(end) - phi(start) = -phi(first observation) (terminal 0: -KAPPA hp0/max0 at the death / the win / the
    truncation), and a running episode's to phi(now) - phi(first); the anchor episode's first potential is the
    restored state's."""
    n, T = len(START), len(TIMELINE[0])
    sh = HpShaping(AUX, n, gamma, coef=K)
    sh.reset(*_obs(START))
    cur, first = list(START), [_phi(*s) for s in START]
    sums, finished = np.zeros(n), {i: [] for i in range(n)}
    for t in range(T):
        nxt = [TIMELINE[i][t][0] for i in range(n)]
        done = np.array([TIMELINE[i][t][1] for i in range(n)])
        f, ended = sh.step(done, *_obs(nxt))
        for i in range(n):
            want = (0.0 if done[i] else gamma * _phi(*nxt[i])) - _phi(*cur[i])
            assert f[i] == pytest.approx(want, abs=1e-7), (t, i)
            assert sh.phi[i] == pytest.approx(_phi(*nxt[i]), abs=1e-12)      # the next episode's first potential
        sums += f
        assert set(ended) == set(np.flatnonzero(done).tolist())
        for i in np.flatnonzero(done):
            assert ended[int(i)] == pytest.approx(sums[i], abs=1e-6)
            finished[int(i)].append((sums[i], first[i]))
            sums[i], first[i] = 0.0, _phi(*nxt[i])
        cur = nxt
    assert [len(finished[i]) for i in range(n)] == [1, 1, 1]
    assert first[1] == pytest.approx(K * 41 / 75)          # the anchor start: the restored ratio, not 50/77 or 1
    if gamma == 1.0:
        for i in range(n):
            total, phi0 = finished[i][0]
            assert total == pytest.approx(-phi0, abs=1e-6)          # phi(end) = 0
            assert sums[i] == pytest.approx(_phi(*cur[i]) - first[i], abs=1e-6)
        assert finished[0][0][0] == pytest.approx(-K, abs=1e-6)            # a death from full HP: -KAPPA
        assert finished[1][0][0] == pytest.approx(-K * 60 / 70, abs=1e-6)  # the win pays back -KAPPA hp0/max0
        assert finished[2][0][0] == pytest.approx(0.0, abs=1e-7)           # started at potential 0


def test_coef_zero_is_off_and_acq_alone_is_unchanged():
    """(a) coef 0 builds no shaping (the trainer's reward is the objective's, as before); --acq-shaping alone through
    the ShapingSet gives bit-identical per-step rewards to a bare AcqShaping; with both on the sum is the sum."""
    assert make_shaper(AUX, DECK, 3, 1.0, acq=False, acq_coefs={}, hp_coef=0.0) is None
    coefs = dict(ACQ_DEFAULTS)
    set_acq = make_shaper(AUX, DECK, 3, 1.0, acq=True, acq_coefs=coefs, hp_coef=0.0)
    set_both = make_shaper(AUX, DECK, 3, 1.0, acq=True, acq_coefs=coefs, hp_coef=K)
    assert list(set_acq.parts) == ["acq"] and list(set_both.parts) == ["acq", "hp"]
    bare, bare_hp = AcqShaping(AUX, DECK, 3, **coefs), HpShaping(AUX, 3, coef=K)
    for s in (set_acq, set_both, bare, bare_hp):
        s.reset(*_obs(START))
    rng = np.random.default_rng(0)
    r_obj = rng.normal(0, 0.01, (len(TIMELINE[0]), 3)).astype(np.float32)
    for t in range(len(TIMELINE[0])):
        nxt = _obs([TIMELINE[i][t][0] for i in range(3)])
        done = np.array([TIMELINE[i][t][1] for i in range(3)])
        fa, ea = set_acq.step(done, *nxt)
        fb, eb = bare.step(done, *nxt)
        fh, _ = bare_hp.step(done, *nxt)
        f2, e2 = set_both.step(done, *nxt)
        assert (r_obj[t] + fa).tobytes() == (r_obj[t] + fb).tobytes() and ea == eb
        np.testing.assert_allclose(f2, fb + fh, atol=1e-7)
        assert set(e2) == set(eb) == set(set_both.ended_parts["hp"])
    np.testing.assert_array_equal(set_acq.phi, bare.phi)
    win = set_both.pop_window()
    assert win["hp"][1] == 3 * len(TIMELINE[0]) and set_both.pop_window()["hp"] == (0.0, 0)


def test_annotate_shaping_fields():
    sh = make_shaper(AUX, DECK, 3, 1.0, acq=True, acq_coefs=dict(ACQ_DEFAULTS), hp_coef=K)
    sh.reset(*_obs(START))
    done = np.array([True, False, True])
    f, ended = sh.step(done, *_obs([(70, 70, 10, 1), (60, 70, 10, 1), (70, 70, 10, 1)]))
    fin = [{"slot": 4, "return": 0.5}, {"slot": 6, "return": 0.1}]     # global slots of a lane starting at 4
    annotate_shaping(fin, ended, sh, lo=4)
    assert fin[0]["hp_shaping"] == pytest.approx(-K) and fin[1]["hp_shaping"] == 0.0     # max_hp 0 -> phi 0
    assert fin[0]["shaped_return"] == pytest.approx(0.5 + fin[0]["shaping"])
    acq_only = make_shaper(AUX, DECK, 3, 1.0, acq=True, acq_coefs=dict(ACQ_DEFAULTS), hp_coef=0.0)
    acq_only.reset(*_obs(START))
    f, ended = acq_only.step(done, *_obs([(70, 70, 10, 1)] * 3))
    fin = [{"slot": 0, "return": 0.5}]
    annotate_shaping(fin, ended, acq_only)
    assert "hp_shaping" not in fin[0] and "shaping" in fin[0]


def test_session_start_injects_no_step():
    """(e) A resume rebuilds the shaper and resets it on the session's first observations (mid-episode included): the
    first step's F is the potential difference from that observation only, and the episode's shaping in the new
    session telescopes from it (nothing crosses the session boundary)."""
    sh = HpShaping(AUX, 3, coef=K)
    sh.reset(*_obs(START))
    for t in range(2):
        sh.step(np.zeros(3, bool), *_obs([TIMELINE[i][t][0] for i in range(3)]))
    mid = [TIMELINE[i][1][0] for i in range(3)]
    resumed = HpShaping(AUX, 3, coef=K)
    resumed.reset(*_obs(mid))
    np.testing.assert_allclose(resumed.phi, sh.phi, atol=1e-12)
    assert not resumed.sum.any()
    nxt = [TIMELINE[i][2][0] for i in range(3)]
    f, _ = resumed.step(np.zeros(3, bool), *_obs(nxt))
    np.testing.assert_allclose(f, [_phi(*nxt[i]) - _phi(*mid[i]) for i in range(3)], atol=1e-7)


def test_real_env_anchor_starts_from_the_restored_hp():
    """(c) The real library with an --harvest `any` ring: every episode that auto-resets into an anchor (a
    determinized clone of a combat entry) starts its HP potential at the anchor's hp / max_hp as the env shows it,
    every finished episode's HP shaping sums to -phi(its first observation), and some anchors start below full HP (so
    a stale or a fresh-run potential would be wrong)."""
    n, spare = 17, 16
    with VecEnv(n, threads=4, step_cap=3000, seed_text="THPSH",
                pool=PoolSpec(characters=("SILENT",), ascension=10, count=64, seed_prefix="THPSH")) as env:
        env.pool.warm()
        base = len(env.pool)
        h = Harvester(env, {"any": 32}, prob=1.0, max_per_step=64, seed=5)
        c_hp, c_max = env.schema.col("aux", "hp"), env.schema.col("aux", "max_hp")
        sh = make_shaper(env.schema["aux"].fields, {"card_id": 0, "upgrade": 1}, n, 1.0, acq=False, acq_coefs={},
                         hp_coef=K)
        obs = env.reset()
        h.reset(obs, env.info()["start_entry"])
        sh.reset(obs["aux"], obs["deck"])
        first = sh.phi.copy()
        sums = np.zeros(n)
        rng = np.random.default_rng(3)
        anchor_starts = below_full = checked = errs = 0
        for t in range(1500):
            a = random_actions(rng, obs["cand_n"])
            a[spare] = -1
            obs, rew, term, trunc, status, info = env.step(a)
            errs += int(np.count_nonzero(status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])))
            done = (term | trunc).astype(bool)
            h.step(obs, done, info["start_entry"], status, upd=1, t=t)
            f, ended = sh.step(done, obs["aux"], obs["deck"])
            sums += f
            for i in np.flatnonzero(done):
                assert ended[int(i)] == pytest.approx(-first[i], abs=1e-5)
                assert sums[i] == pytest.approx(-first[i], abs=1e-5)
                checked += 1
                sums[i] = 0.0
            hp = obs["aux"][:, c_hp].astype(np.float64)
            ratio = hp / np.maximum(obs["aux"][:, c_max].astype(np.float64), 1.0)
            for i in np.flatnonzero(done & (info["start_entry"] >= base)):
                assert sh.phi[i] == pytest.approx(K * ratio[i], abs=1e-9)
                anchor_starts += 1
                below_full += int(ratio[i] < 0.999)
            first[done] = sh.phi[done]
        assert errs == 0 and checked > 20, (errs, checked)
        assert anchor_starts > 0 and below_full > 0, (anchor_starts, below_full)


# ------------------------------------------------------------------------------------------------ the trainer


TRAIN = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2, "envs": 16, "threads": 2, "pool": 8, "rollout": 32,
         "minibatch": 128, "epochs": 1, "max_updates": 3, "ckpt_every": 0, "ckpt_every_min": 0, "log_every": 100,
         "max_hours": 0.5, "torch_threads": 1}


def test_train_coef_zero_is_bit_identical(tmp_path):
    """(a) A run with hp_shaping_coef 0 trains the same weights and logs the same episodes as one without the key."""
    s0 = train(resolve_config({**TRAIN, "out": str(tmp_path / "a")}), log=lambda m: None)
    s1 = train(resolve_config({**TRAIN, "out": str(tmp_path / "b"), "hp_shaping_coef": 0.0}), log=lambda m: None)
    assert s0["weights_hash"] == s1["weights_hash"]
    e0, e1 = read_jsonl(tmp_path / "a" / "episodes.jsonl"), read_jsonl(tmp_path / "b" / "episodes.jsonl")
    assert e0 == e1 and e0
    rows = read_jsonl(tmp_path / "b" / "metrics.jsonl")
    assert not any(k.startswith("hp_") or k == "reward_mean_shaped" for r in rows for k in r)


def test_train_logs_hp_shaping_apart_and_resumes_from_cli(tmp_path):
    """Shaped and unshaped quantities logged apart; config.json records the coefficient; a checkpoint trained
    without it (its config has no hp_shaping_coef key) resumes with the command line's value, and the next resume
    keeps it."""
    from sts2train.cli import main

    run = tmp_path / "run"
    common = ["--preset", "smoke", "--device", "cpu", "--envs", "16", "--threads", "2", "--pool", "8", "--rollout",
              "16", "--minibatch", "64", "--torch-threads", "1", "--ckpt-every", "1", "--log-every", "100",
              "--max-hours", "0.5", "--out", str(run)]
    assert main(["train", *common, "--max-updates", "2"]) == 0          # trained without HP shaping
    assert not any("hp_shaping_mean" in r for r in read_jsonl(run / "metrics.jsonl"))
    for p in (run / "checkpoints").glob("*.pt"):                        # a checkpoint of a build without the flag
        ck = torch.load(p, map_location="cpu", weights_only=False)
        if isinstance(ck.get("config"), dict):
            ck["config"].pop("hp_shaping_coef", None)
            torch.save(ck, p)
    assert main(["train", *common, "--max-updates", "4", "--resume", "auto", "--hp-shaping-coef", "0.1"]) == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["hp_shaping_coef"] == 0.1
    assert conf["sessions"][-1]["config_diff"]["hp_shaping_coef"] == [None, 0.1]
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2, 3, 4]
    for r in rows[2:]:
        for k in ("reward_mean_shaped", "shaping_mean", "hp_shaping_mean", "hp_shaping_sum", "hp_potential_mean"):
            assert k in r, k
        assert "acq_potential_mean" not in r
        assert r["reward_mean_shaped"] == pytest.approx(r["reward_mean"] + r["shaping_mean"], abs=1e-6)
        assert 0.0 < r["hp_potential_mean"] <= 0.1 + 1e-9
        if r["episodes"]:
            assert r["shaped_return_mean"] == pytest.approx(r["return_mean"] + r["shaping_return_mean"], abs=1e-6)
            assert r["hp_shaping_return_mean"] == pytest.approx(r["shaping_return_mean"], abs=1e-6)
            assert -0.1 - 1e-6 <= r["hp_shaping_return_mean"] <= 0.0     # -KAPPA hp0 / max0 per episode
    assert main(["train", *common, "--max-updates", "5", "--resume", "auto"]) == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["hp_shaping_coef"] == 0.1 and "hp_shaping_mean" in read_jsonl(run / "metrics.jsonl")[-1]
    with pytest.raises(ValueError, match="hp_shaping_coef"):
        resolve_config({"hp_shaping_coef": -0.1})
