#!/usr/bin/env python3
"""w3m_fetch.py --key KEY --bucket B (--blob runs/sp3-main/checkpoints/pinned-0003064.pt --dest FILE [--wait S]
                                       | --prefix runs/w3m-a/evals --dest DIR)
VM side (docs/w3m-overnight-plan-2026-11-04.md). --blob: wait (poll every 60 s, up to --wait seconds) until the object
exists and its size is stable across two polls (gcs_sync uploads a checkpoint only once it is >= 60 s old), then
download it to FILE (tmp + rename) and print its sha256. --prefix: download every object under the prefix into DIR
(evals only: no checkpoints), overwriting, for `w3m_compare.sh`."""
import argparse, hashlib, os, sys, time
from google.cloud import storage

a = argparse.ArgumentParser()
a.add_argument("--key", required=True); a.add_argument("--bucket", required=True)
a.add_argument("--blob"); a.add_argument("--prefix"); a.add_argument("--dest", required=True)
a.add_argument("--wait", type=float, default=14400)
o = a.parse_args()
c = storage.Client.from_service_account_json(o.key); b = c.bucket(o.bucket)
if o.blob:
    t0, last = time.time(), None
    while True:
        x = b.get_blob(o.blob)
        size = x.size if x is not None else None
        if size and size == last:
            break
        if time.time() - t0 > o.wait:
            sys.exit(f"gs://{o.bucket}/{o.blob} not available (stable) after {o.wait:.0f} s")
        print(f"{time.strftime('%F %T', time.gmtime())} waiting for gs://{o.bucket}/{o.blob} (size {size})", flush=True)
        last = size; time.sleep(60)
    os.makedirs(os.path.dirname(os.path.abspath(o.dest)), exist_ok=True)
    tmp = o.dest + ".tmp"; x.download_to_filename(tmp); os.replace(tmp, o.dest)
    h = hashlib.sha256(open(o.dest, "rb").read()).hexdigest()
    print(f"got {o.blob} -> {o.dest} ({os.path.getsize(o.dest)} B) sha256 {h}")
else:
    n = 0
    for x in c.list_blobs(o.bucket, prefix=o.prefix.rstrip("/") + "/"):
        if x.name.endswith(".pt"):
            continue
        p = os.path.join(o.dest, x.name[len(o.prefix.rstrip("/")) + 1:])
        os.makedirs(os.path.dirname(p), exist_ok=True); x.download_to_filename(p); n += 1
    print(f"restored {n} files under {o.dest}")
