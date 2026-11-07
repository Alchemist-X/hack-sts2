"""Read-only shared direct-run liabilities. No Store/provider imports or mutations."""
import json,re
M_PLAN='operator-task116-M100m-budget-commitment-v1'
PROJECT_APPROVAL='operator-project-budget-user600-task116-v1'
PLATFORM_POLICY='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT'
TERMINAL={'completed','failed','cancelled','timed_out','resource_censored_with_checkpoint','output_censored_with_checkpoint','failed_with_checkpoint','failed_without_new_checkpoint','partial_terminal'}
def _payload(db,key):
 row=db.execute('SELECT payload FROM events WHERE id=?',(key,)).fetchone();return json.loads(row[0])if row else None
def _digest(x):return isinstance(x,str)and re.fullmatch('[0-9a-f]{64}',x)is not None
def shared_liabilities(db,*,s115_reader):
 s=s115_reader(db);base=s['total_remaining_commitment_cents'];m=_payload(db,M_PLAN);active={};historical={}
 if m is not None:
  slots=m.get('slot_allocations');quote=m.get('full_shared_blind_quote_cents')
  if not isinstance(slots,dict)or set(slots)!={'train','final_dev','shared_blind_increment'}or any(type(v)is not int or v<0 for v in slots.values())or slots!={'train':3992,'final_dev':744,'shared_blind_increment':1026}or type(quote)is not int or quote!=2101 or slots['shared_blind_increment']!=max(quote-1075,0)or sum(slots.values())!=m.get('original_total_maximum_cents')or m.get('active_plan_commitment_cents')!=m.get('original_total_maximum_cents')or m.get('existing_global_blind_base_cents')!=1075 or m.get('project_budget_approval_ref')!=PROJECT_APPROVAL or m.get('project_cap_cents')!=60000 or m.get('platform_admission_policy')!=PLATFORM_POLICY or m.get('actual_cost','MISSING')is not None or not all(_digest(m.get(k))for k in ['root_authority_sha256','bound_release_sha256']):raise ValueError('malformed M commitment: do not ignore unknown obligations')
  active=dict(slots)
  for slot in ['train','final_dev']:
   p=_payload(db,'operator-task116-slot-'+slot.replace('_','-')+'-terminal-history-v1')
   if p is None:continue
   if p.get('plan_event')!=M_PLAN or p.get('slot')!=slot or p.get('completed_allocation_cents')!=slots[slot]or p.get('active_component_cents')!=0 or p.get('actual_cost','MISSING')is not None or p.get('provider_terminal_verified')is not True or p.get('call_terminal')is not True or p.get('financial_terminal_status')not in TERMINAL or p.get('training_root_authority_sha256')!=m['root_authority_sha256']or p.get('training_bound_release_sha256')!=m['bound_release_sha256']or not all(_digest(p.get(k))for k in ['completion_authority_sha256','terminal_proof_sha256','provider_terminal_receipt_sha256']):raise ValueError('M terminal not proven; keep liability')
   historical[slot]=active.pop(slot)
  # Increment stays protected. Actual blind dispatch/replacement requires a separate global-once authority.
 elif any(_payload(db,'operator-task116-slot-'+slot+'-terminal-history-v1')is not None for slot in ['train','final-dev']):raise ValueError('orphan M terminal')
 return {'S115':s,'M116_active_allocations':active,'M116_historical_allocations':historical,'M116_active_commitment_cents':sum(active.values()),'total_direct_commitments_cents':base+sum(active.values()),'shared_blind_base_in_S115_cents':s.get('first_remaining_allocations',{}).get('global_blind_protection',0),'shared_blind_increment_in_M_cents':active.get('shared_blind_increment',0),'archive_not_included_cents':1000,'new_pledge_cents':0}
