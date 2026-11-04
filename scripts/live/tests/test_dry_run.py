"""Dry-run integration: the whole lockstep (two processes, semantic mapping, diffing, execution, bookkeeping) against
the simulator's play server started by the fixture. Every executed twin row is also formatted as a play-server token
from the row's own arguments (``Twin.row_to_token``) and must equal the token the stand-in matched by meaning; the
stand-in's own history must equal what the runner sent. A rewards-screen macro row (TakeRewardCard /
TakeRewardAlternative; the play server has no macros) is its plan's tokens, '; '-joined."""

from __future__ import annotations

import json

import pytest

pytest.importorskip("sts2vec")
pytest.importorskip("sts2train")

SEED = "TW1NA10S01"
CORE_KINDS = {"play", "end_turn", "travel", "claim_reward", "leave", "event_option", "select", "macro"}


def _records(out):
    return [json.loads(line) for line in (out / "trajectory.jsonl").read_text().splitlines()]


@pytest.mark.parametrize("ck", ["tiny_checkpoint", "pre_macro_checkpoint"])
def test_dry_run_cli_with_a_checkpoint(play_server, request, tmp_path, ck):
    """A checkpoint on this library's spec, and one from before the rewards-screen macros (widened on load)."""
    import nn_twin_play

    checkpoint = request.getfixturevalue(ck)
    out = tmp_path / "run"
    rc = nn_twin_play.main(["--checkpoint", str(checkpoint), "--seed", SEED, "--character", "SILENT",
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
        assert r["twin_action"]["token"] == "; ".join(a["native"] for a in r["real_action"])
    # the model scored macro rows (rewards screens with a card reward), widened or not
    assert any(t["action"].startswith("reward ") for r in dec for t in r["top5"])
    config = json.loads((out / "config.json").read_text())
    assert config["game"]["kind"] == "standin" and config["checkpoint"]["aux_heads"] is True
    widened = config["spec_compat"]["widened_enums"]
    if ck == "pre_macro_checkpoint":
        assert config["spec_compat"]["model_inputs"] == "widened"
        assert widened == {"RunActionKind": {"from": 21, "to": 23,
                                             "added": ["TakeRewardCard", "TakeRewardAlternative"]}}
    else:
        assert config["spec_compat"]["model_inputs"] == "identical" and widened == {}
    assert (out / "status.log").read_text().count("\n") >= s["decisions"]


def test_policy_widens_a_pre_macro_checkpoint(pre_macro_checkpoint):
    """Bound to this library, a pre-macro checkpoint runs widened, as in `sts2train eval`. Unwidened, the model would
    clamp the macro kinds (21, 22) to its last kind (PromptCancel) and score every macro row as a cancel."""
    from sts2train.model import ENUM_EMBEDDINGS
    from sts2vec import load, schema_of

    from nn_twin.twin import Policy

    p = Policy(str(pre_macro_checkpoint), torch_threads=1)
    assert p.spec.enums["RunActionKind"] == 21
    compat = p.bind(schema_of(load()))
    assert compat["model_inputs"] == "widened" and p.spec.enums["RunActionKind"] == 23
    assert p.spec.enum_names["RunActionKind"][21:] == ["TakeRewardCard", "TakeRewardAlternative"]
    assert [getattr(p.model, m).num_embeddings for m in ENUM_EMBEDDINGS["RunActionKind"]] == [23, 23]


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


def test_policy_loop_breaker_masks_a_row_that_led_back(tiny_checkpoint):
    """Greedy + sts2train's loop breaker: at an observation the twin was already at, the row taken there is masked
    (here: committed without a step, i.e. a no-op); --loop-breaker off keeps the plain argmax."""
    from nn_twin.twin import Policy, Twin

    twin = Twin(content="all")
    try:
        twin.start_fresh("SILENT", SEED, 10)
        on = Policy(str(tiny_checkpoint), torch_threads=2)
        on.bind(twin.schema)
        assert on.meta["loop_breaker"]["mode"] == "on"
        d1 = on.decide(twin.obs)
        on.commit(d1.row)
        d2 = on.decide(twin.obs)
        assert d2.plain == d1.row and d2.row != d1.row and d2.loop_break
        assert d2.ranked()[-1] == d1.row                      # the masked row ranks last for follow / substitute
        on.commit(d2.row)
        assert on.loop_totals()["no_op_actions"] == 1
        off = Policy(str(tiny_checkpoint), torch_threads=2, loop_breaker=False)
        off.bind(twin.schema)
        a = off.decide(twin.obs)
        off.commit(a.row)
        b = off.decide(twin.obs)
        assert a.row == b.row and not b.loop_break
    finally:
        twin.close()
