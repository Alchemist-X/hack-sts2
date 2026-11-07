"""Task115 terminal-only one selected-checkpoint metadata inspection; no model/native/provider call on import."""
import argparse,hashlib,json,math,os
from pathlib import Path
PLAN_SHA='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
FIRST_PARENT_SHA='7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'
SPEC_SHA='63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
METHOD='task114-map-rest-forward-kl-uniform-v1'
ROLLOUT=131072
HEALTH=('sim_error','content_gap','illegal','cand_over_max_rows','floor_stall','native_candidate_overflow','native_table_overflow','prompt_over_max_rows')
OVERLAY={'ppo.py':'a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14','strategic_kl.py':'ed4ebfd9b0b0e622646f68bf34dfa3b56f2a7907f3cdbd15e34f3ba20b07c6f1'}
def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False).encode()

def sha(path):
 with Path(path).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

def require(ok,msg):
 if not ok:raise ValueError(msg)

def number(x,name,integer=False):
 require(type(x) in (int,float) and math.isfinite(x),name+' missing/nonfinite')
 if integer:require(x>=0 and int(x)==x,name+' not nonnegative integer')
 return x

def marker(x,beta):
 require(isinstance(x,dict) and x.get('method')==METHOD and x.get('beta')==beta and x.get('phase_weights')=={'Map':.5,'RestSite':.5} and x.get('direction')=='KL(Ulegal||policy)' and x.get('old_global_entropy_unchanged')==.02,'strategic method/beta marker differs')

def mechanism(rows):
 result={}
 for phase in ('Map','RestSite'):
  ps=[r['strategic_forward_kl']['phases'][phase] for r in rows]
  n=sum(p['frames'] for p in ps);kl=math.fsum(p['KL_sum'] for p in ps);h=math.fsum(p['normalized_entropy_sum'] for p in ps);near=sum(p['near_deterministic_count'] for p in ps)
  result[phase]={'learner_frames':n,'KL_sum':kl,'KL_mean':kl/n if n else None,'normalized_entropy_sum':h,'normalized_entropy_mean':h/n if n else None,'near_deterministic_count':near,'near_deterministic_share':near/n if n else None}
 return result

def segment_context(release,parent):
 require(release['science_plan_sha256']==PLAN_SHA and release['learning_overlay']==OVERLAY,'fixed115 science/source overlay differs')
 seg=release['segment'];index=seg['index'];require(index in(1,2)and seg['beta']==0,'only115 fixed beta0 segment')
 start_u,start_n=(3783,495845376)if index==1 else(4546,595853312)
 end_u,end_n=(4546,595853312)if index==1 else(5309,695861248)
 require((parent['updates'],parent['env_steps'])==(start_u,start_n),'accepted segment parent dose differs')
 require(parent['actual_accepted']is True and parent['checkpoint_sha256']==release['parent']['model_sha256'],'actual accepted parent identity required')
 if index==1:require(parent['checkpoint_sha256']==FIRST_PARENT_SHA and release['parent']['adam_step']==121056,'first actual114C parent/Adam differs')
 require((seg['target_updates'],seg['target_env_steps'])==(end_u,end_n)and seg['additional_steps']==100007936,'frozen segment target differs')
 return {'segment_index':index,'start_u':start_u,'start_n':start_n,'end_u':end_u,'end_n':end_n,'planned_updates':763,'seed_range':seg['seed_range'],'parent_sha256':parent['checkpoint_sha256']}

def rows_checked(rows,ctx):
 ids=[r['update']for r in rows]
 require(ids==list(range(ctx['start_u']+1,ctx['start_u']+1+len(ids)))and len(rows)<=763,'duplicate/missing/out-of-order or excess segment rows')
 for r in rows:
  require(r['env_steps']==ctx['start_n']+(r['update']-ctx['start_u'])*ROLLOUT and r['env_steps_update']==ROLLOUT,'actual row dose differs')
  require(r['session_update']==r['update']-ctx['start_u'],'segment-local update differs')
  if r.get('strategic_forward_kl')is not None:marker(r['strategic_forward_kl'],0)
  for key in ('learning_samples_update','episodes','wins','starts_update'):
   if r.get(key)is not None:number(r[key],key,True)
  if r.get('learning_samples_update')is not None:require(r['learning_samples_update']<=ROLLOUT,'valid samples exceed rollout')
 return rows

