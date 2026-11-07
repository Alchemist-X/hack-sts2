"""Exact Task114 original-plan accounting. No provider/import side effects."""
import json,hashlib,re
TRAIN_AUTH='4619ee2c088fe36029247a777c44b6ca59073d6449cec61f2762563dc069bc80'
BOUND='27b44591673409459d89a95c3e148cbc2da85919671e938469e8a5312d5d4f8b'
PLAN='operator-task114-four-slot-budget-commitment-v1'
SLOTS={'control_train':1650,'control_dev':250,'treatment_train':1650,'treatment_dev':250}
def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False)
def digest(x):return hashlib.sha256(canonical(x).encode()).hexdigest()
def terminal_id(slot):
 if slot not in SLOTS:raise ValueError('unknown slot')
 return 'operator-task114-slot-'+slot.replace('_','-')+'-terminal-history-v1'
def payload(db,event):
 r=db.execute('SELECT payload FROM events WHERE id=?',(event,)).fetchone()
 return json.loads(r[0])if r else None
def remaining(db):
 original=payload(db,PLAN)
 if not original or original.get('root_authority_sha256')!=TRAIN_AUTH or original.get('bound_release_sha256')!=BOUND or original.get('original_total_maximum_cents')!=3800 or original.get('slot_allocations')!=SLOTS:raise ValueError('original fixed3800 plan missing/different')
 completed={}
 for slot,amount in SLOTS.items():
  p=payload(db,terminal_id(slot))
  if p is None:continue
  if p.get('plan_event')!=PLAN or p.get('slot')!=slot or p.get('completed_allocation_cents')!=amount or p.get('active_component_cents')!=0 or p.get('training_root_authority_sha256')!=TRAIN_AUTH or p.get('training_bound_release_sha256')!=BOUND or p.get('call_terminal')is not True or p.get('actual_cost','MISSING')is not None or p.get('provider_terminal_verified')is not True or p.get('financial_terminal_status')not in TERMINAL_CLASSES or not re.fullmatch('[0-9a-f]{64}',p.get('completion_authority_sha256','')):raise ValueError('malformed or foreign slot retirement')
  completed[slot]=p
 remaining_slots={k:v for k,v in SLOTS.items()if k not in completed}
 return {'original':original,'completed_slots':completed,'remaining_allocations':remaining_slots,'remaining_commitment_cents':sum(remaining_slots.values()),'historical_completed_allocations_cents':sum(SLOTS[k]for k in completed)}
TERMINAL_CLASSES={'completed','failed','cancelled','timed_out','output_censored','output_censored_with_checkpoint','failed_with_checkpoint','failed_without_new_checkpoint','partial_terminal'}
TERMINAL_AUTH_ACTION='ACCEPT_TASK114_EXACT_PROVIDER_TERMINAL_FOR_ACCOUNTING'
def completion_payload(slot,proof,proof_sha,authority,authority_sha):
 if slot not in SLOTS or not re.fullmatch('[0-9a-f]{64}',proof_sha)or not re.fullmatch('[0-9a-f]{64}',authority_sha):raise ValueError('exact slot/hash required')
 arm=slot.split('_')[0]
 if proof.get('slot')!=slot or proof.get('arm_id')!=arm or proof.get('call_terminal')is not True or proof.get('provider_terminal_verified')is not True or proof.get('status')not in TERMINAL_CLASSES or not re.fullmatch('fc-[A-Za-z0-9]+',proof.get('call_id','')) or proof.get('training_root_authority_sha256')!=TRAIN_AUTH or proof.get('training_bound_release_sha256')!=BOUND:raise ValueError('proof is not exact verified providerterminal; scientificsuccess not required')
 receipt=proof.get('provider_terminal_receipt_sha256','')
 if not re.fullmatch('[0-9a-f]{64}',receipt)or proof.get('observation_class')not in {'FunctionCall_returned','FunctionCall_verified_terminal_exception','SDK_verified_terminal_cancel'}:raise ValueError('timeout/pollfailure/unknown is not providerterminal')
 expected={'action':TERMINAL_AUTH_ACTION,'slot':slot,'call_id':proof['call_id'],'terminal_proof_sha256':proof_sha,'provider_terminal_receipt_sha256':receipt,'provider_terminal_verified':True,'terminal_status':proof['status'],'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'original_plan_event':PLAN,'allocation_cents':SLOTS[slot]}
 if any(authority.get(k)!=v for k,v in expected.items()):raise ValueError('new exact rootterminal authority doesnot bind proof/slot/call/receipt')
 return {'plan_event':PLAN,'slot':slot,'completed_allocation_cents':SLOTS[slot],'active_component_cents':0,'actual_cost':None,'call_terminal':True,'provider_terminal_verified':True,'financial_terminal_status':proof['status'],'provider_terminal_receipt_sha256':receipt,'call_id':proof['call_id'],'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'completion_authority_sha256':authority_sha,'terminal_proof_sha256':proof_sha,'terminal_proof':proof,'scientific_acceptance_required_for_financial_retirement':False,'accounting':'Original3800 remains immutable; this exact rootaccepted providerterminal slot maximum is historical, not new spend or actual fee. Failed/censored/partial remain scientifically distinct. Oldunknown/archive preserved.'}
