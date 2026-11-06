#!/bin/bash
# launch_g.sh (GPU A, sts2a2): round-4 line G — after the C continuation (w4-c, TAG c2) reaches u10664 on this runtime,
# fork its pinned-0010664 and train +2000 updates with the exploration rescue on FOUR decision kinds
# (rest-site, card removal, Travel:Elite, ProceedRewards:skipcard; eps 0.3, policy log-probs) + PPO dual-clip 3.0
# (wave-11 bundle, symlink /content/sts2/bundle-w11), --macros all (adopted 2026-11-06 10:00), harvest rings inherited
# from the checkpoint config (boss:1536,elite:512,act2:1024 — present in both G and H). Pre-registered:
# docs/round4-plan-2026-11-06.md (amended 2026-11-06 10:10: fork = C2's u10664; G - H = the two elite/skip selectors).
set -u
L=/content/sts2/runs/w4-c2.driver.log; TAGN=w10-g
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-c2: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed [0-9]+ times in a row: stop|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "C continuation ended abnormally ($last); nothing to continue: done"; exit 1
  fi
  (( SECONDS - t0 > 86400 )) && { say "C continuation not done after 24 h; giving up: done"; exit 1; }
  say "waiting for the C continuation to finish (${last:0:80})"; sleep 300
done
t1=$SECONDS
until [ -f /content/sts2/bundle-w11/run_train.sh ]; do
  (( SECONDS - t1 > 43200 )) && { say "no wave-11 bundle (bundle-w11) after 12 h; giving up: done"; exit 1; }
  say "waiting for the wave-11 bundle at /content/sts2/bundle-w11"; sleep 60
done
B=$(readlink -f /content/sts2/bundle-w11); say "bundle $B"
FORK=/content/sts2/runs/w4-c/checkpoints/pinned-0010664.pt
[ -s "$FORK" ] || { say "fork checkpoint $FORK missing: done"; exit 1; }
TAG=g WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=10664 LAST=12664 ENDS="11164 11664 12164 12664" MACROS=all \
  EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove,Travel:Elite,ProceedRewards:skipcard --explore-eps 0.3 --explore-logp policy --dual-clip 3.0" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh "$B" /content/sts2/runs/w10-g a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