def sum_known(rows,key):
 vals=[r[key]for r in rows if r.get(key)is not None]
 for v in vals:number(v,key)
 return {'value':math.fsum(vals)if len(vals)==len(rows)and rows else None,'observed_sum':math.fsum(vals)if vals else None,'observed_rows':len(vals),'rows':len(rows)}

def describe(rows,ctx,lo,hi):
 chosen=[r for r in rows if lo<=r['update']-ctx['start_u']<=hi]
 phase_rows=[];acts={str(i):0 for i in(1,2,3)};act_n=0
 for r in chosen:
  s=r.get('strategic_forward_kl');valid=isinstance(s,dict)and all(isinstance(s.get('phases',{}).get(p),dict)and all(k in s['phases'][p]for k in('frames','KL_sum','normalized_entropy_sum','near_deterministic_count'))for p in('Map','RestSite'))
  if valid:
   for p in('Map','RestSite'):
    q=s['phases'][p];n=number(q['frames'],'phase frames',True);near=number(q['near_deterministic_count'],'phase near',True);require(near<=n,'phase near exceeds frames')
    for k in('KL_sum','normalized_entropy_sum'):number(q[k],k)
    require(n or(q['KL_sum']==q['normalized_entropy_sum']==0),'nonzero phase sum with zero denominator')
   phase_rows.append(r)
  a=r.get('critic_diagnostics',{})
  if a.get('status')=='ok'and isinstance(a.get('act'),dict)and all(str(i)in a['act']for i in(1,2,3)):
   for i in acts:acts[i]+=number(a['act'][i]['n'],'Act valid count',True)
   act_n+=1
 return {'local_update_interval':[lo,hi],'observed_updates':len(chosen),'expected_updates':hi-lo+1,'window_complete':len(chosen)==hi-lo+1,
 'phase_mechanism_observed':mechanism(phase_rows)if phase_rows else None,'phase_diagnostic_rows':len(phase_rows),'phase_coverage_complete':len(phase_rows)==len(chosen)and bool(chosen),
 'valid_learning_samples':sum_known(chosen,'learning_samples_update'),'Act_valid_learning_counts_observed':acts if act_n else None,'Act_diagnostic_rows':act_n,'Act_coverage_complete':act_n==len(chosen)and bool(chosen),
 'closed_training_episodes':sum_known(chosen,'episodes'),'closed_training_wins':sum_known(chosen,'wins'),'actual_new_starts':sum_known(chosen,'starts_update'),
 'health_counts':{k:sum_known(chosen,k)for k in HEALTH},'timing_seconds':{k:sum_known(chosen,k)for k in('t_env_s','t_infer_s','t_update_s','t_ckpt_s','t_total_s')}}

def checkpoint_metadata(path):
 """One safe CPU load of the selected own checkpoint only. No parent load or forward."""
 os.environ.update(OMP_NUM_THREADS='1',MKL_NUM_THREADS='1',OPENBLAS_NUM_THREADS='1')
 import torch
 torch.set_num_threads(1);torch.set_num_interop_threads(1)
 ck=torch.load(path,map_location='cpu',weights_only=True)
 def adam(raw):
  steps={str(k):float(v['step'].item()if torch.is_tensor(v['step'])else v['step'])for k,v in raw['state'].items()if 'step'in v}
  for v in steps.values():number(v,'actual Adam step',True)
  return {'initialized_states':len(raw['state']),'parameter_slots':sum(len(g['params'])for g in raw['param_groups']),'per_state_steps':steps,'states_without_step':len(raw['state'])-len(steps),'min':min(steps.values())if steps else None,'max':max(steps.values())if steps else None}
 rng=ck.get('rng',{});stream=ck.get('seed_stream',{});inflight=ck.get('inflight',{}).get('slot')
 return {'env_steps':ck['env_steps'],'updates':ck['updates'],'spec_sha256':hashlib.sha256(canonical(ck['spec'])).hexdigest(),'strategy':ck.get('strategic_forward_kl'),'base_adam':adam(ck['optimizer']),'aux_adam':adam(ck['transition_aux']['optimizer']),'recipe_config':dict(ck['config']),
 'rng_present':{'numpy':bool(rng.get('numpy')),'torch':torch.is_tensor(rng.get('torch')),'cuda':isinstance(rng.get('cuda'),list)and len(rng['cuda'])==1 and torch.is_tensor(rng['cuda'][0])},
 'seed_stream':{k:stream.get(k)for k in('prefix','next','issued_until','started','skipped','reserved_skipped')},'inflight_slots':None if inflight is None else len(inflight)}

