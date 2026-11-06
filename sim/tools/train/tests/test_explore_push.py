"""Bounded exploration push (`--explore-push DELTA`, wave 12; `ppo.explore_push_term`, `ppo.ppo_loss`).

Evidence (run w9-e2, 2026-11-06 10:18 EST, --explore-logp policy --dual-clip 3): explored rows at log pi ~ -80 moved by
> 20 nats within one update, the log-ratio clamp then zeroed them and the next update pushed them back: approx_kl
alternating 0.4 / 1e6. The push takes the mixture draws out of the surrogate and gives them
L_push = -mean_explored A x clamp(logp_new - logp_old, -DELTA, +DELTA). Gates here:
(a) DELTA = 0 (default) is bit-identical to the wave-11 loss (inline verbatim copy) incl. 4 Adam updater steps, smoke /
    trunk x aux on / off: stats, weights and Adam state torch.equal;
(b) DELTA = 1: explored samples with |dlogp| < 1 get exactly the gradient of A x log pi (mean over explored) and no
    surrogate contribution;
(c) explored samples with |dlogp| >= DELTA get zero gradient from the push (and are counted in push_clamped);
(d) non-explored samples: loss and gradients do not depend on DELTA; approx_kl / clipfrac are theirs only;
(e) the explorer's `last_explored` mask is exactly the mixture draws;
(f) config validation / CLI; a short CPU train() run logs the push statistics.
CPU only."""

from __future__ import annotations

import hashlib
import math
from pathlib import Path

import numpy as np
import pytest
import torch
import torch.nn.functional as F

from sts2train import ppo
from sts2train.bench import load_spec, random_obs
from sts2train.explore import KindExplorer
from sts2train.features import bind_spec, obs_to_device
from sts2train.model import Policy
from sts2train.ppo import (LOG_RATIO_MAX, MinibatchUpdater, explore_push_term, ppo_loss, ppo_surrogate, resolve_config,
                           stat_names, train)
from sts2train.runlog import read_jsonl

DATA = Path(__file__).parent / "data"
TINY_TRUNK = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}
MAX_ROWS = 64
CLIP, VF, ENT = 0.2, 0.5, 0.01
POLICY_EXPLORE = {"explore_kinds": "ChooseRestOption", "explore_eps": 0.3, "explore_logp": "policy"}


@pytest.fixture(scope="module")
def spec():
    s, dtypes = load_spec()
    bind_spec(s)
    return s, dtypes


@pytest.fixture
def one_thread():
    n = torch.get_num_threads()
    torch.set_num_threads(1)
    yield
    torch.set_num_threads(n)


# ------------------------------------------------------------------------------------------------ the wave-11 loss
def _w11_surrogate(newlogp, oldlogp, madv, *, clip, dual_clip=0.0):
    """ppo.ppo_surrogate as of 69463a9 (wave 11), verbatim."""
    logr = (newlogp - oldlogp).clamp(max=LOG_RATIO_MAX)
    ratio = logr.exp()
    surr = torch.min(ratio * madv, ratio.clamp(1 - clip, 1 + clip) * madv)
    hit = None
    if dual_clip > 0:
        floor = dual_clip * madv
        neg = madv < 0
        hit = neg & (surr < floor)
        surr = torch.where(neg, torch.max(surr, floor), surr)
    return -surr.mean(), ratio, logr, hit


def _w11_loss(model, xb, act, oldlogp, madv, vret, vold, *, clip, vf_coef, ent_coef, aux=None, aux_coef=0.0,
              dual_clip=0.0):
    """ppo.ppo_loss as of 69463a9 (wave 11), verbatim (fp32, no autocast)."""
    heads = None
    if aux is not None:
        logits, v, heads = model(xb, aux=True)
    else:
        logits, v = model(xb)
    logp_all = F.log_softmax(logits, -1)
    newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
    ent = -(logp_all.exp() * logp_all.clamp_min(-1e4)).sum(-1).mean()
    pg, ratio, logr, hit = _w11_surrogate(newlogp, oldlogp, madv, clip=clip, dual_clip=dual_clip)
    vclip = vold + (v - vold).clamp(-clip, clip)
    vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
    loss = pg + vf_coef * vl - ent_coef * ent
    if heads is not None:
        wl, cl, wf, cf_ = ppo.aux_losses(heads, *aux)
        loss = loss + aux_coef * (wl + cl)
    with torch.no_grad():
        kl = ((ratio - 1) - logr).mean()
        cf = ((ratio - 1).abs() > clip).float().mean()
    st = [ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach()]
    if hit is not None:
        st.append(hit.float().mean())
    if heads is not None:
        st += [wl.detach(), cl.detach(), wf, cf_]
    return loss, torch.stack(st)


