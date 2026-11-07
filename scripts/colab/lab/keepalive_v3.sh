#!/bin/bash
# keepalive_v3.sh [INTERVAL=300]   (audit OPS2; replaces colab_keepalive.sh's ': done' grep once shadow-verified)
#
# Every INTERVAL s:
#   1. `colab --auth=adc sessions` lists the live runtimes (no hand-kept sessions.txt). If that call fails (no
#      'Hardware' in its output) the tick does nothing at all: no ping, no stop.
#   2. every GPU runtime gets a kernel ping (`colab exec`; idle GPU runtimes are reclaimed after ~20 min);
#   3. its line_agent heartbeat ops/agents/<session>.json is read from the store:
#        - no heartbeat           -> not agent-managed: ping only, never stopped by this script;
#        - stale (age > STALE_S)  -> ALERT only, never a stop (an agent crash must not cost a running line);
#        - fresh 'drained'        -> counted; the runtime is stopped on the STOP_AFTER-th (default 2nd) consecutive
#                                    fresh 'drained' read; any other read resets the count;
#        - fresh 'blocked'        -> kept alive; ALERT once when blocked longer than BLOCKED_ALERT_S;
#        - a line 'failed' in the heartbeat's queue -> ALERT once per (session, line).
#   4. a runtime listed with Hardware CPU is a proxy-made ghost: ALERT once (STOP_GHOSTS=1 also stops it).
#   5. writes its own heartbeat $KA_DIR/keepalive_heartbeat (watch it from the Mac / the orchestrator).
#
# Env: DRY_RUN=1 (DEFAULT: pings and stops are printed as 'DRY: <command>' and not run; reads still run, so a dry
#      instance is a shadow of the live keepalive), COLAB=colab, KA_DIR=$HOME/sts2keep3 (keepalive.log, ALERT, RUN,
#      counters), STORE=gs://gomoku-zero-20260816-ai-sts2train (or dir:/path for a local store), GCLOUD=gcloud
#      (reads gs:// heartbeats with `gcloud storage cat`), STALE_S=600, STOP_AFTER=2, BLOCKED_ALERT_S=3600,
#      STOP_GHOSTS=0, ONE_TICK=1 (one tick, then exit). Stop the loop: rm $KA_DIR/RUN.
# bash 3.2 compatible; needs python3 (stdlib, -I) and perl (alarm) like watch_rotate.sh.
set -u
case "${1:-}" in -h|--help) sed -n '2,/^set -u$/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit 0 ;; esac
I=${1:-300}
DRY=${DRY_RUN:-1}; COLAB=${COLAB:-colab}; GCLOUD=${GCLOUD:-gcloud}
D=${KA_DIR:-$HOME/sts2keep3}; STORE=${STORE:-gs://gomoku-zero-20260816-ai-sts2train}
STALE_S=${STALE_S:-600}; STOP_AFTER=${STOP_AFTER:-2}; BLOCKED_ALERT_S=${BLOCKED_ALERT_S:-3600}; STOP_GHOSTS=${STOP_GHOSTS:-0}
mkdir -p "$D"; touch "$D/RUN"
exec 3>&1   # DRY lines go to the script's stdout even where a command's output is redirected to the log
log() { echo "$(date -u +%FT%TZ) $*" >> "$D/keepalive.log"; }
alert() { echo "$(date -u +%FT%TZ) $*" >> "$D/ALERT"; log "ALERT $*"; }
alert_once() {   # KEY MESSAGE
  local f="$D/.alerted.$1"
  [ -f "$f" ] && return 0
  : > "$f"; alert "$2"
}
rd() { local a=$1; shift; perl -e "alarm $a; exec @ARGV" "$@" < /dev/null; }   # reads: always run
mut() {   # mutations: printed only under DRY_RUN=1
  local a=$1; shift
  if [ "$DRY" = 1 ]; then echo "DRY: $*" >&3; log "DRY: $*"; return 0; fi
  perl -e "alarm $a; exec @ARGV" "$@" < /dev/null
}
store_cat() {   # KEY -> the object's bytes on stdout (nothing when missing)
  case "$STORE" in
    gs://*) rd 60 "$GCLOUD" storage cat "${STORE%/}/$1" 2>/dev/null ;;
    dir:*) cat "${STORE#dir:}/$1" 2>/dev/null ;;
    *) cat "$STORE/$1" 2>/dev/null ;;
  esac
}
ping() {   # SESSION
  if [ "$DRY" = 1 ]; then mut 90 "$COLAB" --auth=adc exec -s "$1" --timeout 60; return 0; fi
  echo 'print("ping-ok")' | perl -e "alarm 90; exec @ARGV" "$COLAB" --auth=adc exec -s "$1" --timeout 60 2>&1 | grep -q ping-ok
}
# heartbeat JSON (stdin) -> one line: STATE AGE_S ACTIVE FAILED_LINES(comma or -) REASON...
PARSE='
import json, sys, time
try:
    h = json.loads(sys.stdin.read())
    age = time.time() - float(h["heartbeat_unix"])
    failed = [q["line"] for q in h.get("queue") or [] if q.get("state") == "failed"]
    print(h.get("state") or "?", int(age), h.get("active_line") or "-", ",".join(failed) or "-",
          (h.get("reason") or "").replace("\n", " ")[:160])
