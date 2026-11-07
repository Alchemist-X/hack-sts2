"""scripts/analysis/bench_report.py (stdlib reader of the throughput bench) on synthetic result trees: bench-update
JSON (one file per mode and one file with all modes), a bench-rollout JSON with phase splits, the overlap preflight's
bench.json + decision.txt. Asserts the per-update projections, speed-ups, launches column, the check exclusion and the
compile / overlap recommendation lines."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[4] / "scripts" / "analysis" / "bench_report.py"
_spec = importlib.util.spec_from_file_location("bench_report", SCRIPT)
br = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(br)


def mode_row(mode, update_ms, step_ms, *, ok=True, peak=9000.0, kernels=900, mb=4096):
    r = {"mode": mode, "label": f"compile={mode}", "minibatch": mb, "max_rows": 64, "update_ms": update_ms,
         "rollout_step_ms": step_ms, "infer_forward_ms": step_ms / 3, "gpu_peak_mb": peak,
         "profile": {"kernels_per_minibatch": kernels, "kernel_ms_per_minibatch": update_ms * 0.8}}
    if mode != "off":
        r["check"] = {"ok": ok, "loss_rel": 1e-3}
        r["check_forward"] = {"ok": True, "logits_rel": 1e-3}
        r["compile_s"] = 60.0
    return r


def write(p: Path, doc) -> Path:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(doc) if not isinstance(doc, str) else doc)
    return p


def overlap(root: Path, r_inf=1.2, tot=1.25, r_env=1.1, verdict="double"):
    pf = root / "evals" / "preflight"
    write(pf / "bench.json", {"off": {"t_total_s": 12.0}, "double": {"t_total_s": 9.6},
                              "measured": {"infer_ratio": r_inf, "env_ratio": r_env, "total_speedup": tot,
                                           "rollout_speedup": 1.3},
                              "a100_projection": {"speedup": 1.3}})
    if verdict:
        write(pf / "decision.txt", f"r_inf {r_inf} (<= 1.4), ...\nOVERLAP={verdict}\n")


def run(tmp_path, capsys, *args):
    rc = br.main([str(tmp_path), *args])
    return rc, capsys.readouterr().out


def test_per_mode_files_table_and_adopt(tmp_path, capsys):
    ev = tmp_path / "evals"
    # reduce-overhead: update 80 vs default 100 ms, step 20 vs default 36 ms -> adopt reduce-overhead everywhere
    for m, u, s, k in (("off", 120.0, 40.0, 1500), ("default", 100.0, 36.0, 700), ("reduce-overhead", 80.0, 20.0, 700)):
        write(ev / f"bench-update-{m}.json", {"torch": "2.11.0+cu130", "device": "cuda:0",
                                              "results": [mode_row(m, u, s, kernels=k)]})
    overlap(tmp_path)
    js = tmp_path / "out.json"
    rc, out = run(tmp_path, capsys, "--json", str(js))
    assert rc == 0
    doc = json.loads(js.read_text())
    rows = {r["mode"]: r for r in doc["rows"]}
    assert [r["mode"] for r in doc["rows"]] == ["off", "default", "reduce-overhead"]
    # 2048 x 64 / 4096 = 32 minibatches per update; 64 rollout steps
    assert rows["default"]["minibatches_per_update"] == 32
    assert rows["default"]["update_s"] == pytest.approx(3.2)
    assert rows["default"]["infer_s"] == pytest.approx(36.0 * 64 / 1000)
    assert rows["reduce-overhead"]["x_update"] == pytest.approx(1.5)
    assert rows["reduce-overhead"]["x_step"] == pytest.approx(2.0)
    assert rows["off"]["x_gpu"] == pytest.approx(1.0)
    assert rows["reduce-overhead"]["launches"] == 700 and rows["off"]["checks_ok"] is None
    rec = doc["recommendation"]
    assert rec["adopt"] and rec["compile_update"] == rec["compile_infer"] == "reduce-overhead"
    # live 12.9 = env 5.3 + infer 2.4 + update 3.2 + other 2.0 -> 5.3 + 2.4 * 20/36 + 3.2 * 0.8 + 2.0
    assert rec["projected_s"] == pytest.approx(5.3 + 2.4 * 20 / 36 + 3.2 * 0.8 + 2.0, abs=0.01)
    assert "compile: ADOPT --compile reduce-overhead" in out
    assert "overlap: WORTH IT" in out and "OVERLAP=double" in out
    assert "harvest" in out                     # the C-lineage caveat
    assert "mode" in out and "peak MB" in out and "launches" in out


def test_split_recommendation_and_mismatch_excluded(tmp_path, capsys):
    # one file with all modes: reduce-overhead wins the rollout step only; max-autotune's update is fastest but its
    # compiled-vs-eager check fails -> excluded
    write(tmp_path / "evals" / "bench-update.json", {"results": [
        mode_row("off", 120.0, 40.0), mode_row("default", 100.0, 36.0), mode_row("reduce-overhead", 104.0, 18.0),
        mode_row("max-autotune", 60.0, 36.0, ok=False)]})
    rc, out = run(tmp_path, capsys, "--json", "-")
    assert rc == 0
    rec = json.loads(out[out.index("{\n"):])["recommendation"]
    assert rec["compile_update"] == "default" and rec["compile_infer"] == "reduce-overhead"
    assert "--compile-update default --compile-infer reduce-overhead" in out
    assert "MISMATCH" in out and "excluded" in out and "max-autotune" in out
    assert "overlap: no preflight data" in out


def test_keep_when_gain_small_and_overlap_fails(tmp_path, capsys):
    write(tmp_path / "evals" / "bench-update.json", {"results": [
        mode_row("off", 120.0, 40.0), mode_row("default", 100.0, 36.0), mode_row("reduce-overhead", 99.0, 35.0)]})
    overlap(tmp_path, r_inf=1.7, tot=0.95, verdict="off")
    rc, out = run(tmp_path, capsys)
    assert rc == 0
    assert "compile: KEEP --compile default" in out
    assert "overlap: NOT worth it" in out


def test_overlap_numbers_pass_but_compile_check_failed(tmp_path, capsys):
    overlap(tmp_path, r_inf=1.1, tot=1.3, verdict="off")
    rc, out = run(tmp_path, capsys)
    assert rc == 0
    assert "overlap: NOT worth it" in out and "compile check did not" in out
    assert "compile: not enough bench data" in out


def test_rollout_file_overrides_step_and_prints_phases(tmp_path, capsys):
    # bench-update rows plus a wave-21 bench-rollout file (per mode files; a 4096-env row that must be ignored)
    write(tmp_path / "evals" / "bench-update.json", {"results": [mode_row("off", 120.0, 40.0),
                                                                 mode_row("default", 100.0, 36.0)]})

    def rrow(mode, envs, step, window, h2d, fwd, launches, idle):
        return {"mode": mode, "envs": envs, "rollout_steps": 64, "step_ms": step, "window_s": window,
                "forward_ms": fwd, "phase_ms": {"t_h2d_s": h2d, "t_forward_s": fwd, "t_store_s": 1.0,
                                                "t_sample_d2h_s": 2.0, "t_track_s": 0.5},
                "profile": {"launch_calls_per_step": launches, "device_ops_per_step": 400.0},
                "gpu_idle_frac": idle, "gpu_peak_mb": 3000.0}
    write(tmp_path / "evals" / "bench-rollout-off.json", {"results": [rrow("off", 2048, 42.0, 2.73, 9.0, 20.0, 900, 0.6),
                                                                      rrow("off", 4096, 80.0, 5.2, 18.0, 40.0, 900, 0.4)]})
    write(tmp_path / "evals" / "bench-rollout-default.json", {"results": [
        rrow("default", 2048, 30.0, 1.95, 9.0, 12.0, 310, 0.5)]})
    rc, out = run(tmp_path, capsys, "--json", str(tmp_path / "r.json"))
    assert rc == 0
    rows = {r["mode"]: r for r in json.loads((tmp_path / "r.json").read_text())["rows"]}
    assert rows["off"]["step_ms"] == 42.0 and rows["default"]["step_ms"] == 30.0     # the 4096 row is skipped
    assert rows["off"]["infer_s"] == pytest.approx(2.73)                               # window_s, not step x 64
    assert rows["default"]["rollout_launches"] == 310 and rows["default"]["idle_step"] == 0.5
    assert rows["off"]["forward_ms"] == 20.0
    assert "rollout phases" in out and "h2d 9.00" in out and "forward 12.00" in out and "sample_d2h 2.00" in out
    assert "/310" in out and "50%" in out


def test_no_data(tmp_path, capsys):
    write(tmp_path / "evals" / "other.json", {"x": 1})
    rc, out = run(tmp_path, capsys)
    assert rc == 2 and "no bench data" in out
