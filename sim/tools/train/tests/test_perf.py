"""Gate tests of the fast update path: the sync-free eager update reproduces the pre-change trainer bit for bit (the
update loop on a fixed rollout, and the whole `train()` loop on a real env against the trainer as of e80ea22), static
rows keep shapes fixed (one compiled graph) and change nothing but padding, the torch.compile'd update / forward match
eager on a fixed batch (cpu, inductor), a compiled `train()` run finishes, and `bench-update` runs and guards size."""

from __future__ import annotations

import json
import math

import numpy as np
import pytest
import torch
import torch.nn.functional as F

from sts2vec import VecEnv

from sts2train.bench import load_spec, random_obs
from sts2train.features import (ROW_TRUNCATED, USED_BUFFERS, ModelSpec, RolloutStore, _row_need, bind_spec,
                                obs_to_device)
from sts2train.model import ARCH_KEYS, COL_GROUPS, Policy, activation_bytes_per_sample
from sts2train.objective import EpisodeRewards
from sts2train.ppo import (InferenceRunner, MinibatchUpdater, check_compiled, check_compiled_forward, env_kwargs,
                           ppo_loss, resolve_config, train, use_static_rows, weights_hash)
from sts2train.runlog import read_jsonl
from sts2train.startpool import start_pool_from_config, uses_start_saves

TINY_TRUNK = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}
MAX_ROWS = 64


@pytest.fixture(scope="module")
def schema_spec():
    spec, dtypes = load_spec()
    bind_spec(spec)
    return spec, dtypes


@pytest.fixture
def one_thread():
    """Deterministic CPU kernels (multi-threaded embedding / index_put backward accumulates in a varying order)."""
    n = torch.get_num_threads()
    torch.set_num_threads(1)
    yield
    torch.set_num_threads(n)


def _store(spec, dtypes, steps, n, seed, max_cand=MAX_ROWS, max_prompt=MAX_ROWS):
    """A RolloutStore of random observations (step t: cand_n <= max_cand[t] if a list) with legal actions,
    uniform-policy log-probs, random values / rewards."""
    rng = np.random.default_rng(seed)
    store = RolloutStore(spec, steps, n, MAX_ROWS, dtypes)
    for t in range(steps):
        mc = max_cand[t] if isinstance(max_cand, (list, tuple)) else max_cand
        store.put_obs(t, random_obs(spec, n, rng, dtypes=dtypes, max_cand=mc, max_prompt=max_prompt))
    legal = np.minimum(store.cand_n_h, MAX_ROWS)
    store.actions[:] = np.floor(rng.random(legal.shape) * legal).astype(np.int64)
    store.logp[:] = -np.log(legal)
    store.values[:] = rng.standard_normal(legal.shape).astype(np.float32)
    store.rewards[:] = rng.standard_normal(legal.shape).astype(np.float32)
    store.valid[:] = True
    return store


def _targets(store):
    f = lambda a: torch.from_numpy(np.ascontiguousarray(a.reshape(-1)))  # noqa: E731
    adv = store.rewards - store.values
    return f(store.actions), f(store.logp), f(((adv - adv.mean()) / (adv.std() + 1e-8)).astype(np.float32)), \
        f(store.rewards), f(store.values)


def test_col_groups_match_list_indexing(schema_spec):
    spec, dtypes = schema_spec
    m = Policy(spec)
    obs = random_obs(spec, 8, np.random.default_rng(0), dtypes=dtypes)
    for key, buf, names in COL_GROUPS:
        t = torch.from_numpy(obs[buf])
        assert torch.equal(m._cols(key, t), t[..., [spec.cols[buf][k] for k in names]]), key
    assert not any(k.startswith("_ix_") for k in m.state_dict()), "index buffers must stay out of checkpoints"


