"""Gate tests: model forward on a real env step, candidate masking / pointer wiring, finite PPO loss, progress tracker,
model presets (the smoke model stays < 2M parameters; the large preset is a 10-20M parameter net)."""

from __future__ import annotations

import math

import numpy as np
import pytest
import torch
import torch.nn.functional as F
from sts2eval import progress as PG
from sts2vec import PoolSpec, VecEnv, random_actions

from sts2train.features import ModelSpec, RolloutStore, bind_spec, obs_to_torch
from sts2train.model import PRESETS, Policy, param_count
from sts2train.progress import ProgressTracker

N = 64
TINY_TRUNK = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}   # exercises the transformer path cheaply


@pytest.fixture(scope="module")
def env():
    e = VecEnv(N, threads=4, pool=PoolSpec(count=32, seed_prefix="TSMOKE"), seed_text="TSMOKE")
    e.pool.warm()
    yield e
    e.close()


@pytest.fixture(scope="module")
def spec(env):
    s = ModelSpec.from_schema(env.schema)
    bind_spec(s)
    return s


@pytest.fixture(scope="module")
def model(spec):
    torch.manual_seed(0)
    return Policy(spec).eval()


def _advance(env, steps, seed=0):
    rng = np.random.default_rng(seed)
    obs = env.reset()
    for _ in range(steps):
        obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
    return obs


def test_spec_has_protocol_aux_columns(spec):
    for f in ("total_floor", "act_index", "act_floor", "map_row", "map_col", "act_rooms"):
        assert f in spec.aux_fields
    assert spec.aux_fields[:10] == ["total_floor", "act_index", "hp", "max_hp", "gold", "bosses_killed", "combats_won",
                                    "deck_n", "relic_n", "is_combat"]  # appended at the end, earlier order unchanged


def test_param_budget(model):
    assert param_count(model) < 2_000_000          # the smoke config (Policy defaults = PRESETS["smoke"])
    assert {k: getattr(model, k) for k in ("d", "s", "depth")} == {k: PRESETS["smoke"][k] for k in ("d", "s", "depth")}


def test_preset_sizes(spec):
    sizes = {name: param_count(Policy(spec, **kw)) for name, kw in PRESETS.items()}
    # since e9baaefc39811631 RunActionKind has 23 members (21 before: 948,750 / 1,175,150 / 15,804,270)
    assert sizes == {"smoke": 948_974, "small": 1_175_438, "large": 15_805_070}, sizes


@pytest.mark.parametrize("arch", [None, TINY_TRUNK], ids=["smoke", "trunk"])
def test_forward_on_real_env_step(env, spec, model, arch):
    if arch is not None:
        torch.manual_seed(0)
        model = Policy(spec, **arch).eval()
    obs = _advance(env, 40)
    x = obs_to_torch(obs, "cpu")
    with torch.no_grad():
        logits, value = model(x)
    assert logits.shape == (N, x["cand"].shape[1])
    assert value.shape == (N,)
    cand_n = torch.from_numpy(obs["cand_n"].astype(np.int64))
    live = cand_n > 0
    assert live.any()
    assert torch.isfinite(value).all()
    assert torch.isfinite(logits[live].max(-1).values).all()
    # stepping with the greedy action is legal for every live slot
    a = logits.argmax(-1).numpy().astype(np.int32)
    a[~live.numpy()] = -1
    _, _, _, _, status, _ = env.step(a)
    from sts2vec import STATUS

    assert not (status & STATUS["ILLEGAL_ACTION"]).any()


