#!/bin/bash
# w3m_line_v2.sh BUNDLE RUN_DIR LINE FORK_CKPT [OVERLAP]   (scripts/colab/lab/specs/README.md; normally started by line_agent.py)
#
# v2 = a copy of scripts/colab/w3m_line.sh (2026-11-06, audit item OPS2) with exactly four changes, each marked "v2 (n)":
#   (1) STOP: a file RUN_DIR/STOP is checked before every training session and before the final evals; when present the
#       driver logs "... STOP ...: killed" and exits 3 (line_agent.py maps rc 3 to state 'killed').
#   (2) the compile-gate fallback (--compile off) applies to the current segment only; the next segment runs with the
#       line's --compile flag again (v1 appended it to FLAGS for the rest of the driver process).
#   (3) before 'done': one more pass over the endpoint evals (run_eval skips finished ones, so this only redoes a missing
#       or interrupted one), then, with SYNC=1, a final `gcs_sync.py --once`, then every expected endpoint report must
#       exist locally and (SYNC=1) in the bucket; otherwise "endpoint reports incomplete: stop" and exit 4.
#   (4) EVAL_PLAN (env; from the line spec's eval_plan): legacy (default: byte-identical trainer flags to v1) |
#       legacy+dev512 (legacy, but the periodic --eval-every 100 eval runs on DEV512 instead of silent-a10-v1) |
#       dev512 (audit EVAL-C: --eval-every 0, per-100 --eval-at on DEV512 greedy-off only; each segment end also runs a
#       dev-128 greedy-off endpoint eval so the line stays comparable with legacy lines). DEV512 default
#       silent-a10-dev-s512, which must be in the bundle's seedsets (a bundle built after EVAL-C).
#   Exit codes: 0 done, 1 failure (as v1), 2 bad arguments (as v1), 3 STOP (v2), 4 endpoint reports incomplete (v2).
#   Everything else (fork, waits, retries, log lines, 'done') is unchanged, apart from a -h/--help usage path;
#   the v1 header follows.
#
# v1: w3m_line.sh BUNDLE RUN_DIR LINE FORK_CKPT [OVERLAP]   (docs/w3m-overnight-plan-2026-11-04.md)
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
# MACROS (default rest,shop; none = the stop-and-revert lines A0 / B0 of the plan §5.1), ENT0 / ENT1 (entropy
# anneal endpoints, default 0.02 -> 0.005; a continuation line sets both to the value it continues at), TAG (log label), EXTRA (extra trainer flags),
# TRAIN / EV (stand-ins for tests), SYNC=0 (no GCS loop), EVALS=0 (no endpoint evals: the overlap preflight).
set -u
case "${1:-}" in   # usage (not a behavioural change): the header above
  -h|--help) sed -n '2,/^set -u$/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit 0 ;;
  '') sed -n '2,/^set -u$/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//' >&2; exit 2 ;;
esac
B=$1; RUN=$2; L=$3; FORK=$4; OVERLAP=${5:-off}
STS=${STS:-/content/sts2}; BUCKET=${BUCKET:-gomoku-zero-20260816-ai-sts2train}
TRAIN=${TRAIN:-"bash $B/run_train.sh"}; EV=${EV:-"python3 -m sts2train.cli"}
DEV=${DEV:-silent-a10-dev-v1}; AB=${AB:-silent-a10-ab-v1}
DEV512=${DEV512:-silent-a10-dev-s512}; EVAL_PLAN=${EVAL_PLAN:-legacy}   # v2 (4)
FORK_U=${FORK_U:-3064}; LAST=${LAST:-7664}; MACROS=${MACROS:-rest,shop}
ENT0=${ENT0:-0.02}; ENT1=${ENT1:-0.005}   # entropy anneal ENT0 -> ENT1 over the line (ENT0 = ENT1: constant)
EXTRA=${EXTRA:-}   # extra trainer flags appended to FLAGS (word-split), e.g. EXTRA="--hp-shaping-coef 0.1"
ENDS=${ENDS:-"3564 4064 4564 5064 5564 6064 6564 7064 7564 7664"}
WAIT_RUN=${WAIT_RUN:-$(dirname "$(dirname "$FORK")")}; WAIT_MAX_S=${WAIT_MAX_S:-14400}
log() { echo "$(date -u '+%F %T') w3m-${TAG:-$L}: $*"; }   # TAG: log label of a continuation line (default the line letter)
case "$L" in a|b) ;; *) log "LINE must be a or b"; exit 2 ;; esac
case "$OVERLAP" in off|double) ;; *) log "OVERLAP must be off or double"; exit 2 ;; esac
[ "$L" = b ] && [ "$OVERLAP" != off ] && { log "line b runs harvest, which requires --overlap off"; exit 2; }
EVAL_AT=$(echo $ENDS | tr ' ' ',')
case "$EVAL_PLAN" in   # v2 (4): the trainer's eval flags of this line (legacy = v1's, byte for byte)
  legacy)        EVF=(--eval-at-seedset "$DEV" --eval-at-modes greedy-on,greedy-off --eval-every 100 --eval-seedset silent-a10-v1) ;;
  legacy+dev512) EVF=(--eval-at-seedset "$DEV" --eval-at-modes greedy-on,greedy-off --eval-every 100 --eval-seedset "$DEV512") ;;
  dev512)        EVF=(--eval-at-seedset "$DEV512" --eval-at-modes greedy-off --eval-every 0) ;;
  *) log "EVAL_PLAN must be legacy, legacy+dev512 or dev512"; exit 2 ;;