def _legacy_update(model, opt, store, targets, rng, cfg):
    """The trainer's PPO update loop as of ef35697 and unchanged up to e80ea22 (per-minibatch row cuts from .item(),
    stats copied back per minibatch), kept verbatim as the reference for the eager path."""
    d_act, d_logp, d_adv, d_ret, d_val = targets
    idx_all = np.flatnonzero(store.valid.reshape(-1))
    stats = []
    for _ in range(cfg.epochs):
        perm = rng.permutation(idx_all)
        for k in range(0, len(perm), cfg.minibatch):
            mb = torch.from_numpy(np.sort(perm[k:k + cfg.minibatch]))
            x = {kk: v.flatten(0, 1).index_select(0, mb) for kk, v in store.obs.items()}
            c = int(x["cand_n"].max().clamp(1, store.obs["cand"].shape[2]).item())
            p = int(x["global_i"][:, store._prompt_col].max().clamp(1, store.obs["prompt_options"].shape[2]).item())
            x["cand"], x["prompt_options"] = x["cand"][:, :c], x["prompt_options"][:, :p]
            logits, v = model(x)
            logp_all = F.log_softmax(logits, -1)
            act = d_act[mb]
            newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
            ent = -(logp_all.exp() * logp_all.clamp_min(-1e4)).sum(-1).mean()
            oldlogp = d_logp[mb]
            ratio = (newlogp - oldlogp).exp()
            madv = d_adv[mb]
            pg = -torch.min(ratio * madv, ratio.clamp(1 - cfg.clip, 1 + cfg.clip) * madv).mean()
            vret, vold = d_ret[mb], d_val[mb]
            vclip = vold + (v - vold).clamp(-cfg.clip, cfg.clip)
            vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
            loss = pg + cfg.vf_coef * vl - cfg.ent_coef * ent
            opt.zero_grad(set_to_none=True)
            loss.backward()
            gn = torch.nn.utils.clip_grad_norm_(model.parameters(), cfg.max_grad_norm)
            opt.step()
            with torch.no_grad():
                kl = ((ratio - 1) - (newlogp - oldlogp)).mean()
                cf = ((ratio - 1).abs() > cfg.clip).float().mean()
            stats.append(torch.stack([ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach(),
                                      gn.detach().float()]).cpu().numpy())
    return np.mean(stats, 0)


def _new_update(updater, store, targets, rng, cfg, static=False):
    """The trainer's current update loop (ppo.train), sync-free."""
    d_act, d_logp, d_adv, d_ret, d_val = targets
    idx_all = np.flatnonzero(store.valid.reshape(-1))
    mbs = [np.sort(perm[k:k + cfg.minibatch]) for perm in (rng.permutation(idx_all) for _ in range(cfg.epochs))
           for k in range(0, len(perm), cfg.minibatch)]
    d_mbs = torch.split(torch.from_numpy(np.concatenate(mbs)), [len(m) for m in mbs])
    stats = [updater(store.batch(mb, rows=None if static else store.rows(mh), static=static), d_act[mb], d_logp[mb],
                     d_adv[mb], d_ret[mb], d_val[mb], compiled=len(mh) == cfg.minibatch) for mh, mb in zip(mbs, d_mbs)]
    return np.mean(torch.stack(stats).numpy(), 0)


@pytest.mark.parametrize("arch", [{}, TINY_TRUNK], ids=["smoke", "trunk"])
def test_eager_update_reproduces_legacy_loop(schema_spec, one_thread, arch):
    """compile off without static rows = the pre-change update, bit for bit: same weights, Adam state and logged
    statistics after two epochs of minibatches (one of them short) on the same rollout."""
    spec, dtypes = schema_spec
    cfg = resolve_config({**arch, "minibatch": 48, "epochs": 2})
    store = _store(spec, dtypes, 2, 64, seed=1, max_cand=40, max_prompt=24)
    targets = _targets(store)
    torch.manual_seed(0)
    a = Policy(spec, **arch)
    b = Policy(spec, **arch)
    b.load_state_dict(a.state_dict())
    opt_a, opt_b = (torch.optim.Adam(m.parameters(), lr=1e-3, eps=1e-5) for m in (a, b))
    st_a = _legacy_update(a, opt_a, store, targets, np.random.default_rng(5), cfg)
    st_b = _new_update(MinibatchUpdater(b, opt_b, cfg, False, "cpu"), store, targets, np.random.default_rng(5), cfg)
    assert weights_hash(a) == weights_hash(b)
    assert np.array_equal(st_a, st_b)
    for sa, sb in zip(opt_a.state.values(), opt_b.state.values()):
        assert torch.equal(sa["exp_avg"], sb["exp_avg"]) and torch.equal(sa["exp_avg_sq"], sb["exp_avg_sq"])


