"""Eval gates: `best.pt` / `best_eval` only from clean evals (every game replayed, none crashed / errored, the
evaluation itself ran), and `sts2train eval` exits 1 with `replayable` in its JSON when the replay audit fails."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
import torch

from sts2train.ppo import best_skip_reason, is_new_best, resolve_config, train
from sts2train.runlog import read_jsonl

TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}


def _row(**kw):
    r = {"update": 1, "progress_mean": 0.3, "games": 20, "replayable": 20,
         "results": {"win": 0, "loss": 20, "capped": 0, "stuck": 0, "crashed": 0, "aborted": 0, "error": 0}}
    r.update(kw)
    return r


def test_best_skip_reason_and_selection_rules():
    assert best_skip_reason(_row()) is None
    assert best_skip_reason(_row(results={"loss": 18, "stuck": 1, "capped": 1})) is None
    assert best_skip_reason(_row(replayable=19)) == "replay audit: 19/20 games replayable"
    assert best_skip_reason(_row(replayable=None)) == "replay audit: None/20 games replayable"
    assert best_skip_reason(_row(games=0, replayable=0)) == "replay audit: 0/0 games replayable"
    assert best_skip_reason(_row(results={"loss": 19, "crashed": 1})) == "games ended 1 crashed"
    assert best_skip_reason(_row(results={"loss": 17, "crashed": 2, "error": 1})) == "games ended 2 crashed, 1 error"
    assert best_skip_reason(_row(progress_mean=None)) == "no progress mean"
    assert best_skip_reason({"update": 3, "error": "RuntimeError: boom"}) == "evaluation failed: RuntimeError: boom"
    assert best_skip_reason(None) == "no eval"

    clean, higher, unclean_higher = _row(progress_mean=0.3), _row(progress_mean=0.4), _row(progress_mean=0.9,
                                                                                           replayable=19)
    assert is_new_best(clean, None) and is_new_best(higher, clean)
    assert not is_new_best(clean, higher) and not is_new_best(_row(progress_mean=0.3), clean)    # a tie keeps the first
    assert not is_new_best(unclean_higher, clean) and not is_new_best(unclean_higher, None)
    # a best_eval kept from before the rule (e.g. restored from an older checkpoint) that is not clean gives way to
    # any clean eval, even a lower one
    assert is_new_best(_row(progress_mean=0.1), unclean_higher)


def test_training_best_checkpoint_skips_unclean_evals(tmp_path, monkeypatch):
    """During training (eval every update, the evaluator replaced by scripted reports): a higher-progress eval with a
    failed replay audit, an evaluation that raised and a higher-progress eval with a crashed game never become
    best_eval / best.pt; their rows say why; the clean eval at update 5 (0.3) beats the clean one at update 1 (0.2)."""
    import sts2train.evaluate as E

    plan = {1: {"progress_mean": 0.2}, 2: {"progress_mean": 0.5, "replayable": 1}, 3: "raise",
            4: {"progress_mean": 0.6, "results": {"loss": 1, "crashed": 1}}, 5: {"progress_mean": 0.3}}

    def scripted(model, spec, seedset, out_dir, *, weights_hash, checkpoint=None, **kw):
        p = plan[int(Path(out_dir).name[1:])]
        if p == "raise":
            raise RuntimeError("eval worker died")
        h = {"progress_ci95": [None, None], "progress_median": None, "wins": 0, "games": 2, "floor_mean": 5.0,
             "results": {"loss": 2, "crashed": 0, "error": 0}, "act_reached": {"1": 2}, "replayable": 2}
        return {"headline": {**h, **p}}

    monkeypatch.setattr(E, "evaluate_model", scripted)
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    run = tmp_path / "run"
    cfg = resolve_config({"out": str(run), "envs": 8, "threads": 2, "pool": 4, "rollout": 2, "minibatch": 16,
                          "d": 32, "s": 64, "max_updates": 5, "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 1,
                          "eval_seedset": str(ss), "max_hours": 0.5, "torch_threads": 2})
    logs: list[str] = []
    s = train(cfg, log=logs.append)
    ev = read_jsonl(run / "evals.jsonl")
    assert [e["update"] for e in ev] == [1, 2, 3, 4, 5]
    assert "best_skip" not in ev[0] and "best_skip" not in ev[4]
    assert ev[1]["best_skip"] == "replay audit: 1/2 games replayable"
    assert ev[2]["error"] == "RuntimeError: eval worker died"
    assert ev[3]["best_skip"] == "games ended 1 crashed"
    assert s["best_eval"]["update"] == 5 and s["best_eval"]["progress_mean"] == 0.3
    assert json.loads((run / "status.json").read_text())["best_eval"]["update"] == 5
    assert torch.load(run / "checkpoints" / "best.pt", map_location="cpu", weights_only=False)["updates"] == 5
    assert any("[eval u2]" in m and "not eligible for best.pt: replay audit: 1/2" in m for m in logs)
    assert sum("*best*" in m for m in logs) == 2                    # updates 1 and 5


def test_eval_cli_exit_code_and_replayable(tmp_path, monkeypatch, capsys):
    """`sts2train eval` prints `replayable` and exits 1 when a game's history did not replay (0 when all did), like
    `sts2vec eval`."""
    import sts2train.evaluate as E
    from sts2train.cli import main

    head = {"progress_mean": 0.2, "progress_ci95": [0.1, 0.3], "wins": 0, "games": 2, "results": {"loss": 2},
            "floor_mean": 7.0, "act_reached": {"1": 2}}
    for replayable, want in ((2, 0), (1, 1), (0, 1)):
        monkeypatch.setattr(E, "evaluate", lambda *a, r=replayable, **k: {"headline": {**head, "replayable": r}})
        rc = main(["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o")])
        out = capsys.readouterr()
        assert rc == want
        assert json.loads(out.out)["replayable"] == replayable
        assert ("replay audit failed" in out.err) == (want == 1)


def test_eval_cli_real_run_replays(tmp_path, capsys):
    """The real path: an untrained checkpoint on a 2-game seed set; every game replays, exit 0, `replayable` 2."""
    from sts2train.cli import main
    from sts2train.features import ModelSpec, bind_spec
    from sts2train.model import Policy
    from sts2train.ppo import TrainConfig, save_checkpoint
    from sts2vec import load, schema_of

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(1)
    ck = tmp_path / "ck.pt"
    save_checkpoint(ck, Policy(spec), spec, TrainConfig(), {"updates": 0})
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    rc = main(["eval", "--seedset", str(ss), "--checkpoint", str(ck), "--out", str(tmp_path / "ev"), "--threads", "2"])
    out = capsys.readouterr().out
    res = json.loads(out[out.index('{\n "out"'):])
    assert rc == 0 and res["games"] == 2 and res["replayable"] == 2


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
