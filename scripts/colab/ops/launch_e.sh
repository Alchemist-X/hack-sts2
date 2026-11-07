#!/bin/bash
# launch_e.sh: round-3 line E — after round-2 line C is done on this runtime, continue line B's u7664 endpoint (fork switched to B on 11-05 18:55) with
# the exploration rescue (eps 0.3 on rest-site and card-removal rows, policy log-probs) (wave-9 bundle sts2train-57b85d8f0d), ent const 0.005, +2000 updates (8164 8664 9164 9664).
# Pre-registered: docs/round3-plan-2026-11-05.md (E' = exploration rescue vs F' = control). Lines end with ': done'
# when this runtime has nothing more to do.
set -u
L=/content/sts2/runs/w4-c.driver.log; TAGN=w9-e
say() { echo "$(date -u '+%F %T') $TAGN: $*"; }
t0=$SECONDS
while ! grep -q "w3m-c: done" "$L" 2>/dev/null; do
  last=$(tail -1 "$L" 2>/dev/null)
  if echo "$last" | grep -qE '(: stop|failed \(rc [0-9]+\)|check failed|fetch failed|pins it\)|nothing to continue: done|giving up: done|exited rc [0-9]+: done)$'; then
    say "round-2 line C ended abnormally ($last); nothing to continue: done"; exit 1
  fi
  (( SECONDS - t0 > 172800 )) && { say "line C not done after 48 h; giving up: done"; exit 1; }
  say "waiting for round-2 line C to finish (${last:0:80})"; sleep 300
done
mkdir -p /content/sts2/fork
python3 /content/sts2/w3m-src/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json --bucket gomoku-zero-20260816-ai-sts2train \
  --blob runs/w3m-b/checkpoints/pinned-0007664.pt --dest /content/sts2/fork/pinned-0007664.pt --wait 36000 \
  || { say "fork checkpoint fetch failed: done"; exit 1; }
FORK=/content/sts2/fork/pinned-0007664.pt
TAG=e WAIT_RUN=/content/sts2/runs/w4-c WAIT_MAX_S=3600 ENT0=0.005 ENT1=0.005 FORK_U=7664 LAST=9664 ENDS="8164 8664 9164 9664" MACROS=rest,shop EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy" \
  bash /content/sts2/w5-src/scripts/colab/w3m_line.sh /content/sts2/sts2train-57b85d8f0d /content/sts2/runs/w9-e a "$FORK" off
rc=$?; [ $rc -ne 0 ] && say "line driver exited rc $rc: done"; exit $rc
