"""Wave 21 throughput measurement: the rollout-inference phase split (sts2train.phases.InferPhases: t_h2d_s /
t_forward_s / t_store_s / t_sample_d2h_s / t_track_s sum to t_infer_s; the CUDA reattribution of the readback's wait,
on a simulated stream), the stacked action readback (same bytes as three .cpu() copies; a train() run with it is
bit-identical to one with the separate copies), and the bench-rollout / bench-update CLIs on a tiny cpu model."""

from __future__ import annotations

import functools
import json
import time

import numpy as np
import pytest
import torch

from sts2train import phases as phmod
from sts2train import ppo
from sts2train.phases import METRIC_KEYS, PHASES, InferPhases, charge, read_back
from sts2train.ppo import resolve_config, train
from sts2train.runlog import read_jsonl

TINY = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
RUN = {"envs": 8, "threads": 2, "rollout": 8, "minibatch": 32, "epochs": 1, "max_updates": 2, "ckpt_every": 0,
       "ckpt_every_min": 0, "eval_every": 0, "torch_threads": 1, "max_hours": 0.2, "log_every": 100,
       "characters": ("SILENT",), "ascension": 10, "step_cap": 24, **TINY}
TIMING = ("time", "wall_s", "session_s", "env_steps_per_s", "env_steps_per_s_session")
PHASE_KEYS = tuple(METRIC_KEYS[p] for p in PHASES)


# ------------------------------------------------------------------------------------------------ charge / events

def test_charge_sums_to_wall_and_moves_the_wait():
    # launch-bound: every boundary's device work done by the host boundary -> host segments unchanged
    hs = [0.0, 1.0, 3.0, 4.0, 4.5]
    assert charge(hs, [4.0, 2.0, 1.0, 0.0]) == pytest.approx([1.0, 2.0, 1.0, 0.5])
    # device-bound forward: the forward's work ends at 4.3, so the readback's wait (4.0 -> 4.3) is the forward's
    out = charge(hs, [3.4, 0.2, 0.1, 0.0])
    assert out == pytest.approx([1.1, 3.2, 0.1, 0.1]) and sum(out) == pytest.approx(4.5)
    # clamps: a device time beyond the end (timer noise) never exceeds the wall; one segment = its wall time
    assert sum(charge(hs, [-1.0, -1.0, -1.0, 0.0])) == pytest.approx(4.5)
    assert charge([2.0, 5.0], [0.0]) == [3.0]


class _Sim:
    """A host clock and one simulated CUDA stream: enqueue(d) at host time `now` runs d seconds after the stream is free;
    an event records the stream's completion time of everything enqueued before it."""

    def __init__(self):
        self.now = 0.0
        self.stream_end = 0.0

    def enqueue(self, d):
        self.stream_end = max(self.stream_end, self.now) + d

    def wait(self, d=0.0):                  # a blocking readback: the host waits for the stream, then the copy
        self.enqueue(d)
        self.now = self.stream_end


def _fake_events(sim):
    class Ev:
        def __init__(self, enable_timing=False):
            self.t = None

        def record(self):
            self.t = max(sim.stream_end, sim.now)

        def elapsed_time(self, other):
            return (other.t - self.t) * 1000.0

        def synchronize(self):
            pass

    return Ev


