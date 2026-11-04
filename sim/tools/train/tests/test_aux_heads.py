"""--aux-heads: targets and masks of the win-probability / combat-hp heads on synthetic episodes (bootstrap on and
off), masked losses, the model with the heads off is the HEAD model, a real run trains them and records them in the
checkpoint, `sts2train eval` writes their per-step readouts into the game logs, and the architecture flag guards
resume."""

from __future__ import annotations

import json

import numpy as np
import pytest
import torch

from sts2train.heads import AUX_STAT_NAMES, HEAD_OUTPUTS, AuxTargets, aux_losses
from sts2train.model import PRESETS, Policy, arch_of, param_count
from sts2train.ppo import load_checkpoint, plan_run, resolve_config, stat_names, train
from sts2train.runlog import read_jsonl

AUX = ["total_floor", "act_index", "hp", "max_hp", "is_combat", "combats_won"]
LE = ["win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id"]
TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}


def _aux(hp, mx, combat, won):
    a = np.zeros(len(AUX), np.float32)
    a[AUX.index("hp")], a[AUX.index("max_hp")], a[AUX.index("is_combat")], a[AUX.index("combats_won")] = \
        hp, mx, combat, won
    return a


def _le(win=0, final_hp=0, final_max_hp=80):
    r = np.zeros(len(LE), np.int32)
    r[LE.index("win")], r[LE.index("final_hp")], r[LE.index("final_max_hp")] = win, final_hp, final_max_hp
    return r


# Per slot, the observations s_0 .. s_T (hp, max_hp, in combat, combats won) and the step outcomes t -> (term, trunc,
# win, final_hp, final_max_hp) for steps that end an episode (s_{t+1} is then the next episode's first observation).
T = 8
SLOTS = [
    # slot 0: combat from s_1, won at step 3 (s_4: rewards, 30/60 hp), map, combat from s_6 still running at the end
    ([(60, 60, 0, 0), (60, 60, 1, 0), (50, 60, 1, 0), (40, 60, 1, 0), (30, 60, 0, 1), (30, 60, 0, 1),
      (30, 60, 1, 1), (20, 60, 1, 1), (10, 60, 1, 1)], {}),
    # slot 1: dies in a combat at step 2 (Defeat), the next episode (non-combat) runs to the window end
    ([(10, 80, 1, 3), (5, 80, 1, 3), (2, 80, 1, 3), (80, 80, 0, 0), (80, 80, 0, 0), (80, 80, 0, 0), (80, 80, 0, 0),
      (80, 80, 0, 0), (80, 80, 0, 0)], {2: (1, 0, 0, 0, 80)}),
    # slot 2: truncated (step cap) in a combat at step 1; the next episode wins at step 5 out of a combat (final 40/70)
    ([(30, 70, 1, 2), (25, 70, 1, 2), (70, 70, 0, 0), (70, 70, 1, 9), (60, 70, 1, 9), (50, 70, 1, 9),
      (70, 70, 0, 0), (70, 70, 0, 0), (70, 70, 0, 0)], {1: (0, 1, 0, 25, 70), 5: (1, 0, 1, 40, 70)}),
    # slot 3: two combats back to back (combats_won grows while is_combat stays 1), the second won at step 4
    ([(50, 50, 1, 0), (45, 50, 1, 0), (40, 50, 1, 1), (35, 50, 1, 1), (30, 50, 1, 1), (30, 50, 0, 2),
      (30, 50, 0, 2), (30, 50, 0, 2), (30, 50, 0, 2)], {}),
]


def _targets(bootstrap: bool):
    n = len(SLOTS)
    tg = AuxTargets(AUX, LE, T, n, bootstrap=bootstrap)
    for t in range(T):
        tg.before(t, np.stack([_aux(*obs[t]) for obs, _ in SLOTS]))
        term, trunc = np.zeros(n, np.int8), np.zeros(n, np.int8)
        le = np.zeros((n, len(LE)), np.int32)
        for i, (_, ends) in enumerate(SLOTS):
            if t in ends:
                te, tr, win, fh, fm = ends[t]
                term[i], trunc[i], le[i] = te, tr, _le(win, fh, fm)
        tg.after(t, term, trunc, le, np.stack([_aux(*obs[t + 1]) for obs, _ in SLOTS]))
    win_boot, combat_boot = np.full(n, 0.3, np.float32), np.full(n, 0.6, np.float32)
    return tg.targets(win_boot, combat_boot)