@pytest.mark.parametrize("arch", [None, TINY_TRUNK], ids=["smoke", "trunk"])
def test_candidate_masking(env, spec, model, arch):
    if arch is not None:
        torch.manual_seed(0)
        model = Policy(spec, **arch).eval()
    obs = _advance(env, 25, seed=3)
    x = obs_to_torch(obs, "cpu")
    with torch.no_grad():
        logits, _ = model(x)
    C = logits.shape[1]
    cand_n = obs["cand_n"]
    for i in range(N):
        n = int(cand_n[i])
        assert (logits[i, n:] <= -1e8).all(), f"slot {i}: rows >= cand_n must be masked"
        if n:
            assert (logits[i, :n] > -1e8).all()
    p = F.softmax(logits, -1)
    rows = torch.arange(C).view(1, -1) >= torch.from_numpy(cand_n.astype(np.int64)).view(-1, 1)
    assert float(p[rows].sum()) < 1e-6 * N
    # max_rows truncation keeps masking consistent (training cap)
    xt = obs_to_torch(obs, "cpu", max_rows=4)
    with torch.no_grad():
        lt, _ = model(xt)
    assert lt.shape[1] == min(4, max(1, int(cand_n.max())))


def test_candidate_pointers_matter(env, spec, model):
    """Two candidates that differ only in their pointer (hand slot / option index) get different logits."""
    obs = _advance(env, 30, seed=5)
    cc = spec.cols["cand"]
    x = obs_to_torch(obs, "cpu")
    cand = x["cand"]
    found = False
    for i in range(N):
        n = int(obs["cand_n"][i])
        kinds = cand[i, :n, cc["kind"]]
        for k in torch.unique(kinds):
            rows = torch.nonzero(kinds == k).flatten()
            if len(rows) >= 2:
                with torch.no_grad():
                    lg, _ = model({kk: v[i:i + 1] for kk, v in x.items()})
                vals = lg[0, rows]
                if (vals.max() - vals.min()).abs() > 1e-6:
                    found = True
                    break
        if found:
            break
    assert found, "no slot with two same-kind candidates scored differently"


@pytest.mark.parametrize("arch", [None, TINY_TRUNK], ids=["smoke", "trunk"])
def test_loss_finite_and_backward(env, spec, model, arch):
    obs = _advance(env, 20, seed=7)
    dtypes = {k: env.buffers[k].dtype for k in env.buffers.arrays}
    store = RolloutStore(spec, 2, N, 64, dtypes)
    torch.manual_seed(0)
    m = Policy(spec, **(arch or {}))
    opt = torch.optim.Adam(m.parameters(), lr=1e-3)
    rng = np.random.default_rng(0)
    for t in range(2):
        store.put_obs(t, obs)
        x = obs_to_torch(obs, "cpu", max_rows=64)
        with torch.no_grad():
            lg, v = m(x)
            d = torch.distributions.Categorical(logits=lg)
            a = d.sample()
        store.actions[t] = a.numpy()
        store.logp[t] = d.log_prob(a).numpy()
        store.values[t] = v.numpy()
        store.valid[t] = obs["cand_n"] > 0
        acts = a.numpy().astype(np.int32)
        acts[obs["cand_n"] <= 0] = -1
        obs, rew, term, trunc, status, info = env.step(acts)
        store.rewards[t] = rew + rng.random(N).astype(np.float32) * 0.01
    idx = np.flatnonzero(store.valid.reshape(-1))
    x = store.batch(idx)
    assert x["cand"].shape[1] <= 64 and x["cand"].shape[0] == len(idx)
    lg, v = m(x)
    logp = F.log_softmax(lg, -1)
    act = torch.from_numpy(store.actions.reshape(-1)[idx])
    newlogp = logp.gather(1, act.unsqueeze(1)).squeeze(1)
    ratio = (newlogp - torch.from_numpy(store.logp.reshape(-1)[idx])).exp()
    assert torch.allclose(ratio, torch.ones_like(ratio), atol=1e-4), "stored logp reproduces from the stored obs"
    adv = torch.from_numpy(store.rewards.reshape(-1)[idx] - store.values.reshape(-1)[idx])
    ent = -(logp.exp() * logp.clamp_min(-1e4)).sum(-1).mean()
    loss = -(ratio * adv).mean() + 0.5 * ((v - torch.from_numpy(store.rewards.reshape(-1)[idx])) ** 2).mean() - 0.01 * ent
    assert math.isfinite(loss.item())
    opt.zero_grad()
    loss.backward()
    grads = [p.grad for p in m.parameters() if p.grad is not None]
    assert grads and all(torch.isfinite(g).all() for g in grads)
    opt.step()


