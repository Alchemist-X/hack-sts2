"""Disabled prospective115 final-dev ops gates. No SDK/ledger actions on import."""
from pathlib import Path
import json,hashlib,time,os,re,sys
W=Path(__file__).resolve().parent
PLAN='operator-task115-first-segment-budget-commitment-v1'
DISPATCH='operator-task115-final-dev-slot-dispatch-v1'
SCIENCE='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
FUTURE='24f10e8eee7994da54fdf4955c3fba63f40a82a47f499007bf786621edcd0171'
TRAIN_AUTH='6f8c4b69e3fdba42ab5641d01cb796bd37f1b28c74278decc7eaa1f69c1849cd'
TRAIN_BOUND='2bdf2b30089818a9f5b4af29c68b1cd3fd3353624911a1a4d491922e7bc7b412'
H=lambda b:hashlib.sha256(b).hexdigest()
CAN=lambda x:json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False)
def require(ok,why):
 if not ok:raise ValueError(why)
def save(n,x):
 with (W/n).open('x')as f:f.write(CAN(x));f.flush();os.fsync(f.fileno())
 fd=os.open(W,os.O_DIRECTORY);os.fsync(fd);os.close(fd)
def read_bound(root,name,pin):
 require(type(pin)is str and re.fullmatch('[0-9a-f]{64}',pin),'unbound exact proof '+name)
 p=Path(root)/name;require(not p.is_symlink()and H(p.read_bytes())==pin,'exact proof bytes differ '+name)
 return json.loads(p.read_bytes())
def validate(a,r,proof,source,decision,*,phase,now):
 require(phase in ('metadata','deploy'),'unknown phase')
 require(a.get('max_submissions')==1,'one final dev only')
 require(a.get('forward_admission_policy_sha256')=='6241b01cbd1238cbdc569e80af4e8e525077f0c2ae41342404c00fbda13432eb' and a.get('project_cap_cents')==60000 and a.get('platform_admission_policy')=='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT','exact user600 forward authority required')
 if phase=='deploy':require(a.get('allow_consume_existing_slot')is True,'exact existing-slot paid-once authority required')
 require(a['action']=='TASK115_EXACT_FINAL_FORMALDEV'and a['phase']==phase and a['allow_'+phase]is True,'no exact future phase authority')
 require(a['future_components_acceptance_sha256']==FUTURE and a['science_plan_sha256']==r['science_plan_sha256']==SCIENCE,'future-code acceptance is not paid authority')
 require(a['slot']=='final_dev'and a['existing_plan_event']==PLAN and a['existing_slot_cents']==222 and a['new_pledge_cents']==0 and a['training_root_authority_sha256']==TRAIN_AUTH and a['training_bound_release_sha256']==TRAIN_BOUND,'existing222 slot only; no double pledge')
 require(type(a['expires'])is int and now<a['expires'] and type(r['not_after'])is int and now<r['not_after'],'expired/unbound future permission')
 from function_app import validate_release
 validate_release(r,bound=phase=='deploy')
 for k in ('source_volume_id','model_volume_id'):require(isinstance(r[k],str)and re.fullmatch('vo-[A-Za-z0-9]+',r[k]),'actual RO source/model IDs required')
 require(r['source_volume_id']!=r['model_volume_id'],'RO sources must differ')
 require(all(type(a[k])is int and a[k]>=0 for k in ('minimum_gross_cents','minimum_net_cents')),'rootbound monotonic budget basis required')
 require(r['status']=='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT','disabled release has no accepted actual endpoint')
 for k in ('checkpoint_sha256','checkpoint_size','checkpoint_path','checkpoint_steps','checkpoint_updates','model_volume_id','accepted_checkpoint_manifest_sha256','accepted_training_receipt_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256','root_terminal_decision_sha256'):
  require(r[k]is not None and a[k]==r[k]and proof[k]==r[k],'future accepted checkpoint binding differs: '+k)
 require(proof['schema']=='task115-accepted-final-eval-projection-v1'and proof['status']=='ROOT_AND_INDEPENDENT_ACCEPTED_TERMINAL'and proof['call_terminal']is True and re.fullmatch('fc-[A-Za-z0-9]+',proof['call_id'])and re.fullmatch('[0-9a-f]{32}',proof['attempt']),'actual samecall/attempt terminal acceptance required')
 require(proof['selected_checkpoint_rule']in ('normal_final','same_attempt_atomic_latest_after_hardwall'),'no older checkpoint/donor/quality selection')
 if r['checkpoint_updates']<5309:require(decision.get('censor_class')in ('time','budget','provider','byte_limit','safety')and isinstance(decision.get('reason'),str)and bool(decision['reason']),'frozen analysis resource censor class/reason required')
 require(proof['terminal_evaluation']==r['terminal_evaluation'] and proof['science_plan_sha256']==SCIENCE,'projection actual terminal classification differs')
 require(r['checkpoint_sha256']!=r['parent_donor_sha256'],'donor body re-evaluation forbidden')
 require(decision.get('study_donor_checkpoint_sha256')==r['parent_donor_sha256'],'frozen analysis donor identity differs')
 require(decision['actual_terminal_accepted']is True and decision['science_plan_sha256']==SCIENCE and decision['checkpoint_sha256']==r['checkpoint_sha256'] and decision['checkpoint_steps']==r['checkpoint_steps']and decision['checkpoint_updates']==r['checkpoint_updates']and decision['terminal_evaluation']==r['terminal_evaluation'],'root actual stop/no-successor decision required')
 require(source['status']=='ROOT_AND_INDEPENDENT_ACCEPTED_OPERATIONAL_EVIDENCE_CHAIN'and source['call_id']==proof['call_id']and source['attempt']==proof['attempt'],'source proof must bind exact actual call/attempt')
 for k in ('checkpoint_sha256','checkpoint_steps','checkpoint_updates','checkpoint_path','model_volume_id'):require(source[k]==r[k],'actual source endpoint differs: '+k)
 require(source['parent_donor_sha256']=='7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'and source['beta']==0 and source['learning_overlay']=={'ppo.py':'a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14','strategic_kl.py':'ed4ebfd9b0b0e622646f68bf34dfa3b56f2a7907f3cdbd15e34f3ba20b07c6f1'},'frozen control algorithm/source differs')
 require(source['actual_runtime_pins_sha256']==a['accepted_actual_runtime_pins_sha256'],'actual terminal runtime proof not approved')
 for n,pin in a['ops_file_sha256'].items():require(Path(n).name==n and H((W/n).read_bytes())==pin,'ops code differs '+n)
 return a,r