def test_infer_phases_charge_the_readback_wait_to_the_forward(monkeypatch):
    sim = _Sim()
    monkeypatch.setattr(phmod, "_clk", lambda: sim.now)
    monkeypatch.setattr(torch.cuda, "Event", _fake_events(sim))
    ph = InferPhases("cpu", events=True)
    for _ in range(2):                      # two identical steps (the event pool is reused after flush)
        ph.reset()
        sim.now = sim.stream_end = 0.0
        ph.begin("track")
        sim.now = 0.001
        ph.to("h2d")
        sim.now = 0.0015
        sim.enqueue(0.002)                  # uploads: done at 0.0035
        sim.now = 0.003
        ph.to("forward")
        sim.now = 0.004
        sim.enqueue(0.010)                  # forward kernels: done at 0.014 (device-bound)
        sim.now = 0.005
        ph.to("store")
        sim.now = 0.0055
        sim.enqueue(0.001)                  # store copies: done at 0.015
        sim.now = 0.006
        ph.to("sample_d2h")
        sim.wait(0.0005)                    # readback returns at 0.0155
        ph.to("track", synced=True)
        sim.now += 0.0002
        ph.to("store", host=True)           # host-only numpy writes: no chain
        sim.now += 0.0001
        ph.end()
        ph.flush()
        acc = ph.acc
        assert acc["h2d"] == pytest.approx(0.0025) and acc["forward"] == pytest.approx(0.0105)
        assert acc["store"] == pytest.approx(0.001 + 0.0001) and acc["sample_d2h"] == pytest.approx(0.0005)
        assert acc["track"] == pytest.approx(0.001 + 0.0002)
        assert ph.total() == pytest.approx(sim.now)
    assert len(ph._pool) == 4 and not ph._used        # 4 boundary events per step, recycled


def test_infer_phases_host_only_on_cpu():
    ph = InferPhases("cpu")
    assert not ph.events
    t0 = time.perf_counter()
    ph.begin("track")
    for p in ("h2d", "forward", "store", "sample_d2h"):
        ph.to(p)
        time.sleep(0.002)
    ph.to("track", synced=True)
    ph.end()
    ph.flush()
    wall = time.perf_counter() - t0
    assert all(ph.acc[p] >= 0.0015 for p in ("h2d", "forward", "store", "sample_d2h"))
    assert ph.total() == pytest.approx(wall, rel=0.02)
    assert set(ph.metrics()) == set(PHASE_KEYS)


# ------------------------------------------------------------------------------------------------ readback

def test_read_back_stacked_is_bit_identical():
    g = torch.Generator().manual_seed(0)
    a = torch.randint(-2 ** 40, 2 ** 40, (37,), generator=g)
    logp = torch.randn(37, generator=g)
    logp[:4] = torch.tensor([float("-inf"), float("nan"), -0.0, 3.4e38])
    logp.view(torch.int32)[5] = 0x7FC0_1234                       # a NaN with a payload
    value = torch.randn(37, 1, generator=g).half()
    flags = torch.rand(37, generator=g) < 0.5
    ref = [t.cpu().numpy() for t in (a, logp, value, flags)]
    got = read_back(a, logp, value, flags, stacked=True)
    for r, x in zip(ref, got):
        assert x.dtype == r.dtype and x.shape == r.shape and x.tobytes() == r.tobytes()
    # bf16 has no numpy dtype: falls back to the separate copies (which raise like .cpu().numpy() would)
    with pytest.raises(TypeError):
        read_back(a, logp.bfloat16(), stacked=True)
    # default on the cpu: the separate copies (aliasing, as .cpu().numpy())
    a_h, = read_back(a)
    assert np.shares_memory(a_h, a.numpy())


def _strip(rows):
    return [{k: v for k, v in r.items() if not k.startswith("t_") and k not in TIMING} for r in rows]


def _weights(run):
    ck = sorted((run / "checkpoints").glob("*.pt"))[-1]
    st = torch.load(ck, map_location="cpu", weights_only=False)["model"]
    return {k: v for k, v in st.items()}


def _check_phase_sums(rows):
    for r in rows:
        assert all(k in r for k in PHASE_KEYS), sorted(r)
        s = sum(r[k] for k in PHASE_KEYS)
        # 2 % of t_infer_s, plus the rounding of six 3-decimal values
        assert abs(s - r["t_infer_s"]) <= 0.02 * r["t_infer_s"] + 0.003, (s, r["t_infer_s"])
        assert r["t_forward_s"] > 0 and all(r[k] >= 0 for k in PHASE_KEYS)


