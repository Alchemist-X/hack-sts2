"""Future explicit read-only SDK/SQLite admission. Import itself has no side effects."""
from ops_common import *
from decimal import Decimal,ROUND_CEILING
import sqlite3,dataclasses,subprocess

def quote_cents(rates):
 d={k:Decimal(str(rates[k]))for k in ('gpu_hour_cost_h100','cpu_hour_cost','mem_gib_hour_cost','volume_storage_gib_month_cost','egress_gib_cost')}
 require(all(v>0 for v in d.values()),'missing/nonpositive actual Function rates')
 hour=d['gpu_hour_cost_h100']+16*d['cpu_hour_cost']+64*d['mem_gib_hour_cost']
 return int(((hour*1200/3600+(Decimal('.5')*2+Decimal('.5')*5/28+4)*d['volume_storage_gib_month_cost']+Decimal('.5')*d['egress_gib_cost'])*100).to_integral_value(rounding=ROUND_CEILING))

def current_shared_liabilities(db,reader):
 from project_obligations import M_PLAN,shared_liabilities
 row=db.execute('SELECT payload FROM events WHERE id=?',(M_PLAN,)).fetchone()
 require(row is not None,'known actual M116 commitment missing; never assume zero')
 m=json.loads(row[0]);require(m.get('bound_release_sha256')=='6fc6f2544c6196546fe31b9796f73b3aca1aac57b7c2766c0d9454fe9dc1c8a6','known actual M116 commitment bound differs')
 return shared_liabilities(db,s115_reader=reader)

M_FUNCTION='fu-yOQraWLl74vW59NtASwaVu'
S2_FUNCTION='fu-bFBXeKUlFeKA92fDLTcVvX'
def inventory_view(ids,stats):
 require(ids.get('116M')==M_FUNCTION and ids.get('115segment2')==S2_FUNCTION,'exact actual M/S2 inventory required')
 for k,v in stats.items():
  vals=[v[n] for n in ('num_running_inputs','num_total_runners','backlog')]
  require(all(type(x)is int and x>=0 for x in vals),'unknown function stats')
  if k=='116M':require(vals[0]<=1 and vals[1]<=1 and vals[2]==0,'M concurrency outside explicit single-call scope')
  else:require(vals==[0,0,0],'all non-M functions including S2 must be paidquiet')
 return True

def financial_view(evidence,state,state114,*,gross,net,shared):
 from project_obligations import PLATFORM_POLICY
 require(state['first_remaining_allocations'].get('final_dev')==222 and state['first_remaining_allocations'].get('global_blind_protection')==1075,'existing222dev/1075blind must remain, never newly pledge')
 require(shared['S115']==state and shared['M116_active_commitment_cents']>=0,'shared exact reader mismatch')
 held=evidence['spent_and_reserved_cents'][1];mh=evidence['modal_spent_and_reserved_cents'][1]
 plans=shared['total_direct_commitments_cents']+state114['remaining_commitment_cents']
 return {'project_cap_cents':60000,'platform_admission_policy':PLATFORM_POLICY,'project_room_after_all_existing_holds_cents':60000-gross-held-1000-plans,'platform_room_after_all_existing_holds_cents':None,'platform_caps_historical_only':True,'existing_plan_commitments_cents':plans,'ledger_existing_holds_cents':held,'ledger_modal_holds_cents':mh,'global_blind_protected_cents':1075,'shared_blind_increment_cents':shared['shared_blind_increment_in_M_cents'],'M_active_allocations':shared['M116_active_allocations'],'archive_commitment_cents':1000,'old_unknown_retained':True}