def _head_obs_to_device(obs, max_rows):
    """features.obs_to_device as of e80ea22 on the cpu (cand / prompt_options cut to this batch's largest counts)."""
    c, p = _row_need(obs, max_rows)
    out = {}
    for name in USED_BUFFERS:
        a = obs[name]
        if name == "cand":
            a = a[:, :c]
        elif name == "prompt_options":
            a = a[:, :p]
        out[name] = torch.from_numpy(np.ascontiguousarray(a))
    return out


def _head_put(store, t, x):
    """RolloutStore.put as of e80ea22 (no host copies of the row counts)."""
    for name in USED_BUFFERS:
        dst, src = store.obs[name][t], x[name]
        if name in ROW_TRUNCATED:
            w = min(src.shape[1], dst.shape[1])
            dst[:, :w].copy_(src[:, :w])
            dst[:, w:].zero_()
        else:
            dst.copy_(src)


def _head_train(cfg):
    """`ppo.train` as of e80ea22 (HEAD before the trainer-perf merge: start pools, start_entry bookkeeping) for a
    fresh-only cpu run of `cfg.max_updates` updates without baseline, checkpoints or evals, kept verbatim where it
    computes: the set-up order that consumes RNG (seeds, env, model init, the activation probe's forward, pool warm,
    Adam), the rollout (validated Categorical sampling, row cuts per step, rewards with start_entry), GAE and the PPO
    update (`_legacy_update`). Returns (model, optimizer, one statistics row per update as in metrics.jsonl)."""
    torch.set_num_threads(cfg.torch_threads)
    torch.manual_seed(cfg.seed)
    rng = np.random.default_rng(cfg.seed)
    env = VecEnv(cfg.envs, **env_kwargs(cfg, f"PPO{cfg.seed}"))
    try:
        spec = ModelSpec.from_schema(env.schema)
        bind_spec(spec)
        assert start_pool_from_config(env, cfg) is None
        model = Policy(spec, **{k: getattr(cfg, k) for k in ARCH_KEYS})
        activation_bytes_per_sample(model, spec, rows=cfg.max_rows)
        env.pool.warm()
        rewards = EpisodeRewards(cfg.objective, spec.aux_fields, env.schema["last_episode"].fields, cfg.envs,
                                 cfg.ascension, win_bonus=cfg.win_bonus, shaped_floors=cfg.shaped_floors)
        opt = torch.optim.Adam(model.parameters(), lr=cfg.lr, eps=1e-5)
        store = RolloutStore(spec, cfg.rollout, cfg.envs, cfg.max_rows,
                             {k: env.buffers[k].dtype for k in env.buffers.arrays})
        obs = env.reset()
        rewards.reset(obs["aux"], start_entry=env.info()["start_entry"])
        rows = []
        for _ in range(cfg.max_updates):
            episodes = 0
            model.eval()
            for t in range(cfg.rollout):
                cand_n = obs["cand_n"].copy()
                x = _head_obs_to_device(obs, cfg.max_rows)
                with torch.no_grad():
                    logits, value = model(x)
                dist = torch.distributions.Categorical(logits=logits)
                a = dist.sample()
                logp = dist.log_prob(a)
                _head_put(store, t, x)
                a_np = a.cpu().numpy().astype(np.int32)
                valid = cand_n > 0
                a_np[~valid] = -1
                store.actions[t] = np.maximum(a_np, 0)
                store.logp[t] = logp.cpu().numpy()
                store.values[t] = value.cpu().numpy()
                store.valid[t] = valid
                obs, rew, term, trunc, status, info = env.step(a_np)
                done = (term | trunc).astype(bool)
                r, fin = rewards.step(rew, done, info["last_episode"], obs["aux"], start_entry=info["start_entry"])
                store.rewards[t] = r
                store.dones[t] = done
                episodes += len(fin)
            with torch.no_grad():
                _, last_v = model(_head_obs_to_device(obs, cfg.max_rows))
            last_v = last_v.cpu().numpy()
            adv = np.zeros_like(store.rewards)
            gae = np.zeros(cfg.envs, dtype=np.float32)
            for t in reversed(range(cfg.rollout)):
                nv = last_v if t == cfg.rollout - 1 else store.values[t + 1]
                nonterm = 1.0 - store.dones[t]
                delta = store.rewards[t] + cfg.gamma * nv * nonterm - store.values[t]
                gae = delta + cfg.gamma * cfg.lam * nonterm * gae
                adv[t] = gae
            ret = adv + store.values
            model.train()
            idx_all = np.flatnonzero(store.valid.reshape(-1))
            f_adv = adv.reshape(-1)
            a_mean, a_std = float(f_adv[idx_all].mean()), float(f_adv[idx_all].std()) + 1e-8
            to = lambda a_: torch.from_numpy(np.ascontiguousarray(a_))  # noqa: E731
            targets = (to(store.actions.reshape(-1)), to(store.logp.reshape(-1)),
                       to(((f_adv - a_mean) / a_std).astype(np.float32)), to(ret.reshape(-1)),
                       to(store.values.reshape(-1)))
            st = _legacy_update(model, opt, store, targets, rng, cfg)
            sm = dict(zip(("entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac", "loss", "grad_norm"),
                          (float(x) for x in st)))
            ret_v, val_v = ret.reshape(-1)[idx_all], store.values.reshape(-1)[idx_all]
            var_r = float(np.var(ret_v)) if len(ret_v) else 0.0
            ev = (1.0 - float(np.var(ret_v - val_v)) / var_r) if var_r > 1e-12 else None
            rows.append({"episodes": episodes, "env_steps_update": int(np.count_nonzero(store.valid)),
                         "reward_mean": float(store.rewards.reshape(-1)[idx_all].mean()), **sm,
                         "explained_variance": ev})
        return model, opt, rows
    finally:
        env.close()


