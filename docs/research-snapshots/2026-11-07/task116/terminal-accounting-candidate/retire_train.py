"""Exact M116 train terminal-history append only; no provider calls or fee edits."""
import argparse,hashlib,json,re,sys,time
from pathlib import Path
from first_slot_budget import payload,canonical,TERMINAL_CLASSES
from project_obligations import M_PLAN
SECOND=M_PLAN;SECOND_TERMINAL='operator-task116-slot-train-terminal-history-v1'
from project_obligations import shared_liabilities,TERMINAL
TERMINAL_CLASSES=TERMINAL
from plan_liabilities import liabilities
CID='fc-01M6SREBKE87QMT2VQKEMC0Q6T'
BOUND='6fc6f2544c6196546fe31b9796f73b3aca1aac57b7c2766c0d9454fe9dc1c8a6'
TRAIN_AUTH='16bf3bbf6cd5be06cf2e51bf69ce964eee42966132df69e24ae15641b05d77bf'
ATTEMPT='bf5a18d752bf4724943fb6f6f121ab76'
H=lambda b:hashlib.sha256(b).hexdigest()
def completion_payload(proof,proof_sha,authority,authority_sha):
 for x in (proof_sha,authority_sha,proof.get('provider_terminal_receipt_sha256','')):
  if not re.fullmatch('[0-9a-f]{64}',x):raise ValueError('exact hashes required')
 fixed={'slot':'train','arm_id':'M116','call_id':CID,'attempt':ATTEMPT,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'call_terminal':True,'provider_terminal_verified':True}
 if any(proof.get(k)!=v for k,v in fixed.items())or proof.get('status')not in TERMINAL_CLASSES or proof.get('observation_class')not in {'FunctionCall_returned','FunctionCall_verified_terminal_exception','SDK_verified_terminal_cancel'}:raise ValueError('not exact verified M116 terminal')
 expected={'action':'ACCEPT_TASK116_EXACT_PROVIDER_TERMINAL_FOR_ACCOUNTING',**fixed,'terminal_proof_sha256':proof_sha,'provider_terminal_receipt_sha256':proof['provider_terminal_receipt_sha256'],'terminal_status':proof['status'],'original_plan_event':SECOND,'allocation_cents':3992}
 if any(authority.get(k)!=v for k,v in expected.items()):raise ValueError('root authority differs')
 return {'plan_event':SECOND,'slot':'train','completed_allocation_cents':3992,'active_component_cents':0,'actual_cost':None,'call_terminal':True,'provider_terminal_verified':True,'financial_terminal_status':proof['status'],'provider_terminal_receipt_sha256':proof['provider_terminal_receipt_sha256'],'call_id':CID,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'completion_authority_sha256':authority_sha,'terminal_proof_sha256':proof_sha,'terminal_proof':proof,'scientific_acceptance_required_for_financial_retirement':False}
def retire(db,entry,append):
 before=shared_liabilities(db,s115_reader=liabilities);old=payload(db,SECOND_TERMINAL)
 if old is not None:
  if old!=entry:raise ValueError('existing terminal differs; never overwrite')
  return before
 plan=payload(db,SECOND)
 if plan['root_authority_sha256']!=TRAIN_AUTH or plan['bound_release_sha256']!=BOUND:raise ValueError('foreign M116 plan')
 append(db,SECOND_TERMINAL,'operator_bounded_plan_slot_terminal',entry,time.time())
 after=shared_liabilities(db,s115_reader=liabilities)
 assert before['total_direct_commitments_cents']-after['total_direct_commitments_cents']==3992
 return after

def main():
 p=argparse.ArgumentParser();p.add_argument('--proof',type=Path,required=True);p.add_argument('--proof-sha',required=True);p.add_argument('--authority',type=Path,required=True);p.add_argument('--authority-sha',required=True);a=p.parse_args()
 assert H(a.proof.read_bytes())==a.proof_sha and H(a.authority.read_bytes())==a.authority_sha
 proof=json.loads(a.proof.read_bytes());authority=json.loads(a.authority.read_bytes());entry=completion_payload(proof,a.proof_sha,authority,a.authority_sha)
 sub=json.loads((a.proof.parent/'submission-receipt.json').read_bytes());assert sub['call_id']==CID and sub['bound_release_sha256']==BOUND and sub['root_authority_sha256']==TRAIN_AUTH
 name=proof['raw_provider_evidence_file'];assert Path(name).name==name and name not in ('','.','..')
 raw=(a.proof.parent/name).read_bytes();assert H(raw)==proof['provider_terminal_receipt_sha256'];provider=json.loads(raw)
 assert provider['call_id']==CID and provider['provider_terminal_verified']is True and provider['observation_class']==proof['observation_class']
 if proof['observation_class']=='FunctionCall_returned':assert provider['result']['status']==proof['status'] and provider['result']['attempt']==ATTEMPT
 else:assert provider['status']==proof['status']
 root=Path('/var/lib/sts2-modal-production');assert H((root/'control-047-m/sim/tools/modal_research/store.py').read_bytes())=='2d5c4ba55d694e48513c72c1b84da806fc42f068f64df31e0f66d9441403cd7c'
 sys.path.insert(0,str(root/'control-047-m/sim/tools'));from modal_research.store import Store
 store=Store.__new__(Store);store.path=root/'operation.sqlite3';store.clock=time.time
 with store.transaction()as db:after=retire(db,entry,Store.append_event)
 print(canonical({'event_id':SECOND_TERMINAL,'payload':entry,'remaining':after,'actual_cost':None,'new_pledge_cents':0}))
if __name__=='__main__':main()