def optimizer_validation(meta,parent,rows,coverage_complete):
 cfg=meta['recipe_config'];counts=[];unknown=[]
 for r in rows:
  n=r.get('learning_samples_update');mb=r.get('strategic_forward_kl',{}).get('minibatches')
  if n is None or mb is None:unknown.append(r['update']);continue
  number(mb,'actual minibatches',True)
  expected=math.ceil(n/cfg['minibatch'])*cfg['epochs']
  require(mb==expected,'observed minibatches differ from valid-sample batching')
  counts.append(mb)
 delta=sum(counts)if coverage_complete and not unknown and cfg.get('compile')=='off'and cfg.get('amp')is False else None
 result={'observed_minibatch_steps':sum(counts),'expected_exact_delta':delta,'unknown_row_ids':unknown,'status':'verified'if delta is not None else'unknown_due_coverage_or_missing_proof','optimizers':{}}
 for key in('base_adam','aux_adam'):
  actual=meta[key];prior=parent.get(key,{});psteps=prior.get('per_state_steps');uniform=prior.get('uniform_initialized_step')
  require(0<=actual['initialized_states']<=actual['parameter_slots'],'invalid actual initialized state/slot counts')
  steps_complete=len(actual['per_state_steps'])==actual['initialized_states']and actual.get('states_without_step',0)==0
  item={'actual':actual,'expected_per_state':None,'uninitialized_slots_not_fabricated':actual['parameter_slots']-actual['initialized_states'],'identity_map_validation':'unknown_parent_state_IDs'if psteps is None else'available'}
  if delta is not None and psteps is not None and steps_complete:
   expected={k:v+delta for k,v in psteps.items()};item['expected_per_state']=expected
   item['matching_existing_states']=all(actual['per_state_steps'].get(k)==v for k,v in expected.items());require(item['matching_existing_states'],'actual per-initialized-state Adam offset differs')
   item['new_state_IDs_with_unknown_activation']=[k for k in actual['per_state_steps']if k not in psteps]
   if item['new_state_IDs_with_unknown_activation']:result['status']='verified_existing_states_new_state_activation_unknown'
  elif delta is not None and uniform is not None and prior.get('initialized_states')==actual['initialized_states']and steps_complete:
   expected=uniform+delta;item['expected_uniform_initialized_step']=expected;require(all(v==expected for v in actual['per_state_steps'].values()),'actual initialized-state Adam offset differs')
  else:
   item['offset_validation']='unknown; no zero or assumed32-per-update offsets inserted'
   result['status']='unknown_missing_parent_or_actual_state_step_proof'
  result['optimizers'][key]=item
 return result

