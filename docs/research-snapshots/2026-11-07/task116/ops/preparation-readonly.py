import sys,json,time,sqlite3,dataclasses,subprocess
from pathlib import Path
from decimal import Decimal,ROUND_CEILING
sys.path.insert(0,"/data/raven/hack-sts2-modal-research/task114-phase-kl/slot-accounting-candidate")
from slot_budget import remaining
sys.path.insert(0,"/var/lib/sts2-modal-production/control-047-m/sim/tools")
from modal_research.store import Store
import modal
w=modal.Workspace.from_context().hydrate();sm=w.billing.summary();rates=w.billing.rates()
fids={"104":"fu-OpuDzkImcq8qdSKA94997A","109":"fu-rvPCtflk4V7gVDkMmNa1zu","110":"fu-12BqVnqAu3beEDtlkRwHaJ","113":"fu-F0kRZFdZs9mPckjbD8gINt","114train":"fu-AJtUKrtwWSFJBa8dezoD07","115segment1":"fu-7uOTlWd7wtCXAOI4fkA6yI","control_dev":"fu-vSjw4YetEiIz9Z4Bb2xAxm","treatment_dev":"fu-OuRPYoXjAixVVBgpIEwQLy"}
st={k:dataclasses.asdict(modal.Function.from_id(v).hydrate().get_current_stats()) for k,v in fids.items()}
c=sqlite3.connect("file:/var/lib/sts2-modal-production/operation.sqlite3?mode=ro",uri=True);c.row_factory=sqlite3.Row;c.execute("PRAGMA query_only=ON");c.execute("BEGIN")
b=Store.__new__(Store).budget_evidence(c,dict(c.execute("SELECT * FROM study").fetchone()));state=remaining(c);c.rollback();c.close()
ceil=lambda x:int((Decimal(str(x))*100).to_integral_value(rounding=ROUND_CEILING))
gross=max(15100,ceil(sm.metered_cost),b["spent_and_reserved_cents"][0]);net=max(12092,ceil(sm.billed_cost),b["platform_snapshot_cents"]["net_spent"])
snap=b["platform_snapshot_cents"];held=b["spent_and_reserved_cents"][1];mh=b["modal_spent_and_reserved_cents"][1]

import importlib.util
spec=importlib.util.spec_from_file_location('task115financial','/data/raven/hack-sts2-modal-research/task115-first-segment-stage/terminal-accounting-code/slot_budget.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
c=sqlite3.connect("file:/var/lib/sts2-modal-production/operation.sqlite3?mode=ro",uri=True);c.row_factory=sqlite3.Row;c.execute("PRAGMA query_only=ON");c.execute("BEGIN");s115=mod.remaining(c);c.rollback();c.close()
existing=s115['remaining_commitment_cents'];quoteview={'project_room_before_M_cents':30000-gross-held-1000-existing,'platform_room_before_M_cents':min(snap['gross_limit']-gross,snap['net_limit']-net)-mh-1000-existing}
rateskeys=['gpu_hour_cost_h100','cpu_hour_cost','mem_gib_hour_cost','cpu_hour_cost_sandbox','mem_gib_hour_cost_sandbox','volume_storage_gib_month_cost','egress_gib_cost'];rr={k:str(rates[k])for k in rateskeys}
d={'observed_at':time.time(),'raw_gross_usd':str(sm.metered_cost),'raw_net_usd':str(sm.billed_cost),'gross_basis_cents':gross,'net_basis_cents':net,'owned_function_stats':st,'permitted_existing_S115_active':st['115segment1'],'other_known_owned_active':{k:v for k,v in st.items()if k!='115segment1'and any(v[x]for x in ['num_running_inputs','backlog','num_total_runners'])},'original114_remaining_cents':state['remaining_commitment_cents'],'original115_remaining_cents':existing,'original115_remaining_allocations':s115['remaining_allocations'],'old_effective_unknown_holds_cents':held,'old_modal_unknown_holds_cents':mh,'archive_commitment_cents':1000,'blind_protection_already_in_115_cents':1075,'platform_caps_current_ledger_evidence':snap,'fresh_limits_read':False,'rates':rr,'current_ledger_actual_costs_unknown_preserved':True,'resource_context':{'M_requested':'1H100!/32CPU/64GiB','S115_keep_running':True,'M_no_resources_created':True,'workspace_GPU_quota_not_read':True},'ledger_mutations':0,'metadata_creates':0,'new_compute':0,**quoteview}
print(json.dumps(d,sort_keys=True,separators=(',',':')))
