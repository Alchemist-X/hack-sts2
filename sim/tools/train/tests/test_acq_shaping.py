"""--acq-shaping: the potential, the per-step shaping F = gamma phi(s') - phi(s) with phi = 0 after an episode end,
telescoping on synthetic and real episodes, and a run that logs shaped and unshaped quantities apart."""

from __future__ import annotations

import numpy as np
import pytest
from sts2vec import PoolSpec, VecEnv, random_actions

from sts2train.objective import ACQ_DEFAULTS, AcqShaping, EpisodeRewards
from sts2train.ppo import resolve_config, train
from sts2train.runlog import read_jsonl

AUX = ["total_floor", "deck_n", "hp", "relic_n"]
DECK = {"card_id": 0, "upgrade": 1, "enchant_id": 2}
C1, C2, C3 = ACQ_DEFAULTS["deck"], ACQ_DEFAULTS["relic"], ACQ_DEFAULTS["upgrade"]


def _obs(states):
    """states: per slot (deck card count, relics, upgrade levels as a list per card) -> aux, deck arrays."""
    n = len(states)
    aux = np.zeros((n, len(AUX)), np.float32)
    deck = np.zeros((n, 96, 3), np.int32)
    for i, (cards, relics, ups) in enumerate(states):
        aux[i, AUX.index("deck_n")], aux[i, AUX.index("relic_n")] = cards, relics
        deck[i, :cards, DECK["card_id"]] = 7
        deck[i, :len(ups), DECK["upgrade"]] = ups
    deck[:, 95, DECK["upgrade"]] = 5          # a padding row (card_id 0) never counts
    return aux, deck


def _phi(cards, relics, ups):
    return C1 * min(cards - 10, 12) + C2 * relics + C3 * sum(ups)


def test_potential():
    sh = AcqShaping(AUX, DECK, 4)
    aux, deck = _obs([(12, 1, []), (8, 0, [1]), (40, 3, [1, 1, 2]), (22, 2, [0, 1])])
    want = [C1 * 2 + C2, C1 * -2 + C3, C1 * 12 + 3 * C2 + 4 * C3, C1 * 12 + 2 * C2 + C3]
    np.testing.assert_allclose(sh.potential(aux, deck), want, atol=1e-12)
    with pytest.raises(ValueError, match="deck_n"):
        AcqShaping(["hp"], DECK, 1)


# Per slot and step: the observation after the step (cards, relics, upgrade levels) and whether the step ended the
# episode (the observation is then the next episode's first one). Slot 0: a card, a relic, an upgrade, dies at step 4;
# slot 1: a card and a relic, truncated at step 1, the next episode starts at (14 cards, 2 relics, 1 upgrade) and
# drops to (12, 1, none); slot 2 never ends (cards and upgrades only).
TIMELINE = [
    [((13, 1, []), False), ((14, 2, []), False), ((14, 2, [1]), False), ((14, 2, [1]), False), ((12, 1, []), True)],
    [((13, 2, []), False), ((14, 2, [1]), True), ((12, 1, []), False), ((12, 1, []), False), ((12, 1, []), False)],
    [((12, 1, [1]), False), ((12, 1, [1]), False), ((13, 1, [1]), False), ((14, 1, [1, 1]), False),
     ((15, 1, [1, 1]), False)],
]
START = [(12, 1, []), (12, 1, []), (12, 1, [])]


