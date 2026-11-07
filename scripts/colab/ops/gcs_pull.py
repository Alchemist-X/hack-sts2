#!/usr/bin/env python3
"""gcs_pull.py --key KEY --bucket B --prefix runs/sp2-w0 --dest /content/sts2/runs/sp2-w0 [--newest-ckpt-only]
            [--pinned all|newest|none] [--skip-existing] [--workers N]
Restore a run directory from its GCS mirror (VM side). Downloads every object under the prefix; with
--newest-ckpt-only, only the highest-numbered ckpt-*.pt among the checkpoints; --pinned newest keeps only the
highest-numbered pinned-*.pt (a line pins every 100 updates: ~35 MB each, 1.6 GB for a 4600-update line), none drops
them all; --skip-existing skips objects whose local file exists with the same size (resume an interrupted pull);
--workers N downloads N objects concurrently (default 8; 1 = the original sequential behaviour)."""
import argparse, os, re
from concurrent.futures import ThreadPoolExecutor
from google.cloud import storage
a = argparse.ArgumentParser(); a.add_argument("--key", required=True); a.add_argument("--bucket", required=True)
a.add_argument("--prefix", required=True); a.add_argument("--dest", required=True); a.add_argument("--newest-ckpt-only", action="store_true")
a.add_argument("--pinned", choices=("all", "newest", "none"), default="all")
a.add_argument("--skip-existing", action="store_true", help="skip objects whose local file exists with the same size (resume an interrupted pull)")
a.add_argument("--workers", type=int, default=8)
o = a.parse_args()
c = storage.Client.from_service_account_json(o.key); b = c.bucket(o.bucket)
blobs = list(c.list_blobs(o.bucket, prefix=o.prefix.rstrip("/") + "/"))
ck = [x for x in blobs if re.search(r"/checkpoints/ckpt-\d+\.pt$", x.name)]
pn = [x for x in blobs if re.search(r"/checkpoints/pinned-\d+\.pt$", x.name)]
keep = set(x.name for x in blobs)
if o.newest_ckpt_only and ck:
    newest = max(ck, key=lambda x: int(re.search(r"ckpt-(\d+)\.pt$", x.name).group(1)))
    for x in ck:
        if x.name != newest.name: keep.discard(x.name)
if o.pinned != "all" and pn:
    newest_p = max(pn, key=lambda x: int(re.search(r"pinned-(\d+)\.pt$", x.name).group(1)))
    for x in pn:
        if o.pinned == "none" or x.name != newest_p.name: keep.discard(x.name)
todo = []
for x in blobs:
    if x.name not in keep: continue
    rel = x.name[len(o.prefix.rstrip("/")) + 1:]
    p = os.path.join(o.dest, rel); os.makedirs(os.path.dirname(p), exist_ok=True)
    if o.skip_existing and os.path.isfile(p) and os.path.getsize(p) == (x.size or 0): continue
    todo.append((x, rel, p))
def get(t):
    x, rel, p = t
    tmp = p + ".part"; x.download_to_filename(tmp); os.replace(tmp, p)
    return rel, x.size or 0
n = 0; nb = 0
with ThreadPoolExecutor(max_workers=max(1, o.workers)) as ex:
    for rel, sz in ex.map(get, todo):
        n += 1; nb += sz; print("got", rel, sz)
print(f"restored {n} files, {nb/1e6:.1f} MB into {o.dest} ({len(blobs) - len(todo)} skipped/filtered)")
