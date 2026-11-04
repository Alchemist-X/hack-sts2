"""Dry-run integration: the whole lockstep (two processes, semantic mapping, diffing, execution, bookkeeping) against
the simulator's play server started by the fixture. Every executed twin row is also formatted as a play-server token
from the row's own arguments (``Twin.row_to_token``) and must equal the token the stand-in matched by meaning; the
stand-in's own history must equal what the runner sent."""

from __future__ import annotations

import json

import pytest

pytest.importorskip("sts2vec")
pytest.importorskip("sts2train")

SEED = "TW1NA10S01"
CORE_KINDS = {"play", "end_turn", "travel", "claim_reward", "leave", "event_option", "select"}


def _records(out):
    return [json.loads(line) for line in (out / "trajectory.jsonl").read_text().splitlines()]


def test_dry_run_cli_with_a_checkpoint(play_server, tiny_checkpoint, tmp_path):
    import nn_twin_play

    out = tmp_path / "run"
    rc = nn_twin_play.main(["--checkpoint", str(tiny_checkpoint), "--seed", SEED, "--character", "SILENT",
                            "--ascension", "10", "--out", str(out), "--dry-run-server", play_server,
                            "--stop-floor", "3", "--max-decisions", "400", "--torch-threads", "2"])
    s = json.loads((out / "summary.json").read_text())
    assert rc == 0, s.get("error")
    assert s["decisions"] > 5 and s["desync_decisions"] == 0 and s["first_desyncs"] == []
    audit = s["dry_run_audit"]
    assert audit["token_mismatches"] == 0 and audit["history_equal"] is True
    assert audit["lockstep_steps_compared"] == s["decisions"]
    recs = _records(out)
    assert recs[0]["type"] == "start" and recs[0]["twin_origin"]["kind"] == "fresh" and recs[0]["desync"] == []
    dec = [r for r in recs if r["type"] == "decision"]
    assert len(dec) == s["decisions"]
    for r in dec:
        assert r["policy"] == "greedy" and r["mode"] == "lockstep" and r["desync"] == []
        assert 0.0 <= r["win_prob"] <= 1.0                       # the tiny checkpoint has aux heads
        assert r["top5"] and abs(r["top5"][0]["p"] - max(t["p"] for t in r["top5"])) < 1e-9
        assert r["twin_action"]["token"] == r["real_action"][0]["native"]
    config = json.loads((out / "config.json").read_text())
    assert config["game"]["kind"] == "standin" and config["checkpoint"]["aux_heads"] is True
    assert (out / "status.log").read_text().count("\n") >= s["decisions"]


@pytest.mark.parametrize("seeds", [(11, 12, 13, 14, 28)])
def test_random_policy_dry_runs_cover_the_candidate_kinds(play_server, tmp_path, seeds):
    """Seeded random play over the twin's candidates reaches shops, rest sites, prompts, card rewards and potions
    (seed 28: a Neow card-bundle prompt); every step must map onto the stand-in token the twin's own row names, with
    no desync."""
    from nn_twin.runner import Runner
    from nn_twin.standin import PlayServerGame
    from nn_twin.twin import RandomPolicy, Twin

    covered = set()
    for k in seeds:
        seed = f"C0VER{k:02d}"
        twin = Twin(content="all")
        policy = RandomPolicy(seed=k)
        policy.bind(twin.schema)
        game = PlayServerGame(play_server, seed=seed, character="SILENT", ascension=10, content="all")
        out = tmp_path / f"r{k}"
        try:
            s = Runner(game, twin, policy, out, character="SILENT", ascension=10, seed=seed,
                       max_decisions=700, audit_tokens=True, log=lambda *_: None).run()
        finally:
            twin.close()
            game.close()
        assert s["desync_decisions"] == 0, s["first_desyncs"]
        assert s["dry_run_audit"]["token_mismatches"] == 0, s["dry_run_audit"]["first_mismatches"]
        assert s["dry_run_audit"]["history_equal"] is True
        assert s["error"] is None or "max-decisions" in s["error"]
        covered |= {r["twin_action"]["kind"] for r in _records(out)
                    if r["type"] == "decision" and r.get("twin_action")}
    missing = CORE_KINDS - covered
    assert not missing, f"not exercised: {missing} (covered {sorted(covered)})"
