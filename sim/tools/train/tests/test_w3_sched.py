"""Trainer features for long runs (train/w3-sched): the entropy schedule (--ent-coef-final / --ent-anneal-updates /
--ent-anneal-start), periodic fixed-endpoint evals (--eval-at-every), the mid / mid-deep / large presets with the
cpu size guard and the resume architecture check, and checkpoint retention (--pin-every; --keep-ckpts prunes only
ckpt-*.pt)."""

from __future__ import annotations

import argparse
import json

import pytest
import torch

from sts2train.bench import load_spec
from sts2train.features import bind_spec
from sts2train.model import PRESETS, Policy, model_arch, param_count, synthetic_batch
from sts2train.ppo import (LOCAL_PARAM_LIMIT, BigLocalRunError, MinibatchUpdater, check_local_size, config_dict,
                           ent_coef_at, ent_schedule_on, plan_run, ppo_loss, resolve_config, train)
from sts2train.runlog import CheckpointManager, read_jsonl

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 8, "threads": 2, "rollout": 8, "minibatch": 32, "epochs": 1, "eval_every": 0, "ckpt_every": 1,
       "ckpt_every_min": 0, "keep_ckpts": 1, "log_every": 100, "max_hours": 0.5, "torch_threads": 1,
       "eval_threads": 2, "characters": ("SILENT",), "ascension": 10, "start_mode": "stream", **TINY}
TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}
SCHED = {"ent_coef": 0.02, "ent_coef_final": 0.005, "ent_anneal_updates": 4}


def _quiet(m):
    pass


@pytest.fixture(scope="module")
def spec():
    s, _ = load_spec()
    bind_spec(s)
    return s


# ------------------------------------------------------------------------------------------- entropy schedule


def test_ent_schedule_values_and_default_off():
    cfg = resolve_config({})
    assert not ent_schedule_on(cfg) and cfg.ent_coef_final is None and cfg.ent_anneal_start is None
    assert [ent_coef_at(cfg, u) for u in (1, 50, 10_000)] == [cfg.ent_coef] * 3
    cfg = resolve_config({**SCHED, "ent_anneal_start": 10})
    got = [ent_coef_at(cfg, u) for u in (1, 11, 12, 13, 14, 15, 99)]
    assert got == pytest.approx([0.02, 0.02, 0.01625, 0.0125, 0.00875, 0.005, 0.005], abs=1e-12)


@pytest.mark.parametrize("given, msg", [
    ({"ent_coef_final": 0.005}, "needs both"),
    ({"ent_anneal_updates": 4}, "needs both"),
    ({"ent_coef_final": -0.1, "ent_anneal_updates": 4}, "finite number >= 0"),
    ({"ent_coef_final": 0.005, "ent_anneal_updates": -1}, ">= 0"),
    ({"ent_anneal_start": 3}, "--ent-anneal-start needs"),
    ({**SCHED, "ent_anneal_start": -1}, ">= 0"),
    ({"eval_at_every": -2}, ">= 0"),
    ({"pin_every": -1}, ">= 0"),
])
def test_schedule_and_retention_config_errors(given, msg):
    with pytest.raises(ValueError, match=msg):
        resolve_config(given)


def test_resume_schedule_inherit_and_turn_off():
    base = config_dict(resolve_config({**SCHED, "ent_anneal_start": 7}))
    cfg = resolve_config({}, base)              # a resume keeps the schedule and its absolute start
    assert ent_schedule_on(cfg) and cfg.ent_anneal_start == 7
    off = resolve_config({"ent_coef_final": None}, base)    # --ent-coef-final none
    assert not ent_schedule_on(off) and off.ent_anneal_start is None and off.ent_anneal_updates == 0