esac

# the v2 recipe (sp2_serial.sh FLAGS, SP1 seeds) as the main line M ran it (launch_sp3.sh: --cycle-cost 0.01,
# --keep-ckpts 60), plus the W3M changes: room macros, entropy anneal over the 4600 overnight updates, eval/pin every 100
FLAGS=(--start-mode stream --seed-stream-prefix SP1 --env-seed 1 --init-seed 1 --sample-seed 1
       --characters SILENT --ascension 10 --start-saves none
       --objective protocol --win-bonus 1.0 --acq-shaping --acq-deck-coef 0.003 --acq-relic-coef 0.01
       --acq-upgrade-coef 0.012
       --d-model 192 --state-dim 384 --depth 3 --heads 6 --ff-mult 4 --deck-counts --aux-heads --aux-coef 0.1
       --rollout 64 --epochs 1 --minibatch 4096 --lr 3e-4 --gamma 1.0 --lam 0.99 --clip 0.2 --vf-coef 0.5
       --ent-coef "$ENT0" --max-grad-norm 0.5 --max-rows 64 --step-cap 3000 --floor-step-cap 80
       --device cuda --amp --compile default
       --no-best "${EVF[@]}" --loop-breaker on
       --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 60 --max-hours 3 --log-every 1
       --cycle-cost 0.01
       --macros "$MACROS"
       --ent-coef-final "$ENT1" --ent-anneal-updates $((LAST - FORK_U)) --ent-anneal-start $FORK_U
       --eval-at-every 100 --pin-every 100 --eval-at "$EVAL_AT")
[ -n "$EXTRA" ] && FLAGS+=($EXTRA)
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
stop_check() {   # v2 (1): RUN_DIR/STOP -> exit 3 (between sessions only; a running session is never interrupted)
  [ -f "$RUN/STOP" ] || return 0
  log "STOP file $RUN/STOP present ($1): killed"; exit 3
}

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
seg_evals() {   # END: one segment end's endpoint evals (v1's loop body; v2 (4) adds dev-128 greedy-off under dev512)
  local END=$1 CK
  CK=$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$END")
  # confirmatory ab-512 greedy-off of the segment's pinned checkpoint; under evals/ so gcs_sync mirrors it
  run_eval "$(printf '%s/evals/endpoint/u%07d-ab512-greedy-off' "$RUN" "$END")" report.json --seedset "$AB" \
    --checkpoint "$CK" --policy greedy --breaker off --macros "$MACROS" || log "ab-512 eval at u$END failed (continuing)"
  [ "$EVAL_PLAN" = dev512 ] || return 0
  run_eval "$(printf '%s/evals/endpoint/u%07d-dev128-greedy-off' "$RUN" "$END")" report.json --seedset "$DEV" \
    --checkpoint "$CK" --policy greedy --breaker off --macros "$MACROS" || log "dev-128 eval at u$END failed (continuing)"
}
final_evals() {   # the last update's evals (v1's tail)
  local CK
  CK=$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$LAST")
  run_eval "$(printf '%s/evals/endpoint/u%07d-ab512-greedy-on' "$RUN" "$LAST")" report.json --seedset "$AB" \
    --checkpoint "$CK" --policy greedy --breaker on --macros "$MACROS"
  run_eval "$(printf '%s/evals/endpoint/u%07d-dev128-sample-off' "$RUN" "$LAST")" repeats.json --seedset "$DEV" \
    --checkpoint "$CK" --policy sample --breaker off --sample-seed 1 --repeats 3 --macros "$MACROS"
}
expected_reports() {   # v2 (3): every endpoint report this line must leave behind, relative to RUN_DIR
  local END
  for END in $ENDS; do
    printf 'evals/endpoint/u%07d-ab512-greedy-off/report.json\n' "$END"
    if [ "$EVAL_PLAN" = dev512 ]; then printf 'evals/endpoint/u%07d-dev128-greedy-off/report.json\n' "$END"; fi
  done
  printf 'evals/endpoint/u%07d-ab512-greedy-on/report.json\n' "$LAST"
  printf 'evals/endpoint/u%07d-dev128-sample-off/repeats.json\n' "$LAST"
}
final_check() {   # v2 (3): redo missing endpoint evals once, final sync, then every report locally and in the bucket
  local END rel miss=0
  for END in $ENDS; do seg_evals "$END"; done
  final_evals || log "final evals failed (rc $?)"
  if [ "${SYNC:-1}" = 1 ]; then
    log "final gcs_sync --once for $RUN"
    python3 "$STS/gcs_sync.py" --key "$STS/gcs-sts2-ckpt.json" --bucket "$BUCKET" --prefix runs \
      --run "$RUN" --interval 60 --min-age 0 --once >> "$RUN.gcs_sync.log" 2>&1 || log "final gcs_sync --once failed"
  fi
  for rel in $(expected_reports); do
    [ -s "$RUN/$rel" ] || { log "endpoint report missing locally: $rel"; miss=1; }
  done
  if [ "${SYNC:-1}" = 1 ] && [ $miss = 0 ]; then
    expected_reports | python3 -c '
import sys
from google.cloud import storage
key, bucket, prefix = sys.argv[1:4]
want = [l.strip() for l in sys.stdin if l.strip()]
c = storage.Client.from_service_account_json(key)
have = {b.name for b in c.list_blobs(bucket, prefix=prefix + "/evals/endpoint/")}
miss = [w for w in want if prefix + "/" + w not in have]
for m in miss:
    print("endpoint report not in gs://%s/%s/: %s" % (bucket, prefix, m), flush=True)
sys.exit(1 if miss else 0)
' "$STS/gcs-sts2-ckpt.json" "$BUCKET" "runs/$(basename "$RUN")" || { log "endpoint reports not all in the bucket"; miss=1; }
  fi
  [ $miss = 0 ] || { log "endpoint reports incomplete: stop"; exit 4; }
}

