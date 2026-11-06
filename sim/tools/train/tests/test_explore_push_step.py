"""Row-scaled exploration push step (`--explore-push-step ETA`, wave 14; `ppo.MinibatchUpdater.push_step`).

Evidence (wave-13 diagnostic, docs/diag-2026-11-06/explore-sensitivity.md, A's u7664): the explored SMITH / removal rows
have ||grad log pi|| ~ 238 / ~754 (combat rows ~1), nearly parallel across decisions; inside the shared minibatch loss
the push dominates the gradient, the global clip shrinks everything and Adam's per-parameter normalisation turns it back
into a ~12-nat jump per minibatch, so the wave-12 trust region clamps ~96% of the explored samples and the rows random-
walk. The fix takes the push out of the loss / clip / Adam and applies it per explore item as
theta -= ETA / max(||h||^2, 1) x grad L_push with h = grad mean log pi of the item's unclamped explored samples.
Gates here:
(a) ETA = 0 (default): the updater is the wave-12 step (inline copy, push on and off), torch.equal stats / weights /
    Adam state; the external HEAD-diff hash check is the overlay script of the wave-14 bundle;
(b) ETA 0.5 on a toy model whose explored rows' log-probs are linear in the step: one step moves the explored subset's
    mean log pi by ETA (to the second-order term), push_step_dlogp reports it, a non-explored control row moves much
    less, the value parameter does not move;
(c) clamped explored samples (|dl| >= DELTA after the Adam step) produce no step (bit-identical parameters);
(d) the Adam state and the Adam update of the minibatch are those of the coefficient-0 push loss: only the subsequent
    plain step differs, and it is exactly `push_step` applied to the coefficient-0 result;
(e) two explore items -> two steps with their own scales, each moving its own subset by ~ETA;
(f) config validation / resume / CLI; the explorer's `last_explored_item`; a short CPU train() run (overlap off and
    double) logs the push-step statistics.
CPU only."""

from __future__ import annotations

import copy
import hashlib
import json
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
from sts2train.ppo import MinibatchUpdater, ppo_loss, resolve_config, stat_names, train
from sts2train.runlog import read_jsonl

DATA = Path(__file__).parent / "data"
TINY_TRUNK = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}
MAX_ROWS = 64
POLICY_EXPLORE = {"explore_kinds": "ChooseRestOption", "explore_eps": 0.3, "explore_logp": "policy"}
STEP = {**POLICY_EXPLORE, "explore_push": 1.0, "explore_push_step": 0.5}


@pytest.fixture(scope="module")
def spec():
    s, dtypes = load_spec()
    bind_spec(s)
    return s, dtypes


@pytest.fixture(autouse=True)
def one_thread():
    n = torch.get_num_threads()
    torch.set_num_threads(1)
    yield
    torch.set_num_threads(n)


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


def _adam(opt):
    return {k: {kk: (vv.clone() if torch.is_tensor(vv) else vv) for kk, vv in v.items()}
            for k, v in opt.state_dict()["state"].items()}


def _adam_equal(a, b):
    return sorted(a) == sorted(b) and all(
        sorted(a[k]) == sorted(b[k]) and all(torch.equal(a[k][kk], b[k][kk]) if torch.is_tensor(a[k][kk])
                                             else a[k][kk] == b[k][kk] for kk in a[k]) for k in a)


def _logp_of(model, xb, act):
    with torch.no_grad():
        return F.log_softmax(model(xb)[0].float(), -1).gather(1, act.unsqueeze(1)).squeeze(1)


