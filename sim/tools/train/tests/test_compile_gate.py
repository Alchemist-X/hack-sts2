"""The loss half of the compiled-vs-eager numerics gate (`check_compiled`): the loss difference is measured against
the per-sample term magnitudes too, so bf16 noise on a minibatch whose mean terms cancel to ~0 passes (run w4-d,
session 19, update 8664, 2026-11-06: rel 2.31e-02 of a ~6e-3 mean-term scale stopped the run), while a real
difference still fails. Pure-python inputs: no CUDA path, no updater."""

from __future__ import annotations

import numpy as np
import pytest

from sts2train.ppo import loss_gate, loss_scale

VF, ENT = 0.5, 0.01


def _incident_like(seed: int = 0, n: int = 4096) -> dict:
    """Per-sample O(1) terms (normalised advantages, ratio ~1) whose policy mean is ~0: means as late in training."""
    rng = np.random.default_rng(seed)
    adv = rng.standard_normal(n)
    adv = (adv - adv.mean()) / adv.std()
    ratio = 1.0 + 0.01 * rng.standard_normal(n)
    pg = float(-np.minimum(ratio * adv, np.clip(ratio, 0.8, 1.2) * adv).mean())
    return {"loss": -0.00564888, "policy_loss": pg, "value_loss": 0.004, "entropy": 0.35,
            "adv_abs_mean": float(np.abs(adv).mean())}


def test_scale_takes_per_sample_magnitude():
    t = _incident_like()
    s = loss_scale(**t, vf_coef=VF, ent_coef=ENT)
    assert abs(t["policy_loss"]) < 2e-2                        # the policy mean cancels
    assert s["scale_terms"] < 3e-2
    assert s["scale_per_sample"] == pytest.approx(t["adv_abs_mean"] + VF * 0.004 + ENT * 0.35)
    assert s["scale"] == pytest.approx(max(s["scale_loss"], s["scale_terms"], s["scale_per_sample"]))
    assert 0.7 < s["scale"] < 0.9                              # mean |N(0, 1)| ~ 0.8
    assert loss_scale(loss=0.0, policy_loss=0.0, value_loss=0.0, entropy=0.0, adv_abs_mean=0.0, vf_coef=VF,
                      ent_coef=ENT)["scale"] == 1e-6


def test_amp_rounding_noise_on_cancelling_means_ok():
    """(a) the incident: 1.4e-4 absolute on a mean that cancels, bf16 autocast -> ok."""
    t = _incident_like()
    s = loss_scale(**t, vf_coef=VF, ent_coef=ENT)
    g = loss_gate(-0.00564888, -0.00578939, s["scale"], amp=True)
    assert g["tol_loss"] == 1e-2 and g["loss_ok"], (s, g)
    assert g["loss_rel"] < 1e-3


def test_old_scale_reproduces_incident():
    """The logged numbers: rel 2.31e-02 against a ~6e-3 scale (|loss| dominated) fails at tol 1e-2."""
    g = loss_gate(-0.00564888, -0.00578939, 0.00608, amp=True)
    assert g["loss_rel"] == pytest.approx(2.31e-2, rel=2e-2) and not g["loss_ok"]


@pytest.mark.parametrize("amp,tf32", [(True, False), (False, True)])
def test_five_percent_of_term_magnitude_fails(amp, tf32):
    """(b) a difference of 5% of the per-sample term magnitude is a real mismatch."""
    t = _incident_like(seed=1)
    s = loss_scale(**t, vf_coef=VF, ent_coef=ENT)
    g = loss_gate(t["loss"], t["loss"] + 0.05 * s["scale"], s["scale"], amp=amp, tf32=tf32)
    assert g["tol_loss"] == 1e-2 and not g["loss_ok"]
    assert g["loss_rel"] == pytest.approx(0.05)


def test_fp32_tolerance_is_1e4_of_scale():
    """(c) without amp / tf32 the gate is 1e-4 of the scale: just under passes, just over fails."""
    t = _incident_like(seed=2)
    s = loss_scale(**t, vf_coef=VF, ent_coef=ENT)["scale"]
    assert loss_gate(1.0, 1.0 + 0.9e-4 * s, s, amp=False)["loss_ok"]
    g = loss_gate(1.0, 1.0 + 1.1e-4 * s, s, amp=False)
    assert g["tol_loss"] == 1e-4 and not g["loss_ok"]
    # the incident's difference is not fp32 rounding: it still fails in fp32
    assert not loss_gate(-0.00564888, -0.00578939, s, amp=False, tf32=False)["loss_ok"]
