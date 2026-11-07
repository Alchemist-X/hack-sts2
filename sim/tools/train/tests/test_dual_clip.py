"""Dual-clip PPO (`--dual-clip C`, Ye et al. 2020) and the always-on log-ratio clamp (`ppo.ppo_surrogate`).

Incident (run w9-e, 2026-11-06 06:35-06:42 EST): --explore-logp policy stored log pi_old of explored rows the policy
gives ~0 probability, so r = exp(logp_new - logp_old) exploded; min(r A, clip(r) A) is r A (unbounded) for A < 0,
approx_kl reached 2.9e12 then nan. Gates here:
(a) dual_clip = 0 is bit-identical to the pre-change formula (whole ppo_loss + every gradient on a tiny Policy) for
    |log r| <= 20; rows above the clamp (logp_old = -80 / -200 / -inf) are finite now: the one intended difference
    (before: a ratio up to inf, inf x 0 = nan, approx_kl inf / nan);
(b) dual_clip = 3, A < 0, r > 3: the contribution is exactly 3 A and its gradient wrt the logits is 0;
(c) A >= 0 (and A < 0 with r inside the floor) unchanged by dual_clip;
(d) the `dual_clipped` statistic counts exactly the floored samples, in `stat_names` order (after grad_norm).
CPU only."""

from __future__ import annotations

import math

import numpy as np
import pytest
import torch
import torch.nn.functional as F

from sts2train import ppo
from sts2train.bench import load_spec, random_obs
from sts2train.features import bind_spec, obs_to_device
from sts2train.model import Policy
from sts2train.ppo import (LOG_RATIO_MAX, MinibatchUpdater, ppo_loss, ppo_surrogate, resolve_config, stat_names)

TINY_TRUNK = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}
MAX_ROWS = 64
CLIP, VF, ENT = 0.2, 0.5, 0.01


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


def _ref_ppo_loss(model, xb, act, oldlogp, madv, vret, vold, *, clip, vf_coef, ent_coef):
    """ppo_loss as of 8f9e971 (before the log-ratio clamp / dual clip), verbatim, no aux heads, fp32."""
    logits, v = model(xb)
    logp_all = F.log_softmax(logits, -1)
    newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
    ent = -(logp_all.exp() * logp_all.clamp_min(-1e4)).sum(-1).mean()
    ratio = (newlogp - oldlogp).exp()
    pg = -torch.min(ratio * madv, ratio.clamp(1 - clip, 1 + clip) * madv).mean()
    vclip = vold + (v - vold).clamp(-clip, clip)
    vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
    loss = pg + vf_coef * vl - ent_coef * ent
    with torch.no_grad():
        kl = ((ratio - 1) - (newlogp - oldlogp)).mean()
        cf = ((ratio - 1).abs() > clip).float().mean()
    return loss, torch.stack([ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach()])


def _ref_surr(newlogp, oldlogp, madv, clip):
    ratio = (newlogp - oldlogp).exp()
    return torch.min(ratio * madv, ratio.clamp(1 - clip, 1 + clip) * madv), ratio


def _batch(spec, n=48, seed=3):
    s, dtypes = spec
    rng = np.random.default_rng(seed)
    x = obs_to_device(random_obs(s, n, rng, dtypes=dtypes, max_cand=40, max_prompt=30), "cpu", max_rows=MAX_ROWS)
    legal = torch.clamp(x["cand_n"].long(), 1, MAX_ROWS)
    act = torch.from_numpy(np.floor(rng.random(n) * legal.numpy()).astype(np.int64))
    oldlogp = -torch.log(legal.float())
    adv = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    madv = (adv - adv.mean()) / (adv.std() + 1e-8)
    vret = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    vold = torch.from_numpy(rng.standard_normal(n).astype(np.float32))
    return x, act, oldlogp, madv, vret, vold


def _run(model, fn, args, **kw):
    model.zero_grad(set_to_none=True)
    loss, st = fn(model, *args, clip=CLIP, vf_coef=VF, ent_coef=ENT, **kw)
    loss.backward()
    return loss.detach(), st.detach(), {k: p.grad.clone() for k, p in model.named_parameters() if p.grad is not None}


