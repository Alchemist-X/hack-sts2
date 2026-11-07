#!/bin/bash
# launch_h4.sh (wave 14b: push budget 3 nat/update on the push's own movement) — wave-14 arm (after C-rep; push as its own row-scaled step): h = explored samples get the bounded exploration push (--explore-push 1.0, policy log-probs),
# policy-drawn samples get PPO with dual-clip 3.0; --macros all unless stated; bundle = symlink /content/sts2/bundle-w14b.
# Waits for the predecessor marker 'w3m-e3: done' in /content/sts2/runs/w9-e3.driver.log, then for the bundle.
set -u
L=/content/sts2/runs/w4-crep.driver.log; TAGN=w10-h
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-crep: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "predecessor ended abnormally ($last); continuing anyway"; break
  fi
  (( SECONDS - t0 > 86400 )) && { say "predecessor not done after 24 h; giving up: done"; exit 1; }
  say "waiting for the predecessor (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w14b/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-14b bundle (bundle-w14b) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-14b bundle at /content/sts2/bundle-w14b"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w14b); say "bundle $B"
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w4-c/checkpoints/pinned-0010664.pt --dest /content/sts2/fork/$(basename runs/w4-c/checkpoints/pinned-0010664.pt .pt)-h.pt --wait 43200 || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/$(basename runs/w4-c/checkpoints/pinned-0010664.pt .pt)-h.pt
TAG=h WAIT_RUN=/content/sts2/runs/w4-crep WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12664 ENDS="11164 11664 12164 12664" MACROS=all \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --explore-push 3.0 --explore-push-step 0.5 --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-h a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
