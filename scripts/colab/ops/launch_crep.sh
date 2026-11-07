#!/bin/bash
# launch_crep.sh (GPU B, sts2b3): round-2 replication C-rep — line C's recipe (B's u7664 + --macros all, entropy constant
# 0.005, harvest inherited from the checkpoint, wave-6 bundle sts2train-bd6d6f6863) with a DIFFERENT seed stream (SP2),
# +1500 updates (8164 8664 9164). Purpose: the --macros all adoption rests on one paired dev-128 comparison (C-D +0.049)
# with ab-512 n.s.; a second seed either confirms or weakens it (paired against D and C at u9164 on dev-128 / ab-512).
# Started 2026-11-06 11:45 EST after E3 was stopped (explore push unstable; wave-13 diagnostic pending).
set -u
TAGN=w4-crep
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w3m-b/checkpoints/pinned-0007664.pt --dest /content/sts2/fork/pinned-0007664-b.pt --wait 3600 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0007664-b.pt
TAG=crep WAIT_RUN=/content/sts2/runs/w9-e3 WAIT_MAX_S=600 ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9164 ENDS="8164 8664 9164" MACROS=all \
  EXTRA="--seed-stream-prefix SP2" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-bd6d6f6863 /content/sts2/runs/w4-crep a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
