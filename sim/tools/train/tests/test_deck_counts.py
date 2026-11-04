"""--deck-counts: the count-aware deck / pile pooling (embedding-bag sum with log(1 + copies) per card, count
features, copies of the candidate's card in the deck), its parameter budget, and a real run with the flag."""

from __future__ import annotations

import math

import numpy as np
import pytest
import torch

from sts2train.bench import load_spec, random_obs
from sts2train.features import bind_spec, obs_to_device
from sts2train.model import DECK_STATS, PILE_STATS, PRESETS, Policy, masked_mean, param_count
from sts2train.ppo import load_checkpoint, plan_run, resolve_config, train

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}


@pytest.fixture(scope="module")
def spec_dtypes():
    spec, dtypes = load_spec()
    bind_spec(spec)
    return spec, dtypes


def _batch(spec, dtypes, n=6, seed=0):
    return obs_to_device(random_obs(spec, n, np.random.default_rng(seed), dtypes=dtypes), "cpu", max_rows=64)


def _set_deck(x, spec, rows):
    """Overwrite the deck table with `rows` per sample: lists of (card_id, upgrade)."""
    dc = spec.cols["deck"]
    x["deck"].zero_()
    for i, r in enumerate(rows):
        for j, (cid, up) in enumerate(r):
            x["deck"][i, j, dc["card_id"]] = cid
            x["deck"][i, j, dc["upgrade"]] = up


def test_parameter_budget_and_init(spec_dtypes):
    spec, _ = spec_dtypes
    for name, kw in PRESETS.items():
        torch.manual_seed(0)
        a = Policy(spec, **kw)
        torch.manual_seed(0)
        b = Policy(spec, **kw, deck_counts=True)
        assert abs(param_count(b) / param_count(a) - 1) < 0.05, name
        assert param_count(b) - param_count(a) == (DECK_STATS + PILE_STATS + 1) * kw["d"]
        sa, sb = a.state_dict(), b.state_dict()
        assert set(sb) - set(sa) == {"deck_count_proj.weight", "pile_count_proj.weight", "c_copies.weight"}
        assert all(torch.equal(sa[k], sb[k]) for k in sa), name
        assert b.arch == {**a.arch, "deck_counts": True}


def test_deck_bag_is_a_count_aware_embedding_bag(spec_dtypes):
    """3 x A + B: bag = log(4) e_A + log(2) e_B (rows of one card id share their mean), invariant to row order and
    padding; [A, B] and [A, A, B, B] have the same mean embedding but different count-aware bags and statistics."""
    spec, _ = spec_dtypes
    torch.manual_seed(0)
    m = Policy(spec, deck_counts=True)
    A, B = 5, 9
    ids = torch.tensor([[A, A, B, A, 0, 0], [B, 0, A, A, 0, A], [A, B, 0, 0, 0, 0], [A, A, B, B, 0, 0]])
    dm = ids != 0
    drow = m.card_emb(ids)
    cnt = m._deck_counts(ids, dm)
    assert cnt[0, A] == 3 and cnt[0, B] == 1 and cnt[0, 0] == 0 and cnt[0].sum() == 4
    bag = m._deck_bag(drow, cnt.gather(1, ids), dm)
    e = m.card_emb.weight
    want = math.log(4) * e[A] + math.log(2) * e[B]
    assert torch.allclose(bag[0], want, atol=1e-6) and torch.allclose(bag[1], want, atol=1e-6)
    assert torch.allclose(masked_mean(drow, dm)[2], masked_mean(drow, dm)[3], atol=1e-6)
    assert not torch.allclose(bag[2], bag[3], atol=1e-3)
    assert torch.allclose(bag[3], math.log(3) * (e[A] + e[B]), atol=1e-6)


