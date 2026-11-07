import sys,json,time,sqlite3,dataclasses,subprocess,hashlib,tempfile,os
from pathlib import Path
from decimal import Decimal,ROUND_CEILING
AUTH=BOUND=None
if len(sys.argv)>1:
 from ops_common import inputs
 AUTH,BOUND=sys.argv[1:3];authority,release=inputs(AUTH,BOUND)
sys.path.insert(0,"/data/raven/hack-sts2-modal-research/task114-phase-kl/slot-accounting-candidate")
from slot_budget import remaining
sys.path.insert(0,"/var/lib/sts2-modal-production/control-047-m/sim/tools")
from modal_research.store import Store
import modal
w=modal.Workspace.from_context().hydrate();sm=w.billing.summary();rates=w.billing.rates()
fids={"104":"fu-OpuDzkImcq8qdSKA94997A","109":"fu-rvPCtflk4V7gVDkMmNa1zu","110":"fu-12BqVnqAu3beEDtlkRwHaJ","113":"fu-F0kRZFdZs9mPckjbD8gINt","114train":"fu-AJtUKrtwWSFJBa8dezoD07","control_dev":"fu-vSjw4YetEiIz9Z4Bb2xAxm","treatment_dev":"fu-OuRPYoXjAixVVBgpIEwQLy"}
st={k:dataclasses.asdict(modal.Function.from_id(v).hydrate().get_current_stats()) for k,v in fids.items()}
c=sqlite3.connect("file:/var/lib/sts2-modal-production/operation.sqlite3?mode=ro",uri=True);c.row_factory=sqlite3.Row;c.execute("PRAGMA query_only=ON");c.execute("BEGIN")
b=Store.__new__(Store).budget_evidence(c,dict(c.execute("SELECT * FROM study").fetchone()));state=remaining(c)
ledger_dir=Path(tempfile.mkdtemp(prefix="task115-ledger-proof.",dir="/data/raven/hack-sts2-modal-research"));os.chmod(ledger_dir,0o700);ledger_path=ledger_dir/"ledger.sqlite3";dest=sqlite3.connect(ledger_path);c.backup(dest);dest.close();os.chmod(ledger_path,0o400);ledger_sha=hashlib.sha256(ledger_path.read_bytes()).hexdigest();c.rollback();c.close()
ceil=lambda x:int((Decimal(str(x))*100).to_integral_value(rounding=ROUND_CEILING))
gross=max(15100,ceil(sm.metered_cost),b["spent_and_reserved_cents"][0]);net=max(12092,ceil(sm.billed_cost),b["platform_snapshot_cents"]["net_spent"])
snap=b["platform_snapshot_cents"];held=b["spent_and_reserved_cents"][1];mh=b["modal_spent_and_reserved_cents"][1]

state114=state;assert state114['remaining_commitment_cents']==0
rr={k:str(rates[k])for k in ['gpu_hour_cost_h100','cpu_hour_cost','mem_gib_hour_cost','cpu_hour_cost_sandbox','mem_gib_hour_cost_sandbox','volume_storage_gib_month_cost','egress_gib_cost']}
p={k:Decimal(v)for k,v in rr.items()};hour=p['gpu_hour_cost_h100']+32*p['cpu_hour_cost']+64*p['mem_gib_hour_cost']
train=ceil(hour*Decimal(9080)/3600+Decimal(1)*2*p['volume_storage_gib_month_cost']+32*Decimal('.375')*5/28*p['volume_storage_gib_month_cost']+4*p['volume_storage_gib_month_cost']+32*Decimal('.375')*p['egress_gib_cost'])
ehour=p['gpu_hour_cost_h100']+16*p['cpu_hour_cost']+64*p['mem_gib_hour_cost'];dev=ceil(ehour*1200/3600+(Decimal('.5')*2+Decimal('.5')*5/28+4)*p['volume_storage_gib_month_cost']+Decimal('.5')*p['egress_gib_cost'])
blindcompute=(p['gpu_hour_cost_h100']+16*p['cpu_hour_cost_sandbox']+64*p['mem_gib_hour_cost_sandbox'])*4800/3600
rb=3*163461760+512*1024**2+2*512*1024+16*1024**2;tb=rb+2*163461760
blind=max(1075,ceil(blindcompute+Decimal(rb)/1024**3*(2+Decimal(4)/28+Decimal(5)/28)*p['volume_storage_gib_month_cost']+Decimal(tb)/1024**3*p['egress_gib_cost']))
project=30000-gross-held-1000-2926;platform=min(snap['gross_limit']-gross,snap['net_limit']-net)-mh-1000-2926
quiet=all(x['num_running_inputs']==x['backlog']==x['num_total_runners']==0 for x in st.values())
timers={}
for u in ['sts2-modal-production-tick','sts2-modal-production-agent','sts2-modal-blind-final','sts2-modal-monitor']:
 for ext,cmd,valid in [('.timer','is-enabled',{'disabled'}),('.timer','is-active',{'inactive'}),('.service','is-active',{'inactive','failed'})]:
  q=subprocess.run(['systemctl',cmd,u+ext],capture_output=True,text=True,timeout=10);assert q.stdout.strip()in valid;timers[u+ext+':'+cmd]=q.stdout.strip()
assert train<=1629 and dev<=222 and blind<=1075
out={'schema':'task115-first-segment-readonly-funding-v2','root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'segment_scope_sha256':'751b140b38e7fe77d2b5a6ac9fd50a20feacb449bf4de561d56894208e05e8e2','authoritative_ledger_path':str(ledger_path),'authoritative_ledger_sha256':ledger_sha,'funding_source_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'status':'PASS'if quiet and min(project,platform)>=0 else'BLOCKED_FUNDS_OR_ACTIVE','observed_at':time.time(),'raw_gross_usd':str(sm.metered_cost),'raw_net_usd':str(sm.billed_cost),'gross_effective_cents':gross,'net_effective_cents':net,'cycle_start':sm.start.date().isoformat(),'rates':rr,'old_effective_holds_cents':held,'old_modal_holds_cents':mh,'archive_commitment_cents':1000,'proposed_commitment_cents':2926,'allocations':{'segment1_train':1629,'final_dev':222,'global_blind_protection':1075},'quote_train_cents':train,'quote_dev_cents':dev,'quote_blind_cents':blind,'project_room_after_cents':project,'platform_room_after_cents':platform,'platform_snapshot':snap,'owned_function_stats':st,'no_other_paid_active':quiet,'old_services':timers,'task114_remaining_commitment_cents':0,'seed_range':[1099649195878,1099783413606],'prior_reserved_end':1099649195878,'seed_evidence':'exact114bound27b445 bothreservedranges; no tail reclamation; external live grant CAS before send','second_segment_committed':False,'science_plan_sha256':'548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb','ledger_mutations':0,'metadata_creates':0,'new_compute':0,'actual_cost':None}
print(json.dumps(out,sort_keys=True,separators=(',',':')))
