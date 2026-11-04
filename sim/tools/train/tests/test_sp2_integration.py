"""Integration of the seed stream / isolation / episode log (train/sp2-seeds) with the UI revisit cycles / eval modes /
fixed endpoints (train/sp2-cycles): the --eval-at seed set goes through the blind gate and the isolation guard like
the periodic one, fixed-endpoint rows never become best / last on a resume, --no-best survives the resume's
evals.jsonl recompute, and a stream run logs cycles per episode."""

from __future__ import annotations

import json

import pytest
from sts2vec import protocol as PG

from sts2train.ppo import evals_history, plan_run, resolve_config, train
from sts2train.runlog import read_jsonl

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 8, "threads": 2, "rollout": 16, "minibatch": 64, "epochs": 1, "eval_every": 0, "ckpt_every": 1,
       "ckpt_every_min": 0, "keep_ckpts": 10, "log_every": 100, "max_hours": 0.5, "torch_threads": 1,
       "eval_threads": 2, "characters": ("SILENT",), "ascension": 10, "start_mode": "stream", **TINY}
TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}


def _quiet(m):
    pass


def test_eval_at_blind_set_refused_before_the_run_directory(tmp_path):
    from sts2train.cli import main

    run = tmp_path / "run"
    with pytest.raises(PG.BlindSetError):
        train(resolve_config({**RUN, "out": str(run), "eval_at": "1", "eval_at_seedset": "silent-a10-blind-v1",
                              "max_updates": 1}), log=_quiet)
    assert not run.exists()
    assert main(["train", "--out", str(run), "--start-mode", "stream", "--eval-every", "0", "--eval-at", "1",
                 "--eval-at-seedset", "silent-a10-blind-v1", "--envs", "4", "--max-updates", "1"]) == 2
    assert not run.exists()
    assert main(["train", "--out", str(run), "--eval-every", "0", "--eval-at", "1", "--eval-at-seedset",
                 "no-such-seedset-v9", "--envs", "4", "--max-updates", "1"]) == 2           # unknown set: exit 2
    assert not run.exists()


def test_eval_at_allowed_blind_access_is_logged(tmp_path, monkeypatch):
    sets = tmp_path / "sets"
    sets.mkdir()
    games = [{"seed": "Z9TESTBL01", "character": "SILENT"}, {"seed": "Z9TESTBL02", "character": "SILENT"}]
    (sets / "syn-blind.json").write_text(json.dumps({"name": "syn-blind", "id": "syn-blind-v1", "role": "blind",
                                                     "ascension": 10, "content": "ported_only", "games": games}))
    monkeypatch.setenv("STS2VEC_SEEDSETS", str(sets))
    run = tmp_path / "run"
    train(resolve_config({**RUN, "out": str(run), "eval_at": "1", "eval_at_seedset": "syn-blind-v1",
                          "eval_at_modes": "greedy-off", "max_updates": 1, "allow_blind": True,
                          "blind_purpose": "eval-at access log"}), log=_quiet)
    rows = [json.loads(x) for x in (run / "blind-access.jsonl").read_text().splitlines()]
    assert [r["purpose"] for r in rows] == ["eval-at access log"]
    assert (run / "evals" / "u0000001" / "greedy-off" / "blind-access.jsonl").exists()
    conf = json.loads((run / "config.json").read_text())
    assert conf["blind_eval_at"]["purpose"] == "eval-at access log" and conf["blind_eval"] is None
    assert "syn-blind-v1" in {x["id"] for x in conf["isolation"]["checked_sets"]}
    ev = read_jsonl(run / "evals.jsonl")
    assert [(r["update"], r["mode"]) for r in ev] == [(1, "greedy-off")] and not ev[0].get("error")


def test_evals_history_skips_fixed_endpoint_rows(tmp_path):
    run = tmp_path / "src"
    (run / "checkpoints").mkdir(parents=True)
    clean = {"games": 2, "replayable": 2, "results": {"loss": 2}, "seedset": "s"}
    rows = [{"update": 1, "progress_mean": 0.1, **clean}, {"update": 2, "progress_mean": 0.3, **clean},
            {"update": 2, "progress_mean": 0.9, "mode": "greedy-off", "eval_at": True, **clean},
            {"update": 2, "progress_mean": 0.8, "mode": "greedy-on", "eval_at": True, **clean}]
    (run / "evals.jsonl").write_text("".join(json.dumps(r) + "\n" for r in rows))
    best, last, rec = evals_history(run / "checkpoints" / "ckpt-0000002.pt", tmp_path / "fork", 2, "s", None, None)
    assert best["progress_mean"] == 0.3 and "mode" not in best
    assert last["update"] == 2 and "mode" not in last and rec["rows"] == 4


def test_no_best_survives_the_resume_recompute_and_cycles_per_episode(tmp_path):
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    run = tmp_path / "run"
    given = {**RUN, "out": str(run), "eval_every": 1, "eval_seedset": str(ss), "no_best": True, "max_updates": 2}
    cfg, _, _ = plan_run(given, None, log=_quiet)
    train(cfg, log=_quiet, given=given)
    assert len(read_jsonl(run / "evals.jsonl")) == 2
    cfg2, ck2, p2 = plan_run({"out": str(run), "max_updates": 3, "eval_at": "3", "eval_at_seedset": str(ss),
                              "eval_at_modes": "greedy-on"}, "auto", log=_quiet)
    assert cfg2.no_best and cfg2.start_mode == "stream"
    s = train(cfg2, log=_quiet, resume=ck2, resume_path=p2)
    assert s["best_eval"] is None and not (run / "checkpoints" / "best.pt").exists()
    conf = json.loads((run / "config.json").read_text())
    assert conf["sessions"][-1]["best_eval_source"]["source"] == "same run directory"
    ev = read_jsonl(run / "evals.jsonl")
    assert [(r["update"], r.get("mode")) for r in ev] == [(1, None), (2, None), (3, None), (3, "greedy-on")]
    assert s["last_eval"]["update"] == 3 and "mode" not in s["last_eval"]
    eps = [r for r in read_jsonl(run / "episodes.jsonl") if r["result"] not in ("dropped", "start_failed")]
    assert all(isinstance(r["cycles"], int) for r in eps)
    m = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in m] == [1, 2, 3] and all("cycles_total" in r and "seed_stream_next" in r for r in m)
    assert sum(r["cycles_total"] for r in m) == len(read_jsonl(run / "cycles.jsonl"))