def test_scheduled_updater_loss_equals_constant(spec):
    """The scheduled updater (0-d tensor coefficient, set in place) computes the same loss bit for bit as the
    constant-float path at the same value."""
    torch.manual_seed(0)
    model = Policy(spec, **TINY)
    x = synthetic_batch(spec, 6)
    n = x["cand"].shape[0]
    args = (x, torch.zeros(n, dtype=torch.long), torch.full((n,), -1.0), torch.randn(n), torch.randn(n),
            torch.randn(n))
    want, _ = ppo_loss(model, *args, clip=0.2, vf_coef=0.5, ent_coef=0.0125)
    cfg = resolve_config({**SCHED, **TINY})
    up = MinibatchUpdater(model, torch.optim.Adam(model.parameters()), cfg, False, "cpu")
    assert up.ent_t is not None and up.coefs[1] == 0.02
    up.set_ent_coef(0.0125)
    got, _ = up.loss(*args)
    assert torch.equal(got, want) and up.coefs[1] == 0.0125
    const = MinibatchUpdater(model, torch.optim.Adam(model.parameters()), resolve_config(TINY), False, "cpu")
    assert const.ent_t is None
    with pytest.raises(RuntimeError):
        const.set_ent_coef(0.01)


# ------------------------------------------------------------------------------------------- presets / size guard


def test_presets_param_counts_and_local_guard(spec):
    n = {k: param_count(Policy(spec, **v)) for k, v in PRESETS.items()}
    assert n["smoke"] < n["small"] < n["mid"] < LOCAL_PARAM_LIMIT < n["mid-deep"] < n["large"]
    assert PRESETS["large"] == {"d": 384, "s": 768, "depth": 6, "heads": 8, "ff_mult": 4}
    assert PRESETS["mid-deep"] == {"d": 192, "s": 384, "depth": 6, "heads": 6, "ff_mult": 4}
    cpu = torch.device("cpu")
    for name in ("mid-deep", "large"):
        with pytest.raises(BigLocalRunError, match="--allow-big-local"):
            check_local_size(resolve_config({"preset": name}), n[name], 1e5, cpu)
        check_local_size(resolve_config({"preset": name, "allow_big_local": True}), n[name], 1e5, cpu)
        check_local_size(resolve_config({"preset": name}), n[name], 1e5, torch.device("cuda"))
    check_local_size(resolve_config({"preset": "mid"}), n["mid"], 1e5, cpu)


def test_resume_mid_checkpoint_into_large_is_refused(tmp_path):
    run = tmp_path / "run"
    cfg = resolve_config({"preset": "mid", "deck_counts": True, "aux_heads": True, "out": str(run)})
    CheckpointManager(run).save({"optimizer": {}, "config": config_dict(cfg), "arch": model_arch(cfg)}, 5)
    with pytest.raises(ValueError, match=r"architecture mismatch.*depth 3 -> 6.*--preset large.*cannot change"):
        plan_run({"out": str(run), "preset": "large"}, "auto", log=_quiet)
    with pytest.raises(ValueError, match=r"architecture mismatch.*depth 3 -> 6"):
        plan_run({"out": str(run), "preset": "mid-deep"}, "auto", log=_quiet)
    cfg2, ck, _ = plan_run({"out": str(run)}, "auto", log=_quiet)       # no model flags: resumes as mid
    assert (cfg2.d, cfg2.depth, cfg2.deck_counts) == (192, 3, True) and ck is not None


# ------------------------------------------------------------------------------------------- retention


def test_keep_ckpts_prunes_only_ckpt_files(tmp_path):
    m = CheckpointManager(tmp_path, keep=1)
    for u in (1, 2):
        p = m.save({"u": u}, u)
        m.pin(p, u)
    m.mark_best(p)
    (m.dir / "other.pt").write_bytes(b"x")
    m.save({"u": 3}, 3)
    names = sorted(q.name for q in m.dir.iterdir())
    assert names == ["best.pt", "ckpt-0000003.pt", "other.pt", "pinned-0000001.pt", "pinned-0000002.pt"]
    assert m.latest().name == "ckpt-0000003.pt"


# ------------------------------------------------------------------------------------------- integration


