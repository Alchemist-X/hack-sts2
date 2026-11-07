import json
import os
import subprocess
import sys

from sts2vec.cli import main


def test_bench_smoke(capsys):
    assert main(["bench", "--envs", "8", "--threads", "2", "--steps", "3", "--warmup", "1", "--pool", "4"]) == 0
    out = capsys.readouterr().out
    assert "env-steps/s" in out and "ms/batch" in out and "MB obs/batch" in out and "resets/s" in out
    assert "GC server" in out or "GC unknown" in out
    assert "status counts" in out


def test_bench_json(capsys):
    assert main(["bench", "--envs", "4", "--threads", "1", "--steps", "2", "--warmup", "0", "--pool", "2", "--json"]) == 0
    r = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    for key in ("env_steps_per_s", "ms_per_batch", "obs_mb_per_batch", "gc", "resets_per_s", "status_counts", "pool"):
        assert key in r
    assert r["envs"] == 4 and r["steps"] == 2 and r["pool"] == 2
    assert sum(r["status_counts"].values()) >= 4 * 2


def test_bench_warm_flag(capsys):
    # default: the pool's fresh templates are created before timing and the warm time is reported separately
    args = ["bench", "--envs", "4", "--threads", "2", "--steps", "2", "--warmup", "0", "--pool", "3"]
    assert main(args + ["--json"]) == 0
    r = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert r["warm"] is True and r["warmed_templates"] == 3 and r["warm_ms"] > 0
    assert main(args + ["--no-warm", "--json"]) == 0
    r = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert r["warm"] is False and r["warmed_templates"] == 0 and r["warm_ms"] == 0
    assert main(args) == 0
    assert "pool warm: 3 templates in " in capsys.readouterr().out
    assert main(args + ["--no-warm"]) == 0
    assert "pool warm: off" in capsys.readouterr().out


def test_bench_branch_smoke(capsys):
    args = ["bench-branch", "--envs", "20", "--threads", "2", "--k", "4", "--rounds", "1", "--warmup-rounds", "1",
            "--advance", "3", "--pool", "4"]
    assert main(args + ["--json"]) == 0
    r = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert r["roots"] == 4 and r["branched_slots_per_round"] == 16 and r["abi_version"] == 2
    for key in ("branch_exact", "branch_det", "many_exact", "many_det", "old_exact", "old_det", "restore_many",
                "restore_loop", "reset_mask"):
        assert r["us_per_slot"][key] > 0, key
    assert r["old_path_entries_created"] == 2 * (4 + 16) and r["sim_error_or_content_gap_slots"] == 0
    assert main(args) == 0
    out = capsys.readouterr().out
    assert "branch, one call for all roots" in out and "restore_many" in out


def test_check_smoke(capsys):
    assert main(["check", "--envs", "4", "--steps", "5", "--pool", "2"]) == 0
    out = capsys.readouterr().out
    assert "schema/buffers: consistent" in out
    assert "state hashes equal" in out and "byte-identical every step" in out
    assert "check: OK" in out
    assert "ABI 2 | exports 21" in out


def test_digest_json(capsys):
    assert main(["digest", "--envs", "4", "--steps", "3", "--pool", "2", "--threads", "2"]) == 0
    d = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert len(d["state_hash_sha256"]) == 64 and len(d["obs_sha256"]) == 64
    assert d["nonzero_hashes"] == 4


def test_console_entry_point_in_subprocess():
    r = subprocess.run([sys.executable, "-m", "sts2vec.cli", "bench", "--envs", "4", "--threads", "2", "--steps", "2",
                        "--pool", "2"], capture_output=True, text=True, timeout=300, env=dict(os.environ))
    assert r.returncode == 0, r.stdout + r.stderr
    assert "env-steps/s" in r.stdout