@pytest.mark.parametrize("arch", [{}, TINY_TRUNK], ids=["smoke", "trunk"])
def test_train_default_path_reproduces_head_loop(tmp_path, one_thread, arch):
    """The default path (compile off, no static rows) with fresh starts only (no --start-saves): `train()` on a real
    env reproduces the trainer loop as of e80ea22 (`_head_train`) bit for bit: same weights, Adam state and
    per-update statistics after three updates of two epochs, each epoch ending in a short minibatch."""
    given = {**arch, "out": str(tmp_path / "run"), "envs": 16, "threads": 2, "pool": 8, "rollout": 8,
             "minibatch": 40, "epochs": 2, "max_updates": 3, "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0,
             "baseline_steps": 0, "torch_threads": 1, "max_hours": 0.5, "seed": 3}
    cfg = resolve_config(given)
    assert cfg.compile == "off" and not use_static_rows(cfg) and not uses_start_saves(cfg)
    s = train(cfg, log=lambda m: None)
    assert s["updates"] == 3 and s["static_rows"] is False and s["compile_check"] is None
    ref_model, ref_opt, ref_rows = _head_train(resolve_config(given))
    rows = read_jsonl(tmp_path / "run" / "metrics.jsonl")
    assert len(rows) == len(ref_rows) == 3
    assert all(r["env_steps_update"] > 2 * cfg.minibatch and r["env_steps_update"] % cfg.minibatch for r in ref_rows)
    assert [{k: r[k] for k in ref} for r, ref in zip(rows, ref_rows)] == ref_rows
    assert s["weights_hash"] == weights_hash(ref_model)
    got = torch.load(s["last_checkpoint"], map_location="cpu", weights_only=False)["optimizer"]["state"]
    want = ref_opt.state_dict()["state"]
    assert sorted(got) == sorted(want) and len(want) > 100       # parameters that never get a gradient have none
    for k in want:
        assert torch.equal(got[k]["exp_avg"], want[k]["exp_avg"]) and torch.equal(got[k]["exp_avg_sq"],
                                                                                   want[k]["exp_avg_sq"])


def test_rollout_sampling_matches_validated_categorical(schema_spec):
    """InferenceRunner.sample (no argument validation, no host syncs) draws the same actions and log-probs as the
    validated torch.distributions.Categorical of the old rollout from the same RNG state."""
    spec, dtypes = schema_spec
    torch.manual_seed(0)
    m = Policy(spec, **TINY_TRUNK).eval()
    x = obs_to_device(random_obs(spec, 32, np.random.default_rng(2), dtypes=dtypes), "cpu", max_rows=MAX_ROWS)
    torch.manual_seed(7)
    a_new, lp_new, v_new = InferenceRunner(m, False).sample(x)
    torch.manual_seed(7)
    with torch.no_grad():
        logits, value = m(x)
    dist = torch.distributions.Categorical(logits=logits)
    a_old = dist.sample()
    assert torch.equal(a_new, a_old) and torch.equal(lp_new, dist.log_prob(a_old)) and torch.equal(v_new, value)
    assert (a_new < torch.from_numpy(np.minimum(x["cand_n"].numpy(), MAX_ROWS))).all()


