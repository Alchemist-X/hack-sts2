#!/usr/bin/env bash
# sts2train on a box without .NET (the Google Colab A100 runtime): pip-install the bundle's wheels into the current
# python, point sts2vec at the bundled linux-x64 NativeAOT library, run `sts2vec check`, then train. No uv needed.
#
#   bash run_train.sh RUN_DIR [THREADS] [ENVS] [sts2train train flags...]
#     THREADS  env worker threads (default 12: the Colab A100 runtime's vCPUs)
#     ENVS     env slots (default: the trainer's 1024, or the resumed checkpoint's value)
#   bash run_train.sh /content/drive/MyDrive/sts2/run1 12 2048 --preset large --device cuda --max-hours 6 \
#       --eval-every 50 --ckpt-every 50 --ckpt-every-min 15
#
# Always passes --resume auto (newest checkpoint under RUN_DIR; a fresh run if there is none), so re-running the same
# command after a disconnect continues the run; flags given here override the checkpoint's. RUN_DIR gets the trainer's
# files (status.json, metrics.jsonl, checkpoints/, evals/, train.log) plus sts2vec-check.txt.
# Environment: PYTHON (default python3), SKIP_INSTALL=1 (wheels already installed), SKIP_CHECK=1.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -lt 1 || "$1" == -h || "$1" == --help ]]; then sed -n '2,16p' "$0"; exit 2; fi
RUN_DIR="$1"; shift
THREADS=12
ENVS=""
if [[ $# -ge 1 && "$1" =~ ^[0-9]+$ ]]; then THREADS="$1"; shift; fi
if [[ $# -ge 1 && "$1" =~ ^[0-9]+$ ]]; then ENVS="$1"; shift; fi
PY="${PYTHON:-python3}"
export PIP_DISABLE_PIP_VERSION_CHECK=1 PIP_ROOT_USER_ACTION=ignore

if [[ "${SKIP_INSTALL:-0}" != 1 ]]; then
  echo "== pip install (requirements-colab.txt, then the bundle wheels with --no-deps)"
  "$PY" -m pip install --quiet -r "$HERE/requirements-colab.txt"
  "$PY" -m pip install --quiet --no-deps --force-reinstall "$HERE"/wheels/sts2vec-*.whl \
    "$HERE"/wheels/sts2train_smoke-*.whl
fi
"$PY" - <<'PY' || { echo "torch / sts2train not importable in $PY (Colab preinstalls torch; elsewhere install it first)" >&2; exit 1; }
import platform, numpy, torch, sts2vec, sts2train
cuda = torch.cuda.get_device_name(0) if torch.cuda.is_available() else "no CUDA"
print(f"python {platform.python_version()} | torch {torch.__version__} ({cuda}) | numpy {numpy.__version__} | "
      f"sts2train {sts2train.__version__}")
PY

export STS2VEC_AOT_LIB="$HERE/lib/Sts2Sim.Vec.so"     # sts2vec loads the NativeAOT library: no CLR, no dotnet
export STS2VEC_SEEDSETS="$HERE/seedsets"               # protocol seed sets for the periodic eval (std-a0-v1, ...)
export STS2TRAIN_BUNDLE="$(basename "$HERE")"           # recorded in RUN_DIR/config.json
mkdir -p "$RUN_DIR"
if [[ "${SKIP_CHECK:-0}" != 1 ]]; then
  echo "== sts2vec check (library $STS2VEC_AOT_LIB)"
  if ! "$PY" -m sts2vec.cli check --threads-b "$THREADS" > "$RUN_DIR/sts2vec-check.txt" 2>&1; then
    cat "$RUN_DIR/sts2vec-check.txt"
    echo "sts2vec check FAILED (see $RUN_DIR/sts2vec-check.txt)" >&2
    exit 1
  fi
  cat "$RUN_DIR/sts2vec-check.txt"
fi

ARGS=(--out "$RUN_DIR" --resume auto --threads "$THREADS")
if [[ -n "$ENVS" ]]; then ARGS+=(--envs "$ENVS"); fi
echo "== $PY -m sts2train.cli train ${ARGS[*]} $*"
exec "$PY" -u -m sts2train.cli train "${ARGS[@]}" "$@"