@pytest.mark.parametrize("gamma", [1.0, 0.9])
def test_shaping_per_step_and_telescoping(gamma):
    n, T = len(START), len(TIMELINE[0])
    sh = AcqShaping(AUX, DECK, n, gamma)
    sh.reset(*_obs(START))
    cur = list(START)
    sums, ep_sums, ep_first = np.zeros(n), {i: [] for i in range(n)}, [_phi(*s) for s in START]
    for t in range(T):
        nxt = [TIMELINE[i][t][0] for i in range(n)]
        done = np.array([TIMELINE[i][t][1] for i in range(n)])
        f, ended = sh.step(done, *_obs(nxt))
        for i in range(n):
            want = (0.0 if done[i] else gamma * _phi(*nxt[i])) - _phi(*cur[i])
            assert f[i] == pytest.approx(want, abs=1e-7), (t, i)
        sums += f
        for i in np.flatnonzero(done):
            assert ended[int(i)] == pytest.approx(sums[i], abs=1e-6)
            ep_sums[int(i)].append((sums[i], ep_first[i]))
            sums[i] = 0.0
            ep_first[i] = _phi(*nxt[i])
        assert set(ended) == set(np.flatnonzero(done).tolist())
        cur = nxt
    if gamma == 1.0:
        # finished episodes: phi(last) - phi(first) with phi(last) = 0; running ones: phi(now) - phi(first)
        for i in range(n):
            for total, first in ep_sums[i]:
                assert total == pytest.approx(-first, abs=1e-6)
            assert sums[i] == pytest.approx(_phi(*cur[i]) - ep_first[i], abs=1e-6)
        assert len(ep_sums[0]) == 1 and len(ep_sums[1]) == 1 and len(ep_sums[2]) == 0


def test_real_env_shaping_telescopes():
    """Random play on the real env: every finished episode's shaping sums to -phi(its first observation), the env's
    episode bookkeeping is unchanged (EpisodeRewards returns the objective's reward)."""
    with VecEnv(32, threads=2, step_cap=150, pool=PoolSpec(count=16, seed_prefix="TACQ"), seed_text="TACQ") as env:
        env.pool.warm()
        cols = env.schema.cols("deck")
        sh = AcqShaping(env.schema["aux"].fields, {"card_id": cols.card_id, "upgrade": cols.upgrade}, 32)
        rw = EpisodeRewards("protocol", env.schema["aux"].fields, env.schema["last_episode"].fields, 32)
        obs = env.reset()
        rw.reset(obs["aux"])
        sh.reset(obs["aux"], obs["deck"])
        first = sh.phi.copy()
        sums = np.zeros(32)
        rng = np.random.default_rng(1)
        checked = moved = 0
        for _ in range(400):
            obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
            done = (term | trunc).astype(bool)
            r, fin = rw.step(rew, done, info["last_episode"], obs["aux"])
            f, ended = sh.step(done, obs["aux"], obs["deck"])
            sums += f
            moved += int(np.count_nonzero(np.abs(f[~done]) > 0))
            for i in np.flatnonzero(done):
                assert ended[int(i)] == pytest.approx(-first[i], abs=1e-6)
                assert sums[i] == pytest.approx(-first[i], abs=1e-5)
                checked += 1
                sums[i] = 0.0
            first[done] = sh.phi[done]
        assert checked > 20 and moved > 0, (checked, moved)


def test_train_logs_shaped_and_unshaped_apart(tmp_path):
    run = tmp_path / "run"
    cfg = resolve_config({"out": str(run), "d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2, "envs": 16,
                          "threads": 2, "pool": 8, "rollout": 32, "minibatch": 128, "max_updates": 3, "ckpt_every": 0,
                          "ckpt_every_min": 0, "log_every": 100, "max_hours": 0.5, "torch_threads": 2,
                          "acq_shaping": True, "acq_relic_coef": 0.02})
    logs: list[str] = []
    train(cfg, log=logs.append)
    rows = read_jsonl(run / "metrics.jsonl")
    assert any("acquisition shaping: phi = 0.003 x min(deck - 10, 12) + 0.02 x relics" in m for m in logs)
    for r in rows:
        for k in ("reward_mean_shaped", "shaping_mean", "shaped_return_mean", "shaping_return_mean",
                  "acq_potential_mean"):
            assert k in r, k
        assert r["reward_mean_shaped"] == pytest.approx(r["reward_mean"] + r["shaping_mean"], abs=1e-6)
    fin = [r for r in rows if r["episodes"]]
    assert fin
    for r in fin:      # fresh starts, no wins: the unshaped return is exactly the protocol progress
        assert r["wins"] == 0 and r["return_mean"] == pytest.approx(r["progress_mean"], abs=1e-6)
        assert r["shaped_return_mean"] == pytest.approx(r["return_mean"] + r["shaping_return_mean"], abs=1e-6)
        assert r["shaping_return_mean"] < 0          # -phi(first observation): a starter deck has a relic