def test_long_run_flags_stream_run_and_resume(tmp_path):
    """4 stream updates with the entropy schedule, --eval-at-every 2 on a 2-game set and --pin-every 2 (keep 1), then a
    resume to 6 updates that continues the schedule (constant at the final value) and the periodic evals."""
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    run = tmp_path / "run"
    given = {**RUN, **SCHED, "out": str(run), "max_updates": 4, "eval_at_every": 2, "eval_at_seedset": str(ss),
             "eval_at_modes": "greedy-on,greedy-off", "pin_every": 2}
    cfg, _, _ = plan_run(given, None, log=_quiet)
    s = train(cfg, log=_quiet, given=given)
    assert s["updates"] == 4
    m = read_jsonl(run / "metrics.jsonl")
    assert [r["ent_coef"] for r in m] == pytest.approx([0.02, 0.01625, 0.0125, 0.00875], abs=1e-9)
    assert [r["t_eval_at_s"] > 0 for r in m] == [False, True, False, True]
    assert all(r["t_eval_s"] >= r["t_eval_at_s"] >= 0 for r in m)
    ev = read_jsonl(run / "evals.jsonl")
    assert [(r["update"], r["mode"]) for r in ev] == [(2, "greedy-on"), (2, "greedy-off"), (4, "greedy-on"),
                                                     (4, "greedy-off")]
    assert all(r["eval_at"] and r["games"] == 2 and r["checkpoint"].endswith(f"pinned-{r['update']:07d}.pt")
               for r in ev)
    ck = sorted(p.name for p in (run / "checkpoints").iterdir())
    assert ck == ["ckpt-0000004.pt", "pinned-0000002.pt", "pinned-0000004.pt"]
    assert json.loads((run / "config.json").read_text())["config"]["ent_anneal_start"] == 0

    cfg2, ck2, p2 = plan_run({"out": str(run), "max_updates": 6}, "auto", log=_quiet)
    assert cfg2.ent_anneal_start == 0 and ent_schedule_on(cfg2) and cfg2.eval_at_every == 2
    train(cfg2, log=_quiet, resume=ck2, resume_path=p2)
    m = read_jsonl(run / "metrics.jsonl")
    assert [r["ent_coef"] for r in m][4:] == pytest.approx([0.005, 0.005], abs=1e-12)
    assert [r["update"] for r in read_jsonl(run / "evals.jsonl")][-2:] == [6, 6]
    assert (run / "checkpoints" / "pinned-0000006.pt").exists()


def test_flags_off_rows_unchanged(tmp_path):
    """Without the new flags a metrics row has no t_eval_at_s and ent_coef is the constant; no pinned files."""
    run = tmp_path / "run"
    given = {**RUN, "out": str(run), "max_updates": 1, "keep_ckpts": 3}
    cfg, _, _ = plan_run(given, None, log=_quiet)
    train(cfg, log=_quiet, given=given)
    (row,) = read_jsonl(run / "metrics.jsonl")
    assert "t_eval_at_s" not in row and row["ent_coef"] == cfg.ent_coef == 0.01
    assert not list((run / "checkpoints").glob("pinned-*.pt"))


def test_cli_flags_parse():
    from sts2train.cli import _train_parser

    ap = argparse.ArgumentParser()
    _train_parser(ap.add_subparsers(dest="cmd"))
    d = vars(ap.parse_args(["train", "--ent-coef-final", "none", "--ent-anneal-updates", "0", "--ent-anneal-start",
                            "3", "--eval-at-every", "5", "--pin-every", "7", "--preset", "mid-deep"]))
    assert d["ent_coef_final"] is None and d["ent_anneal_updates"] == 0 and d["ent_anneal_start"] == 3
    assert d["eval_at_every"] == 5 and d["pin_every"] == 7 and d["preset"] == "mid-deep"
    assert vars(ap.parse_args(["train", "--ent-coef-final", "0.005"]))["ent_coef_final"] == 0.005
