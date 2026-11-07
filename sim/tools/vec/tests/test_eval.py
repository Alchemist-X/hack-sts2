"""`sts2vec eval`: the policy-agnostic protocol evaluator (built-in policies, plug-in policies, replay audit,
thread independence, run-directory layout)."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from sts2vec import policies
from sts2vec.cli import main
from sts2vec.evaluate import evaluate

EXAMPLES = Path(__file__).resolve().parents[1] / "examples"
SS = {"name": "t3", "ascension": 0, "content": "ported_only",
      "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"},
                {"seed": "E1A0DE01", "character": "DEFECT"}]}


def _quiet(_):
    pass


def _summaries(out: Path) -> dict[str, dict]:
    return {p.name: json.loads(p.read_text()) for p in sorted((out / "games").glob("*.summary.json"))}


@pytest.mark.parametrize("policy", ["random", "heuristic", "first"])
def test_builtin_policies_write_protocol_run(tmp_path, full, policy):
    out = tmp_path / policy
    rep = evaluate(SS, policy, out, threads=2, log=_quiet)
    h = rep["headline"]
    assert h["games"] == 3 and h["progress_games"] == 3 and h["replayable"] == 3
    assert 0.0 <= h["progress_mean"] <= 1.0
    for name in ("config.json", "run.json", "report.json", "report.md"):
        assert (out / name).exists(), name
    assert (out / "report.md").read_text().startswith("# Eval report")
    cfg = json.loads((out / "config.json").read_text())
    assert cfg["policy"] == policy and cfg["prompt_hash"] == f"builtin:{policy}" and cfg["reveal_moves"] is False
    assert cfg["limits"] == {"step_cap": 1200, "floor_step_cap": 80} and cfg["server_version"].startswith("sts2vec/")
    for s in _summaries(out).values():
        assert s["result"] in ("win", "loss", "capped", "stuck", "crashed")
        assert s["steps"] == len(s["history_actions"]) and s["replayable"]
        assert s["act_rooms_source"] in ("map", "win") and s["illegal"] == 0
        assert s["progress"] == pytest.approx(rep["games"][[g["seed"] for g in rep["games"]].index(s["seed"])]["progress"])


def test_reward_macros_are_taken_and_replay(tmp_path, full):
    """Rewards-screen macros (2026-11-04): the heuristic takes its cards through TakeRewardCard rows (one decision each),
    never sees a Skip row, and the recorded candidate indices replay to the same end (replay audit n/n)."""
    kinds_taken: list[str] = []
    inner = policies.HeuristicPolicy()

    def recording(obs, ctx):
        a = inner(obs, ctx)
        names = ctx.schema.vocab.enums["RunActionKind"]
        col = ctx.cols("cand").kind
        for i in np.flatnonzero(ctx.live & (a >= 0)):
            kinds_taken.append(names.name(int(obs["cand"][i, a[i], col])))
        return a

    rep = evaluate(SS, recording, tmp_path / "macros", threads=2, log=_quiet)
    assert rep["headline"]["replayable"] == 3
    assert kinds_taken.count("TakeRewardCard") >= 3, sorted(set(kinds_taken))
    assert "PickRewardCard" not in kinds_taken, "the heuristic never needs the open card screen without a REROLL"


def test_results_independent_of_threads_and_batch(tmp_path, full):
    a = _summaries((evaluate(SS, "random", tmp_path / "a", threads=1, log=_quiet), tmp_path / "a")[1])
    b = _summaries((evaluate(SS, "random", tmp_path / "b", threads=4, log=_quiet), tmp_path / "b")[1])
    one = dict(SS, games=SS["games"][1:2])
    c = _summaries((evaluate(one, "random", tmp_path / "c", threads=1, log=_quiet), tmp_path / "c")[1])
    keys = ("result", "act", "floor", "steps", "history_actions", "final_state_hash", "progress")
    for name in a:
        assert {k: a[name][k] for k in keys} == {k: b[name][k] for k in keys}, name
    name = "SILENT-E1A0SI01.summary.json"
    assert {k: a[name][k] for k in keys} == {k: c[name][k] for k in keys}   # same game alone in its batch


def test_plugin_policies_file_and_module(tmp_path, full):
    rep = evaluate(SS, f"{EXAMPLES / 'policy_template.py'}:act", tmp_path / "file", threads=2, log=_quiet)
    assert rep["headline"]["replayable"] == 3 and rep["prompt_hash"].startswith("sha256:")
    assert rep["policy"] == "policy_template.py:act"
    rep = evaluate(SS, "sts2vec.policies:HeuristicPolicy", tmp_path / "mod", threads=2, log=_quiet)
    ref = evaluate(SS, "heuristic", tmp_path / "builtin", threads=2, log=_quiet)
    assert rep["headline"]["progress_mean"] == ref["headline"]["progress_mean"]


def test_illegal_actions_are_counted_and_capped(tmp_path, full):
    def bad(obs, ctx):
        return np.full(ctx.num_envs, 10_000, dtype=np.int32)

    one = dict(SS, games=SS["games"][:1])
    rep = evaluate(one, bad, tmp_path / "bad", threads=1, step_cap=50, floor_step_cap=5, log=_quiet)
    g = rep["games"][0]
    assert g["result"] == "stuck" and g["illegal"] == 5 and g["steps"] == 5 and g["replayable"]


def test_policy_shape_is_checked(tmp_path, full):
    with pytest.raises(ValueError, match="shape"):
        evaluate(SS, lambda obs, ctx: np.zeros(1, dtype=np.int32), tmp_path / "x", threads=1, log=_quiet)


def test_resolve_errors():
    with pytest.raises(ValueError):
        policies.resolve("no-such-policy")
    with pytest.raises(FileNotFoundError):
        policies.resolve("does/not/exist.py:act")


def test_cli_eval(tmp_path, full, capsys):
    p = tmp_path / "ss.json"
    p.write_text(json.dumps(dict(SS, games=SS["games"][:2])))
    out = tmp_path / "cli"
    assert main(["eval", "--policy", "heuristic", "--seedset", str(p), "--out", str(out), "--threads", "2"]) == 0
    d = json.loads(capsys.readouterr().out.split("\n", 2)[-1])
    assert d["games"] == 2 and d["replayable"] == 2 and d["out"] == str(out)
