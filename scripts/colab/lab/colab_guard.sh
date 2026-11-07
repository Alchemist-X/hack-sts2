#!/bin/bash
# colab_guard.sh — every ssh / scp / colab exec to a Colab runtime goes through this (audit OPS2 / ops F6).
#
# An ssh or scp to a session name that no longer exists makes the colab proxy create a fresh, billing CPU runtime under
# that name (three ghosts in two days). The guard first runs `colab --auth=adc sessions` and only passes the command
# on when every target session is listed AND its Hardware is not CPU (REQUIRE_GPU=1, default). Otherwise it refuses
# (exit 3), prints why on stderr and appends a line to $GUARD_ALERT. A failing `colab sessions` call also refuses.
#
#   colab_guard.sh check NAME...                 exit 0 if every NAME is a live GPU runtime
#   colab_guard.sh sessions                      print "NAME HARDWARE" for every live runtime
#   colab_guard.sh ssh NAME [ssh args / remote command...]      -> ssh [-F $SSH_CONFIG] colab-NAME ...
#   colab_guard.sh scp [scp args...]             every colab-NAME:path argument is checked -> scp [-F $SSH_CONFIG] ...
#   colab_guard.sh exec NAME [colab exec args...]                -> colab --auth=adc exec -s NAME ... (stdin passed on)
#
# Env: DRY_RUN=1 (check for real, then print 'DRY: <command>' instead of running it), COLAB=colab,
#      SSH_CONFIG (an ssh_config with the Host colab-* blocks; see scripts/colab/ops/ssh_config.example),
#      REQUIRE_GPU=1, GUARD_ALERT=$HOME/.colab_guard/ALERT, SESSIONS_TIMEOUT=120.
# Session names must match [A-Za-z0-9_.-]+. The guard never creates, stops or renames a runtime. bash 3.2 compatible.
set -u
usage() { sed -n '2,/^set -u$/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }
COLAB=${COLAB:-colab}; DRY=${DRY_RUN:-0}; REQUIRE_GPU=${REQUIRE_GPU:-1}
ALERTF=${GUARD_ALERT:-$HOME/.colab_guard/ALERT}; ST=${SESSIONS_TIMEOUT:-120}
refuse() {
  echo "colab_guard: REFUSED: $*" >&2
  mkdir -p "$(dirname "$ALERTF")" 2>/dev/null && echo "$(date -u +%FT%TZ) colab_guard refused: $*" >> "$ALERTF"
  exit 3
}
LIVE=""
live() {   # cache `colab sessions` as "NAME HARDWARE" lines for this invocation
  [ -n "$LIVE" ] && return 0
  local out
  out=$(perl -e "alarm $ST; exec @ARGV" "$COLAB" --auth=adc sessions 2>&1 < /dev/null)
  echo "$out" | grep -q "Hardware" || refuse "colab sessions failed: ${out:0:160}"
  LIVE=$(echo "$out" | sed -n 's/^\[\([A-Za-z0-9_.-]*\)\].*Hardware: *\([A-Za-z0-9_-]*\).*/\1 \2/p')
  [ -n "$LIVE" ] || LIVE="-"
}
check_one() {   # NAME
  local n=$1 hw
  case "$n" in ''|*[!A-Za-z0-9_.-]*) refuse "bad session name '$n'" ;; esac
  live
  hw=$(echo "$LIVE" | awk -v n="$n" '$1 == n { print $2; exit }')
  [ -n "$hw" ] || refuse "session '$n' is not in 'colab sessions' (a connection would create a ghost runtime)"
  if [ "$REQUIRE_GPU" = 1 ] && [ "$hw" = CPU ]; then refuse "session '$n' is a CPU runtime (Hardware: CPU)"; fi
  return 0
}
run() { if [ "$DRY" = 1 ]; then echo "DRY: $*"; exit 0; fi; exec "$@"; }

cmd=${1:-}; [ $# -gt 0 ] && shift
case "$cmd" in
  -h|--help|help) usage; exit 0 ;;
  check)
    [ $# -ge 1 ] || { usage >&2; exit 2; }
    for n in "$@"; do check_one "$n"; done; echo "ok: $*" ;;
  sessions) live; [ "$LIVE" = - ] || echo "$LIVE" ;;
  ssh)
    [ $# -ge 1 ] || { usage >&2; exit 2; }
    n=$1; shift; check_one "$n"
    run ssh ${SSH_CONFIG:+-F "$SSH_CONFIG"} "colab-$n" "$@" ;;
  scp)
    [ $# -ge 2 ] || { usage >&2; exit 2; }
    found=0
    for a in "$@"; do
      case "$a" in
        colab-*:*) n=${a#colab-}; n=${n%%:*}; check_one "$n"; found=1 ;;
        *@colab-*:*) n=${a#*@colab-}; n=${n%%:*}; check_one "$n"; found=1 ;;
      esac
    done
    [ $found = 1 ] || refuse "scp without a colab-NAME: target"
    run scp ${SSH_CONFIG:+-F "$SSH_CONFIG"} "$@" ;;
  exec)
    [ $# -ge 1 ] || { usage >&2; exit 2; }
    n=$1; shift; check_one "$n"
    run "$COLAB" --auth=adc exec -s "$n" "$@" ;;
  *) usage >&2; exit 2 ;;
esac
