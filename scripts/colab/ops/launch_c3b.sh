#!/bin/bash
# launch_c3b.sh (GPU A, sts2a2): line C3 = pure continuation of the C lineage from C2's u10664 (local pinned
# checkpoint) for +1500 updates (11164 11664 12164), --macros all, entropy 0.005, harvest inherited, NO exploration,
# NO saves, NO boss bonus — on the wave-17 bundle (symlink /content/sts2/bundle-w17; those features off = bit-identical
# code to what S runs) so that C3 is the paired CONTROL for line S (same fork, same bundle, only S's two flags differ).
# Started 2026-11-06 16:30 EST after line G was stopped (its push drove the policy's own upgrade mass from 0.34 to 1e-28
# in 48 updates and dev-128 fell to 0.398).
set -u
TAGN=w4-c3
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
B=$(readlink -f /content/sts2/bundle-w17); [ -f "$B/run_train.sh" ] || { say "bundle-w17 missing: done"; exit 1; }
FORK=/content/sts2/runs/w4-c/checkpoints/pinned-0010664.pt
[ -s "$FORK" ] || { say "fork checkpoint $FORK missing: done"; exit 1; }
TAG=c3 WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12164 ENDS="11164 11664 12164" MACROS=all \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w4-c3 a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
