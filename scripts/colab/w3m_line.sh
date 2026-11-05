#!/bin/bash
# w3m_line.sh BUNDLE RUN_DIR LINE FORK_CKPT [OVERLAP]   (docs/w3m-overnight-plan-2026-11-04.md)
#
#   BUNDLE     unpacked sts2train-<sha> bundle dir with the 7aaa846a6a35 AOT library (room macros)
#   RUN_DIR    this line's run dir, e.g. /content/sts2/runs/w3m-a or /content/sts2/runs/w3m-b
#   LINE       a = main line M + room macros + entropy anneal; b = a + harvest (always --overlap off)
#   FORK_CKPT  the fork checkpoint, /content/sts2/runs/sp3-main/checkpoints/pinned-0003064.pt (may not exist yet:
#              the driver waits for it, and for M's trainer to exit, before the first session)
#   OVERLAP    line a only: off (default) or double (only after scripts/colab/w3m_preflight_overlap.sh passed)
#
# Idempotent and resumable, like sp2_serial.sh: the fork copy happens once (RUN_DIR/checkpoints/ckpt-0003064.pt,
# never overwritten); every segment is `run_train.sh` (--resume auto) to a fixed --max-updates and is skipped once
# status.json reaches it; every endpoint eval is skipped once its report.json exists (an interrupted one is redone).
# Re-run the same command after a VM restart (after gcs_pull.py restored RUN_DIR) and it continues.
#
# Env knobs: STS=/content/sts2 (gcs_sync.py + key live there), BUCKET, WAIT_RUN (the source run whose trainer must have
# exited first; default the FORK_CKPT's run dir), WAIT_MAX_S (default 14400), ENDS (segment ends, tests only),
# TRAIN / EV (stand-ins for tests), SYNC=0 (no GCS loop), EVALS=0 (no endpoint evals: the overlap preflight).
set -u
B=$1; RUN=$2; L=$3; FORK=$4; OVERLAP=${5:-off}
STS=${STS:-/content/sts2}; BUCKET=${BUCKET:-gomoku-zero-20260816-ai-sts2train}
TRAIN=${TRAIN:-"bash $B/run_train.sh"}; EV=${EV:-"python3 -m sts2train.cli"}
DEV=${DEV:-silent-a10-dev-v1}; AB=${AB:-silent-a10-ab-v1}
FORK_U=${FORK_U:-3064}; LAST=${LAST:-7664}
ENDS=${ENDS:-"3564 4064 4564 5064 5564 6064 6564 7064 7564 7664"}
WAIT_RUN=${WAIT_RUN:-$(dirname "$(dirname "$FORK")")}; WAIT_MAX_S=${WAIT_MAX_S:-14400}
log() { echo "$(date -u '+%F %T') w3m-$L: $*"; }
case "$L" in a|b) ;; *) log "LINE must be a or b"; exit 2 ;; esac
case "$OVERLAP" in off|double) ;; *) log "OVERLAP must be off or double"; exit 2 ;; esac
[ "$L" = b ] && [ "$OVERLAP" != off ] && { log "line b runs harvest, which requires --overlap off"; exit 2; }
EVAL_AT=$(echo $ENDS | tr ' ' ',')

# the v2 recipe (sp2_serial.sh FLAGS, SP1 seeds) as the main line M ran it (launch_sp3.sh: --cycle-cost 0.01,
# --keep-ckpts 60), plus the W3M changes: room macros, entropy anneal over the 4600 overnight updates, eval/pin every 100
FLAGS=(--start-mode stream --seed-stream-prefix SP1 --env-seed 1 --init-seed 1 --sample-seed 1
       --characters SILENT --ascension 10 --start-saves none
       --objective protocol --win-bonus 1.0 --acq-shaping --acq-deck-coef 0.003 --acq-relic-coef 0.01
       --acq-upgrade-coef 0.012
       --d-model 192 --state-dim 384 --depth 3 --heads 6 --ff-mult 4 --deck-counts --aux-heads --aux-coef 0.1
       --rollout 64 --epochs 1 --minibatch 4096 --lr 3e-4 --gamma 1.0 --lam 0.99 --clip 0.2 --vf-coef 0.5
       --ent-coef 0.02 --max-grad-norm 0.5 --max-rows 64 --step-cap 3000 --floor-step-cap 80
       --device cuda --amp --compile default
       --no-best --eval-at-seedset "$DEV" --eval-at-modes greedy-on,greedy-off
       --eval-every 100 --eval-seedset silent-a10-v1 --loop-breaker on
       --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 60 --max-hours 3 --log-every 1
       --cycle-cost 0.01
       --macros rest,shop
       --ent-coef-final 0.005 --ent-anneal-updates $((LAST - FORK_U)) --ent-anneal-start $FORK_U
       --eval-at-every 100 --pin-every 100 --eval-at "$EVAL_AT")
if [ "$L" = a ]; then
  FLAGS+=(--overlap "$OVERLAP")
