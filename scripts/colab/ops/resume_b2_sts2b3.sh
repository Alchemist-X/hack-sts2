#!/bin/bash
# resume_b2_sts2b3.sh — second attempt (the first restore ssh was cut at 900 s and its gcs_pull kept running next to the
# resume script's own pull: two writers on the same files). Steps: finish the pull (skip existing same-size files),
# verify the small logs + newest checkpoints + episodes against the bucket's md5, re-download any mismatch, re-create line
# B's fork checkpoint and ': done' marker, start the D driver (launch_d2.sh) and the round-3 F' wrapper (launch_f.sh).
set -u
cd /content/sts2
K=/content/sts2/gcs-sts2-ckpt.json; B=gomoku-zero-20260816-ai-sts2train
say() { echo "$(date -u '+%F %T') $*"; }
say "pull w4-d (skip existing)"
python3 gcs_pull.py --key $K --bucket $B --prefix runs/w4-d --dest runs/w4-d --newest-ckpt-only --skip-existing 2>&1 | tail -1
verify() {
python3 - <<'PY'
import base64, hashlib, os, sys
from google.cloud import storage
c = storage.Client.from_service_account_json("/content/sts2/gcs-sts2-ckpt.json"); b = c.bucket("gomoku-zero-20260816-ai-sts2train")
bad = []
for rel in ["status.json", "metrics.jsonl", "evals.jsonl", "train.log", "config.json", "episodes.jsonl", "cycles.jsonl",
            "checkpoints/ckpt-0008664.pt", "checkpoints/pinned-0008664.pt", "evals/endpoint/u0008164-ab512-greedy-off/report.json",
            "evals/endpoint/u0008664-ab512-greedy-off/report.json"]:
    bl = b.get_blob(f"runs/w4-d/{rel}"); p = f"/content/sts2/runs/w4-d/{rel}"
    loc = base64.b64encode(hashlib.md5(open(p, "rb").read()).digest()).decode() if os.path.exists(p) else None
    ok = bl is not None and loc == bl.md5_hash
    print(rel, "OK" if ok else f"MISMATCH local={loc} bucket={bl.md5_hash if bl else None}")
    if not ok and bl is not None:
        os.makedirs(os.path.dirname(p), exist_ok=True); bl.download_to_filename(p); print("  re-downloaded", rel); bad.append(rel)
sys.exit(1 if bad else 0)
PY
}
say "verify md5 vs bucket (pass 1)"; verify || { say "verify md5 vs bucket (pass 2)"; verify || { say "RESTORE STILL INCONSISTENT: NOT starting"; exit 1; }; }
( cd runs/w4-d && [ -s status.json ] && [ -s metrics.jsonl ] && [ -s evals.jsonl ] ) || { say "RESTORE INCOMPLETE: NOT starting"; exit 1; }
python3 -c "import json;print('w4-d status update', json.load(open('runs/w4-d/status.json'))['update'])"
mkdir -p runs/w3m-b/checkpoints
[ -s runs/w3m-b/checkpoints/pinned-0007664.pt ] || python3 w3m-src/scripts/colab/w3m_fetch.py --key $K --bucket $B --blob runs/w3m-b/checkpoints/pinned-0007664.pt --dest runs/w3m-b/checkpoints/pinned-0007664.pt --wait 600 2>&1 | tail -1
[ -s runs/w3m-b/checkpoints/pinned-0007664.pt ] || { say "B fork checkpoint missing: NOT starting"; exit 1; }
grep -q "w3m-b: done" runs/w3m-b.driver.log 2>/dev/null || echo "$(date -u '+%F %T') w3m-b: done" >> runs/w3m-b.driver.log
setsid nohup bash /content/sts2/launch_d2.sh >> runs/w4-d.driver.log 2>&1 < /dev/null & disown
sleep 2
setsid nohup bash /content/sts2/launch_f.sh >> runs/w9-f.driver.log 2>&1 < /dev/null & disown
sleep 5; say "wrappers running: $(pgrep -f 'launch_[df]' | wc -l)"
say "RESUME-DONE"