except Exception as e:
    print("unparsable", 0, "-", "-", type(e).__name__)
'

tick() {
  local live S hw hb st age act failed reason c f t0
  live=$(rd 120 "$COLAB" --auth=adc sessions 2>&1)
  if ! echo "$live" | grep -q "Hardware"; then log "colab sessions failed (no action this tick): ${live:0:160}"; return; fi
  echo "$live" | sed -n 's/^\[\([A-Za-z0-9_.-]*\)\].*Hardware: *\([A-Za-z0-9_-]*\).*/\1 \2/p' > "$D/.live"
  while read -r S hw; do
    [ -z "$S" ] && continue
    if [ "$hw" = CPU ]; then
      alert_once "ghost.$S" "$S is a CPU runtime (proxy ghost?)"
      [ "$STOP_GHOSTS" = 1 ] && mut 120 "$COLAB" --auth=adc stop -s "$S" >> "$D/keepalive.log" 2>&1
      continue
    fi
    ping "$S" || { sleep 10; ping "$S"; } || log "$S ping FAILED (after retry)"
    hb=$(store_cat "ops/agents/$S.json")
    if [ -z "$hb" ]; then log "$S ($hw): no agent heartbeat: ping only"; rm -f "$D/drained.$S"; continue; fi
    read -r st age act failed reason <<EOF
$(printf '%s' "$hb" | python3 -I -c "$PARSE")
EOF
    for f in $(echo "$failed" | tr ',' ' '); do
      [ "$f" = - ] || alert_once "failed.$S.$f" "line $f on $S failed (see runs/<run>/line_status.json)"
    done
    if [ "$st" = unparsable ] || [ "$age" -gt "$STALE_S" ]; then
      alert_once "stale.$S.$(date -u +%Y%m%d%H)" "$S heartbeat stale or unreadable (state $st, age ${age}s > ${STALE_S}s): agent down? NOT stopping"
      rm -f "$D/drained.$S"; continue
    fi
    case "$st" in
      drained)
        c=$(( $(cat "$D/drained.$S" 2>/dev/null || echo 0) + 1 ))
        if [ "$c" -ge "$STOP_AFTER" ]; then
          log "$S drained ($c consecutive fresh reads, age ${age}s): stopping the runtime"
          mut 120 "$COLAB" --auth=adc stop -s "$S" >> "$D/keepalive.log" 2>&1
          rm -f "$D/drained.$S"
        else
          echo "$c" > "$D/drained.$S"; log "$S drained ($c/$STOP_AFTER, age ${age}s): $reason"
        fi
        rm -f "$D/blocked.$S" ;;
      blocked)
        rm -f "$D/drained.$S"
        [ -f "$D/blocked.$S" ] || date +%s > "$D/blocked.$S"
        t0=$(cat "$D/blocked.$S")
        log "$S blocked (age ${age}s, $(( $(date +%s) - t0 ))s so far): $reason"
        [ $(( $(date +%s) - t0 )) -gt "$BLOCKED_ALERT_S" ] && alert_once "blocked.$S.$t0" "$S blocked for more than ${BLOCKED_ALERT_S}s: $reason"
        ;;
      *)
        rm -f "$D/drained.$S" "$D/blocked.$S"; log "$S $st (age ${age}s, line $act): $reason" ;;
    esac
  done < "$D/.live"
  date -u +%FT%TZ > "$D/keepalive_heartbeat"
}

while [ -f "$D/RUN" ]; do
  tick
  [ "${ONE_TICK:-0}" = 1 ] && break
  sleep "$I"
done