def _batch(spec, n=48, seed=3, jitter=0.4):
    s, dtypes = spec
    rng = np.random.default_rng(seed)
    x = obs_to_device(random_obs(s, n, rng, dtypes=dtypes, max_cand=40, max_prompt=30), "cpu", max_rows=MAX_ROWS)
    legal = torch.clamp(x["cand_n"].long(), 1, MAX_ROWS)
    act = torch.from_numpy(np.floor(rng.random(n) * legal.numpy()).astype(np.int64))
    oldlogp = -torch.log(legal.float()) + torch.from_numpy((jitter * rng.standard_normal(n)).astype(np.float32))
    adv = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    madv = (adv - adv.mean()) / (adv.std() + 1e-8)
    vret = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    vold = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    return x, act, oldlogp, madv, vret, vold


def _aux_targets(n, k):
    g = torch.Generator().manual_seed(k)
    return (torch.rand(n, generator=g), (torch.rand(n, generator=g) > 0.5).float(), torch.rand(n, generator=g),
            (torch.rand(n, generator=g) > 0.5).float())


def _hash(m):
    return hashlib.sha256(b"".join(p.detach().numpy().tobytes() for p in m.parameters())).hexdigest()[:16]


# ------------------------------------------------------------------------------------------------ (a) off = wave 11
@pytest.mark.parametrize("aux", [False, True], ids=["noaux", "aux"])
@pytest.mark.parametrize("arch", [{}, TINY_TRUNK], ids=["smoke", "trunk"])
def test_off_bit_identical_to_wave11(spec, one_thread, arch, aux):
    """explore_push = 0 (default): MinibatchUpdater's 4 Adam steps give the same statistics, weights and Adam state as
    the wave-11 loss + the wave-11 update step (zero_grad, backward, clip_grad_norm_, step, stats layout), torch.equal;
    also a single ppo_loss call with an `explored` mask but push = 0 equals the wave-11 loss exactly."""
    res = {}
    for name in ("new", "ref"):
        torch.manual_seed(0)
        m = Policy(spec[0], **arch, **({"aux_heads": True} if aux else {}))
        cfg = resolve_config({"aux_heads": aux, "minibatch": 64})
        assert cfg.explore_push == 0.0 and cfg.explore_push_coef == 1.0
        opt = torch.optim.Adam(m.parameters(), lr=3e-4)
        up = MinibatchUpdater(m, opt, cfg, False, "cpu") if name == "new" else None
        sts = []
        for k in range(4):
            b = _batch(spec, n=64, seed=10 + k)
            tg = _aux_targets(64, k) if aux else ()
            if up is not None:
                sts.append(up(*b, *tg, compiled=False))
            else:
                opt.zero_grad(set_to_none=True)
                loss, st = _w11_loss(m, *b, clip=cfg.clip, vf_coef=cfg.vf_coef, ent_coef=cfg.ent_coef,
                                     aux=tg if aux else None, aux_coef=cfg.aux_coef)
                loss.backward()
                gn = torch.nn.utils.clip_grad_norm_(m.parameters(), cfg.max_grad_norm)
                opt.step()
                sts.append(torch.cat([st[:6], gn.detach().float().view(1), st[6:]]) if aux
                           else torch.cat([st, gn.detach().float().view(1)]))
        res[name] = (torch.stack(sts), _hash(m),
                     {k: (v["exp_avg"].clone(), v["exp_avg_sq"].clone()) for k, v in opt.state_dict()["state"].items()})
    (sn, hn, on), (sr, hr, orf) = res["new"], res["ref"]
    assert torch.equal(sn, sr) and hn == hr
    assert sorted(on) == sorted(orf) and all(torch.equal(on[k][0], orf[k][0]) and torch.equal(on[k][1], orf[k][1])
                                             for k in orf)
    assert len(sn[0]) == len(stat_names(resolve_config({"aux_heads": aux})))

    torch.manual_seed(1)
    m = Policy(spec[0], **arch)
    b = _batch(spec, seed=4)
    explored = torch.arange(len(b[1])) % 3 == 0
    outs = []
    for kw in ({}, {"explored": explored}, {"explored": explored, "push": 0.0}):
        m.zero_grad(set_to_none=True)
        loss, st = ppo_loss(m, *b, clip=CLIP, vf_coef=VF, ent_coef=ENT, **kw)
        loss.backward()
        outs.append((loss.detach(), st, {k: p.grad.clone() for k, p in m.named_parameters() if p.grad is not None}))
    m.zero_grad(set_to_none=True)
    loss, st = _w11_loss(m, *b, clip=CLIP, vf_coef=VF, ent_coef=ENT)
    loss.backward()
    g_r = {k: p.grad.clone() for k, p in m.named_parameters() if p.grad is not None}
    for l_, s_, g_ in outs:
        assert torch.equal(l_, loss.detach()) and torch.equal(s_, st) and len(s_) == 6
        assert sorted(g_) == sorted(g_r) and all(torch.equal(g_[k], g_r[k]) for k in g_r)