def test_progress_tracker_matches_protocol(env, spec):
    """Finished random episodes: the tracker's progress equals sts2eval.progress.compute on (act, floor) with the
    act's start floor / room count, and act-1 deaths score floor-based ((floor-1)/17)/3."""
    rng = np.random.default_rng(11)
    tr = ProgressTracker(spec.aux_fields, env.schema["last_episode"].fields, N)
    obs = env.reset()
    tr.observe(obs["aux"])
    eps = []
    for _ in range(300):
        obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
        eps += tr.finished((term | trunc).astype(bool), info["last_episode"])
        tr.observe(obs["aux"])
    assert len(eps) > 50
    for e in eps:
        assert 0.0 <= e["progress"] <= 1.0
        if e["act"] == 1 and not e["win"]:
            L = int(e["act_rooms"].get("1", 17))
            assert e["progress"] == pytest.approx(PG.compute("loss", 1, e["floor"], 0, {}, {1: L})["progress"])
            assert L in (17, 18)  # 18 = a GOLDEN_COMPASS golden path
    mean = float(np.mean([e["progress"] for e in eps]))
    assert 0.02 < mean < 0.2, mean


def test_eval_entry_point_writes_protocol_report(tmp_path, spec):
    """`sts2train-smoke eval` on a 2-game seed set with an untrained checkpoint: protocol run dir + report.md."""
    import json

    from sts2train.evaluate import evaluate
    from sts2train.ppo import TrainConfig, save_checkpoint

    torch.manual_seed(1)
    ck = tmp_path / "ck.pt"
    save_checkpoint(ck, Policy(spec), spec, TrainConfig(), {"updates": 0})
    ss = {"name": "t2", "ascension": 0, "content": "ported_only",
          "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}
    p = tmp_path / "t2.json"
    p.write_text(json.dumps(ss))
    out = tmp_path / "run"
    rep = evaluate(str(p), str(ck), str(out), threads=2, log=lambda m: None)
    h = rep["headline"]
    assert h["games"] == 2 and h["progress_games"] == 2
    assert 0.0 <= h["progress_mean"] <= 1.0
    assert h["replayable"] == 2
    assert (out / "report.md").read_text().startswith("# Eval report")
    for g in ss["games"]:
        s = json.loads((out / "games" / f"{g['character']}-{g['seed']}.summary.json").read_text())
        assert s["result"] in ("win", "loss", "capped", "stuck", "crashed")
        assert s["act_rooms_source"] in ("map", "win")
        assert s["steps"] == len(s["history_actions"])
        assert s["weights_hash"] and s["policy"] == "nn:ck.pt"
    # the run directory is the LLM runner's layout: sts2eval's own report reproduces the headline
    from sts2eval.report import build_report

    h2 = build_report(out)["headline"]
    for k in ("games", "wins", "progress_mean", "floor_mean", "replayable", "steps_total"):
        assert h2[k] == h[k], k


def test_trunk_checkpointing_is_exact_and_saves_memory(env, spec):
    """--grad-checkpoint recomputes the transformer blocks: same logits and gradients, fewer saved activations."""
    from sts2train.model import activation_bytes_per_sample

    obs = _advance(env, 15, seed=9)
    x = obs_to_torch(obs, "cpu", max_rows=64)
    torch.manual_seed(0)
    m = Policy(spec, **TINY_TRUNK)
    m.train()
    grads = []
    for ck in (False, True):
        m.grad_ckpt = ck
        m.zero_grad()
        lg, v = m(x)
        (lg.logsumexp(-1).mean() + v.mean()).backward()
        grads.append((lg.detach(), [p.grad.clone() for p in m.parameters() if p.grad is not None]))
    assert torch.allclose(grads[0][0], grads[1][0], atol=1e-5)
    assert all(torch.allclose(a, b, atol=1e-5) for a, b in zip(grads[0][1], grads[1][1]))
    m.grad_ckpt = False
    full = activation_bytes_per_sample(m, spec)
    m.grad_ckpt = True
    assert 0 < activation_bytes_per_sample(m, spec) < full
