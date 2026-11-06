#!/bin/bash
# resume_b_sts2b3.sh — one-shot, unattended: finish restoring round-2 line D (runs/w4-d) on sts2b3 from the GCS mirror,
# re-create line B's fork checkpoint + ': done' marker (line B finished at u7664 on sts2b2, official readout 2026-11-05
# 20:00 EST; the keepalive stopped sts2b2 at 2026-11-06 04:45Z after D's trainer tripped the compile gate), then start
# the D driver (launch_d2.sh) and the round-3 F' wrapper (launch_f.sh). Log: /content/sts2/resume_b.log
set -u
cd /content/sts2
K=/content/sts2/gcs-sts2-ckpt.json; B=gomoku-zero-20260816-ai-sts2train
echo "$(date -u '+%F %T') pull w4-d (skip existing)"
python3 gcs_pull.py --key $K --bucket $B --prefix runs/w4-d --dest runs/w4-d --newest-ckpt-only --skip-existing 2>&1 | tail -2
( cd runs/w4-d && [ -s status.json ] && [ -s metrics.jsonl ] && [ -s evals.jsonl ] ) || { echo "RESTORE INCOMPLETE (status/metrics/evals missing): NOT starting"; exit 1; }
python3 -c "import json;print('w4-d status update', json.load(open('runs/w4-d/status.json'))['update'])"
ls runs/w4-d/evals/endpoint/
mkdir -p runs/w3m-b/checkpoints
python3 w3m-src/scripts/colab/w3m_fetch.py --key $K --bucket $B --blob runs/w3m-b/checkpoints/pinned-0007664.pt --dest runs/w3m-b/checkpoints/pinned-0007664.pt --wait 600 2>&1 | tail -1
[ -s runs/w3m-b/checkpoints/pinned-0007664.pt ] || { echo "B fork checkpoint missing: NOT starting"; exit 1; }
echo "$(date -u '+%F %T') w3m-b: done" >> runs/w3m-b.driver.log
setsid nohup bash /content/sts2/launch_d2.sh >> runs/w4-d.driver.log 2>&1 < /dev/null & disown
sleep 2
setsid nohup bash /content/sts2/launch_f.sh >> runs/w9-f.driver.log 2>&1 < /dev/null & disown
sleep 5; echo "wrappers running: $(pgrep -fc 'launch_[df]')"
echo "$(date -u '+%F %T') RESUME-DONE"