# ------------------------------------------------------------------------------------------------ (a) off = before
@pytest.mark.parametrize("arch", [{}, TINY_TRUNK], ids=["smoke", "trunk"])
def test_off_bit_identical_to_reference(spec, one_thread, arch):
    """dual_clip = 0 (the default): loss, the six statistics and every parameter gradient equal the pre-change
    formula bit for bit (torch.equal), on a batch whose log ratios are moderate (|log r| << 20)."""
    torch.manual_seed(0)
    m = Policy(spec[0], **arch)
    args = _batch(spec)
    with torch.no_grad():
        oldlogp = args[2] + 0.3 * torch.randn(len(args[2]), generator=torch.Generator().manual_seed(1))
    args = (*args[:2], oldlogp, *args[3:])
    l_r, s_r, g_r = _run(m, _ref_ppo_loss, args)
    l_n, s_n, g_n = _run(m, ppo_loss, args)
    l_0, s_0, g_0 = _run(m, ppo_loss, args, dual_clip=0.0)
    assert torch.isfinite(s_r).all() and s_r[4] > 0, "some samples clipped: the clip branch is exercised"
    for l_, s_, g_ in ((l_n, s_n, g_n), (l_0, s_0, g_0)):
        assert torch.equal(l_, l_r) and torch.equal(s_, s_r) and len(s_) == 6
        assert sorted(g_) == sorted(g_r) and all(torch.equal(g_[k], g_r[k]) for k in g_r)


def test_off_surrogate_extremes(one_thread):
    """Random tensors incl. logp_old = -80 / -200 / -inf. Rows with log r <= 20: surrogate, ratio and per-row
    gradient identical to the reference. Rows above: ratio exactly exp(20) (finite), where the reference gives a
    ratio of 2e34 (-80), inf (-200, -inf) and nan for A == 0 at an infinite ratio; approx_kl / the loss are finite now
    (the one intended difference: before, inf / nan)."""
    g = torch.Generator().manual_seed(5)
    n = 64
    logits = torch.randn(n, 7, generator=g)
    act = torch.randint(0, 7, (n,), generator=g)
    madv = torch.randn(n, generator=g)
    madv[[3, 9, 11]] = 0.0
    with torch.no_grad():
        nl = F.log_softmax(logits, -1).gather(1, act.unsqueeze(1)).squeeze(1)
    oldlogp = nl + 0.5 * torch.randn(n, generator=g)
    ext = {1: -80.0, 3: -200.0, 5: -80.0, 9: -math.inf, 11: -80.0, 20: -200.0}
    for i, v in ext.items():
        oldlogp[i] = v
    big = torch.zeros(n, dtype=torch.bool)
    big[list(ext)] = True

    def fwd(fn):
        lg = logits.clone().requires_grad_(True)
        newlogp = F.log_softmax(lg, -1).gather(1, act.unsqueeze(1)).squeeze(1)
        out = fn(newlogp)
        return lg, newlogp, out

    lg_r, nl_r, (surr_r, ratio_r) = fwd(lambda nlp: _ref_surr(nlp, oldlogp, madv, CLIP))
    assert not torch.isfinite(ratio_r[big]).all() and torch.isnan(surr_r[[3, 9]]).all(), "reference: inf / nan"
    lg_n, nl_n, (pg, ratio, logr, hit) = fwd(lambda nlp: ppo_surrogate(nlp, oldlogp, madv, clip=CLIP))
    assert hit is None
    assert torch.isfinite(pg) and torch.isfinite(ratio).all() and torch.isfinite(logr).all()
    assert torch.equal(ratio[big], torch.tensor(LOG_RATIO_MAX).exp().expand(int(big.sum())))
    assert torch.equal(ratio[~big], ratio_r[~big]) and torch.equal(logr[~big], (nl_r - oldlogp)[~big].detach())
    kl = ((ratio - 1) - logr).mean()
    assert torch.isfinite(kl)
    # per-row gradients on the moderate rows equal the reference's (the clamp passes them through unchanged)
    surr_n = torch.min(ratio * madv, ratio.clamp(1 - CLIP, 1 + CLIP) * madv)
    mod = (~big).nonzero().squeeze(1)
    (gr,) = torch.autograd.grad(surr_r[mod].sum(), lg_r)
    (gn,) = torch.autograd.grad(surr_n[mod].sum(), lg_n, retain_graph=True)
    assert torch.equal(gr[mod], gn[mod]) and torch.isfinite(gn).all()
    assert torch.isnan(gr[[3, 9, 20]]).all(), "reference: exp backward 0 x inf = nan even on rows outside the sum"
    # the clamped rows carry no gradient (the clamp's backward is 0 above the bound): finite, no nan
    (gb,) = torch.autograd.grad(-pg, lg_n)
    assert torch.isfinite(gb).all() and torch.equal(gb[big], torch.zeros_like(gb[big]))