def resume_evidence(proof,config,parent,release,ctx):
 unknown=[];checks={};proof=proof or{};sessions=config.get('sessions')or[];session=sessions[-1]if sessions else{}
 expected={'parent_model_sha256':parent['checkpoint_sha256'],'strict_resume':True,'fresh_episodes':True,'old_inflight_restored':False,'strategic_kl_beta':0.,'segment_index':ctx['segment_index'],'requested_env_steps':ctx['end_n'],'parent_next':release['parent']['cursor'],'parent_issued':release['parent']['issued'],'base_max_adam_step':release['parent']['adam_step'],'aux_adam_step':release['parent']['adam_step'],'new_seed_begin':ctx['seed_range'][0],'new_seed_end':ctx['seed_range'][1]}
 for k,v in expected.items():
  if k not in proof:unknown.append('resume-proof.'+k)
  else:require(proof[k]==v,'resume proof differs: '+k)
 for k,v in {'update_start':ctx['start_u'],'adam_step':release['parent']['adam_step'],'dropped_at_start':2048}.items():
  if k not in session:unknown.append('session.'+k)
  else:require(session[k]==v,'actual session parent/drop offset differs: '+k)
 restored=session.get('rng_restored',{})
 for k in('numpy','torch','cuda'):
  if k not in restored:unknown.append('rng_restore.'+k)
  else:require(restored[k]=='ok','actual RNG restoration failed: '+k)
 isolation=proof.get('isolation',{});sets=isolation.get('checked_sets');expected_sets=parent.get('required_guard_sets');roles=isolation.get('roles');expected_roles=parent.get('required_guard_roles')
 if sets is None or expected_sets is None:unknown.append('full_guard_sets')
 else:
  project=lambda xs:{(s['id'],s['role'],s['sha256'],s['games'])for s in xs}
  require(project(expected_sets)<=project(sets),'old dev/blind/AB exclusions removed or changed')
  require(any(s['role']=='ab'and s['sha256']==release['ab_content_sha256']for s in sets),'113AB fence absent')
 if roles is None or expected_roles is None:unknown.append('full_guard_roles')
 else:require(set(expected_roles)<=set(roles),'old guard role policy dropped')
 return {'status':'verified'if not unknown else'unknown_proof_fields','missing_proof_fields':unknown,'discarded_parent_inflight_slots':session.get('dropped_at_start'),'rng_restored_observed':{k:restored.get(k)for k in('numpy','torch','cuda')},'guard_sets_observed':None if sets is None else[{k:s[k]for k in('id','role','sha256','games')}for s in sets],'guard_roles_observed':roles,'discarded_slots_are_not_closed_losses':True}

