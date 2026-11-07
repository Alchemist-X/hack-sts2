from ops_common import *
from decimal import Decimal
from project_obligations import PROJECT_APPROVAL,PLATFORM_POLICY,M_PLAN
AUTH,BOUND=sys.argv[1:3];a,r=inputs(AUTH,BOUND)
import subprocess
assert a['allow_dynamic_fresh_admission']is True
assert not(W/'fresh-precommit-funding.json').exists(),'previous precommit attempt requires reconcile'
assert H((W/'funding_snapshot.py').read_bytes())==a['ops_file_sha256']['funding_snapshot.py']
q=subprocess.run([sys.executable,'-B',str(W/'funding_snapshot.py'),AUTH,BOUND],capture_output=True,check=True)
f=json.loads(q.stdout);save('fresh-precommit-funding.json',f)
assert f['segment_scope_sha256']==r['segment_authority_sha256']==a['segment_scope_sha256']
assert f['allocations']=={'segment2_train':1629} and f['existing_protected_allocations']=={'final_dev':222,'global_blind_protection':1075}
assert f['existing_protected_commitment_cents']==1297 and f['first_parent_proof_sha256']==a['first_parent_proof_sha256']
assert f['root_authority_sha256']==AUTH and f['bound_release_sha256']==BOUND and f['funding_source_sha256']==a['ops_file_sha256']['funding_snapshot.py']
assert H(Path(f['authoritative_ledger_path']).read_bytes())==f['authoritative_ledger_sha256']
assert a['allow_commit_and_first_spawn']is True and a['new_train_commitment_cents']==1629 and a['existing_protected_commitment_cents']==1297
assert f['status']=='PASS' and time.time()-f['observed_at']<120 and f['science_plan_sha256']==r['science_plan_sha256'] and f['proposed_commitment_cents']==1629 and f['project_room_after_cents']>=0 and f['project_cap_cents']==60000 and f['platform_admission_policy']==PLATFORM_POLICY
R=Path('/var/lib/sts2-modal-production');assert H((R/'control-047-m/sim/tools/modal_research/store.py').read_bytes())=='2d5c4ba55d694e48513c72c1b84da806fc42f068f64df31e0f66d9441403cd7c'
sys.path.insert(0,str(R/'control-047-m/sim/tools'));from modal_research.store import Store
s=Store.__new__(Store);s.path=R/'operation.sqlite3';s.clock=time.time
rr=f['rates'];s.record_metered_usage(gross_used=str(Decimal(f['gross_effective_cents'])/100),net_spent=str(Decimal(f['net_effective_cents'])/100),cycle_start=f['cycle_start'],observed_at=f['observed_at'],rates={'gpu_per_second':str(Decimal(rr['gpu_hour_cost_h100'])/3600),'cpu_per_core_second':str(Decimal(rr['cpu_hour_cost_sandbox'])/3600),'memory_per_gib_second':str(Decimal(rr['mem_gib_hour_cost_sandbox'])/3600),'volume_per_gib_month':rr['volume_storage_gib_month_cost']})
E='operator-task115-second-segment-budget-commitment-v1'
from first_slot_budget import remaining
from plan_liabilities import liabilities
from project_obligations import shared_liabilities,PROJECT_APPROVAL,PLATFORM_POLICY,M_PLAN
with s.transaction()as c:
 state=remaining(c);assert state['remaining_commitment_cents']==1297 and state['remaining_allocations']=={'final_dev':222,'global_blind_protection':1075}
 assert not c.execute('SELECT 1 FROM events WHERE id=?',(E,)).fetchone(),'existing commitment; reconcile'
 cs=dict(c.execute('SELECT * FROM study').fetchone());assert cs['approved_budget']==cs['execution_cap']==60000 and cs['approval_ref']==PROJECT_APPROVAL
 ob=shared_liabilities(c,s115_reader=liabilities);assert c.execute('SELECT 1 FROM events WHERE id=?',(M_PLAN,)).fetchone();assert ob['S115']['second_remaining_commitment_cents']==0
 b=s.budget_evidence(c,cs);gross,held=b['spent_and_reserved_cents'];gross=max(gross,f['gross_effective_cents']);assert 60000-gross-held-1000-ob['total_direct_commitments_cents']-1629>=0
 p={'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':1629,'active_plan_commitment_cents':1629,'existing_protected_commitment_cents':1297,'existing_protected_plan_event':'operator-task115-first-segment-budget-commitment-v1','slot_allocations':f['allocations'],'actual_cost':None,'archive_offledger_commitment_cents':1000,'funding_evidence_sha256':H((W/'fresh-precommit-funding.json').read_bytes()),'second_segment_committed':True,'max_segments_total':2,'max_training_submissions':1,'global_blind_is_protected_obligation_not_invocation':True,'old_unknown_history_unchanged':True,'seed_range':r['segment']['seed_range']}
 Store.append_event(c,E,'operator_bounded_second_segment_budget_commitment',p,time.time())
admission={'status':'PASS','observed_at':time.time(),'root_authority_sha256':AUTH,'bound_release_sha256':BOUND,'remaining_commitment_cents':1629,'existing_protected_commitment_cents':1297,'commitment_event':E,'payload':p};save('commitment-receipt.json',admission);save('current-admission.json',admission);print(CAN(admission))