# ------------------------------------------------------------------------------------------------ (a) off = wave 12
@pytest.mark.parametrize("push", [0.0, 1.0], ids=["push0", "push1"])
@pytest.mark.parametrize("aux", [False, True], ids=["noaux", "aux"])
@pytest.mark.parametrize("arch", [{}, TINY_TRUNK], ids=["smoke", "trunk"])
def test_off_is_the_wave12_step(spec, arch, aux, push):
    """explore_push_step = 0 (default): 4 MinibatchUpdater steps (explored mask passed when push > 0, an
    `explored_item` keyword ignored) equal the wave-12 step written out (ppo_loss with push / coef 1, zero_grad,
    backward, clip_grad_norm_, Adam step, stats layout): statistics, weights and Adam state torch.equal."""
    given = {"aux_heads": aux, "minibatch": 64, **({**POLICY_EXPLORE, "explore_push": push} if push else {})}
    res = {}
    for name in ("new", "ref"):
        torch.manual_seed(0)
        m = Policy(spec[0], **arch, **({"aux_heads": True} if aux else {}))
        cfg = resolve_config(given)
        assert cfg.explore_push_step == 0.0
        opt = torch.optim.Adam(m.parameters(), lr=3e-4)
        up = MinibatchUpdater(m, opt, cfg, False, "cpu")
        assert up.push_step_eta == 0.0
        sts = []
        for k in range(4):
            b = _batch(spec, n=64, seed=10 + k)
            explored = torch.arange(64) % 5 == k
            ex = (explored,) if push else ()
            tg = _aux_targets(64, k) if aux else ()
            if name == "new":
                kw = {"explored_item": np.where(explored.numpy(), 0, -1)} if push else {}
                sts.append(up(*b, *ex, *tg, compiled=False, **kw))
            else:
                opt.zero_grad(set_to_none=True)
                loss, st = ppo_loss(m, *b, clip=cfg.clip, vf_coef=cfg.vf_coef, ent_coef=cfg.ent_coef,
                                    aux=tg if aux else None, aux_coef=cfg.aux_coef,
                                    **({"explored": explored, "push": push, "push_coef": 1.0} if push else {}))
                loss.backward()
                gn = torch.nn.utils.clip_grad_norm_(m.parameters(), cfg.max_grad_norm)
                opt.step()
                sts.append(torch.cat([st[:6], gn.detach().float().view(1), st[6:]]) if (aux or push)
                           else torch.cat([st, gn.detach().float().view(1)]))
        res[name] = (torch.stack(sts), _hash(m), _adam(opt))
    (sn, hn, on), (sr, hr, orf) = res["new"], res["ref"]
    assert torch.equal(sn, sr) and hn == hr and _adam_equal(on, orf)
    assert sn.shape[1] == len(stat_names(resolve_config(given)))


# ------------------------------------------------------------------------------------------------ toy model
class Toy(torch.nn.Module):
    """logits = <f, w> + bias per row (x["f"] [B, K, D], x["bias"] [B, K]); value = a constant parameter. The explored
    rows sit 20 nats below the top row (bias -20, top row f = 0), so log pi(a) = <f_a, w> - 20 - log(1 + ~e^-20): linear
    in w up to ~e^-20, and grad log pi(a) = f_a (~exactly)."""

    def __init__(self, d):
        super().__init__()
        self.w = torch.nn.Parameter(torch.zeros(d))
        self.v = torch.nn.Parameter(torch.zeros(1))

    def forward(self, x, aux=False):
        logits = (x["f"] * self.w).sum(-1) + x["bias"]
        return logits, self.v.expand(x["f"].shape[0])