def test_targets_and_masks_on_synthetic_episodes():
    wy, wm, cy, cm, st = _targets(bootstrap=True)
    nan = np.nan

    def masked(y, m):                       # [T, N] -> one row per slot
        return np.where(m > 0, y, nan).T

    # win: slot 0 runs past the window (bootstrap 0.3); slot 1 lost at step 2 (0), then bootstrap; slot 2: truncated
    # at step 1 (masked), then won at step 5 (1), then bootstrap; slot 3 bootstrap
    np.testing.assert_allclose(masked(wy, wm), [
        [.3] * 8, [0, 0, 0, .3, .3, .3, .3, .3], [nan, nan, 1, 1, 1, 1, .3, .3], [.3] * 8], atol=1e-6)
    # combat: slot 0 won at step 3 with 30/60, then the running combat from s_6 (bootstrap 0.6); slot 1 died at
    # step 2 (0 hp); slot 2: the combat cut by the truncation is masked, the won one ends at 40/70; slot 3: the first
    # combat ends at step 1 (s_2 = start of the next one, 40/50), the second at step 4 (30/50)
    np.testing.assert_allclose(masked(cy, cm), [
        [nan, .5, .5, .5, nan, nan, .6, .6], [0, 0, 0, nan, nan, nan, nan, nan],
        [nan, nan, nan, 40 / 70, 40 / 70, 40 / 70, nan, nan], [.8, .8, .6, .6, .6, nan, nan, nan]], atol=1e-6)
    assert wy.dtype == cy.dtype == wm.dtype == cm.dtype == np.float32
    assert not np.isnan(wy).any() and not np.isnan(cy).any() and (wy[wm == 0] == 0).all() and (cy[cm == 0] == 0).all()
    assert st["aux_win_targets"] == 30 and st["aux_win_exact"] == 7 and st["aux_combat_targets"] == 16
    assert st["aux_combat_exact"] == 14


def test_targets_without_bootstrap_keep_only_outcomes_inside_the_window():
    wy, wm, cy, cm, st = _targets(bootstrap=False)
    nan = np.nan
    np.testing.assert_allclose(np.where(wm > 0, wy, nan).T, [
        [nan] * 8, [0, 0, 0] + [nan] * 5, [nan, nan, 1, 1, 1, 1, nan, nan], [nan] * 8], atol=1e-6)
    np.testing.assert_allclose(np.where(cm > 0, cy, nan).T, [
        [nan, .5, .5, .5, nan, nan, nan, nan], [0, 0, 0, nan, nan, nan, nan, nan],
        [nan, nan, nan, 40 / 70, 40 / 70, 40 / 70, nan, nan], [.8, .8, .6, .6, .6, nan, nan, nan]], atol=1e-6)
    assert st["aux_win_targets"] == st["aux_win_exact"] == 7 and st["aux_combat_targets"] == st["aux_combat_exact"] == 14
    with pytest.raises(ValueError, match="lack"):
        AuxTargets(["hp"], LE, T, 1)


def test_aux_losses_are_masked_means():
    torch.manual_seed(0)
    h = {"win_logit": torch.randn(6, requires_grad=True), "combat_logit": torch.randn(6, requires_grad=True)}
    wy, cy = torch.rand(6), torch.rand(6)
    wm = torch.tensor([1., 1., 0., 0., 1., 0.])
    cm = torch.tensor([0., 1., 1., 0., 0., 0.])
    wl, cl, wf, cf = aux_losses(h, wy, wm, cy, cm)
    k = wm.bool()
    ref_w = torch.nn.functional.binary_cross_entropy_with_logits(h["win_logit"][k], wy[k])
    ref_c = ((torch.sigmoid(h["combat_logit"][cm.bool()]) - cy[cm.bool()]) ** 2).mean()
    assert torch.allclose(wl, ref_w) and torch.allclose(cl, ref_c) and wf == 0.5 and cf == pytest.approx(1 / 3)
    (wl + cl).backward()
    assert (h["win_logit"].grad[~k] == 0).all() and (h["combat_logit"].grad[~cm.bool()] == 0).all()
    zero = torch.zeros(6)
    assert aux_losses(h, wy, zero, cy, zero)[0].item() == 0.0      # no target in the minibatch: 0, not NaN


