#!/bin/bash
# launch_e2.sh (GPU B, sts2b3): round-3 line E' relaunch — after F' is done on this runtime, wait for the wave-11 bundle
# (symlink /content/sts2/bundle-w11 -> the sts2train-<sha> dir with PPO dual-clip), fork line A's u7664 from the bucket and
# run the exploration rescue (eps 0.3 on rest-site / card-removal rows, policy log-probs) WITH --dual-clip 3.0.
# Why: the first E' (06:35-06:42 EST, 2026-11-06, runs/w9-e) diverged: with --explore-logp policy the stored log pi_old of an
# explored row the policy gives ~0 makes the PPO ratio unbounded (kl 5870 at u7670, 2.9e12 at u7676, NaN after); the
# negative-advantage side r*A has no clip. Dual-clip (Ye et al. 2020) bounds it at c*A. Lines end with ': done' when
# this runtime has nothing more to do.
set -u
L=/content/sts2/runs/w9-f.driver.log; TAGN=w9-e2
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-f: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "round-3 line F' ended abnormally ($last); continuing anyway (E2 does not depend on F')"; break
  fi
  (( SECONDS - t0 > 86400 )) && { say "line F' not done after 24 h; giving up: done"; exit 1; }
  say "waiting for line F' to finish (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w11/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-11 bundle (bundle-w11) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-11 bundle at /content/sts2/bundle-w11"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w11); say "bundle $B"
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w3m-a/checkpoints/pinned-0007664.pt --dest /content/sts2/fork/pinned-0007664-a.pt --wait 36000 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0007664-a.pt
TAG=e2 WAIT_RUN=/content/sts2/runs/w9-f WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9664 ENDS="8164 8664 9164 9664" MACROS=rest,shop \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w9-e2 a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
