#!/bin/bash
# launch_c3.sh (GPU A, sts2a2): continue round-2 line C (runs/w4-c, --macros all, harvest inherited from B's checkpoint)
# from its u9164 endpoint to u10664 (segments 9664 10164 10664), entropy constant 0.005, same wave-6 bundle. Pure
# continuation: does C's dev-128 0.480 at u9164 hold? The run dir must have been restored from the bucket first
# (checkpoints incl. pinned-0009164.pt, status/metrics/evals) — w3m_line.sh then skips the fork and resumes.
set -u
TAGN=w4-c2
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
[ -s /content/sts2/runs/w4-c/checkpoints/pinned-0009164.pt ] || { say "runs/w4-c not restored (pinned-0009164.pt missing): done"; exit 1; }
TAG=c2 WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=600 ENT0=0.005 ENT1=0.005 FORK_U=9164 LAST=10664 ENDS="9664 10164 10664" MACROS=all \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-bd6d6f6863 /content/sts2/runs/w4-c a /content/sts2/runs/w4-c/checkpoints/pinned-0009164.pt off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