def test_train_phase_keys_and_stacked_readback_bit_identical(tmp_path, monkeypatch):
    """The phase keys are in every metrics row and sum to t_infer_s; a train() run whose readback goes through the
    stacked byte copy (the CUDA path, forced on the cpu) is bit-identical to one with the three separate copies:
    same weights, metrics (t_* aside) and episodes."""
    runs = {}
    for name, rb in (("separate", lambda *ts: tuple(t.cpu().numpy() for t in ts)),
                     ("stacked", functools.partial(read_back, stacked=True))):
        monkeypatch.setattr(ppo, "read_back", rb)
        run = tmp_path / name
        train(resolve_config({**RUN, "out": str(run)}), log=lambda m: None)
        runs[name] = (read_jsonl(run / "metrics.jsonl"), read_jsonl(run / "episodes.jsonl"), _weights(run))
    (ra, ea, wa), (rb_, eb, wb) = runs["separate"], runs["stacked"]
    assert _strip(ra) == _strip(rb_) and ea == eb
    assert wa.keys() == wb.keys() and all(torch.equal(wa[k], wb[k]) for k in wa)
    _check_phase_sums(ra + rb_)


def test_overlap_double_phase_keys(tmp_path):
    run = tmp_path / "double"
    train(resolve_config({**RUN, "out": str(run), "envs": 12, "overlap": "double"}), log=lambda m: None)
    _check_phase_sums(read_jsonl(run / "metrics.jsonl"))


# ------------------------------------------------------------------------------------------------ bench CLIs

def test_bench_rollout_cli_cpu(tmp_path, capsys):
    from sts2train.cli import main

    out = tmp_path / "rollout.json"
    t0 = time.monotonic()
    rc = main(["bench-rollout", "--d-model", "32", "--state-dim", "64", "--depth", "1", "--heads", "2",
               "--ff-mult", "2", "--device", "cpu", "--envs", "8,16", "--rollout-steps", "4", "--warmup", "1",
               "--repeats", "2", "--torch-threads", "2", "--profile", "--profile-steps", "2", "--json", str(out)])
    assert rc == 0 and time.monotonic() - t0 < 60
    text = capsys.readouterr().out
    lines = [ln for ln in text.splitlines() if ln.startswith("bench-rollout mode=")]
    assert len(lines) == 2 and "envs=8" in lines[0] and "envs=16" in lines[1] and "A100" in text
    res = json.loads(out.read_text())["results"]
    assert [r["envs"] for r in res] == [8, 16] and all(r["mode"] == "off" and r["rollout_steps"] == 4 for r in res)
    for r in res:
        assert set(r["phase_ms"]) == set(PHASE_KEYS) and r["step_ms"] > 0 and r["forward_ms"] > 0
        assert r["phase_sum_ms"] == pytest.approx(r["step_ms"], rel=0.05)
        assert r["profile"]["aten_ops_per_step"] > 0 and r["profile"]["launch_calls_per_step"] is None
        assert (tmp_path / f"rollout-off-n{r['envs']}.trace.json").exists() and r["trace"]


def test_bench_update_one_line_and_trace(tmp_path, capsys):
    from sts2train.cli import main

    out = tmp_path / "update.json"
    rc = main(["bench-update", "--d-model", "32", "--state-dim", "64", "--depth", "1", "--heads", "2",
               "--ff-mult", "2", "--device", "cpu", "--minibatch", "32", "--envs", "16", "--steps", "1",
               "--warmup", "1", "--infer-steps", "1", "--torch-threads", "2", "--profile", "--profile-steps", "1",
               "--top", "3", "--json", str(out), "--trace-dir", str(tmp_path / "tr")])
    assert rc == 0
    lines = [ln for ln in capsys.readouterr().out.splitlines() if ln.startswith("bench-update mode=")]
    assert len(lines) == 1 and "B=32" in lines[0] and "update_ms=" in lines[0]
    r = json.loads(out.read_text())["results"][0]
    assert (tmp_path / "tr" / "update-off-b32.trace.json").exists() and r["trace"]