def refresh(auth_sha,release_sha,phase,*,output_name=None):
 a,r=inputs(auth_sha,release_sha,phase=phase,gate=False)
 # No SDK import before all actual terminal/phase/root/code bindings pass.
 import modal
 require(modal.__version__=='1.6.3','approved SDK only')
 from plan_liabilities import liabilities
 from task114_slot_budget import remaining as remaining114
 from project_obligations import shared_liabilities,PROJECT_APPROVAL,PLATFORM_POLICY
 control=Path('/var/lib/sts2-modal-production/control-047-m/sim/tools')
 require(H((control/'modal_research/store.py').read_bytes())=='2d5c4ba55d694e48513c72c1b84da806fc42f068f64df31e0f66d9441403cd7c','actual Store preimage differs')
 sys.path.insert(0,str(control));from modal_research.store import Store
 ws=modal.Workspace.from_context().hydrate();require(ws.name=='hycrpg','exact workspace')
 bill=ws.billing.summary();rates=ws.billing.rates();now=time.time();quote=quote_cents(rates);require(quote<=222,'fresh complete dev quote exceeds existing222; hold, no augment')
 required={'104','109','110','113','114train','114control_dev','114treatment_dev','115segment1','115segment2','116M'}
 require(required<=set(a['owned_function_ids']) and all(re.fullmatch('fu-[A-Za-z0-9]+',v)for v in a['owned_function_ids'].values()),'complete rootbound owned-function inventory')
 stats={k:dataclasses.asdict(modal.Function.from_id(v).hydrate().get_current_stats())for k,v in a['owned_function_ids'].items()}
 quiet=inventory_view(a['owned_function_ids'],stats)
 db=sqlite3.connect('file:/var/lib/sts2-modal-production/operation.sqlite3?mode=ro',uri=True);db.row_factory=sqlite3.Row;db.execute('PRAGMA query_only=ON');db.execute('BEGIN')
 try:
  state=liabilities(db);state114=remaining114(db);shared=current_shared_liabilities(db,liabilities)
  study=dict(db.execute('SELECT * FROM study').fetchone());require(study['approved_budget']==study['execution_cap']==60000 and study['approval_ref']==PROJECT_APPROVAL,'canonical approved600 budget required')
  require(not db.execute('SELECT 1 FROM events WHERE id=?',('operator-task115-second-segment-budget-commitment-v1',)).fetchone()or '115segment2'in a['owned_function_ids'],'any second-segment function must remain in the owned quiet inventory')
  require(not db.execute('SELECT 1 FROM events WHERE id=?',(DISPATCH,)).fetchone(),'finaldev already claimed; unknown acknowledgement never resubmits')
  b=Store.__new__(Store).budget_evidence(db,dict(db.execute('SELECT * FROM study').fetchone()));ledger_sha=H(db.serialize())
 finally:db.rollback();db.close()
 cents=lambda value:int((Decimal(str(value))*100).to_integral_value(rounding=ROUND_CEILING))
 gross=max(a['minimum_gross_cents'],cents(bill.metered_cost),b['spent_and_reserved_cents'][0]);net=max(a['minimum_net_cents'],cents(bill.billed_cost),b['platform_snapshot_cents']['net_spent'])
 cycle_start=bill.start.date().isoformat()
 for oldname in ('metadata-admission.json','deploy-admission.json','send-admission.json'):
  old=W/oldname
  if old.exists():
   prior=json.loads(old.read_bytes())
   if prior.get('cycle_start')==cycle_start:
    gross=max(gross,prior['gross_effective_cents']);net=max(net,prior['net_effective_cents'])
 view=financial_view(b,state,state114,gross=gross,net=net,shared=shared);require(view['project_room_after_all_existing_holds_cents']>=0,'project600 all old holds+plans+blind+archive retained')
 timers={}
 for u in ('sts2-modal-production-tick','sts2-modal-production-agent','sts2-modal-blind-final','sts2-modal-monitor'):
  for suffix,op,valid in (('.timer','is-enabled',{'disabled'}),('.timer','is-active',{'inactive'}),('.service','is-active',{'inactive','failed'})):
   q=subprocess.run(['systemctl',op,u+suffix],capture_output=True,text=True,timeout=10);require(q.stdout.strip()in valid,'old dispatcher must remain stopped');timers[u+suffix+':'+op]=q.stdout.strip()
 proof={'status':'PASS','observed_at':now,'root_eval_authority_sha256':auth_sha,'bound_release_sha256':release_sha,'funding_source_sha256':H(Path(__file__).read_bytes()),'liabilities_source_sha256':H((W/'plan_liabilities.py').read_bytes()),'authoritative_ledger_state_sha256':ledger_sha,'existing_plan_event':PLAN,'slot':'final_dev','slot_unconsumed':True,'existing_slot_cents':222,'new_pledge_cents':0,'quote_cents':quote,'gross_effective_cents':gross,'net_effective_cents':net,'cycle_start':cycle_start,'no_other_paid_active':quiet,'owned_function_stats':stats,'rates':{k:str(rates[k])for k in ('gpu_hour_cost_h100','cpu_hour_cost','mem_gib_hour_cost','volume_storage_gib_month_cost','egress_gib_cost')},'old_services':timers,'original_plan_liabilities':state,'shared_liabilities':shared,'project_obligations_source_sha256':H((W/'project_obligations.py').read_bytes()),'allowed_M_concurrency_only':True,'ledger_mutations':0,**view}
 check_admission(proof,a,r,auth_sha,release_sha,now=time.time());save(output_name or phase+'-admission.json',proof);return proof
if __name__=='__main__':print(CAN(refresh(*sys.argv[1:4])))