def test_heads_off_is_the_head_model_and_on_keeps_its_init(spec_live):
    for name, kw in PRESETS.items():
        torch.manual_seed(0)
        a = Policy(spec_live, **kw)
        torch.manual_seed(0)
        b = Policy(spec_live, **kw, aux_heads=True)
        sa, sb = a.state_dict(), b.state_dict()
        assert set(sb) - set(sa) == {f"{h}.{i}.{w}" for h in ("win_head", "combat_head") for i in (0, 2)
                                     for w in ("weight", "bias")}
        assert all(torch.equal(sa[k], sb[k]) for k in sa), name
        assert a.arch == {k: kw[k] for k in ("d", "s", "depth", "heads", "ff_mult")}
        assert b.arch == {**a.arch, "aux_heads": True} and arch_of(b.arch)["aux_heads"] and not arch_of(a.arch)["aux_heads"]
        assert param_count(b) - param_count(a) == 2 * (kw["s"] * (kw["s"] // 4) + 2 * (kw["s"] // 4) + 1)
    with pytest.raises(ValueError, match="aux_heads=True"):
        a(None, aux=True)
    assert stat_names(resolve_config({"aux_heads": True}))[-4:] == AUX_STAT_NAMES
    assert stat_names(resolve_config({})) == ("entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac", "loss",
                                              "grad_norm")
    with pytest.raises(ValueError, match="aux_coef"):
        resolve_config({"aux_coef": -1.0})


@pytest.fixture(scope="module")
def spec_live():
    from sts2vec import load, schema_of

    from sts2train.features import ModelSpec, bind_spec

    s = ModelSpec.from_schema(schema_of(load()))
    bind_spec(s)
    return s


@pytest.fixture(scope="module")
def aux_run(tmp_path_factory):
    run = tmp_path_factory.mktemp("aux") / "run"
    cfg = resolve_config({**TINY, "out": str(run), "envs": 16, "threads": 2, "pool": 8, "rollout": 16,
                          "minibatch": 64, "max_updates": 3, "ckpt_every": 0, "ckpt_every_min": 0, "log_every": 100,
                          "max_hours": 0.5, "torch_threads": 2, "aux_heads": True, "aux_coef": 0.2})
    logs: list[str] = []
    s = train(cfg, log=logs.append)
    return run, s, logs


def test_train_with_aux_heads_logs_and_checkpoints_them(aux_run):
    run, s, logs = aux_run
    assert s["updates"] == 3 and s["arch"]["aux_heads"] is True
    rows = read_jsonl(run / "metrics.jsonl")
    for r in rows:
        for k in AUX_STAT_NAMES + ("aux_win_targets", "aux_combat_targets", "aux_win_prob_mean"):
            assert k in r, k
        assert np.isfinite(r["aux_win_loss"]) and np.isfinite(r["aux_combat_loss"]) and 0 < r["aux_win_prob_mean"] < 1
        assert r["aux_win_targets"] > 0 and 0 < r["aux_win_frac"] <= 1
    assert sum(r["aux_combat_targets"] for r in rows) > 0
    assert any("aux heads: win_prob" in m for m in logs)
    model, _, ck = load_checkpoint(s["last_checkpoint"])
    assert ck["heads"] == HEAD_OUTPUTS and ck["arch"]["aux_heads"] is True and model.aux_heads
    assert json.loads((run / "config.json").read_text())["arch"]["aux_heads"] is True


def test_resume_keeps_the_heads_flag(aux_run):
    run, s, _ = aux_run
    with pytest.raises(ValueError, match="architecture"):
        plan_run({"out": str(run), "aux_heads": False}, "auto", log=lambda m: None)
    cfg, ck, _ = plan_run({"out": str(run), "max_updates": 4}, "auto", log=lambda m: None)
    assert cfg.aux_heads and cfg.aux_coef == 0.2 and ck["arch"]["aux_heads"]


def test_eval_writes_per_step_head_readouts(aux_run, tmp_path):
    """`sts2train eval` of an aux-heads checkpoint: every game summary carries win_prob (one value in [0, 1] per
    recorded action) and combat_hp_pred (null exactly outside combat); the report is unchanged."""
    from sts2eval.report import build_report

    from sts2train.cli import main

    _, s, _ = aux_run
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    out = tmp_path / "ev"
    rc = main(["eval", "--seedset", str(ss), "--checkpoint", s["last_checkpoint"], "--out", str(out), "--threads", "2"])
    assert rc == 0
    for g in TWO_GAMES["games"]:
        sm = json.loads((out / "games" / f"{g['character']}-{g['seed']}.summary.json").read_text())
        n = len(sm["history_actions"])
        assert n > 0 and len(sm["win_prob"]) == len(sm["combat_hp_pred"]) == n
        assert all(0.0 <= p <= 1.0 for p in sm["win_prob"])
        assert sm["win_prob_first"] == sm["win_prob"][0] and sm["win_prob_last"] == sm["win_prob"][-1]
        cp = [p for p in sm["combat_hp_pred"] if p is not None]
        assert 0 < len(cp) < n and all(0.0 <= p <= 1.0 for p in cp)
    conf = json.loads((out / "config.json").read_text())
    assert conf["heads"] == ["win_prob", "combat_hp_pred"] and conf["arch"]["aux_heads"] is True
    h = build_report(out)["headline"]
    assert h["games"] == 2 and h["replayable"] == 2