def test_stat_names_order(spec, one_thread):
    assert stat_names(resolve_config({})) == ppo.STAT_NAMES
    on = resolve_config({**POLICY_EXPLORE, "explore_push": 1.0})
    assert stat_names(on) == ppo.STAT_NAMES + ppo.PUSH_STAT_NAMES
    all_on = resolve_config({**POLICY_EXPLORE, "explore_push": 1.0, "dual_clip": 3.0, "aux_heads": True})
    assert stat_names(all_on) == (ppo.STAT_NAMES + ppo.DUAL_STAT_NAMES + ppo.PUSH_STAT_NAMES + ppo.AUX_STAT_NAMES)
    torch.manual_seed(0)
    m = Policy(spec[0], aux_heads=True)
    b = _batch(spec, n=40, seed=5)
    explored = torch.zeros(40, dtype=torch.bool)
    explored[[1, 4, 9, 22]] = True
    up = MinibatchUpdater(m, torch.optim.SGD(m.parameters(), lr=0.0), all_on, False, "cpu")
    out = up(*b, explored, *_aux_targets(40, 0), compiled=False)
    names = stat_names(all_on)
    assert len(out) == len(names) and torch.isfinite(out).all()
    d = dict(zip(names, out.tolist()))
    assert d["push_share"] == pytest.approx(4 / 40) and d["grad_norm"] > 0


# ------------------------------------------------------------------------------------------------ (b), (c) the push
def _leaf(n=32, k=6, seed=0):
    g = torch.Generator().manual_seed(seed)
    logits = (torch.randn(n, k, generator=g) * 3).requires_grad_(True)
    act = torch.randint(0, k, (n,), generator=g)
    newlogp = F.log_softmax(logits, -1).gather(1, act.unsqueeze(1)).squeeze(1)
    return logits, act, newlogp


def test_push_gradient_inside_the_trust_region():
    """DELTA = 1, explored rows with |dlogp| < 1: grad L_push = grad of -mean_explored A x log pi (autograd of A x
    logp on those rows); the non-explored rows get no push gradient; the excluded rows get no surrogate gradient."""
    logits, act, newlogp = _leaf()
    n = len(newlogp)
    g = torch.Generator().manual_seed(1)
    explored = torch.zeros(n, dtype=torch.bool)
    explored[::3] = True
    dl = torch.empty(n).uniform_(-0.9, 0.9, generator=g)
    oldlogp = (newlogp - dl).detach()
    oldlogp[~explored] = -80.0                  # far rows off the push path: must not matter
    madv = torch.randn(n, generator=g)
    madv[explored] = madv[explored].abs() + 0.1  # A > 0 on the explored rows
    push, st = explore_push_term(newlogp, oldlogp, madv, explored, delta=1.0)
    k = int(explored.sum())
    want = -(madv[explored] * newlogp[explored]).sum() / k
    (gp,) = torch.autograd.grad(push, logits, retain_graph=True)
    (gw,) = torch.autograd.grad(want, logits, retain_graph=True)
    assert torch.allclose(gp, gw, rtol=1e-6, atol=1e-7) and (gp[explored].abs().sum(-1) > 0).all()
    assert torch.equal(gp[~explored], torch.zeros_like(gp[~explored]))
    assert push.item() == pytest.approx(-(madv[explored] * dl[explored]).sum().item() / k, rel=1e-5, abs=1e-5)
    share, clamped, dlogp, pl = st.tolist()
    assert share == pytest.approx(k / n) and clamped == 0.0 and pl == pytest.approx(push.item())
    assert dlogp == pytest.approx(dl[explored].mean().item(), abs=1e-5)
    # the surrogate with exclude: those rows contribute 0 (value and gradient), the mean stays over the minibatch
    pg, _, _, _ = ppo_surrogate(newlogp, oldlogp, madv, clip=CLIP, exclude=explored)
    logr = (newlogp - oldlogp).clamp(max=LOG_RATIO_MAX)
    r = logr.exp()
    surr = torch.min(r * madv, r.clamp(1 - CLIP, 1 + CLIP) * madv)
    assert torch.equal(pg, -torch.where(explored, torch.zeros_like(surr), surr).mean())
    (gs,) = torch.autograd.grad(pg, logits)
    assert torch.equal(gs[explored], torch.zeros_like(gs[explored]))


