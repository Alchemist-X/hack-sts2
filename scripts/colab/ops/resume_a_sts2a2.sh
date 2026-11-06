#!/bin/bash
# resume_a_sts2a2.sh — one-shot, unattended, on the fresh A100 sts2a2: restore line A's run dirs needed now (w3m-a: the
# round-3 fork checkpoints; w4-c: round-2 line C, to continue it), verify against the bucket's md5, then start launch_c3.sh.
set -u
cd /content/sts2
K=/content/sts2/gcs-sts2-ckpt.json; B=gomoku-zero-20260816-ai-sts2train
say() { echo "$(date -u '+%F %T') $*"; }
for r in w3m-a w4-c; do
  say "pull $r (newest ckpt + pinned, skip existing)"
  python3 gcs_pull.py --key $K --bucket $B --prefix runs/$r --dest runs/$r --newest-ckpt-only --pinned newest --skip-existing --workers 8 2>&1 | tail -1
done
python3 - <<'PY'
import base64, hashlib, os, sys
from google.cloud import storage
c = storage.Client.from_service_account_json("/content/sts2/gcs-sts2-ckpt.json"); b = c.bucket("gomoku-zero-20260816-ai-sts2train")
checks = {"w4-c": ["status.json", "metrics.jsonl", "evals.jsonl", "train.log", "config.json", "episodes.jsonl", "checkpoints/pinned-0009164.pt",
                   "evals/endpoint/u0009164-ab512-greedy-off/report.json", "evals/endpoint/u0009164-ab512-greedy-on/report.json",
                   "evals/endpoint/u0009164-dev128-sample-off/repeats.json"],
          "w3m-a": ["checkpoints/pinned-0007664.pt", "status.json", "evals.jsonl"]}
bad = []
for run, rels in checks.items():
    for rel in rels:
        bl = b.get_blob(f"runs/{run}/{rel}"); p = f"/content/sts2/runs/{run}/{rel}"
        loc = base64.b64encode(hashlib.md5(open(p, "rb").read()).digest()).decode() if os.path.exists(p) else None
        ok = bl is not None and loc == bl.md5_hash
        print(run, rel, "OK" if ok else f"MISMATCH local={loc} bucket={bl.md5_hash if bl else None}")
        if not ok and bl is not None:
            os.makedirs(os.path.dirname(p), exist_ok=True); bl.download_to_filename(p); print("  re-downloaded", rel); bad.append(rel)
sys.exit(1 if bad else 0)
PY
rc=$?; [ $rc -ne 0 ] && { say "md5 pass 1 had re-downloads; NOT starting automatically — rerun to verify"; exit 1; }
python3 -c "import json;print('w4-c status update', json.load(open('runs/w4-c/status.json'))['update'])"
setsid nohup bash /content/sts2/launch_c3.sh >> runs/w4-c2.driver.log 2>&1 < /dev/null & disown
sleep 5; say "launch_c3 running: $(pgrep -f 'launch_c3.s[h]' | wc -l)"
say "RESUME-DONE"