def _toy_batch(groups, n_ctrl=4, d=12, k=3, seed=0):
    """Decisions of K rows: row 0 = the policy's top row (f = 0, bias 0), row 1 = the explore row (bias -20). `groups`
    = [(item, n, dims, scale)]: n explored decisions of that item whose row-1 features are scale x (u + 0.2 noise) on
    `dims` (u a common direction: nearly parallel gradients, as wave 13 measured), action 1. Then `n_ctrl` control
    decisions (not explored, action 0 = their top row, p ~ 1) and one control decision whose action is a low row with
    features on the last dims plus a small overlap. -> (xb, act, items)."""
    g = torch.Generator().manual_seed(seed)
    fs, bs, acts, items = [], [], [], []
    for item, n, dims, scale in groups:
        u = torch.randn(len(dims), generator=g)
        u = u / u.norm()
        for _ in range(n):
            f = torch.zeros(k, d)
            f[1, dims] = scale * (u + 0.2 * torch.randn(len(dims), generator=g))
            b = torch.tensor([0.0, -20.0, -25.0])
            fs.append(f), bs.append(b), acts.append(1), items.append(item)
    for _ in range(n_ctrl):
        f = torch.zeros(k, d)
        f[2] = torch.randn(d, generator=g)
        fs.append(f), bs.append(torch.tensor([0.0, -20.0, -25.0])), acts.append(0), items.append(-1)
    f = torch.zeros(k, d)
    f[1, -3:] = 5.0 * torch.randn(3, generator=g)
    f[1, :2] = 0.05
    fs.append(f), bs.append(torch.tensor([0.0, -15.0, -25.0])), acts.append(1), items.append(-1)
    return {"f": torch.stack(fs), "bias": torch.stack(bs)}, torch.tensor(acts), np.array(items)


def _toy_updater(model, given=None, opt=None):
    cfg = resolve_config({**STEP, **(given or {})})
    return MinibatchUpdater(model, opt or torch.optim.SGD(model.parameters(), lr=0.0), cfg, False, "cpu"), cfg


def _toy_args(model, xb, act, items, adv=1.0, shift=None):
    n = len(act)
    old = _logp_of(model, xb, act)
    if shift is not None:
        old = old + shift
    madv = torch.where(torch.from_numpy(items >= 0), torch.as_tensor(adv, dtype=torch.float32).expand(n),
                       torch.zeros(n))
    return xb, act, old, madv, torch.zeros(n), torch.zeros(n), torch.from_numpy(items >= 0)


# ------------------------------------------------------------------------------------------------ (b) the step size
def test_one_step_moves_the_subset_by_eta():
    """ETA 0.5, 16 explored rows (A = +1, |dl| = 0 < DELTA) of one item with ||grad log pi|| ~ 30 (above the 1.0
    floor): through the updater (SGD lr 0 for the main step, so only the push step moves the model) the subset's mean
    log pi rises by 0.5 to the second-order term (here ~e^-19), push_step_dlogp says the same, push_step_n = 16,
    push_step_hnorm = ||mean f||; the control rows move much less; the value parameter does not move."""
    torch.manual_seed(0)
    model = Toy(12)
    xb, act, items = _toy_batch([(0, 16, list(range(0, 6)), 30.0)])
    up, cfg = _toy_updater(model)
    args = _toy_args(model, xb, act, items)
    lp0 = _logp_of(model, xb, act)
    out = up(*args, compiled=False, explored_item=items)
    lp1 = _logp_of(model, xb, act)
    d = dict(zip(stat_names(cfg), out.tolist()))
    ex = torch.from_numpy(items >= 0)
    moved = float((lp1 - lp0)[ex].mean())
    assert moved == pytest.approx(0.5, abs=1e-4)
    assert d["push_step_dlogp"] == pytest.approx(moved, abs=1e-5) and d["push_step_n"] == 16
    hbar = xb["f"][ex, 1].mean(0)
    assert d["push_step_hnorm"] == pytest.approx(float(hbar.norm()), rel=1e-4)
    assert float((lp1 - lp0)[~ex].abs().max()) < 0.02, "control rows (not explored) move much less than ETA"
    assert float(model.v.detach().abs().max()) == 0.0, "the value parameter gets no push"
    assert d["push_share"] == pytest.approx(16 / len(act)) and d["push_clamped"] == 0.0

    # A = -1: the same step downwards; A = +-1 half / half: ~0 (the rows are pushed by ETA x mean A)
    for adv, want in ((-1.0, -0.5), (torch.tensor([1.0, -1.0] * 8 + [0.0] * 5), 0.0)):
        model = Toy(12)
        up, _ = _toy_updater(model)
        lp0 = _logp_of(model, xb, act)
        up(*_toy_args(model, xb, act, items, adv=adv), compiled=False, explored_item=items)
        assert float((_logp_of(model, xb, act) - lp0)[ex].mean()) == pytest.approx(want, abs=0.1)


