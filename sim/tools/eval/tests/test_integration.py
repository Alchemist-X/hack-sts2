"""End to end against the real server (skipped unless the Release server dll is built)."""

import json
import shutil

import pytest

from sts2eval.cli import main
from sts2eval.server import server_dll

pytestmark = pytest.mark.skipif(not server_dll().exists() or shutil.which("dotnet") is None,
                                reason="server dll not built (dotnet build sim/Sts2Sim.sln -c Release)")


def test_spawn_random_two_games(tmp_path):
    seedset = {"name": "tiny", "ascension": 0, "content": "ported_only",
               "games": [{"seed": "ITEST01", "character": "IRONCLAD"},
                         {"seed": "ITEST02", "character": "DEFECT"}]}
    sp = tmp_path / "tiny.json"
    sp.write_text(json.dumps(seedset))
    out = tmp_path / "out"
    rc = main(["run", "--seedset", str(sp), "--policy", "random", "--concurrency", "2", "--spawn",
               "--out", str(out), "--progress-interval", "0"])
    assert rc == 0
    for key in ("IRONCLAD-ITEST01", "DEFECT-ITEST02"):
        s = json.loads((out / "games" / f"{key}.summary.json").read_text())
        assert s["result"] in ("win", "loss", "capped"), s
        assert s["steps"] > 0
        assert s["history_steps"] == s["steps"] and len(s["history_tokens"]) == s["steps"]
        assert s["replayable"] is True
        lines = (out / "games" / f"{key}.jsonl").read_text().splitlines()
        assert sum(1 for x in lines if json.loads(x)["type"] == "step") == s["steps"]
        # the recorded tokens are exactly the server history
        steps = [json.loads(x) for x in lines if json.loads(x)["type"] == "step"]
        assert [r["token"] for r in steps] == s["history_tokens"]
    assert (out / "report.md").exists() and (out / "report.json").exists()
    cfg = json.loads((out / "config.json").read_text())
    assert cfg["server_version"] and cfg["server_spawned"] is True
    assert (out / "server.log").exists()

    # deterministic: the same seed set with the random policy replays the same token lists
    out2 = tmp_path / "out2"
    assert main(["run", "--seedset", str(sp), "--policy", "random", "--concurrency", "2", "--spawn",
                 "--out", str(out2), "--progress-interval", "0"]) == 0
    for key in ("IRONCLAD-ITEST01", "DEFECT-ITEST02"):
        a = json.loads((out / "games" / f"{key}.summary.json").read_text())["history_tokens"]
        b = json.loads((out2 / "games" / f"{key}.summary.json").read_text())["history_tokens"]
        assert a == b

    # --resume skips finished games; `report` re-aggregates
    assert main(["run", "--seedset", str(sp), "--policy", "random", "--spawn", "--resume",
                 "--out", str(out), "--progress-interval", "0"]) == 0
    assert main(["report", str(out)]) == 0


def test_claude_policy_on_real_observations(tmp_path):
    """The Claude code path end to end with a fake client that answers the last listed legal token
    read back from the real user message (prompt building, parsing, accounting, transcripts)."""
    import asyncio
    import re

    from fakes import FakeAnthropic, response
    from sts2eval import prices as P
    from sts2eval.policies import ClaudePolicy
    from sts2eval.runner import RunLimits, RunState, run_all
    from sts2eval.server import SpawnedServer
    from sts2eval.simclient import SimClient

    def answer(kw):
        msg = kw["messages"][0]["content"]
        legal = msg.split("-- legal actions", 1)[1].split("==", 1)[0]
        toks = [m.group(1).strip() for m in re.finditer(r"^ (\S.*?)\s{2,}:", legal, re.M)]
        return response(f"TOKEN: {toks[-1].upper()}\nlast option")

    seedset = {"name": "tiny", "ascension": 0, "content": "ported_only",
               "games": [{"seed": "ITEST03", "character": "SILENT"}]}
    tracker = P.CostTracker()
    policy = ClaudePolicy(FakeAnthropic([answer] * 5000), tracker=tracker)
    with SpawnedServer(tmp_path) as srv:
        async def go():
            c = SimClient(srv.url)
            try:
                return await run_all(c, policy, seedset, tmp_path, RunLimits(step_cap=300), RunState(tracker=tracker),
                                     concurrency=1, progress_interval=0)
            finally:
                await c.aclose()
        [s] = asyncio.run(go())
    assert s["steps"] > 0 and s["result"] in ("win", "loss", "capped")
    assert s["illegal"] == 0 and s["fallbacks"] == 0 and s["retries"] == 0
    assert s["api_calls"] == s["steps"] == tracker.calls
    assert s["replayable"] is True
    assert s["cost_usd"] > 0
