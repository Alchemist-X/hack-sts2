#!/bin/bash
# launch_c.sh (VM A): round-2 line C — once overnight line A is done, continue line B's endpoint (pinned-0007664, fetched from GCS; fork switched to B on 11-05 18:55 so GPU B does not idle) with the v2
# prompt macros (--macros all, wave-6 bundle (sts2train-bd6d6f6863: wave-4 prompt macros + run-state logging fields, same AOT key 03f89cc37d4f)), entropy constant 0.005, +1500 updates (segments 8164 8664 9164).
# Every line this prints ends with ': done' when there is nothing more for this runtime to do (the Tokyo keepalive then
# stops the runtime).
set -u
L=/content/sts2/runs/w3m-a.driver.log; TAGN=w4-c
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-a: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed \(rc [0-9]+\)|check failed|fetch failed|pins it\))$'; then
    say "line A ended abnormally ($last); nothing to continue: done"; exit 1
  fi
  (( SECONDS - t0 > 108000 )) && { say "line A not done after 30 h; giving up: done"; exit 1; }
  say "waiting for line A to finish (${last:0:80})"; sleep 300
done
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w3m-b/checkpoints/pinned-0007664.pt --dest /content/sts2/fork/pinned-0007664.pt --wait 36000 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0007664.pt
TAG=c WAIT_MAX_S=3600 WAIT_RUN=/content/sts2/runs/w3m-a ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9164 ENDS="8164 8664 9164" MACROS=all \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-bd6d6f6863 /content/sts2/runs/w4-c a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
