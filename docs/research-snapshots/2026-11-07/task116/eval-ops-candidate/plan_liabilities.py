"""One read-only view of Task115 obligations, no Store/provider imports or mutation."""
import re
from first_slot_budget import remaining,payload,TERMINAL_CLASSES
SECOND='operator-task115-second-segment-budget-commitment-v1'
SECOND_TERMINAL='operator-task115-slot-segment2-train-terminal-history-v1'
def liabilities(db):
 first=remaining(db);p=payload(db,SECOND);second=0
 if p is not None:
  if p.get('original_total_maximum_cents')!=1629 or p.get('active_plan_commitment_cents')!=1629 or p.get('slot_allocations')!={'segment2_train':1629} or p.get('existing_protected_commitment_cents')!=1297 or p.get('existing_protected_plan_event')!='operator-task115-first-segment-budget-commitment-v1' or p.get('actual_cost','MISSING')is not None or any(not re.fullmatch('[0-9a-f]{64}',p.get(k,''))for k in ['root_authority_sha256','bound_release_sha256']):raise ValueError('malformed second commitment; never ignore unknown liability')
  if 'segment1_train'not in first['completed_slots']:raise ValueError('second commitment before first retirement')
  second=1629;t=payload(db,SECOND_TERMINAL)
  if t is not None:
   if t.get('plan_event')!=SECOND or t.get('slot')!='segment2_train' or t.get('completed_allocation_cents')!=1629 or t.get('active_component_cents')!=0 or t.get('actual_cost','MISSING')is not None or t.get('call_terminal')is not True or t.get('provider_terminal_verified')is not True or t.get('financial_terminal_status')not in TERMINAL_CLASSES or t.get('training_root_authority_sha256')!=p['root_authority_sha256'] or t.get('training_bound_release_sha256')!=p['bound_release_sha256'] or any(not re.fullmatch('[0-9a-f]{64}',t.get(k,''))for k in ['completion_authority_sha256','provider_terminal_receipt_sha256','terminal_proof_sha256']):raise ValueError('second terminal unknown/malformed; never release')
   second=0
 elif payload(db,SECOND_TERMINAL)is not None:raise ValueError('orphan second terminal')
 return {'first_remaining_commitment_cents':first['remaining_commitment_cents'],'second_remaining_commitment_cents':second,'total_remaining_commitment_cents':first['remaining_commitment_cents']+second,'first_remaining_allocations':first['remaining_allocations'],'new_pledge_cents':0}
