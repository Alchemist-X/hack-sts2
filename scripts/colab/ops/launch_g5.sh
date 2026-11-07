#!/bin/bash
# launch_g5.sh (GPU A, sts2a2): resume line G (runs/w10-g, already forked from C2's u10664) on the wave-14c bundle
# (symlink /content/sts2/bundle-w14c) — a pure performance change of the push step (results bit-identical to 14b).
# Same recipe as launch_g4.sh: four explore kinds, eps 0.3, policy log-probs, push budget 3.0 nat/update, ETA 0.5,
# dual-clip 3.0, --macros all. Started by hand after the 14b driver was stopped; w3m_line.sh skips the fork (checkpoints
# exist) and resumes from the newest checkpoint.
set -u
TAGN=w10-g
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t1=$SECONDS
until [ -f /content/sts2/bundle-w14c/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-14c bundle after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-14c bundle at /content/sts2/bundle-w14c"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w14c); say "bundle $B (resume of w10-g)"
FORK=/content/sts2/runs/w4-c/checkpoints/pinned-0010664.pt
ls /content/sts2/runs/w10-g/checkpoints/ckpt-*.pt > /dev/null 2>&1 || { say "runs/w10-g has no checkpoints: done"; exit 1; }
TAG=g WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12664 ENDS="11164 11664 12164 12664" MACROS=all \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove,Travel:Elite,ProceedRewards:skipcard --explore-eps 0.3 --explore-logp policy --explore-push 3.0 --explore-push-step 0.5 --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-g a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
