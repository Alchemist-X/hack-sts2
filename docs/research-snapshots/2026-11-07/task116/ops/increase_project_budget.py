"""Root-authorized canonical Store.approve increase; no fee/jobs/platform-setting changes."""
from pathlib import Path
import json,hashlib,sqlite3,time,sys,fcntl,os
R=Path('/var/lib/sts2-modal-production');W=Path(__file__).resolve().parent;H=lambda b:hashlib.sha256(b).hexdigest();CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'))
AUTH=sys.argv[1];raw=(W/'root-budget-increase-authorization.json').read_bytes();assert H(raw)==AUTH;a=json.loads(raw)
assert a['action']=='INCREASE_PROJECT_BUDGET_FROM300_TO600' and (a['before_cents'],a['after_cents'],a['execution_cap_cents'])==(30000,60000,60000) and a['approval_ref']=='operator-project-budget-user600-task116-v1' and a['platform_policy']=='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT' and time.time()<a['expires']
assert a['implementation_sha256']==H(Path(__file__).read_bytes())
store_path=R/'control-047-m/sim/tools/modal_research/store.py';assert H(store_path.read_bytes())=='2d5c4ba55d694e48513c72c1b84da806fc42f068f64df31e0f66d9441403cd7c'
sys.path.insert(0,str(store_path.parent.parent));from modal_research.store import Store
fds=[]
try:
 for n in ['operation.tick.lock','operation.agent.lock']:
  fd=os.open(R/n,os.O_RDWR|os.O_NOFOLLOW);fds.append(fd);fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
 s=Store.__new__(Store);s.path=R/'operation.sqlite3';s.clock=time.time
 def readstate():
  c=sqlite3.connect('file:'+str(s.path)+'?mode=ro',uri=True);c.row_factory=sqlite3.Row;c.execute('PRAGMA query_only=ON');c.execute('BEGIN');study=dict(c.execute('SELECT * FROM study').fetchone());approvals=[dict(x)for x in c.execute('SELECT * FROM approvals ORDER BY rowid')];tables=[x[0]for x in c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT IN ('study','approvals','events')")];fp={t:H(CAN([dict(x)for x in c.execute('SELECT * FROM "'+t+'" ORDER BY rowid')]).encode())for t in tables};c.rollback();c.close();return study,approvals,fp
 before,oldapprovals,fp=readstate();assert (before['approved_budget'],before['execution_cap'])==(30000,30000),'existing increase: reconcile exactprior event; never reset'
 s.approve('600',a['approval_ref'],execution_cap_usd='600')
 after,newapprovals,afterfp=readstate();assert fp==afterfp and all(after[k]==v for k,v in before.items()if k not in {'approved_budget','approval_ref','execution_cap'})
 assert (after['approved_budget'],after['execution_cap'],after['approval_ref'])==(60000,60000,a['approval_ref'])
 assert all(x in newapprovals for x in oldapprovals)and len(newapprovals)==len(oldapprovals)+1
 payload={'authority_sha256':AUTH,'approval_ref':a['approval_ref'],'before_approved_cents':30000,'before_execution_cap_cents':30000,'after_approved_cents':60000,'after_execution_cap_cents':60000,'original_approval_history_preserved':True,'all_other_table_hashes_unchanged':True,'platform_snapshot_unchanged':after['platform_snapshot']==before['platform_snapshot'],'platform_policy_task116':a['platform_policy'],'all_unknown_actualNULL_and_commitments_preserved':True,'additional_authorization_is_not_spend':True,'S115_live_release_unchanged':True}
 with s.transaction()as c:
  assert not c.execute('SELECT 1 FROM events WHERE id=?',(a['approval_ref'],)).fetchone()
  Store.append_event(c,a['approval_ref'],'operator_project_budget_authorization_increase',payload,time.time())
 receipt={'status':'APPLIED_VIA_CANONICAL_APPROVE','event_id':a['approval_ref'],'at':time.time(),'payload':payload,'new_compute':0,'metadata_creates':0}
 with(W/'budget-increase-receipt.json').open('x')as f:f.write(CAN(receipt));f.flush();os.fsync(f.fileno())
 print(CAN(receipt))
finally:
 for fd in reversed(fds):os.close(fd)
