#!/usr/bin/env python3
"""gcs_pull.py --key KEY --bucket B --prefix runs/sp2-w0 --dest /content/sts2/runs/sp2-w0 [--newest-ckpt-only]
Restore a run directory from its GCS mirror (VM side). Downloads every object under the prefix; with
--newest-ckpt-only, only the highest-numbered ckpt-*.pt plus pinned-*.pt among the checkpoints."""
import argparse, os, re
from google.cloud import storage
a = argparse.ArgumentParser(); a.add_argument("--key", required=True); a.add_argument("--bucket", required=True)
a.add_argument("--prefix", required=True); a.add_argument("--dest", required=True); a.add_argument("--newest-ckpt-only", action="store_true")
a.add_argument("--skip-existing", action="store_true", help="skip objects whose local file exists with the same size (resume an interrupted pull)")
o = a.parse_args()
c = storage.Client.from_service_account_json(o.key); b = c.bucket(o.bucket)
blobs = list(c.list_blobs(o.bucket, prefix=o.prefix.rstrip("/") + "/"))
ck = [x for x in blobs if re.search(r"/checkpoints/ckpt-\d+\.pt$", x.name)]
keep = set(x.name for x in blobs)
if o.newest_ckpt_only and ck:
    newest = max(ck, key=lambda x: int(re.search(r"ckpt-(\d+)\.pt$", x.name).group(1)))
    for x in ck:
        if x.name != newest.name: keep.discard(x.name)
n = 0; nb = 0
for x in blobs:
    if x.name not in keep: continue
    rel = x.name[len(o.prefix.rstrip("/")) + 1:]
    p = os.path.join(o.dest, rel); os.makedirs(os.path.dirname(p), exist_ok=True)
    if o.skip_existing and os.path.isfile(p) and os.path.getsize(p) == (x.size or 0): continue
    x.download_to_filename(p); n += 1; nb += x.size or 0
    print("got", rel, x.size)
print(f"restored {n} files, {nb/1e6:.1f} MB into {o.dest}")