# ------------------------------------------------------------------------------------------------ segments
FAILS=0   # consecutive run_train.sh failures (a session that ends normally resets it)
for END in $ENDS; do
  tries=0; SEG_FLAGS=()   # v2 (2): per-segment extra flags (the compile fallback), reset at every segment
  until reached "$RUN" "$END"; do
    stop_check "before a session to update $END"   # v2 (1)
    tries=$((tries + 1))
    if [ $tries -gt 4 ]; then log "$RUN is not at update $END after 4 sessions: stop"; exit 1; fi
    log "train $RUN to update $END (session $tries, overlap $OVERLAP)"
    $TRAIN "$RUN" 12 2048 "${FLAGS[@]}" ${SEG_FLAGS[@]+"${SEG_FLAGS[@]}"} --max-updates "$END"; rc=$?
    [ $rc -eq 0 ] && { FAILS=0; continue; }
    FAILS=$((FAILS + 1))
    log "run_train.sh failed (rc $rc, failure $FAILS in a row)"
    [ $FAILS -ge 3 ] && { log "run_train.sh failed 3 times in a row: stop"; exit 1; }
    # The trainer's compiled-vs-eager numerics gate (ppo.py check_compiled) trips on bf16 rounding noise when the
    # minibatch loss is ~0 (w4-d session 19 at u8664, 2026-11-06 04:38Z: loss rel 2.3e-2 > 1e-2 while every gradient
    # agreed to 3.8e-2 << 0.25); before this retry the exit cascaded into the wrappers' ': done' and the keepalive
    # stopped the runtime. Continue eager instead (recorded in the run's config.json / train.log).
    if tail -c 20000 "$RUN/train.log" 2>/dev/null | grep -q "compiled minibatch update vs eager.*MISMATCH\|CompiledMismatchError"; then
      SEG_FLAGS=(--compile off); log "compile gate tripped: retrying with --compile off (this segment only)"   # v2 (2)
    fi
    sleep 30
  done
  log "$RUN at update $END"
  CK=$(printf '%s/checkpoints/pinned-%07d.pt' "$RUN" "$END")
  [ -f "$CK" ] || { log "missing $CK (--eval-at pins it)"; exit 1; }
  [ "${EVALS:-1}" = 1 ] || continue
  seg_evals "$END"
done
[ "${EVALS:-1}" = 1 ] || { log "done (EVALS=0)"; exit 0; }
stop_check "before the final evals"   # v2 (1)
final_evals
final_check   # v2 (3)
log "done"
