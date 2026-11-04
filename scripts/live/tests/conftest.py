"""Fixtures for the twin lockstep runner tests.

Run: ``nice -n 10 uv run --project sim/tools/train pytest -q scripts/live/tests`` (the trainer environment has torch,
sts2vec and sts2train). The integration tests need the Release build of the simulator
(``dotnet build sim/Sts2Sim.sln -c Release``); they start their own play server on a free port and skip when the
build is missing.
"""

from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import pytest

LIVE = Path(__file__).resolve().parents[1]
ROOT = LIVE.parents[1]
if str(LIVE) not in sys.path:
    sys.path.insert(0, str(LIVE))

SERVER_DLL = ROOT / "sim" / "Sts2Sim.Server" / "bin" / "Release" / "net9.0" / "Sts2Sim.Server.dll"
# The spec stored in the 2026-11-04 Silent checkpoints (s3c / s4a / s5b; encoder 5dbfa09dd9dcb5ed): RunActionKind 0..20,
# before the rewards-screen macros (sim/tools/train/tests/test_resume_compat.py pins it).
PRE_MACRO_SPEC = ROOT / "sim" / "tools" / "train" / "tests" / "data" / "spec-5dbfa09dd9dcb5ed.json"


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture(scope="session")
def play_server():
    """A play server process of this tree's Release build on a free loopback port; yields its URL."""
    if not SERVER_DLL.exists():
        pytest.skip(f"no Release build of the play server ({SERVER_DLL}); run dotnet build sim/Sts2Sim.sln -c Release")
    port = _free_port()
    proc = subprocess.Popen(["dotnet", str(SERVER_DLL), "--port", str(port), "--no-crash-log"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, cwd=ROOT,
                            preexec_fn=lambda: os.nice(10))
    url = f"http://127.0.0.1:{port}"
    try:
        deadline = time.monotonic() + 60
        while True:
            try:
                with urllib.request.urlopen(url + "/health", timeout=2) as r:
                    if r.status == 200:
                        break
            except OSError:
                pass
            if proc.poll() is not None or time.monotonic() > deadline:
                pytest.skip("the play server did not start")
            time.sleep(0.3)
        yield url
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()


@pytest.fixture(scope="session")
def tiny_checkpoint(tmp_path_factory):
    """An untrained small checkpoint with aux heads (exercises load_checkpoint, spec binding and the win_prob path)."""
    torch = pytest.importorskip("torch")
    from sts2train.features import ModelSpec, bind_spec
    from sts2train.model import Policy
    from sts2train.ppo import TrainConfig, save_checkpoint
    from sts2vec import load, schema_of

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(7)
    path = tmp_path_factory.mktemp("ck") / "tiny.pt"
    model = Policy(spec, d=64, s=128, depth=1, heads=4, ff_mult=2, aux_heads=True)
    save_checkpoint(path, model, spec, TrainConfig(), {"updates": 0})
    return path


@pytest.fixture(scope="session")
def pre_macro_checkpoint(tmp_path_factory):
    """An untrained small checkpoint on the pre-macro spec (`PRE_MACRO_SPEC`), as the 2026-11-04 Silent runs were
    trained: on this library the twin has to widen its RunActionKind embedding (21 -> 23) to score macro rows."""
    torch = pytest.importorskip("torch")
    from sts2train.features import ModelSpec
    from sts2train.model import Policy
    from sts2train.ppo import TrainConfig, save_checkpoint

    spec = ModelSpec.from_json(json.loads(PRE_MACRO_SPEC.read_text()))
    torch.manual_seed(11)
    path = tmp_path_factory.mktemp("ck-pre-macro") / "pre-macro.pt"
    model = Policy(spec, d=64, s=128, depth=1, heads=4, ff_mult=2, aux_heads=True)
    save_checkpoint(path, model, spec, TrainConfig(), {"updates": 0})
    return path