def analyze(root,manifest,release,parent,selection,*,metadata_reader=checkpoint_metadata):
 ctx=segment_context(release,parent)
 require(manifest.get('call_terminal')is True and manifest['segment_index']==ctx['segment_index'],'actual terminal same115 segment required; no live inspection')
 require(manifest['release_sha256']==hashlib.sha256(canonical(release)).hexdigest()and manifest['learning_overlay']==OVERLAY,'actual release/source binding differs')
 require(selection['root_selected']is True and selection['same_attempt']is True and(selection['call_id'],selection['attempt'])==(manifest['call_id'],manifest['attempt']),'root exact same-attempt selection proof required')
 chosen=selection['selected_file'];require(Path(chosen).name==chosen and chosen.endswith('.pt'),'one explicit chosen checkpoint basename required')
 require(chosen in manifest['files']and all(k in manifest['files']for k in('latest.json','config.json','metrics.jsonl')),'chosen/checkpoint metadata or metrics missing')
 require(all(not n.endswith('.pt')or n==chosen for n in manifest['files']),'manifest may bind only the sole selected model body, no older/donor body reads')
 for name,ref in manifest['files'].items():
  p=root/name;require(Path(name).name==name and not p.is_symlink()and p.is_file(),'unsafe bound artifact')
  require(p.stat().st_size==ref['bytes']and sha(p)==ref['sha256'],'bound artifact bytes/hash differ: '+name)
 require(0<manifest['files'][chosen]['bytes']<=128*1024**2 and sum(v['bytes']for k,v in manifest['files'].items()if k.endswith('.jsonl'))<=256*1024**2,'frozen body/log bounds exceeded')
 require(selection['selected_sha256']==manifest['files'][chosen]['sha256'],'root selection/chosen SHA differs')
 latest=json.loads((root/'latest.json').read_bytes());config=json.loads((root/'config.json').read_bytes());proof=json.loads((root/'resume-proof.json').read_bytes())if'resume-proof.json'in manifest['files']else None
 marker(config.get('strategic_forward_kl'),0)
 require(latest['sha256']==selection['selected_sha256']and latest['bytes']==manifest['files'][chosen]['bytes']and latest['parent_model_sha256']==parent['checkpoint_sha256'],'chosen sole atomic latest or parent differs')
 normal=(root/'final.json').exists()or(root/'final.pt').exists()
 require(selection['normal_final_present']is normal,'root snapshot normal-final presence differs')
 final=None
 if normal:
  require(all(k in manifest['files']for k in('episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl','status.json','summary.json','resume-proof.json')),'normal final requires all closed log/metadata files')
  require(selection['kind']=='normal_final'and chosen=='final.pt'and'final.json'in manifest['files']and'final.pt'in manifest['files'],'normal final exists: cannot bypass it with older periodic model')
  final=json.loads((root/'final.json').read_bytes());require(final['final_sha256']==selection['selected_sha256']and final['latest']==latest,'normal final/latest mismatch; no fallback')
 else:
  require(selection['kind']=='sole_atomic_latest'and chosen==Path(latest['checkpoint']).name,'hardstop may use only the explicit latest pointer, no search')
 meta=metadata_reader(root/chosen)  # exactly one selected body; never parent/older generations
 require(ctx['start_u']<meta['updates']<=ctx['end_u']and meta['env_steps']==ctx['start_n']+(meta['updates']-ctx['start_u'])*ROLLOUT,'chosen model must advance own parent, within same fixed segment')
 require(selection['selected_sha256']!=parent['checkpoint_sha256'],'zero-step donor cannot be endpoint')
 marker(meta['strategy'],0);require(meta['spec_sha256']==SPEC_SHA,'ModelSpec changed')
 cfg=meta['recipe_config'];require(all(canonical(cfg.get(k))==canonical(v)for k,v in release['spec']['ppo_config'].items()),'fixed beta0 PPO recipe changed')
 require((cfg.get('init_seed'),cfg.get('sample_seed'),cfg.get('env_seed'),cfg.get('input_arch'),cfg.get('critic_arch'))==(1457924769,759982036,818190218,'reward_choices_public_handselect_v1','state_legal_candidates_detached_mean_v1'),'seed/input/critic changed')
 meta['seed_and_arch_config']={k:cfg.get(k)for k in('init_seed','sample_seed','env_seed','input_arch','critic_arch')}
 meta['recipe_config']={k:cfg[k]for k in release['spec']['ppo_config']}
 require(all(meta['rng_present'].values()),'selected checkpoint lacks complete RNG; no fabricated state')
 lo,hi=ctx['seed_range'];stream=meta['seed_stream'];require(stream['prefix']=='MODA10PH104FRESH1'and lo<=stream['next']<=stream['issued_until']<=hi,'selected checkpoint stream escaped root-reserved fence')
 restore=resume_evidence(proof,config,parent,release,ctx)
 rows=rows_checked([json.loads(line)for line in(root/'metrics.jsonl').read_text().splitlines()if line.strip()],ctx)
 selected_rows=[r for r in rows if r['update']<=meta['updates']];post=[r['update']for r in rows if r['update']>meta['updates']]
 missing=list(range(ctx['start_u']+1+len(selected_rows),meta['updates']+1));coverage=not missing
 if normal:
  require(coverage and not post,'normal final requires full closed metrics matching chosen checkpoint')
  result=final.get('result')or{};require(result.get('env_steps')==meta['env_steps']and result.get('updates')==meta['updates'],'normal final result/dose differs')
  require(final.get('dose_complete')is(meta['updates']==ctx['end_u']),'normal writer dose_complete differs')
 optim=optimizer_validation(meta,parent,selected_rows,coverage)
 actual_local=meta['updates']-ctx['start_u'];full=meta['updates']==ctx['end_u']
 windows={'all':describe(selected_rows,ctx,1,actual_local),'first100':describe(selected_rows,ctx,1,100),'last100':describe(selected_rows,ctx,max(1,actual_local-99),actual_local)}
 next_descriptor={'actual_accepted':False,'status':'INSPECTED_NOT_ROOT_ACCEPTED_FOR_SUCCESSOR','checkpoint_sha256':selection['selected_sha256'],'updates':meta['updates'],'env_steps':meta['env_steps'],'base_adam':meta['base_adam'],'aux_adam':meta['aux_adam'],'seed_stream':stream,'inflight_slots':meta['inflight_slots'],'required_guard_sets':parent.get('required_guard_sets'),'required_guard_roles':parent.get('required_guard_roles')}
 return {'schema':'task115-terminal-training-inspection-v1','status':'normal_full_segment_inspected'if normal and full else'actual_censored_or_partial_inspected','segment':ctx,'selection':selection,'selected_checkpoint_sha256':selection['selected_sha256'],'actual_metadata':meta,'parent_model_sha256':parent['checkpoint_sha256'],'source_overlay':OVERLAY,'actual_checkpoint_at_segment_target':full,
 'coverage':{'planned_updates':763,'selected_checkpoint_updates':actual_local,'observed_selected_rows':len(selected_rows),'selected_prefix_complete':coverage,'missing_selected_update_rows':missing,'callback_or_snapshot_tail_missing':bool(missing),'post_selected_snapshot_update_rows_excluded':post,'observed_log_rows':len(rows),'normal_full_closed_log':normal and coverage and not post,'hardstop_full_process_coverage_claim':False,'all_unobserved_values_unknown':True},
 'resume_evidence':restore,'optimizer_validation':optim,'windows':windows,'parent_descriptor_for_later_root_acceptance':next_descriptor,'actual_metadata_is_not_successor_authority':True,
 'semantics':['all/first100/last100 are predefined within this selected segment; last100 ends at the selected actual checkpoint, may contain missing callback tail; first100 stays partial if fewer100 updates','phase SUM/count are repeated learner-frame diagnostics, not independent samples or formal quality','only chosen-checkpoint update prefix is summarized; newer log rows beyond atomiclatest are disclosed and excluded','missing logs or restore/optimizer proof produce unknown coverage/values, never fake zero or offset','discard2048 freshresume slots are not closed losses; reserved seed gaps are not game/collision counts','no model forward/native/new game/bootstrap/new health or quality gate']}

