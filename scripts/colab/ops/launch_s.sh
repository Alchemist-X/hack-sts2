#!/bin/bash
# launch_s.sh (GPU B, sts2b3): line S = the "boss school" — after the round-2 replication C-rep is done on this runtime,
# fork the C continuation's u10664 (fetched from the bucket; waits for it) and train +1500 updates (11164 11664 12164)
# with 25% of starts drawn from the human save pool (THE SILENT, A9/A10, acts 2-3; determinized copies) and a one-time
# +0.2 reward at every earned act-2+ boss kill (--boss-bonus, wave 17, bundle symlink /content/sts2/bundle-w17);
# --macros all; harvest rings inherited from the checkpoint config (no --harvest change). Pre-registered:
# docs/boss-school-plan-2026-11-06.md. Leading indicator: dev-128 act_reached 3 (act-2 boss killed).
set -u
L=/content/sts2/runs/w4-crep.driver.log; TAGN=w10-s
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-crep: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "C-rep ended abnormally ($last); continuing anyway (S does not depend on it)"; break
  fi
  (( SECONDS - t0 > 86400 )) && { say "C-rep not done after 24 h; giving up: done"; exit 1; }
  say "waiting for C-rep to finish (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w17/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-17 bundle (bundle-w17) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-17 bundle at /content/sts2/bundle-w17"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w17); say "bundle $B"
[ -d /content/sts2/saves-silent-a9a10-late ] || { say "save pool /content/sts2/saves-silent-a9a10-late missing: done"; exit 1; }
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w4-c/checkpoints/pinned-0010664.pt --dest /content/sts2/fork/pinned-0010664-c.pt --wait 43200 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0010664-c.pt
TAG=s WAIT_RUN=/content/sts2/runs/w4-crep WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12164 ENDS="11164 11664 12164" MACROS=all \
  EXTRA="--start-saves /content/sts2/saves-silent-a9a10-late --save-mix 0.25 --copies-mode determinized --boss-bonus 0.2" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-s a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
