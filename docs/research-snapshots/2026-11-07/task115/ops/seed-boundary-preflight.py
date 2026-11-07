import sqlite3,json,time,hashlib
from pathlib import Path
c=sqlite3.connect("file:/var/lib/sts2-modal-production/operation.sqlite3?mode=ro",uri=True);c.row_factory=sqlite3.Row;c.execute("PRAGMA query_only=ON");c.execute("BEGIN")
a=[]
for row in c.execute("SELECT job_id,grant_json FROM broker_detached"):
 d=json.loads(row["grant_json"]);seed=d.get("seed_range")
 if seed:a.append(seed)
raw=Path("/data/raven/hack-sts2-modal-research/task114-phase-kl/release.bound.json").read_bytes();assert hashlib.sha256(raw).hexdigest()=="27b44591673409459d89a95c3e148cbc2da85919671e938469e8a5312d5d4f8b";r=json.loads(raw)
proof={"at":time.time(),"broker_detached_range_count":len(a),"broker_max_raw_end":max([x[1]for x in a]or[0]),"manual114_ranges":{k:v["seed_range"]for k,v in r["arms"].items()},"first115_range":[1099649195878,1099783413606],"old_reserved_end":1099649195878,"overlap_with_broker":any(x[0]<1099783413606 and 1099649195878<x[1] for x in a),"no_115_event_yet":c.execute("SELECT count(*) FROM events WHERE id=?",("operator-task115-first-segment-budget-commitment-v1",)).fetchone()[0]==0,"ledger_mutations":0}
assert not proof["overlap_with_broker"] and proof["no_115_event_yet"] and max(v["seed_range"][1]for v in r["arms"].values())==1099649195878
print(json.dumps(proof,sort_keys=True));c.rollback()