def inputs(auth_sha,release_sha,*,phase,gate=False):
 a=read_bound(W,'root-'+phase+'-authorization.json',auth_sha);r=read_bound(W,'release.'+phase+'-bound.json',release_sha)
 require(H(CAN(r).encode())==release_sha and a['bound_release_sha256']==release_sha,'exact canonical release differs')
 proof=read_bound(W,'accepted-final-endpoint.json',a['accepted_final_endpoint_proof_sha256']);source=read_bound(W,'accepted-training-source-proof.json',a['accepted_training_source_proof_sha256']);decision=read_bound(W,'root-terminal-decision.json',a['root_terminal_decision_sha256'])
 validate(a,r,proof,source,decision,phase=phase,now=time.time())
 manifest=read_bound(W,'accepted-checkpoint-manifest.json',a['accepted_checkpoint_manifest_sha256'])
 read_bound(W,'accepted-training-receipt.json',a['accepted_training_receipt_sha256'])
 read_bound(W,'independent-final-acceptance.json',a['independent_final_acceptance_sha256'])
 require(manifest['call_terminal']is True and manifest['call_id']==proof['call_id']and manifest['attempt']==proof['attempt'],'manifest must be same actual terminal call/attempt')
 if gate:check_admission(json.loads((W/(phase+'-admission.json')).read_bytes()),a,r,auth_sha,release_sha,now=time.time())
 return a,r

def check_admission(g,a,r,auth_sha,release_sha,*,now):
 require(now<a['expires']and now<r['not_after'],'future root/deadline expired during fresh query')
 require(g['status']=='PASS'and g['root_eval_authority_sha256']==auth_sha and g['bound_release_sha256']==release_sha and 0<=now-g['observed_at']<120,'dynamic fresh admission not current')
 require(g['funding_source_sha256']==a['ops_file_sha256']['fresh_admission.py']and g['liabilities_source_sha256']==a['ops_file_sha256']['plan_liabilities.py'],'fresh reader code not rootbound')
 require(g['existing_plan_event']==PLAN and g['slot']=='final_dev'and g['existing_slot_cents']==222 and g['slot_unconsumed']is True and g['new_pledge_cents']==0 and g['quote_cents']<=222,'no new pledge or stale/consumed slot')
 require(g['global_blind_protected_cents']>=1075 and g['archive_commitment_cents']>=1000 and g['old_unknown_retained']is True and g['no_other_paid_active']is True,'blind/archive/unknown/quiet guards')
 require(g.get('project_cap_cents')==60000 and g.get('platform_admission_policy')=='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT' and g.get('platform_caps_historical_only')is True and g['platform_room_after_all_existing_holds_cents']is None and g['project_room_after_all_existing_holds_cents']>=0,'canonical600 budget/backend enforcement; no fabricated cap')
 require(g.get('project_obligations_source_sha256')==a['ops_file_sha256']['project_obligations.py'] and g.get('allowed_M_concurrency_only')is True,'rootbound shared liability/inventory reader')
 return g

def metadata_continuity(a,r):
 ma=read_bound(W,'root-metadata-authorization.json',a['metadata_authority_sha256']);mr=read_bound(W,'release.metadata-bound.json',a['metadata_release_sha256']);receipt=read_bound(W,'metadata-receipt.json',a['metadata_receipt_sha256'])
 require(ma['phase']=='metadata'and ma['allow_metadata']is True and receipt['root_eval_authority_sha256']==a['metadata_authority_sha256']and receipt['bound_release_sha256']==a['metadata_release_sha256'],'exact separately authorized metadata chain')
 require(receipt['workspace']=='hycrpg'and receipt['output_version']==2 and receipt['output_empty']is True and receipt['claim_empty']is True,'new independent empty metadata')
 require(receipt['names']=={'output_volume':'sts2-task115-final-dev256-output-v2','claim_dict':'sts2-task115-final-dev256-claims-once'},'stable single115 claim identity')
 require((receipt['output_volume_id'],receipt['claim_dict_id'])==(r['output_volume_id'],r['claim_dict_id']),'IDs must come from actual metadata receipt')
 for k in r:
  if k not in ('output_volume_id','claim_dict_id','not_after'):require(mr[k]==r[k],'metadata/deploy immutable accepted binding differs '+k)
 return receipt
