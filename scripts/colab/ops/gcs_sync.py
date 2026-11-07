#!/usr/bin/env python3
"""gcs_sync.py - mirror a sts2train run directory to a GCS bucket (runs on the Colab VM).

Usage: gcs_sync.py --key KEY.json --bucket BUCKET --prefix PREFIX --run RUN_DIR [--interval 60] [--min-age 60] [--once]
Uploads checkpoints/*.pt (only when older than --min-age seconds, so a checkpoint still being written is skipped),
plus metrics.jsonl, evals.jsonl, status.json, config.json, summary.json, train.log, spec.json and evals/** whenever
their size or mtime changed. Upload state is kept in <run>/.gcs_sync_state.json; nothing is ever deleted remotely,
so checkpoints pruned locally by --keep-ckpts stay in the bucket.
"""
import argparse, json, os, time
from google.cloud import storage

SMALL = ["metrics.jsonl", "evals.jsonl", "cycles.jsonl", "episodes.jsonl", "blind-access.jsonl", "status.json", "config.json", "summary.json", "train.log", "spec.json", "curve.png"]

def iter_files(run):
    for name in SMALL:
        p = os.path.join(run, name)
        if os.path.isfile(p):
            yield p, False
    ck = os.path.join(run, "checkpoints")
    if os.path.isdir(ck):
        for f in sorted(os.listdir(ck)):
            if f.endswith(".pt"):
                yield os.path.join(ck, f), True
    ev = os.path.join(run, "evals")
    if os.path.isdir(ev):
        for root, _, files in os.walk(ev):
            for f in files:
                yield os.path.join(root, f), False

def now_s():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key", required=True); ap.add_argument("--bucket", required=True)
    ap.add_argument("--prefix", required=True); ap.add_argument("--run", required=True)
    ap.add_argument("--interval", type=float, default=60); ap.add_argument("--min-age", type=float, default=60)
    ap.add_argument("--once", action="store_true")
    a = ap.parse_args()
    client = storage.Client.from_service_account_json(a.key)
    bucket = client.bucket(a.bucket)
    run = os.path.abspath(a.run.rstrip("/"))
    state_path = os.path.join(run, ".gcs_sync_state.json")
    state = json.load(open(state_path)) if os.path.exists(state_path) else {}
    print(f"{now_s()} gcs_sync start run={run} -> gs://{a.bucket}/{a.prefix}/{os.path.basename(run)}/", flush=True)
    while True:
        now = time.time(); n = 0; nbytes = 0
        for path, is_ckpt in iter_files(run):
            try:
                st = os.stat(path)
            except FileNotFoundError:
                continue
            if is_ckpt and now - st.st_mtime < a.min_age:
                continue
            rel = os.path.relpath(path, run)
            sig = f"{st.st_size}:{int(st.st_mtime)}"
            if state.get(rel) == sig:
                continue
            blob = bucket.blob(f"{a.prefix}/{os.path.basename(run)}/{rel}")
            t0 = time.time()
            try:
                blob.upload_from_filename(path, timeout=600)
            except Exception as e:  # transient network errors: retry next cycle
                print(f"{now_s()} FAIL {rel}: {e}", flush=True)
                continue
            state[rel] = sig; n += 1; nbytes += st.st_size
            print(f"{now_s()} up {rel} {st.st_size} B {time.time()-t0:.1f}s", flush=True)
            tmp = state_path + ".tmp"
            with open(tmp, "w") as f:
                json.dump(state, f)
            os.replace(tmp, state_path)
        if n:
            print(f"{now_s()} cycle done: {n} files {nbytes/1e6:.1f} MB", flush=True)
        if a.once:
            break
        time.sleep(a.interval)

if __name__ == "__main__":
    main()
