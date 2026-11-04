#!/usr/bin/env bash
# Validate an sts2train Colab bundle on a linux x86_64 CPU box with plain python and no .NET: install CPU torch if the
# interpreter has none, then run_train.sh (pip install, `sts2vec check`, a short CPU training run with checkpoints,
# --floor-step-cap 80 and a protocol eval on std-a0-v1), then check the run directory (the cap in effect, the encoder
# version), check that the bundled library fills the appended observation buffers (event_option_values is resolved by
# reflection, which a NativeAOT publish could trim), and print the throughput.
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
  --rollout 32 --minibatch 2048 --ckpt-every 2 --ckpt-every-min 1 --eval-every 4 --eval-seedset std-a0-v1 \
  --log-every 1 --floor-step-cap 80
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
conf = json.loads((run / "config.json").read_text())
lib = conf["library"]
assert conf["config"]["floor_step_cap"] == 80 and conf.get("env", {}).get("floor_step_cap") == 80, \
    f"floor_step_cap not in effect: config {conf['config'].get('floor_step_cap')}, env {conf.get('env')}"
print(f"library: {lib['mode']} {lib['version']} ({lib['path']}); encoder_version {conf['encoder_version']}")
print(f"floor_step_cap in effect: {conf['env']['floor_step_cap']} (config.json env; FLOOR_STALL truncations "
      f"{summ.get('floor_stall')}, sim errors {summ['sim_error']}, content gaps {summ['content_gap']})")
print(f"updates {st['update']}, env steps {st['env_steps']:,}, episodes {st['episodes']:,}, "
      f"env-steps/s {summ['env_steps_per_s_session']:,.0f} (env only {summ['env_only_steps_per_s']:,.0f}), "
      f"time split {summ['time_split_s']}")
print(f"checkpoints: {[p.name for p in ck]} + best.pt={(run / 'checkpoints' / 'best.pt').exists()}; "
      f"metrics rows {len(rows)}; evals " + ", ".join(f"u{e['update']}: progress {e['progress_mean']:.3f} "
                                                      f"({e['games']} games, {e['seconds']} s)" for e in evals))
print("smoke_bundle: OK")
PY
echo "== appended observation buffers under the bundled library (256 envs x 400 random steps)"
STS2VEC_AOT_LIB="$HERE/lib/Sts2Sim.Vec.so" "$PY" - "$THREADS" <<'PY'
# The six buffers appended after last_episode (sim/ENV_SPEC.md §5, 35..40) must exist with their documented shapes and
# be filled. event_option_values resolves each option's placeholders by reflection over the event models
# (Sts2Sim.Vec/EventOptionVars.cs); a NativeAOT publish that trimmed those members would leave it all zero.
import sys
import numpy as np
from sts2vec import PoolSpec, VecEnv, random_actions
SHAPES = {"global_x": (2,), "hand_x": (10, 2), "draw_marked": (32, 6), "discard_order": (64, 6),
          "exhaust_order": (64, 6), "event_option_values": (8, 9)}
pool = PoolSpec(count=256, seed_prefix="APPENDCHK")
with VecEnv(256, threads=int(sys.argv[1]), seed_text="APPENDCHK", pool=pool) as env:
    S = env.schema
    for name, shape in SHAPES.items():
        assert tuple(S[name].shape) == shape and env.buffers[name].dtype == np.int32, (name, S[name].shape)
    E, X, G = S.cols("event_option_values"), S.cols("global_x"), S.cols("global_i")
    rng = np.random.default_rng(0)
    obs = env.reset()
    n = dict(event_option_rows=0, with_numbers=0, with_items=0, numbers_nonzero=0, ancient_shown=0,
             discard_rows=0, exhaust_rows=0, draw_marked_rows=0, hand_x_nonzero=0)
    for _ in range(400):
        ev = obs["event_option_values"]
        n["event_option_rows"] += int((np.arange(ev.shape[1]) < obs["global_i"][:, G.event_option_n, None]).sum())
        n["with_numbers"] += int((ev[:, :, E.value_n] > 0).sum())
        n["with_items"] += int((ev[:, :, E.item_n] > 0).sum())
        n["numbers_nonzero"] += int((ev[:, :, [E.value_0, E.value_1, E.value_2]] != 0).sum())
        n["ancient_shown"] += int((obs["global_x"][:, X.ancient_id] > 1).sum())
        n["discard_rows"] += int((obs["discard_order"][:, :, 0] > 1).sum())
        n["exhaust_rows"] += int((obs["exhaust_order"][:, :, 0] > 1).sum())
        n["draw_marked_rows"] += int((obs["draw_marked"][:, :, 0] > 1).sum())
        n["hand_x_nonzero"] += int(obs["hand_x"].any(axis=2).sum())
        obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
    print(f"   {env.lib.mode} {env.lib.version()}: " + ", ".join(f"{k} {v:,}" for k, v in n.items()))
missing = [k for k in ("with_numbers", "with_items", "numbers_nonzero", "ancient_shown", "discard_rows") if not n[k]]
assert not missing, f"appended buffers not filled under this library: {missing}"
print("appended buffers: OK")
PY
echo "== raw env throughput on this box (sts2vec bench, random policy, NativeAOT library, 1024 envs x $THREADS threads)"
STS2VEC_AOT_LIB="$HERE/lib/Sts2Sim.Vec.so" "$PY" -m sts2vec.cli bench --envs 1024 --threads "$THREADS" --steps 500 \
  --pool 1024 || echo "(bench failed; the training smoke above passed)"
