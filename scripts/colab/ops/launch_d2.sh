#!/bin/bash
# launch_d.sh (VM B): round-2 line D — the control for C: once overnight line B is done, continue line B's own endpoint (fork switched to B on 11-05 18:55)
# (pinned-0007664, fetched from GCS) with A's own recipe (--macros rest,shop, wave-6 bundle (sts2train-bd6d6f6863: wave-4 prompt macros + run-state logging fields, same AOT key 03f89cc37d4f)), entropy constant 0.005,
# +1500 updates (segments 8164 8664 9164). Lines end with ': done' when the runtime has nothing more to do.
set -u
L=/content/sts2/runs/w3m-b.driver.log; TAGN=w4-d
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-b: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed \(rc [0-9]+\)|check failed|fetch failed|pins it\))$'; then
    say "line B ended abnormally ($last); nothing to continue: done"; exit 1
  fi
  (( SECONDS - t0 > 115200 )) && { say "line B not done after 32 h; giving up: done"; exit 1; }
  say "waiting for line B to finish (${last:0:80})"; sleep 300
done
TAG=d WAIT_RUN=/content/sts2/runs/w3m-b WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9164 ENDS="8164 8664 9164" MACROS=rest,shop \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-bd6d6f6863 /content/sts2/runs/w4-d a /content/sts2/runs/w3m-b/checkpoints/pinned-0007664.pt off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