def test_low_sensitivity_rows_get_the_floored_step():
    """Explored rows with ||grad log pi||^2 < 1 (here 0.25): lr = ETA / max(||h||^2, 1) = ETA, so the subset moves by
    ETA x ||h||^2 = 0.125 < ETA (no unbounded step for rows with ~zero gradient); rows with p ~ 1 and grad ~ 0 do not
    move the model at all."""
    model = Toy(12)
    xb, act, items = _toy_batch([(0, 8, [0], 0.5)], seed=1)
    xb["f"][:8, 1, 0] = 0.5                     # exactly parallel: ||h|| = 0.5
    up, cfg = _toy_updater(model)
    lp0 = _logp_of(model, xb, act)
    out = up(*_toy_args(model, xb, act, items), compiled=False, explored_item=items)
    d = dict(zip(stat_names(cfg), out.tolist()))
    assert d["push_step_dlogp"] == pytest.approx(0.125, abs=1e-4)
    assert float((_logp_of(model, xb, act) - lp0)[:8].mean()) == pytest.approx(0.125, abs=1e-4)
    model = Toy(12)
    up, _ = _toy_updater(model)
    items0 = np.full(len(act), -1)
    items0[8:12] = 0                           # the explored "rows" are p ~ 1 top rows (action 0): grad ~ 0
    w0 = model.w.detach().clone()
    up(*_toy_args(model, xb, act, items0), compiled=False, explored_item=items0)
    assert float((model.w.detach() - w0).abs().max()) < 1e-6


# ------------------------------------------------------------------------------------------------ (c) clamped
def test_clamped_samples_produce_no_step():
    """Every explored sample at |dl| >= DELTA (stored log pi_old 1, 2.5 nats away, or -inf): no parameter moves (bitwise),
    push_step_n = 0; with half of them inside DELTA only those are pushed (n = 8) and the others' log pi_old does not
    enter the step."""
    model = Toy(12)
    xb, act, items = _toy_batch([(0, 16, list(range(6)), 30.0)])
    n = len(act)
    shift = torch.zeros(n)
    shift[:16] = torch.tensor([1.0, -1.0, 2.5, -2.5] * 4)
    up, cfg = _toy_updater(model)
    w0 = model.w.detach().clone()
    out = up(*_toy_args(model, xb, act, items, shift=shift), compiled=False, explored_item=items)
    d = dict(zip(stat_names(cfg), out.tolist()))
    assert torch.equal(model.w.detach(), w0) and d["push_step_n"] == 0 and d["push_step_dlogp"] == 0.0
    args = list(_toy_args(model, xb, act, items))
    args[2] = args[2].clone()
    args[2][0] = -math.inf
    out = up(*args, compiled=False, explored_item=np.where(np.arange(n) == 0, 0, -1))
    names = stat_names(cfg)
    assert torch.equal(model.w.detach(), w0)
    assert all(math.isfinite(out[names.index(k)].item()) for k in ppo.PUSH_STEP_STAT_NAMES + ("loss",))

    shift[:16] = torch.tensor([0.3, 5.0] * 8)
    m_a, m_b = Toy(12), Toy(12)
    up_a, _ = _toy_updater(m_a)
    out = up_a(*_toy_args(m_a, xb, act, items, shift=shift), compiled=False, explored_item=items)
    assert dict(zip(stat_names(cfg), out.tolist()))["push_step_n"] == 8
    keep = np.where(np.arange(n) % 2 == 0, items, -1)          # the same 8 samples alone
    up_b, _ = _toy_updater(m_b)
    up_b(*_toy_args(m_b, xb, act, keep, shift=shift), compiled=False, explored_item=keep)
    assert torch.allclose(m_a.w.detach(), m_b.w.detach(), rtol=1e-6, atol=1e-7)