def test_push_zero_beyond_delta():
    """Explored rows with |dlogp| >= DELTA (both signs, incl. a stored log pi_old of -80 / -inf, i.e. dlogp ~ +80 /
    +inf): zero push gradient, finite loss, counted in push_clamped; the rows inside still get theirs."""
    logits, act, newlogp = _leaf(n=12, seed=3)
    explored = torch.ones(12, dtype=torch.bool)
    explored[10:] = False
    dl = torch.tensor([1.5, -1.5, 3.0, -2.0, 1.25, -1.75, 0.2, -0.3, 0.5, 0.0, 0.1, 0.1])
    oldlogp = (newlogp - dl).detach()
    oldlogp[2] = -80.0
    oldlogp[3] = -math.inf
    madv = torch.linspace(-1.0, 1.0, 12)
    push, st = explore_push_term(newlogp, oldlogp, madv, explored, delta=1.0)
    assert torch.isfinite(push)
    (gp,) = torch.autograd.grad(push, logits)
    beyond = torch.arange(6)                      # |dlogp| > 1 (row 2 dlogp ~ 80, row 3 inf)
    assert torch.isfinite(gp).all() and torch.equal(gp[beyond], torch.zeros_like(gp[beyond]))
    inside = torch.tensor([6, 7, 8, 9])
    assert (gp[inside[madv[inside] != 0]].abs().sum(-1) > 0).all()
    share, clamped, dlogp, _ = st.tolist()
    assert share == pytest.approx(10 / 12)
    assert clamped == pytest.approx(6 / 10), "rows 0-5: |dlogp| >= 1"
    assert math.isinf(dlogp), "the unclamped mean shows the -inf stored log-prob"


# ------------------------------------------------------------------------------------------------ (d) the rest
def test_non_explored_unchanged_by_delta(spec, one_thread):
    """With push_coef = 0 the loss and every gradient do not depend on DELTA, and equal the wave-11 loss on the same
    batch with the explored rows' advantage set to 0 (their surrogate is exactly 0 then too). approx_kl / clipfrac are
    over the non-explored rows. With push_coef = 1 the difference to coef 0 is exactly the push term."""
    torch.manual_seed(0)
    m = Policy(spec[0])
    x, act, oldlogp, madv, vret, vold = _batch(spec, n=48, seed=6)
    explored = torch.zeros(48, dtype=torch.bool)
    explored[[0, 5, 6, 13, 30, 47]] = True
    oldlogp = oldlogp.clone()
    oldlogp[[0, 5]] = -80.0                     # explored rows at ~0 probability: far outside the ratio window

    def run(fn, *args, **kw):
        m.zero_grad(set_to_none=True)
        loss, st = fn(m, *args, clip=CLIP, vf_coef=VF, ent_coef=ENT, **kw)
        loss.backward()
        return loss.detach(), st, {k: p.grad.clone() for k, p in m.named_parameters() if p.grad is not None}

    args = (x, act, oldlogp, madv, vret, vold)
    outs = [run(ppo_loss, *args, explored=explored, push=d, push_coef=0.0) for d in (0.5, 1.0, 4.0)]
    madv0 = torch.where(explored, torch.zeros_like(madv), madv)
    l_r, s_r, g_r = run(_w11_loss, x, act, oldlogp, madv0, vret, vold)
    for l_, s_, g_ in outs:
        assert len(s_) == 10
        assert torch.allclose(l_, l_r, rtol=0, atol=1e-6) and torch.allclose(s_[[0, 1, 2, 5]], s_r[[0, 1, 2, 5]],
                                                                               rtol=0, atol=1e-6)
        assert sorted(g_) == sorted(g_r) and all(torch.allclose(g_[k], g_r[k], rtol=1e-5, atol=1e-7) for k in g_r)
        assert torch.equal(l_, outs[0][0]) and all(torch.equal(g_[k], outs[0][2][k]) for k in g_)
    with torch.no_grad():
        logits, _ = m(x)
        nl = F.log_softmax(logits, -1).gather(1, act.unsqueeze(1)).squeeze(1)
        keep = ~explored
        logr = (nl - oldlogp).clamp(max=LOG_RATIO_MAX)[keep]
        kl = ((logr.exp() - 1) - logr).mean()
        cf = ((logr.exp() - 1).abs() > CLIP).float().mean()
    st = outs[1][1]
    assert st[3].item() == pytest.approx(kl.item(), rel=1e-5, abs=1e-7) and st[4].item() == pytest.approx(cf.item())
    assert s_r[3] > 1e3 * max(kl.item(), 1e-6), "without the exclusion the -80 rows dominate approx_kl"
    l1, s1, _ = run(ppo_loss, *args, explored=explored, push=1.0, push_coef=1.0)
    assert l1.item() == pytest.approx((outs[1][0] + s1[9]).item(), abs=1e-6), "loss = rest + coef x push_loss"