def main():
 p=argparse.ArgumentParser(description=__doc__)
 for k in('input-binding','input-binding-sha256','once-authority','once-authority-sha256','output'):p.add_argument('--'+k,required=True)
 a=p.parse_args();require(sha(a.input_binding)==a.input_binding_sha256 and sha(a.once_authority)==a.once_authority_sha256,'exact external binding/authority differs')
 binding=json.loads(Path(a.input_binding).read_bytes());auth=json.loads(Path(a.once_authority).read_bytes());code=Path(__file__).resolve().parent
 require(auth.get('allow_once_training_inspection')is True and auth['input_binding_sha256']==a.input_binding_sha256 and auth['helper_pins_sha256']==sha(code/'pins.json')and auth['output']==a.output,'root exact once helper authority required')
 for n,h in json.loads((code/'pins.json').read_bytes())['files'].items():require(Path(n).name==n and sha(code/n)==h,'frozen helper differs')
 def bound(name):
  r=binding[name];f=Path(r['path']);require(f.is_file()and not f.is_symlink()and f.stat().st_size<=2*1024**2 and sha(f)==r['sha256'],'bound metadata differs: '+name);return json.loads(f.read_bytes())
 release=bound('release');parent=bound('parent_metadata');selection=bound('selection');manifest=bound('manifest')
 require(binding['science_plan_sha256']==PLAN_SHA and manifest.get('call_terminal')is True,'no live study/body inspection')
 require(auth['selection_sha256']==binding['selection']['sha256']and auth['parent_metadata_sha256']==binding['parent_metadata']['sha256'],'root selected model/parent proof differs')
 require(auth['parent_actual_acceptance_sha256']==parent['actual_acceptance_sha256'],'root actual parent acceptance differs')
 target=Path(a.output);claim=target.with_suffix(target.suffix+'.once');require(not target.exists()and not claim.exists(),'prior helper evidence; no retry')
 with claim.open('x')as f:f.write(json.dumps({'input_binding_sha256':a.input_binding_sha256,'authority_sha256':a.once_authority_sha256}));f.flush();os.fsync(f.fileno())
 try:
  result=analyze(Path(binding['input_dir']),manifest,release,parent,selection)
  result.update(input_binding_sha256=a.input_binding_sha256,root_once_authority_sha256=a.once_authority_sha256)
  with target.open('x')as f:f.write(json.dumps(result,indent=2,allow_nan=False)+'\n')
 except Exception as e:
  with target.open('x')as f:f.write(json.dumps({'status':'failed_after_once_claim_no_retry','error_type':type(e).__name__})+'\n')
  raise
 print(json.dumps({'status':result['status'],'output_sha256':sha(target)}))

if __name__=='__main__':main()