# ------------------------------------------------------------------------------------------------ (d) Adam untouched
@pytest.mark.parametrize("variant", [{}, {"dual_clip": 3.0, "aux_heads": True}], ids=["plain", "dual_aux"])
def test_adam_update_is_the_coef0_update(spec, variant):
    """One minibatch on the smoke Policy with explored samples (A = +1, log pi_old = their current log pi): the
    updater with --explore-push-step 0.5 and the wave-12 updater with push coefficient 0 have the same Adam state
    (exp_avg, exp_avg_sq, step) afterwards and the same main statistics; the step-on parameters are exactly the
    coefficient-0 parameters followed by `push_step` (bitwise), and differ from them (the step did something). A second
    minibatch: the Adam moments still never see the push gradient (equal to an Adam run fed the step-on parameters)."""
    aux = bool(variant.get("aux_heads"))
    b = list(_batch(spec, n=64, seed=21))
    explored = torch.arange(64) % 4 == 1
    items = np.where(explored.numpy(), (np.arange(64) // 4) % 2, -1)      # two items
    tg = _aux_targets(64, 0) if aux else ()
    runs = {}
    for name, given in (("on", {**STEP, **variant}),
                        ("ref", {**POLICY_EXPLORE, "explore_push": 1.0, "explore_push_coef": 0.0, **variant})):
        torch.manual_seed(0)
        m = Policy(spec[0], **({"aux_heads": True} if aux else {}))
        bb = list(b)
        bb[2] = torch.where(explored, _logp_of(m, b[0], b[1]), b[2])
        bb[3] = torch.where(explored, torch.ones(64), b[3])
        cfg = resolve_config(given)
        opt = torch.optim.Adam(m.parameters(), lr=3e-4)
        up = MinibatchUpdater(m, opt, cfg, False, "cpu")
        out = up(*bb, explored, *tg, compiled=False, explored_item=items)
        runs[name] = (m, opt, up, out, cfg, bb)
    (m_on, o_on, up_on, out_on, cfg_on, bb), (m_ref, o_ref, up_ref, out_ref, cfg_ref, _) = runs["on"], runs["ref"]
    assert _adam_equal(_adam(o_on), _adam(o_ref))
    names_on, names_ref = stat_names(cfg_on), stat_names(cfg_ref)
    assert tuple(n for n in names_on if n not in ppo.PUSH_STEP_STAT_NAMES) == names_ref
    for k in names_ref:
        assert out_on[names_on.index(k)].item() == out_ref[names_ref.index(k)].item(), k
    h_before = _hash(m_ref)
    up_ref.push_step_eta = 0.5                  # the same step, applied by hand to the coefficient-0 result
    pst = up_ref.push_step(bb[0], bb[1], bb[2], bb[3], items)
    assert _hash(m_ref) == _hash(m_on) != h_before
    d = dict(zip(names_on, out_on.tolist()))
    assert d["push_step_n"] > 0 and pst[0].item() == d["push_step_n"]
    assert len(out_on) == len(names_on) and names_on.index("push_step_n") == names_on.index("push_loss") + 1

    # minibatch 2: Adam on the step-on model == a fresh copy of the Adam state run on the same parameters
    b2 = list(_batch(spec, n=64, seed=22))
    m_cp = Policy(spec[0], **({"aux_heads": True} if aux else {}))
    m_cp.load_state_dict(m_on.state_dict())
    o_cp = torch.optim.Adam(m_cp.parameters(), lr=3e-4)
    o_cp.load_state_dict(copy.deepcopy(o_on.state_dict()))      # CPU Adam.load_state_dict shares tensors
    up_cp = MinibatchUpdater(m_cp, o_cp, resolve_config({**POLICY_EXPLORE, "explore_push": 1.0,
                                                         "explore_push_coef": 0.0, **variant}), False, "cpu")
    none = np.full(64, -1)
    up_on(*b2, explored, *tg, compiled=False, explored_item=none)       # no explored item: no push step
    up_cp(*b2, explored, *tg, compiled=False)
    assert _hash(m_on) == _hash(m_cp) and _adam_equal(_adam(o_on), _adam(o_cp))


# ------------------------------------------------------------------------------------------------ (e) per item
def test_two_items_two_steps_with_their_own_scale():
    """Item 0 (||grad|| ~ 10, dims 0-3) and item 1 (||grad|| ~ 60, dims 4-7) in one minibatch: two steps (two
    grad + after forwards each), each subset's mean log pi rises by ETA; the hnorm statistic is the n-weighted mean of
    the two scales. Lumping them into one item instead moves the low-scale item by far less than ETA."""
    model = Toy(12)
    xb, act, items = _toy_batch([(0, 10, [0, 1, 2, 3], 10.0), (1, 6, [4, 5, 6, 7], 60.0)], seed=3)
    up, cfg = _toy_updater(model)
    calls = []
    orig = up._logp
    up._logp = lambda *a, **k: (calls.append(1), orig(*a, **k))[1]
    lp0 = _logp_of(model, xb, act)
    out = up(*_toy_args(model, xb, act, items), compiled=False, explored_item=items)
    lp1 = _logp_of(model, xb, act)
    d = dict(zip(stat_names(cfg), out.tolist()))
    assert len(calls) == 4, "one grad forward + one after-forward per item"
    for it in (0, 1):
        sel = torch.from_numpy(items == it)
        assert float((lp1 - lp0)[sel].mean()) == pytest.approx(0.5, abs=1e-3), it
    assert d["push_step_n"] == 16 and d["push_step_dlogp"] == pytest.approx(0.5, abs=1e-3)
    h0 = float(xb["f"][torch.from_numpy(items == 0), 1].mean(0).norm())
    h1 = float(xb["f"][torch.from_numpy(items == 1), 1].mean(0).norm())
    assert d["push_step_hnorm"] == pytest.approx((10 * h0 + 6 * h1) / 16, rel=1e-4)

    lumped = Toy(12)
    up_l, _ = _toy_updater(lumped)
    one = np.where(items >= 0, 0, -1)
    up_l(*_toy_args(lumped, xb, act, one), compiled=False, explored_item=one)
    small = float((_logp_of(lumped, xb, act) - lp0)[torch.from_numpy(items == 0)].mean())
    assert small < 0.1, "one shared scale: the large-gradient item takes the step, the small one barely moves"


def test_push_groups_split_rare_and_common_draws():
    """One explore item with 6 rare draws (log pi ~ -20, ||grad|| ~ 30) and 10 draws of the policy's own p ~ 1 row
    (grad ~ 0; HEAL under --explore-kinds ChooseRestOption): as one group the p ~ 1 samples dilute ||h||^2 and the rare
    rows overshoot to ETA x 16 / 6; with `push_groups` (2 x item + rare) the rare rows move by ETA and the common
    group's step is ~0. `push_groups` itself: -1 stays -1, item i -> 2i (common) / 2i + 1 (rare)."""
    assert np.array_equal(ppo.push_groups(np.array([-1, 0, 0, 1, 1, 2]), np.array([-50.0, -0.1, -9.0, -2.9, -3.1, 0.0])),
                          np.array([-1, 0, 1, 2, 3, 4]))
    xb, act, items = _toy_batch([(0, 6, list(range(6)), 30.0)], n_ctrl=10, seed=4)
    items = items.copy()
    items[6:16] = 0
    rare = torch.from_numpy(np.arange(len(act)) < 6)
    res = {}
    for name in ("lumped", "grouped"):
        model = Toy(12)
        up, _ = _toy_updater(model)
        args = _toy_args(model, xb, act, items)
        groups = items if name == "lumped" else ppo.push_groups(items, args[2].numpy())
        lp0 = _logp_of(model, xb, act)
        up(*args, compiled=False, explored_item=groups)
        res[name] = float((_logp_of(model, xb, act) - lp0)[rare].mean())
    assert res["lumped"] == pytest.approx(0.5 * 16 / 6, rel=1e-2)
    assert res["grouped"] == pytest.approx(0.5, abs=1e-3)


# ------------------------------------------------------------------------------------------------ (f) config / plumbing
def test_config_validation_and_resume(monkeypatch, tmp_path):
    c = resolve_config({})
    assert c.explore_push_step == 0.0 and not set(ppo.PUSH_STEP_STAT_NAMES) & set(stat_names(c))
    on = resolve_config(STEP)
    assert on.explore_push_step == 0.5 and on.explore_push == 1.0
    assert stat_names(on) == ppo.STAT_NAMES + ppo.PUSH_STAT_NAMES + ppo.PUSH_STEP_STAT_NAMES
    assert stat_names(resolve_config({**STEP, "dual_clip": 3.0, "aux_heads": True})) == (
        ppo.STAT_NAMES + ppo.DUAL_STAT_NAMES + ppo.PUSH_STAT_NAMES + ppo.PUSH_STEP_STAT_NAMES + ppo.AUX_STAT_NAMES)
    for bad, msg in (({**STEP, "explore_push_step": -0.5}, "explore_push_step"),
                     ({**STEP, "explore_push_step": math.inf}, "explore_push_step"),
                     ({**STEP, "explore_push_step": math.nan}, "explore_push_step"),
                     ({**POLICY_EXPLORE, "explore_push_step": 0.5}, "needs --explore-push"),
                     ({"explore_push_step": 0.5}, "needs --explore-push"),
                     ({**STEP, "explore_push_coef": 1.0}, "explore-push-coef"),
                     ({**STEP, "explore_logp": "decoupled"}, "explore-logp policy")):
        with pytest.raises(ValueError, match=msg):
            resolve_config(bad)
    assert resolve_config({**STEP, "explore_push_step": 0.0}).explore_push_step == 0.0
    base = ppo.config_dict(on)
    assert base["explore_push_step"] == 0.5
    r = resolve_config({}, base)
    assert r.explore_push_step == 0.5 and r.explore_push == 1.0, "a resume inherits the step"
    assert resolve_config({"explore_push_step": 0.25}, base).explore_push_step == 0.25
    assert resolve_config({"explore_push_step": 0.0}, base).explore_push_step == 0.0, "a resume can turn it off"
    off = resolve_config({"explore_kinds": ""}, base)
    assert off.explore_push == 0.0 and off.explore_push_step == 0.0, "explorer off on a resume drops push and step"
    old = {k: v for k, v in base.items() if k != "explore_push_step"}
    assert resolve_config({}, old).explore_push_step == 0.0, "a checkpoint without the field resumes with 0"
    w12 = ppo.config_dict(resolve_config({**POLICY_EXPLORE, "explore_push": 1.0}))
    assert resolve_config({}, {k: v for k, v in w12.items() if k != "explore_push_step"}).explore_push_coef == 1.0
    from sts2train import cli

    seen = {}

    def fake_plan(given, resume, log=print):
        seen.update(given)
        raise ValueError("stop")

    monkeypatch.setattr(ppo, "plan_run", fake_plan)
    assert cli.main(["train", "--out", str(tmp_path / "r"), "--explore-push", "1", "--explore-push-step", "0.5"]) == 2
    assert seen["explore_push_step"] == 0.5 and "explore_push_coef" not in seen
    seen.clear()
    cli.main(["train", "--out", str(tmp_path / "r")])
    assert "explore_push_step" not in seen


def _obs(spec, n, seed=0):
    return {k: np.ascontiguousarray(v).copy() for k, v in random_obs(spec, n, np.random.default_rng(seed)).items()}


def _set_rows(spec, obs, s, kinds):
    names, C = spec.enum_names["RunActionKind"], spec.cols["cand"]
    obs["cand_n"][s] = len(kinds)
    for j, k in enumerate(kinds):
        obs["cand"][s, j, C["kind"]] = names.index(k)
        obs["cand"][s, j, C["B"]] = -1
        obs["cand"][s, j, C["option_index"]] = -1


def test_explorer_item_of_each_draw():
    """`last_explored_item` = the item (order of --explore-kinds) of the drawn row on the mixture draws, -1 elsewhere;
    the same lane slicing as `last_explored`."""
    spec = load_spec(str(DATA / "spec-fc2eb34c07bc8513.json"))[0]
    n, w, eps = 60, 8, 0.6
    obs = _obs(spec, n)
    for s in range(n):
        _set_rows(spec, obs, s, (["ChooseRestOption"] * 3 + ["LeaveRestSite"] if s % 3 == 0 else
                                 ["PlayCard"] * 2 + ["EndTurn"] if s % 3 == 1 else ["ChooseRestOption", "EndTurn"]))
    lg = torch.zeros(n, w)
    for s in range(n):
        lg[s, int(obs["cand_n"][s]):] = -1e9
    ex = KindExplorer(spec, "ChooseRestOption,EndTurn", eps, 5, logp_mode="policy")
    kind = spec.enum_names["RunActionKind"]
    seen = set()
    for t in range(10):
        dist = torch.distributions.Categorical(logits=lg, validate_args=False)
        a = dist.sample()
        a2, _, _ = ex(obs, lg, dist, a, dist.log_prob(a), update=2, t=t)
        it = ex.last_explored_item
        assert it.dtype == np.int8 and it.shape == (n,)
        assert np.array_equal(it >= 0, ex.last_explored)
        for s in np.flatnonzero(it >= 0):
            k = kind[int(obs["cand"][s, int(a2[s]), spec.cols["cand"]["kind"]])]
            assert k == ("ChooseRestOption" if it[s] == 0 else "EndTurn")
            seen.add(int(it[s]))
        ex(obs_slice(obs, 12, 40), lg[12:40], torch.distributions.Categorical(logits=lg[12:40], validate_args=False),
           a[12:40], dist.log_prob(a)[12:40], update=2, t=t, lo=12, n_total=n)
        assert np.array_equal(ex.last_explored_item, it[12:40])
    assert seen == {0, 1}


def obs_slice(obs, lo, hi):
    return {k: v[lo:hi] for k, v in obs.items()}


TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 12, "threads": 2, "rollout": 16, "minibatch": 64, "epochs": 1, "max_updates": 3, "ckpt_every": 1,
       "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2, "log_every": 100,
       "characters": ("SILENT",), "ascension": 10, "step_cap": 24, "start_mode": "stream", "seed": 2, **TINY}


@pytest.mark.parametrize("overlap", ["off", "double"])
def test_train_logs_push_step_metrics(tmp_path, overlap):
    """A short CPU run with frequent explore kinds (two items), --explore-push 1 --explore-push-step 0.5: every update
    logs finite push-step statistics (pushed samples <= explored samples), the train.log line carries them, the config
    and the checkpoint record explore_push_step."""
    logs = []
    kw = {"explore_kinds": "PlayCard,EndTurn", "explore_eps": 0.3, "explore_logp": "policy", "explore_push": 1.0,
          "explore_push_step": 0.5, "dual_clip": 3.0, "overlap": overlap}
    train(resolve_config({**RUN, "out": str(tmp_path / "p"), **kw}), log=logs.append)
    rows = read_jsonl(tmp_path / "p" / "metrics.jsonl")
    assert len(rows) == 3
    for r in rows:
        assert r["explore_steps"] > 0 and 0 < r["push_share"] < 1
        assert 0 < r["push_step_n"] <= r["explore_steps"]
        assert all(math.isfinite(r[k]) for k in ppo.PUSH_STEP_STAT_NAMES + ppo.PUSH_STAT_NAMES + ("loss",))
        assert r["push_step_hnorm"] > 0
    assert any("push_step_n=" in m and "push_step_dlogp=" in m for m in logs)
    cfg = json.loads((tmp_path / "p" / "config.json").read_text())["config"]
    assert cfg["explore_push_step"] == 0.5
    ck = torch.load(sorted((tmp_path / "p" / "checkpoints").glob("*.pt"))[-1], map_location="cpu", weights_only=False)
    assert ck["config"]["explore_push_step"] == 0.5