def test_deck_stats_and_pile_bag(spec_dtypes):
    spec, dtypes = spec_dtypes
    torch.manual_seed(0)
    m = Policy(spec, deck_counts=True)
    x = _batch(spec, dtypes, n=2)
    _set_deck(x, spec, [[(5, 0), (5, 1), (9, 0), (11, 2)], []])
    dc = spec.cols["deck"]
    x["deck"][0, 2, dc["enchant_id"]] = 3
    dk = x["deck"]
    dm = dk[..., dc["card_id"]] != 0
    cnt = m._deck_counts(dk[..., dc["card_id"]].long(), dm)
    st = m._deck_stats(dk, dm, cnt)
    want0 = [math.log1p(4), 4 / 32, math.log1p(3), math.log1p(2), 2 / 4, math.log1p(1), 0.0]
    assert torch.allclose(st[0], torch.tensor(want0), atol=1e-6)
    assert torch.equal(st[1], torch.zeros(DECK_STATS))                    # empty deck (outside a run)
    # piles: log(1 + copies) bag + distinct ids / upgraded copies, on top of the default features
    for k in ("draw_counts", "discard_counts", "exhaust_counts"):
        x[k].zero_()
    x["draw_counts"][0, 2 * 5] = 3            # 3 x card 5
    x["draw_counts"][0, 2 * 7 + 1] = 1        # 1 x card 7+
    m.pile_count_proj.weight.data.zero_()
    p_counts = m._piles(x)
    W = m.card_emb.weight
    bag = math.log1p(3) * W[5] + math.log1p(1) * W[7]
    feats = torch.cat([bag, torch.tensor([math.log1p(4), 0.25])] + [torch.zeros(48 + 2)] * 2)
    assert torch.allclose(p_counts[0], m.pile_proj(feats), atol=1e-5)


def test_candidate_sees_copies_of_its_card(spec_dtypes):
    """Only the c_copies term changes when its weight changes: for a candidate whose card has copies in the deck the
    logit moves, for a card without copies (or no card) it does not."""
    spec, dtypes = spec_dtypes
    torch.manual_seed(0)
    m = Policy(spec, deck_counts=True).eval()
    x = _batch(spec, dtypes, n=1, seed=3)
    _set_deck(x, spec, [[(5, 0), (5, 0), (9, 0)]])
    cc = spec.cols["cand"]
    x["cand"][0, :3, cc["card_id"]] = torch.tensor([5, 9, 0])
    x["cand"][0, 3:, cc["card_id"]] = 13
    x["cand_n"][:] = 6
    with torch.no_grad():
        m.c_copies.weight.zero_()
        l0, _ = m(x)
        m.c_copies.weight.normal_()
        l1, _ = m(x)
    d = (l1 - l0)[0, :6].abs()
    assert d[0] > 0 and d[1] > 0 and d[2] == 0 and (d[3:] == 0).all()


def test_forward_backward_trunk(spec_dtypes):
    spec, dtypes = spec_dtypes
    torch.manual_seed(0)
    m = Policy(spec, **TINY, deck_counts=True)
    x = _batch(spec, dtypes, n=8, seed=4)
    lg, v = m(x)
    (lg[lg > -1e8].logsumexp(-1) + v.sum()).backward()
    for k in ("deck_count_proj.weight", "pile_count_proj.weight", "c_copies.weight"):
        g = dict(m.named_parameters())[k].grad
        assert g is not None and torch.isfinite(g).all() and g.abs().sum() > 0, k


def test_train_with_deck_counts(tmp_path):
    run = tmp_path / "run"
    cfg = resolve_config({**TINY, "out": str(run), "envs": 16, "threads": 2, "pool": 8, "rollout": 8,
                          "minibatch": 64, "max_updates": 2, "ckpt_every": 0, "ckpt_every_min": 0, "log_every": 100,
                          "max_hours": 0.5, "torch_threads": 2, "deck_counts": True})
    s = train(cfg, log=lambda m_: None)
    assert s["updates"] == 2 and s["arch"]["deck_counts"] is True
    model, _, ck = load_checkpoint(s["last_checkpoint"])
    assert model.deck_counts and ck["arch"]["deck_counts"] is True and "heads" not in ck
    with pytest.raises(ValueError, match="architecture"):
        plan_run({"out": str(run), "deck_counts": False}, "auto", log=lambda m_: None)