# ------------------------------------------------------------------------------------------------ (e) the mask
def _obs(spec, n, seed=0):
    return {k: np.ascontiguousarray(v).copy() for k, v in random_obs(spec, n, np.random.default_rng(seed)).items()}


def _set_rows(spec, obs, s, kinds, B=None):
    names, C = spec.enum_names["RunActionKind"], spec.cols["cand"]
    obs["cand_n"][s] = len(kinds)
    for j, k in enumerate(kinds):
        obs["cand"][s, j, C["kind"]] = names.index(k)
        obs["cand"][s, j, C["B"]] = -1 if B is None else B[j]
        obs["cand"][s, j, C["option_index"]] = -1


def test_explorer_mask_marks_the_mixture_draws():
    spec = load_spec(str(DATA / "spec-fc2eb34c07bc8513.json"))[0]
    n, w, eps = 64, 8, 0.5
    obs = _obs(spec, n)
    for s in range(n):
        if s % 2 == 0:
            _set_rows(spec, obs, s, ["ChooseRestOption"] * 5 + ["LeaveRestSite"], B=[-1, 0, 1, 2, 3, -1])
        else:
            _set_rows(spec, obs, s, ["PlayCard"] * 3 + ["EndTurn"])
    lg = torch.full((n, w), -1e9)
    g = torch.Generator().manual_seed(0)
    for s in range(n):
        k = int(obs["cand_n"][s])
        lg[s, :k] = torch.randn(k, generator=g) * 3
        if s % 2 == 0:
            lg[s, 1:5] -= 80.0
    ex = KindExplorer(spec, "ChooseRestOption", eps, 7, logp_mode="policy")
    total = 0
    for t in range(12):
        torch.manual_seed(t)
        dist = torch.distributions.Categorical(logits=lg, validate_args=False)
        a = dist.sample()
        a2, lp2, wts = ex(obs, lg, dist, a, dist.log_prob(a), update=3, t=t)
        u = ex.uniforms(3, t, n)
        want = (np.arange(n) % 2 == 0) & (u[0] < eps)
        m = ex.last_explored
        assert m.dtype == bool and m.shape == (n,) and np.array_equal(m, want) and wts is None
        assert not m[1::2].any()
        total += int(m.sum())
        # lane slicing (--overlap double): the same N-slot draw, this lane's rows
        a_l = a[16:40]
        ex(obs_slice(obs, 16, 40), lg[16:40], torch.distributions.Categorical(logits=lg[16:40], validate_args=False),
           a_l, dist.log_prob(a)[16:40], update=3, t=t, lo=16, n_total=n)
        assert np.array_equal(ex.last_explored, want[16:40])
    assert total > 0
    combat = _obs(spec, 8)
    for s in range(8):
        _set_rows(spec, combat, s, ["PlayCard"] * 3 + ["EndTurn"])
    ex(combat, torch.zeros(8, 8), None, torch.zeros(8, dtype=torch.long), torch.zeros(8), update=1, t=0)
    assert ex.last_explored.shape == (8,) and not ex.last_explored.any()


