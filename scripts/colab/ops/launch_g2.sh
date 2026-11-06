#!/bin/bash
# launch_g2.sh — wave-12 arm: g = explored samples get the bounded exploration push (--explore-push 1.0, policy log-probs),
# policy-drawn samples get PPO with dual-clip 3.0; --macros all unless stated; bundle = symlink /content/sts2/bundle-w12.
# Waits for the predecessor marker 'w3m-c2: done' in /content/sts2/runs/w4-c2.driver.log, then for the bundle.
set -u
L=/content/sts2/runs/w4-c2.driver.log; TAGN=w10-g
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-c2: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "predecessor ended abnormally ($last); continuing anyway"; break
  fi
  (( SECONDS - t0 > 86400 )) && { say "predecessor not done after 24 h; giving up: done"; exit 1; }
  say "waiting for the predecessor (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w12/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-12 bundle (bundle-w12) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-12 bundle at /content/sts2/bundle-w12"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w12); say "bundle $B"
FORK=/content/sts2/runs/w4-c/checkpoints/pinned-0010664.pt
[ -s "$FORK" ] || { say "fork checkpoint $FORK missing: done"; exit 1; }
TAG=g WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12664 ENDS="11164 11664 12164 12664" MACROS=all \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove,Travel:Elite,ProceedRewards:skipcard --explore-eps 0.3 --explore-logp policy --explore-push 1.0 --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-g a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
