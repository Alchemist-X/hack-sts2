"""Long-run machinery: metrics writer, atomic writes, checkpoint rotation, checkpoint/resume round trip, run directory."""

from __future__ import annotations

import json
import math

import numpy as np
import pytest
import torch

from sts2train.features import ModelSpec
from sts2train.model import Policy
from sts2train.ppo import (TrainConfig, env_kwargs, load_checkpoint, make_checkpoint, plan_run, resolve_config,
                           restore_checkpoint, train, weights_hash)
from sts2train.runlog import CheckpointManager, JsonlWriter, atomic_write_json, read_jsonl


def test_metrics_writer_roundtrip_and_resume_truncation(tmp_path):
    p = tmp_path / "metrics.jsonl"
    w = JsonlWriter(p)
    for u in range(1, 6):
        w.write({"update": u, "loss": np.float32(0.5) * u, "nan": float("nan"), "inf": math.inf, "n": np.int64(u),
                 "arr": np.arange(2), "t": (1, 2)})
    w.close()
    rows = read_jsonl(p)
    assert [r["update"] for r in rows] == [1, 2, 3, 4, 5]
    assert rows[1] == {"update": 2, "loss": 1.0, "nan": None, "inf": None, "n": 2, "arr": [0, 1], "t": [1, 2]}
    with open(p, "a") as f:          # a torn last line (killed mid-write) is skipped
        f.write('{"update": 6, "lo')
    assert len(read_jsonl(p)) == 5
    w = JsonlWriter(p, keep_upto=3)  # resuming from update 3: later rows are dropped, appends continue
    w.write({"update": 4, "loss": 9})
    w.close()
    assert [r["update"] for r in read_jsonl(p)] == [1, 2, 3, 4]
    assert read_jsonl(p)[-1]["loss"] == 9


def test_atomic_write_json(tmp_path):
    p = tmp_path / "status.json"
    atomic_write_json(p, {"a": float("nan"), "b": np.float64(2.5)})
    atomic_write_json(p, {"a": 1})
    assert json.loads(p.read_text()) == {"a": 1}
    assert [x.name for x in tmp_path.iterdir()] == ["status.json"]


def test_checkpoint_manager_keeps_last_k(tmp_path):
    m = CheckpointManager(tmp_path, keep=2)
    assert m.latest() is None
    for u in (5, 10, 15, 20):
        m.save({"updates": u}, u)
    assert [u for u, _ in m.list()] == [15, 20]
    assert m.latest().name == "ckpt-0000020.pt"
    best = m.mark_best(m.path_for(15))
    m.save({"updates": 25}, 25)
    assert [u for u, _ in m.list()] == [20, 25] and best.exists()
    assert torch.load(best, weights_only=False)["updates"] == 15
    assert not list((tmp_path / "checkpoints").glob("*.tmp"))


@pytest.fixture(scope="module")
def spec():
    from sts2vec import VecEnv

    e = VecEnv(2, threads=1, pool=None)
    s = ModelSpec.from_schema(e.schema)
    e.close()
    return s