def obs_slice(obs, lo, hi):
    return {k: v[lo:hi] for k, v in obs.items()}


# ------------------------------------------------------------------------------------------------ (f) config / CLI
def test_config_validation(monkeypatch, tmp_path):
    c = resolve_config({})
    assert c.explore_push == 0.0 and c.explore_push_coef == 1.0
    on = resolve_config({**POLICY_EXPLORE, "explore_push": 1, "explore_push_coef": 0.5})
    assert on.explore_push == 1.0 and on.explore_push_coef == 0.5
    for bad, msg in (({**POLICY_EXPLORE, "explore_push": -1.0}, "explore_push"),
                     ({**POLICY_EXPLORE, "explore_push": math.inf}, "explore_push"),
                     ({**POLICY_EXPLORE, "explore_push": math.nan}, "explore_push"),
                     ({**POLICY_EXPLORE, "explore_push": 1.0, "explore_push_coef": -1.0}, "explore_push_coef"),
                     ({**POLICY_EXPLORE, "explore_logp": "decoupled", "explore_push": 1.0}, "explore-logp policy"),
                     ({**POLICY_EXPLORE, "explore_eps": 0.15, "explore_logp": "mixture", "explore_push": 1.0},
                      "explore-logp policy"),
                     ({"explore_push": 1.0}, "explorer")):
        with pytest.raises(ValueError, match=msg):
            resolve_config(bad)
    assert resolve_config({**POLICY_EXPLORE, "explore_logp": "decoupled"}).explore_push == 0.0
    base = ppo.config_dict(on)
    assert resolve_config({}, base).explore_push == 1.0, "a resume inherits the push"
    off = resolve_config({"explore_kinds": ""}, base)
    assert off.explore_eps == 0.0 and off.explore_push == 0.0, "turning the explorer off on a resume drops the push"
    old = {k: v for k, v in base.items() if k not in ("explore_push", "explore_push_coef")}
    r = resolve_config({}, old)
    assert r.explore_push == 0.0 and r.explore_push_coef == 1.0, "a checkpoint without the fields resumes 0 / 1.0"
    from sts2train import cli

    seen = {}

    def fake_plan(given, resume, log=print):
        seen.update(given)
        raise ValueError("stop")

    monkeypatch.setattr(ppo, "plan_run", fake_plan)
    assert cli.main(["train", "--out", str(tmp_path / "r"), "--explore-push", "1", "--explore-push-coef", "2"]) == 2
    assert seen["explore_push"] == 1.0 and seen["explore_push_coef"] == 2.0
    seen.clear()
    cli.main(["train", "--out", str(tmp_path / "r")])
    assert "explore_push" not in seen and "explore_push_coef" not in seen


TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 12, "threads": 2, "rollout": 16, "minibatch": 64, "epochs": 1, "max_updates": 3, "ckpt_every": 1,
       "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2, "log_every": 100,
       "characters": ("SILENT",), "ascension": 10, "step_cap": 24, "start_mode": "stream", "seed": 2, **TINY}


def test_train_logs_push_metrics(tmp_path):
    """A short CPU run with frequent explore kinds, --explore-logp policy --explore-push 1 --dual-clip 3: every update
    logs finite push statistics with explored samples present; the config records the fields; the explore steps
    equal the explored samples (push_share x valid samples, up to the minibatch averaging)."""
    logs = []
    kw = {"explore_kinds": "PlayCard,EndTurn", "explore_eps": 0.3, "explore_logp": "policy", "explore_push": 1.0,
          "dual_clip": 3.0}
    s = train(resolve_config({**RUN, "out": str(tmp_path / "p"), **kw}), log=logs.append)
    rows = read_jsonl(tmp_path / "p" / "metrics.jsonl")
    assert len(rows) == 3
    for r in rows:
        assert r["explore_steps"] > 0 and 0 < r["push_share"] < 1
        assert all(math.isfinite(r[k]) for k in ppo.PUSH_STAT_NAMES + ("approx_kl", "clipfrac", "loss"))
        assert 0 <= r["push_clamped"] <= 1
    assert any("push_share=" in m and "push_dlogp=" in m for m in logs)
    import json
    cfg = json.loads((tmp_path / "p" / "config.json").read_text())["config"]
    assert cfg["explore_push"] == 1.0 and cfg["explore_push_coef"] == 1.0
    assert s["explore"]["explore_steps"] == sum(r["explore_steps"] for r in rows)


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