# ------------------------------------------------------------------------------------------------ (b), (c) dual clip
def _leaf(n=32, k=6, seed=0):
    g = torch.Generator().manual_seed(seed)
    logits = torch.randn(n, k, generator=g).requires_grad_(True)
    act = torch.randint(0, k, (n,), generator=g)
    newlogp = F.log_softmax(logits, -1).gather(1, act.unsqueeze(1)).squeeze(1)
    return logits, newlogp


def test_dual_clip_floors_negative_advantage():
    """A < 0 and r = 5 > c = 3: min(r A, clip(r) A) = 5 A, floored to exactly 3 A; zero gradient wrt the logits.
    A < 0 with r = 1.5 < c (inside the floor): the standard clipped surrogate, same gradient as dual_clip = 0."""
    logits, newlogp = _leaf()
    n = len(newlogp)
    hitrows = torch.arange(n) % 2 == 0
    r = torch.where(hitrows, torch.tensor(5.0), torch.tensor(1.5))
    oldlogp = (newlogp - r.log()).detach()
    madv = -(0.5 + torch.rand(n, generator=torch.Generator().manual_seed(2)))
    pg, ratio, _, hit = ppo_surrogate(newlogp, oldlogp, madv, clip=CLIP, dual_clip=3.0)
    assert torch.equal(hit, hitrows)
    surr = torch.where(hitrows, 3.0 * madv, torch.min(ratio * madv, ratio.clamp(1 - CLIP, 1 + CLIP) * madv))
    assert torch.equal(pg, -surr.mean())
    (g,) = torch.autograd.grad(pg, logits, retain_graph=True)
    assert torch.equal(g[hitrows], torch.zeros_like(g[hitrows])) and (g[~hitrows].abs().sum(-1) > 0).all()
    # only-hit batch: pg == -mean(3 A) exactly and the whole gradient is 0
    pg_h, _, _, _ = ppo_surrogate(newlogp[hitrows], oldlogp[hitrows], madv[hitrows], clip=CLIP, dual_clip=3.0)
    assert torch.equal(pg_h, -(3.0 * madv[hitrows]).mean())
    (gh,) = torch.autograd.grad(pg_h, logits, retain_graph=True)
    assert torch.equal(gh, torch.zeros_like(gh))
    # the unfloored rows: identical to dual_clip = 0 (value and gradient)
    sel = ~hitrows
    a, _, _, _ = ppo_surrogate(newlogp[sel], oldlogp[sel], madv[sel], clip=CLIP, dual_clip=3.0)
    b, _, _, _ = ppo_surrogate(newlogp[sel], oldlogp[sel], madv[sel], clip=CLIP)
    assert torch.equal(a, b)
    (ga,) = torch.autograd.grad(a, logits, retain_graph=True)
    (gb,) = torch.autograd.grad(b, logits)
    assert torch.equal(ga, gb)


def test_dual_clip_leaves_nonnegative_advantage():
    """A >= 0 (incl. A == 0, ratios from 0.01 to exp(20)): dual_clip = 3 gives the same pg, ratio and gradient as off,
    and no sample is counted."""
    logits, newlogp = _leaf(n=40, seed=4)
    g = torch.Generator().manual_seed(6)
    oldlogp = (newlogp - torch.empty(40).uniform_(-4.6, 25.0, generator=g)).detach()
    madv = torch.rand(40, generator=g) * 2
    madv[:5] = 0.0
    a, ra, _, hit = ppo_surrogate(newlogp, oldlogp, madv, clip=CLIP, dual_clip=3.0)
    b, rb, _, _ = ppo_surrogate(newlogp, oldlogp, madv, clip=CLIP)
    assert not hit.any() and torch.equal(a, b) and torch.equal(ra, rb) and torch.isfinite(a)
    (ga,) = torch.autograd.grad(a, logits, retain_graph=True)
    (gb,) = torch.autograd.grad(b, logits)
    assert torch.equal(ga, gb)


