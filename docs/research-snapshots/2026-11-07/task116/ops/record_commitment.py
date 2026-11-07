from ops_common import *
import subprocess
from decimal import Decimal
from project_obligations import shared_liabilities,M_PLAN,PROJECT_APPROVAL
from plan_liabilities import liabilities
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND);assert a['allow_dynamic_fresh_admission']is True and a['allow_commit_and_spawn']is True
assert not(W/'fresh-precommit-funding.json').exists(),'previous precommit attempt: reconcile'
assert H((W/'funding_snapshot.py').read_bytes())==a['ops_file_sha256']['funding_snapshot.py']
q=subprocess.run([sys.executable,'-B',str(W/'funding_snapshot.py'),AUTH,BOUND],capture_output=True,check=True);f=json.loads(q.stdout);save('fresh-precommit-funding.json',f)
assert f['status']=='PASS'and f['root_authority_sha256']==AUTH and f['bound_release_sha256']==BOUND and f['root_scope_sha256']==SCOPE and f['source_sha256']==a['ops_file_sha256']['funding_snapshot.py']and f['new_commitment_cents']==5762 and f['new_allocations']=={'train':3992,'final_dev':744,'shared_blind_increment':1026}and time.time()-f['observed_at']<120
assert H(Path(f['authoritative_ledger_path']).read_bytes())==f['authoritative_ledger_sha256']
R=Path('/var/lib/sts2-modal-production');assert H((R/'control-047-m/sim/tools/modal_research/store.py').read_bytes())=='2d5c4ba55d694e48513c72c1b84da806fc42f068f64df31e0f66d9441403cd7c';sys.path.insert(0,str(R/'control-047-m/sim/tools'));from modal_research.store import Store
s=Store.__new__(Store);s.path=R/'operation.sqlite3';s.clock=time.time;rr=f['rates'];s.record_metered_usage(gross_used=str(Decimal(f['gross_effective_cents'])/100),net_spent=str(Decimal(f['net_effective_cents'])/100),cycle_start=f['cycle_start'],observed_at=f['observed_at'],rates={'gpu_per_second':str(Decimal(rr['gpu_hour_cost_h100'])/3600),'cpu_per_core_second':str(Decimal(rr['cpu_hour_cost_sandbox'])/3600),'memory_per_gib_second':str(Decimal(rr['mem_gib_hour_cost_sandbox'])/3600),'volume_per_gib_month':rr['volume_storage_gib_month_cost']})
NAMESPACE_EVENT='operator-task116-namespace-reservation-v1'
with s.transaction()as c:
 state=dict(c.execute('SELECT * FROM study').fetchone());assert state['approved_budget']==state['execution_cap']==60000 and state['approval_ref']==PROJECT_APPROVAL
 assert not c.execute('SELECT 1 FROM events WHERE id IN (?,?)',(M_PLAN,NAMESPACE_EVENT)).fetchone(),'existing plan/namespace: reconcile only'
 prior=shared_liabilities(c,s115_reader=liabilities);b=s.budget_evidence(c,state);gross,held=b['spent_and_reserved_cents'];assert 60000-gross-held-1000-prior['total_direct_commitments_cents']-5762>=0
 for row in c.execute("SELECT payload FROM events WHERE kind='operator_training_namespace_reservation'"):
  assert json.loads(row[0]).get('namespace')!='MODA10PH116MFRESH1','namespace already reserved'
 namespace={'namespace':'MODA10PH116MFRESH1','seed_range':[0,134217728],'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'train_manifest_sha256':r['spec']['train_seed_manifest_hash'],'status':'RESERVED_FOR_ONE_FRESH_M_CALL','no_reuse_after_unknown_ack':True}
 Store.append_event(c,NAMESPACE_EVENT,'operator_training_namespace_reservation',namespace,time.time())
 p={'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':5762,'active_plan_commitment_cents':5762,'slot_allocations':{'train':3992,'final_dev':744,'shared_blind_increment':1026},'full_shared_blind_quote_cents':2101,'existing_global_blind_base_cents':1075,'actual_cost':None,'project_budget_approval_ref':PROJECT_APPROVAL,'project_cap_cents':60000,'platform_admission_policy':POLICY,'existing_direct_obligations':prior,'archive_offledger_commitment_cents':1000,'funding_evidence_sha256':H((W/'fresh-precommit-funding.json').read_bytes()),'max_training_submissions':1,'no_auto_successor':True}
 Store.append_event(c,M_PLAN,'operator_M_fresh100m_budget_commitment',p,time.time())
receipt={'status':'PASS','observed_at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'remaining_commitment_cents':5762,'commitment_event':M_PLAN,'namespace_event':NAMESPACE_EVENT,'payload':p};save('commitment-receipt.json',receipt);save('current-admission.json',receipt);print(CAN(receipt))