@pytest.mark.parametrize("arch", [{}, {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}], ids=["smoke", "trunk"])
def test_checkpoint_resume_roundtrip(tmp_path, spec, arch):
    """Weights, Adam moments and RNG states survive save -> load: the next optimizer step and the next random draws
    after a restore are identical to the uninterrupted run."""
    device = torch.device("cpu")
    cfg = resolve_config(dict(arch))
    torch.manual_seed(0)
    a = Policy(spec, **arch)
    opt_a = torch.optim.Adam(a.parameters(), lr=1e-3)
    params = [p for p in a.parameters() if p.dim() > 1][:4]

    def step(model_params, opt):
        loss = sum((p.float() ** 2).mean() for p in model_params)
        opt.zero_grad()
        loss.backward()
        opt.step()

    for _ in range(3):
        step(params, opt_a)
    rng_a = np.random.default_rng(7)
    rng_a.random(5)
    ck = make_checkpoint(a, opt_a, spec, cfg, {"updates": 3, "env_steps": 1234}, rng_a, device)
    path = tmp_path / "ck.pt"
    torch.save(ck, path)
    draws_a = torch.rand(3)                 # the uninterrupted run's next torch draws

    b = Policy(spec, **arch)
    opt_b = torch.optim.Adam(b.parameters(), lr=1e-3)
    rng_b = np.random.default_rng(999)
    restore_checkpoint(torch.load(path, weights_only=False), b, opt_b, rng_b, device)
    assert weights_hash(b) == weights_hash(a) == ck["weights_hash"]
    assert rng_b.random() == rng_a.random()
    assert torch.equal(torch.rand(3), draws_a)
    step(params, opt_a)
    step([p for p in b.parameters() if p.dim() > 1][:4], opt_b)
    assert weights_hash(b) == weights_hash(a), "Adam state was not restored"
    m2, spec2, ck2 = load_checkpoint(path)
    assert spec2.encoder_version == spec.encoder_version and ck2["updates"] == 3 and m2.arch == a.arch


def test_resolve_config_precedence():
    base = {"lr": 1e-4, "envs": 512, "d": 384, "s": 768, "depth": 6, "heads": 8, "bogus_old_key": 1}
    cfg = resolve_config({"envs": 2048}, base)
    assert (cfg.lr, cfg.envs, cfg.depth) == (1e-4, 2048, 6)          # checkpoint < explicit flags
    cfg = resolve_config({"preset": "small", "heads": 2})
    assert (cfg.d, cfg.depth, cfg.heads) == (128, 2, 2)             # preset < explicit flags
    assert resolve_config({"shaped": True}).objective == "shaped"
    with pytest.raises(ValueError):
        resolve_config({"objective": "floors"})


def test_env_kwargs_floor_step_cap_pass_through():
    class Old:
        def __init__(self, num_envs, *, threads=8, step_cap=3000, auto_reset=True, seed_text="", pool=None,
                     pool_cache=4096):
            pass

    class New(Old):
        def __init__(self, num_envs, *, floor_step_cap=0, **kw):
            pass

    cfg = TrainConfig(threads=3)
    assert "floor_step_cap" not in env_kwargs(cfg, "S", Old)
    assert env_kwargs(cfg, "S", Old)["threads"] == 3
    cfg = TrainConfig(floor_step_cap=80)
    assert env_kwargs(cfg, "S", New)["floor_step_cap"] == 80
    with pytest.raises(ValueError, match="floor_step_cap"):
        env_kwargs(cfg, "S", Old)