def test_static_rows_shapes_and_equivalence(schema_spec):
    """Static rows: cand / prompt_options always max_rows wide (store gather and env upload), the same logits on live
    candidates, values and loss as the per-batch cut, and a static-shape compiled function never recompiles when the
    batch's largest cand_n / prompt_option_n changes (the dynamic cut would)."""
    spec, dtypes = schema_spec
    store = _store(spec, dtypes, 3, 32, seed=3, max_cand=[3, 17, 60], max_prompt=50)
    targets = _targets(store)
    torch.manual_seed(0)
    m = Policy(spec, **TINY_TRUNK)
    widths = set()
    for t in range(3):
        idx = np.arange(t * 32, (t + 1) * 32)
        xs, xd = store.batch(idx, static=True), store.batch(idx)
        assert xs["cand"].shape[1] == xs["prompt_options"].shape[1] == MAX_ROWS
        widths.add((xd["cand"].shape[1], xd["prompt_options"].shape[1]))
        ls, vs = m(xs)
        ld, vd = m(xd)
        c = ld.shape[1]
        assert (ls[:, c:] <= -1e8).all(), "rows past the cut are masked"
        live = ld > -1e8
        assert torch.allclose(ls[:, :c][live], ld[live], atol=1e-5) and torch.allclose(vs, vd, atol=1e-5)
        args = [a_[torch.from_numpy(idx)] for a_ in targets]
        kw = dict(clip=0.2, vf_coef=0.5, ent_coef=0.01)
        assert math.isclose(ppo_loss(m, xs, *args, **kw)[0].item(), ppo_loss(m, xd, *args, **kw)[0].item(),
                            rel_tol=1e-5, abs_tol=1e-6)
    assert len(widths) == 3, widths
    obs = random_obs(spec, 4, np.random.default_rng(4), dtypes=dtypes, max_cand=5, max_prompt=3)
    x = obs_to_device(obs, "cpu", max_rows=MAX_ROWS, static=True)
    assert x["cand"].shape[1] == x["prompt_options"].shape[1] == MAX_ROWS
    assert obs_to_device(obs, "cpu", max_rows=MAX_ROWS)["cand"].shape[1] <= 5

    def count_graphs(static):
        graphs = []

        def backend(gm, example_inputs):
            graphs.append(gm)
            return gm.forward

        torch._dynamo.reset()
        fn = torch.compile(lambda xb, *a: ppo_loss(m, xb, *a, clip=0.2, vf_coef=0.5, ent_coef=0.01),
                           backend=backend, dynamic=False)
        for t in range(3):
            idx = np.arange(t * 32, (t + 1) * 32)
            fn(store.batch(idx, static=static), *[a_[torch.from_numpy(idx)] for a_ in targets])
        torch._dynamo.reset()
        return len(graphs)

    assert count_graphs(True) == 1
    assert count_graphs(False) == 3


@pytest.fixture
def inductor_cpu():
    torch._dynamo.reset()
    yield
    torch._dynamo.reset()


def test_compiled_update_matches_eager(schema_spec, inductor_cpu):
    """torch.compile (inductor, cpu, fp32) of the PPO minibatch loss + backward and of the rollout forward: the
    same loss (rel <= 1e-4) and per-parameter gradient norms as eager on a fixed batch, and the same loss trajectory
    over three Adam steps."""
    spec, dtypes = schema_spec
    cfg = resolve_config({**TINY_TRUNK, "minibatch": 32, "compile": "default"})
    store = _store(spec, dtypes, 1, 32, seed=6)
    targets = _targets(store)
    idx = torch.arange(32)
    args = (store.batch(idx, static=True), *[a_[idx] for a_ in targets])
    torch.manual_seed(0)
    m = Policy(spec, **TINY_TRUNK)
    opt = torch.optim.Adam(m.parameters(), lr=1e-3, eps=1e-5)
    upd = MinibatchUpdater(m, opt, cfg, False, "cpu")
    chk = check_compiled(upd, args, amp=False)
    assert chk["ok"] and chk["loss_rel"] <= 1e-4, chk
    assert all(p.grad is None for p in m.parameters()), "the check leaves no gradients behind"

    torch.manual_seed(0)
    m2 = Policy(spec, **TINY_TRUNK)
    m2.load_state_dict(m.state_dict())
    upd2 = MinibatchUpdater(m2, torch.optim.Adam(m2.parameters(), lr=1e-3, eps=1e-5), cfg, False, "cpu")
    for _ in range(3):
        se, sc = upd2(*args, compiled=False), upd(*args)
        assert math.isclose(sc[5].item(), se[5].item(), rel_tol=1e-4, abs_tol=1e-6), (sc, se)

    m.eval()
    runner = InferenceRunner(m, False, "default", "cpu")
    x = obs_to_device(random_obs(spec, 16, np.random.default_rng(8), dtypes=dtypes), "cpu", max_rows=MAX_ROWS,
                      static=True)
    fchk = check_compiled_forward(runner, x, amp=False)
    assert fchk["ok"] and fchk["logits_rel"] <= 1e-4, fchk