# ------------------------------------------------------------------------------------------------ (d) the statistic
def test_dual_clipped_stat_counts_floored_samples(spec, one_thread):
    """ppo_loss with dual_clip = 3 appends the share of floored samples after loss; MinibatchUpdater puts it after
    grad_norm, matching stat_names; dual_clip = 0 appends nothing. Rows set up: k with A < 0 and r = 10 (floored),
    rows with A < 0 and r ~ 1, rows with A > 0 and r = 10 (not floored)."""
    torch.manual_seed(0)
    s, _ = spec
    m = Policy(s)
    x, act, _, _, vret, vold = _batch(spec, n=40, seed=8)
    with torch.no_grad():
        logits, _ = m(x)
        nl = F.log_softmax(logits, -1).gather(1, act.unsqueeze(1)).squeeze(1)
    n = len(act)
    madv = torch.ones(n)
    logr = torch.zeros(n)
    floored = torch.zeros(n, dtype=torch.bool)
    floored[[0, 3, 4, 17, 31]] = True
    madv[floored] = -1.0
    logr[floored] = math.log(10.0)
    madv[[5, 6, 7]] = -1.0                       # A < 0, r ~ 1: not floored
    logr[[8, 9]] = math.log(10.0)                # A > 0, r = 10: not floored (upper clip only)
    oldlogp = nl - logr
    args = (x, act, oldlogp, madv, vret, vold)
    _, st = ppo_loss(m, *args, clip=CLIP, vf_coef=VF, ent_coef=ENT, dual_clip=3.0)
    assert len(st) == 7 and st[6].item() == pytest.approx(5 / n)
    _, st0 = ppo_loss(m, *args, clip=CLIP, vf_coef=VF, ent_coef=ENT)
    assert len(st0) == 6 and torch.equal(st0[[0, 1, 3, 4]], st[[0, 1, 3, 4]])   # entropy, value, kl, clipfrac
    assert st[2] < st0[2], "the floored rows' surrogate rose from 10 A to 3 A: policy loss smaller"

    cfg = resolve_config({"dual_clip": 3.0})
    names = stat_names(cfg)
    assert names == ppo.STAT_NAMES + ("dual_clipped",)
    up = MinibatchUpdater(m, torch.optim.SGD(m.parameters(), lr=0.0), cfg, False, "cpu")
    out = up(*args, compiled=False)
    assert len(out) == len(names) == 8 and out[7].item() == pytest.approx(5 / n)
    assert out[6].item() > 0 and torch.equal(out[:6], st[:6])        # grad_norm after the six PPO statistics
    up0 = MinibatchUpdater(m, torch.optim.SGD(m.parameters(), lr=0.0), resolve_config({}), False, "cpu")
    assert len(up0(*args, compiled=False)) == len(stat_names(resolve_config({}))) == 7
    assert stat_names(resolve_config({})) == ppo.STAT_NAMES
    aux_names = stat_names(resolve_config({"dual_clip": 3.0, "aux_heads": True}))
    assert aux_names[:8] == names and aux_names[8:] == ppo.AUX_STAT_NAMES


def test_config_and_cli(monkeypatch, tmp_path):
    assert resolve_config({}).dual_clip == 0.0 and resolve_config({"dual_clip": 3}).dual_clip == 3.0
    for bad in (1.0, 0.5, -3.0, math.inf, math.nan):
        with pytest.raises(ValueError, match="dual_clip"):
            resolve_config({"dual_clip": bad})
    assert resolve_config({}, base={"clip": 0.2}).dual_clip == 0.0, "a checkpoint without the field resumes off"
    assert resolve_config({}, base={"dual_clip": 3.0}).dual_clip == 3.0
    from sts2train import cli

    seen = {}

    def fake_plan(given, resume, log=print):
        seen.update(given)
        raise ValueError("stop")

    monkeypatch.setattr(ppo, "plan_run", fake_plan)
    assert cli.main(["train", "--out", str(tmp_path / "r"), "--dual-clip", "3"]) == 2 and seen["dual_clip"] == 3.0
    seen.clear()
    cli.main(["train", "--out", str(tmp_path / "r")])
    assert "dual_clip" not in seen