def test_train_resume_run_directory(tmp_path):
    """A tiny real run: checkpoints (rotated, best.pt), metrics.jsonl, status.json, evals.jsonl + eval run dirs;
    then `--resume auto` continues the counters, keeps the config and drops nothing / duplicates nothing."""
    seedset = {"name": "t2", "ascension": 0, "content": "ported_only",
               "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(seedset))
    run = tmp_path / "run"
    given = {"out": str(run), "envs": 24, "threads": 2, "pool": 12, "rollout": 4, "minibatch": 64, "d": 32, "s": 64,
             "depth": 1, "heads": 2, "ff_mult": 2, "max_updates": 3, "ckpt_every": 1, "keep_ckpts": 2,
             "eval_every": 2, "eval_seedset": str(ss), "eval_threads": 2, "log_every": 100, "max_hours": 0.5,
             "torch_threads": 2}
    cfg, ck, ck_path = plan_run(given, None, log=lambda m: None)
    assert ck is None
    s1 = train(cfg, log=lambda m: None)
    assert s1["updates"] == 3 and s1["stop_reason"] == "max_updates"
    assert sorted(p.name for p in (run / "checkpoints").iterdir()) == ["best.pt", "ckpt-0000002.pt", "ckpt-0000003.pt"]
    st = json.loads((run / "status.json").read_text())
    assert st["state"] == "finished" and st["update"] == 3 and st["last_checkpoint"] == "checkpoints/ckpt-0000003.pt"
    assert st["env_steps"] == s1["env_steps"] > 0 and st["best_eval"]["update"] in (2, 3)
    ev = read_jsonl(run / "evals.jsonl")
    assert [e["update"] for e in ev] == [2, 3]          # every 2 updates + the final eval of a budget-ended session
    assert ev[0]["games"] == 2 and ev[0]["replayable"] == 2 and (run / ev[0]["dir"] / "report.md").exists()
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2, 3]
    for k in ("env_steps", "episodes", "progress_mean", "win_rate", "floor_mean", "policy_loss", "value_loss",
              "entropy", "env_steps_per_s", "t_env_s", "t_infer_s", "t_update_s", "t_eval_s"):
        assert k in rows[-1], k
    with pytest.raises(ValueError, match="already holds checkpoints"):
        plan_run(given, None, log=lambda m: None)

    cfg2, ck2, ck2_path = plan_run({"out": str(run), "max_updates": 5}, "auto", log=lambda m: None)
    assert ck2_path.name == "ckpt-0000003.pt" and cfg2.depth == 1 and cfg2.eval_every == 2 and cfg2.max_updates == 5
    with pytest.raises(ValueError, match="architecture"):
        plan_run({"out": str(run), "d": 48}, "auto", log=lambda m: None)
    s2 = train(cfg2, log=lambda m: None, resume=ck2, resume_path=ck2_path)
    assert s2["update_start"] == 3 and s2["updates"] == 5 and s2["session"] == 2
    assert s2["env_steps"] == s1["env_steps"] + s2["session_env_steps"]
    assert [r["update"] for r in read_jsonl(run / "metrics.jsonl")] == [1, 2, 3, 4, 5]
    assert [e["update"] for e in read_jsonl(run / "evals.jsonl")] == [2, 3, 4, 5]
    conf = json.loads((run / "config.json").read_text())
    assert [x["update_start"] for x in conf["sessions"]] == [0, 3]
    m, _, ck5 = load_checkpoint(run / "checkpoints" / "ckpt-0000005.pt")
    assert ck5["updates"] == 5 and ck5["counters"]["sessions"] == 2 and ck5["weights_hash"] == s2["weights_hash"]


def test_local_size_guard():
    """cpu / mps refuse the large preset or > 3M parameters unless allow_big_local; cuda is never refused."""
    from sts2train.ppo import LOCAL_PARAM_LIMIT, BigLocalRunError, check_local_size

    cpu, mps, cuda = torch.device("cpu"), torch.device("mps"), torch.device("cuda")
    large = resolve_config({"preset": "large", "minibatch": 4096})
    with pytest.raises(BigLocalRunError, match=r"29\.6 GiB"):
        check_local_size(large, 15_804_270, 7.39 * 2 ** 20, mps)
    with pytest.raises(BigLocalRunError, match="--allow-big-local"):
        check_local_size(resolve_config({"d": 256, "depth": 4}), LOCAL_PARAM_LIMIT + 1, 2 ** 20, cpu)
    check_local_size(large, 15_804_270, 7.39 * 2 ** 20, cuda)
    check_local_size(resolve_config({"preset": "large", "allow_big_local": True}), 15_804_270, 1.0, cpu)
    check_local_size(resolve_config({"preset": "small"}), 1_175_150, 1.2 * 2 ** 20, mps)
    # the override is never inherited from a checkpoint's config
    assert resolve_config({}, {"allow_big_local": True, "preset": "large"}).allow_big_local is False


def test_cli_refuses_large_preset_on_cpu(tmp_path, capsys):
    from sts2train.cli import main

    rc = main(["train", "--preset", "large", "--device", "cpu", "--envs", "4", "--threads", "1", "--pool", "4",
               "--torch-threads", "2", "--minibatch", "512", "--out", str(tmp_path / "big")])
    assert rc == 2
    out = capsys.readouterr().out
    assert "refusing to train a 15,805,070-parameter model (preset large) on cpu" in out and "GiB" in out
    assert not (tmp_path / "big" / "checkpoints").exists()