def test_compiled_check_flags_wrong_gradients(schema_spec):
    """The startup check fails a "compiled" function whose loss is right but whose gradient is not (the signature of
    the MPS inductor bug: slot-embedding gradients x batch size, identical loss), and passes the eager function."""
    spec, dtypes = schema_spec
    cfg = resolve_config({**TINY_TRUNK, "minibatch": 32})
    store = _store(spec, dtypes, 1, 32, seed=9)
    idx = torch.arange(32)
    args = (store.batch(idx, static=True), *[a_[idx] for a_ in _targets(store)])
    torch.manual_seed(0)
    m = Policy(spec, **TINY_TRUNK)
    upd = MinibatchUpdater(m, torch.optim.Adam(m.parameters()), cfg, False, "cpu")
    upd.compiled = upd.eager
    assert check_compiled(upd, args, amp=False)["ok"]

    def wrong_grad(*a):
        loss, st = upd.eager(*a)
        extra = (m.hand_pos.weight * 3.0).sum()
        return loss + extra - extra.detach(), st                # same value, gradient of hand_pos shifted by 3

    upd.compiled = wrong_grad
    chk = check_compiled(upd, args, amp=True)                   # even with the loose bf16 tolerances
    assert not chk["ok"] and chk["worst_param"] == "hand_pos.weight" and chk["loss_rel"] == 0.0, chk


def test_train_compiled_run(tmp_path, inductor_cpu):
    """A tiny real run with --compile default: the eager-vs-compiled checks pass, the run finishes and logs."""
    run = tmp_path / "run"
    cfg = resolve_config({"out": str(run), "envs": 16, "threads": 2, "pool": 8, "rollout": 4, "minibatch": 32,
                          "d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2, "max_updates": 2, "ckpt_every": 0,
                          "ckpt_every_min": 0, "log_every": 100, "max_hours": 0.5, "torch_threads": 2,
                          "compile": "default"})
    logs = []
    s = train(cfg, log=logs.append)
    assert s["updates"] == 2 and s["stop_reason"] == "max_updates" and s["static_rows"] is True
    assert s["compile_check"]["update"]["ok"] and s["compile_check"]["forward"]["ok"]
    assert json.loads((run / "status.json").read_text())["state"] == "finished"
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2] and all(np.isfinite(r["loss"]) for r in rows)
    assert any("compiled minibatch update vs eager" in m_ and "OK" in m_ for m_ in logs)


def test_bench_update_cli(tmp_path, capsys):
    from sts2train.cli import main

    out = tmp_path / "bench.json"
    rc = main(["bench-update", "--preset", "smoke", "--device", "cpu", "--minibatch", "32", "--envs", "16",
               "--steps", "2", "--warmup", "1", "--infer-steps", "2", "--torch-threads", "2", "--profile",
               "--profile-steps", "1", "--top", "5", "--json", str(out)])
    assert rc == 0
    text = capsys.readouterr().out
    assert "ms per minibatch of 32" in text and "A100" in text and "profile" in text
    r = json.loads(out.read_text())["results"][0]
    assert r["mode"] == "off" and r["update_ms"] > 0 and r["update_loss_finite"] and len(r["profile"]["ops"]) == 5
    rc = main(["bench-update", "--preset", "large", "--device", "cpu", "--minibatch", "4096", "--steps", "1"])
    assert rc == 2 and "GiB" in capsys.readouterr().out
