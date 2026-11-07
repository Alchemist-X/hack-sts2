#!/bin/bash
# launch_f.sh: round-3 line F — after round-2 line D is done on this runtime, continue line B's u7664 endpoint (fork switched to B on 11-05 18:55) with
# no exploration (control) (wave-9 bundle sts2train-57b85d8f0d), ent const 0.005, +2000 updates (8164 8664 9164 9664).
# Pre-registered: docs/round3-plan-2026-11-05.md (E' = exploration rescue vs F' = control). Lines end with ': done'
# when this runtime has nothing more to do.
set -u
L=/content/sts2/runs/w4-d.driver.log; TAGN=w9-f
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-d: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed \(rc [0-9]+\)|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "round-2 line D ended abnormally ($last); nothing to continue: done"; exit 1
  fi
  (( SECONDS - t0 > 172800 )) && { say "line D not done after 48 h; giving up: done"; exit 1; }
  say "waiting for round-2 line D to finish (${last:0:80})"; sleep 300
done
FORK=/content/sts2/runs/w3m-b/checkpoints/pinned-0007664.pt
TAG=f WAIT_RUN=/content/sts2/runs/w4-d WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9664 ENDS="8164 8664 9164 9664" MACROS=rest,shop EXTRA="" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-57b85d8f0d /content/sts2/runs/w9-f a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
