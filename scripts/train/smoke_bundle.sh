#!/usr/bin/env bash
# Validate an sts2train Colab bundle on a linux x86_64 CPU box with plain python and no .NET: install CPU torch if the
# interpreter has none, then run_train.sh (pip install, `sts2vec check`, a short CPU training run with checkpoints and
# a protocol eval on std-a0-v1), then check the run directory and print the throughput.
#
#   bash smoke_bundle.sh [RUN_DIR] [MINUTES] [THREADS]        (defaults: ./smoke-run, 2, nproc)
#   docker run --rm -v "$PWD:/w" -w /w python:3.13 bash sts2train-<sha>/smoke_bundle.sh /w/smoke-run 2
#
# It installs into the current interpreter: run it in a throwaway container or venv. Exit 0 = the bundle works.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="${1:-$PWD/smoke-run}"
MINUTES="${2:-2}"
THREADS="${3:-$(nproc)}"
PY="${PYTHON:-python3}"
export PIP_DISABLE_PIP_VERSION_CHECK=1 PIP_ROOT_USER_ACTION=ignore
if [[ "$(uname -s)-$(uname -m)" != Linux-x86_64 ]]; then
  echo "smoke_bundle.sh: the bundled library is linux-x86_64 (this is $(uname -s)-$(uname -m))" >&2
  exit 2
fi
if command -v dotnet > /dev/null; then echo "note: dotnet is on PATH here; the bundle does not use it"; fi
if ! "$PY" -c "import torch" 2> /dev/null; then
  echo "== installing CPU torch (download.pytorch.org/whl/cpu)"
  "$PY" -m pip install --quiet torch --index-url https://download.pytorch.org/whl/cpu
fi
rm -rf "$RUN_DIR"
HOURS="$("$PY" -c "print(float('$MINUTES') / 60)")"
T0=$SECONDS
bash "$HERE/run_train.sh" "$RUN_DIR" "$THREADS" 256 --device cpu --preset small --max-hours "$HOURS" \
  --rollout 32 --minibatch 2048 --ckpt-every 2 --ckpt-every-min 1 --eval-every 4 --eval-seedset std-a0-v1 --log-every 1
echo "== run_train.sh finished in $((SECONDS - T0)) s; checking $RUN_DIR"
"$PY" - "$RUN_DIR" <<'PY'
import json, sys
from pathlib import Path
run = Path(sys.argv[1])
check = (run / "sts2vec-check.txt").read_text()
assert "check: OK" in check, "sts2vec check did not pass"
st = json.loads((run / "status.json").read_text())
assert st["state"] == "finished", st["state"]
ck = sorted((run / "checkpoints").glob("ckpt-*.pt"))
assert ck and st["last_checkpoint"], "no checkpoint"
rows = [json.loads(l) for l in open(run / "metrics.jsonl")]
evals = [json.loads(l) for l in open(run / "evals.jsonl")]
assert rows and evals and all("error" not in e for e in evals), evals
assert all(e["replayable"] == e["games"] for e in evals)
summ = json.loads((run / "summary.json").read_text())
lib = json.loads((run / "config.json").read_text())["library"]
print(f"library: {lib['mode']} {lib['version']} ({lib['path']})")
print(f"updates {st['update']}, env steps {st['env_steps']:,}, episodes {st['episodes']:,}, "
      f"env-steps/s {summ['env_steps_per_s_session']:,.0f} (env only {summ['env_only_steps_per_s']:,.0f}), "
      f"time split {summ['time_split_s']}")
print(f"checkpoints: {[p.name for p in ck]} + best.pt={(run / 'checkpoints' / 'best.pt').exists()}; "
      f"metrics rows {len(rows)}; evals " + ", ".join(f"u{e['update']}: progress {e['progress_mean']:.3f} "
                                                      f"({e['games']} games, {e['seconds']} s)" for e in evals))
print("smoke_bundle: OK")
PY
echo "== raw env throughput on this box (sts2vec bench, random policy, NativeAOT library, 1024 envs x $THREADS threads)"
STS2VEC_AOT_LIB="$HERE/lib/Sts2Sim.Vec.so" "$PY" -m sts2vec.cli bench --envs 1024 --threads "$THREADS" --steps 500 \
  --pool 1024 || echo "(bench failed; the training smoke above passed)"
