#!/bin/bash
# launch_h.sh (GPU B, sts2b3): round-4 control H — after E2 is done on this runtime, fork the C continuation's u10664
# (runs/w4-c/checkpoints/pinned-0010664.pt, fetched from the bucket; waits for it) and train +2000 updates with the
# exploration rescue on rest-site + card-removal rows only (eps 0.3, policy log-probs) + PPO dual-clip 3.0 (wave-11
# bundle), --macros all, harvest inherited. G - H = the Travel:Elite + ProceedRewards:skipcard selectors.
set -u
L=/content/sts2/runs/w9-e2.driver.log; TAGN=w10-h
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-e2: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "E2 ended abnormally ($last); continuing anyway (H does not depend on E2's result)"; break
  fi
  (( SECONDS - t0 > 86400 )) && { say "E2 not done after 24 h; giving up: done"; exit 1; }
  say "waiting for E2 to finish (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w11/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-11 bundle (bundle-w11) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-11 bundle at /content/sts2/bundle-w11"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w11); say "bundle $B"
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w4-c/checkpoints/pinned-0010664.pt --dest /content/sts2/fork/pinned-0010664-c.pt --wait 43200 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0010664-c.pt
TAG=h WAIT_RUN=/content/sts2/runs/w9-e2 WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12664 ENDS="11164 11664 12164 12664" MACROS=all \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-h a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