else
  FLAGS+=(--overlap off --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25 --harvest-start-share 0.33)
fi

reached() {   # RUN_DIR N: status.json update >= N
  python3 - "$1" "$2" <<'PY'
import json, sys
from pathlib import Path
p = Path(sys.argv[1]) / "status.json"
sys.exit(0 if p.exists() and int(json.loads(p.read_text()).get("update") or 0) >= int(sys.argv[2]) else 1)
PY
}
busy() { pgrep -f -- "--out $WAIT_RUN( |\$)" > /dev/null; }   # the source run's trainer is still alive

# ---------------------------------------------------------------------------------------------------- the fork
mkdir -p "$RUN/checkpoints"
DST=$(printf '%s/checkpoints/ckpt-%07d.pt' "$RUN" "$FORK_U")
if ! ls "$RUN"/checkpoints/ckpt-*.pt > /dev/null 2>&1; then
  t0=$SECONDS
  until [ -f "$FORK" ] && ! busy; do
    (( SECONDS - t0 > WAIT_MAX_S )) && { log "no $FORK (or $WAIT_RUN still training) after $WAIT_MAX_S s: stop"; exit 1; }
    log "waiting for $FORK and for the trainer of $WAIT_RUN to exit"; sleep 60
  done
  python3 - "$FORK" "$FORK_U" <<'PY' || { log "fork checkpoint check failed"; exit 1; }
import sys, torch
ck = torch.load(sys.argv[1], map_location="cpu", weights_only=False)
u = int(ck.get("updates") or 0)
print(f"fork checkpoint {sys.argv[1]}: updates {u}, weights_hash {ck.get('weights_hash')}, macros {(ck.get('config') or {}).get('macros')!r}")
sys.exit(0 if u == int(sys.argv[2]) else 1)
PY
  cp "$FORK" "$DST.tmp" && mv "$DST.tmp" "$DST" || exit 1
  sha256sum "$FORK" "$DST" | tee "$RUN/fork.sha256"
  log "forked $RUN from $FORK (u$FORK_U)"
fi

# ------------------------------------------------------------------------------------------------ GCS mirror
if [ "${SYNC:-1}" = 1 ] && ! pgrep -f -- "gcs_sync.py.*--run $RUN( |\$)" > /dev/null; then
  setsid nohup python3 "$STS/gcs_sync.py" --key "$STS/gcs-sts2-ckpt.json" --bucket "$BUCKET" --prefix runs \
    --run "$RUN" --interval 60 --min-age 60 >> "$RUN.gcs_sync.log" 2>&1 < /dev/null &
  log "started gcs_sync for $RUN (log $RUN.gcs_sync.log)"
fi

export STS2VEC_AOT_LIB="$B/lib/Sts2Sim.Vec.so" STS2VEC_SEEDSETS="$B/seedsets"
run_eval() {   # OUT DONE_FILE eval-args...: skip a finished evaluation, redo an interrupted one from scratch
  local out=$1 done=$2; shift 2
  [ -f "$out/$done" ] && return 0
  rm -rf "$out"; log "eval -> $out"; $EV eval --out "$out" --threads 12 --device cuda "$@"
}

# ------------------------------------------------------------------------------------------------ segments
for END in $ENDS; do
  tries=0
  until reached "$RUN" "$END"; do
    tries=$((tries + 1))
    if [ $tries -gt 4 ]; then log "$RUN is not at update $END after 4 sessions: stop"; exit 1; fi
    log "train $RUN to update $END (session $tries, overlap $OVERLAP)"
    $TRAIN "$RUN" 12 2048 "${FLAGS[@]}" --max-updates "$END" || { log "run_train.sh failed (rc $?)"; exit 1; }
  done
  log "$RUN at update $END"
  CK=$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$END")
  [ -f "$CK" ] || { log "missing $CK (--eval-at pins it)"; exit 1; }
  [ "${EVALS:-1}" = 1 ] || continue
  # confirmatory ab-512 greedy-off of the segment's pinned checkpoint; under evals/ so gcs_sync mirrors it
  run_eval "$(printf '%s/evals/endpoint/u%07d-ab512-greedy-off' "$RUN" "$END")" report.json --seedset "$AB" \
    --checkpoint "$CK" --policy greedy --breaker off --macros rest,shop || log "ab-512 eval at u$END failed (continuing)"
done
[ "${EVALS:-1}" = 1 ] || { log "done (EVALS=0)"; exit 0; }
CK=$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$LAST")
run_eval "$(printf '%s/evals/endpoint/u%07d-ab512-greedy-on' "$RUN" "$LAST")" report.json --seedset "$AB" \
  --checkpoint "$CK" --policy greedy --breaker on --macros rest,shop
run_eval "$(printf '%s/evals/endpoint/u%07d-dev128-sample-off' "$RUN" "$LAST")" repeats.json --seedset "$DEV" \
  --checkpoint "$CK" --policy sample --breaker off --sample-seed 1 --repeats 3 --macros rest,shop
log "done"
