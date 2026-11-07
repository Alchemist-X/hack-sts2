"""One terminal-only M weights_only CPU inspection. No forward/native/model initialization/provider."""
import argparse,hashlib,importlib.util,json,math,os,re,sys,time
from pathlib import Path
import base_115_helper as B
from collect_terminal import CID,BOUND,ATTEMPT,VOLUME,HARD_DEADLINE,CLASSES
PLAN_SHA='edf98387857cec63590d6c4f840ef5b3ed6d748b0c188aec4f4548993c8a6871'
P=Path(__file__).resolve().parent
SHA=B.sha;require=B.require;CAN=B.canonical

def model_metadata(path,release):
 os.environ.update(OMP_NUM_THREADS='1',MKL_NUM_THREADS='1',OPENBLAS_NUM_THREADS='1')
 import torch
 torch.set_num_threads(1);torch.set_num_interop_threads(1);sys.path.insert(0,str(P/'safe_source'))
 spec=importlib.util.spec_from_file_location('M_terminal_contract',P/'profile-overlay/transition_contract.py');contract=importlib.util.module_from_spec(spec);spec.loader.exec_module(contract)
 ck=torch.load(path,map_location='cpu',weights_only=True)  # sole selected body, exactly one load
 contract.validate_checkpoint(ck,release['spec']);contract.validate_gpu_gate(ck.get('first_transition_gate'),release['first_update_gate_context'],spec=release['spec'])
 require(ck['first_transition_gate']['context']==release['first_update_gate_context'],'exact own firstinput gate context differs')
 require(sum(t.numel()for t in ck['model'].values())==16253584 and sum(t.numel()for t in ck['transition_aux']['head'].values())==74052,'M complete actor/head tensor count differs')
 require(all(torch.is_tensor(t)and t.dtype==torch.float32 and bool(torch.isfinite(t).all())for t in ck['model'].values()),'M weights dtype/finite differs')
 def adam(raw,weights):
  names=list(weights)
  require(len(raw['param_groups'])==1,'one actual Adam group required');ids=raw['param_groups'][0]['params'];require(len(ids)==len(names),'Adam/name inventory differs')
  steps={str(k):float(v['step'].item()if torch.is_tensor(v['step'])else v['step'])for k,v in raw['state'].items()if'step'in v}
  for key,state in raw['state'].items():
   require(type(key)is int and key in ids,'Adam state has no actual parameter binding');weight=weights[names[ids.index(key)]]
   fields={'step','exp_avg','exp_avg_sq'}|({'max_exp_avg_sq'}if raw['param_groups'][0].get('amsgrad')else set())
   require(type(state)is dict and set(state)==fields,'actual initialized Adam state incomplete')
   for name in fields-{'step'}:
    moment=state[name];require(torch.is_tensor(moment)and moment.shape==weight.shape and moment.dtype==torch.float32 and bool(torch.isfinite(moment).all()),'actual Adam moment shape/dtype/finite differs')
    if name in ('exp_avg_sq','max_exp_avg_sq'):require(bool((moment>=0).all()),'negative actual Adam squared moment')
   step=state['step'];require(torch.is_tensor(step)and step.ndim==0 and step.dtype==torch.float32,'actual Adam step scalar/dtype differs')
  for v in steps.values():B.number(v,'actual Adam step',True);require(v>0,'initialized Adam clock must be positive')
  return {'initialized_states':len(raw['state']),'parameter_slots':len(ids),'per_state_steps':steps,'parameter_names_by_id':{str(k):n for k,n in zip(ids,names)},'states_without_step':len(raw['state'])-len(steps),'min':min(steps.values())if steps else None,'max':max(steps.values())if steps else None}
 rng=ck.get('rng',{});stream=ck['seed_stream'];infl=ck.get('inflight',{});cfg=ck['config']
 return {'env_steps':ck['env_steps'],'updates':ck['updates'],'spec_sha256':hashlib.sha256(CAN(ck['spec'])).hexdigest(),'strategy':ck.get('strategic_forward_kl'),'base_adam':adam(ck['optimizer'],ck['model']),'aux_adam':adam(ck['transition_aux']['optimizer'],ck['transition_aux']['head']),'recipe_config':dict(cfg),'rng_present':{'numpy':bool(rng.get('numpy')),'torch':torch.is_tensor(rng.get('torch')),'cuda':isinstance(rng.get('cuda'),list)and len(rng['cuda'])==1 and torch.is_tensor(rng['cuda'][0])},'seed_stream':{k:stream.get(k)for k in('prefix','next','issued_until','started','failed','skipped','reserved_skipped')},'inflight':{k:infl.get(k)for k in('slot','n','seed_text','steps')},'counters':dict(ck['counters']),'base_parameters':16253584,'aux_parameters':74052,'gate':ck['first_transition_gate'],'typed_M_fullstate_validated':True}

def check_fresh(proof,config,release):
 spec=release['spec'];require(proof['mode']=='fresh_random'and proof['parent_checkpoint']is None and proof['original_M_checkpoint_loaded']is False and proof['game_state_resume']is False,'freshM proof differs')
 for k,v in {'base_params':16253584,'transition_params':74052,'init_seed':spec['init_seed'],'sample_seed':spec['policy_seed'],'env_seed':spec['env_seed'],'namespace':spec['env_seed_namespace'],'seed_range':release['seed_range']}.items():require(proof[k]==v,'fresh init proof differs: '+k)
 sessions=config['sessions'];require(len(sessions)==1,'fresh own one training session required');s=sessions[0]
 require(s['update_start']==0 and s['resumed_from']is None and s['adam_step']==0 and s['dropped_at_start']==0,'fresh session must not fabricate prior Adam/environment restore')
 guards=json.loads((P/'required-guards.json').read_bytes());project=lambda xs:{(x['id'],x['role'],x['sha256'],x['games'])for x in xs}
 require(project(guards['required_guard_sets'])<=project(proof['isolation']['checked_sets']),'original dev/blind/113ab fence changed');require(set(guards['required_guard_roles'])<=set(proof['isolation']['roles']),'guard roles dropped')
 return {'mode':'fresh_random','resumed_from':None,'starting_Adam_step':0,'dropped_at_start':0,'initial_game_state_restored':False,'guard_sets_observed':[{k:x[k]for k in('id','role','sha256','games')}for x in proof['isolation']['checked_sets']]}

def episode_accounting(root,manifest,meta,rows,normal,namespace):
 infl=meta['inflight'];n=infl['n'];slots=infl['slot'];texts=infl['seed_text'];require(all(isinstance(x,list)for x in(n,slots,texts))and len(n)==len(slots)==len(texts),'actual inflight inventory absent')
 require(len(n)==2048 and len(set(n))==len(n)and len(set(slots))==len(slots)and all(type(x)is int and 0<=x<2048 for x in slots),'inflight duplicate/invalid slot')
 seen=set();closed=failed=post=0;by_update={};digest=hashlib.sha256()
 if'episodes.jsonl'not in manifest['files']:return {'status':'unknown_missing_episode_log','inflight_slots':len(n)}
 with(root/'episodes.jsonl').open()as f:
  for line in f:
   if not line.strip():continue
   e=json.loads(line);u=e['update'];require(type(u)is int and 1<=u<=763,'episode row update outside fresh run')
   if u>meta['updates']:post+=1;continue
   key=e['n'];require(type(key)is int and 0<=key<2**27 and e['seed_text']==f'{namespace}-SILENT-{key}','episode seed identity/fence differs');require(key not in seen,'duplicate closed/failed seed');seen.add(key);digest.update(CAN([key,e['result']]))
   if e['result']=='start_failed':failed+=1
   else:
    require(e['result']in('win','death','trunc','stall','error','other'),'unexpected fresh episode class');closed+=1;by_update[u]=by_update.get(u,0)+1
 for key,text in zip(n,texts):require(type(key)is int and 0<=key<2**27 and text==f'{namespace}-SILENT-{key}'and key not in seen,'inflight overlaps finished/invalid seed')
 st=meta['seed_stream'];c=meta['counters'];require(c['dropped']==0 and st['reserved_skipped']==0,'freshM has no inherited drops/reserved gap');require(st['started']==c['episodes']+st['failed']+len(n),'checkpoint per-run conservation differs');require(st['next']==st['started']+st['skipped'],'fresh consumed seed conservation differs')
 metrics_by={x['update']:x for x in rows};expected_closed=sum(x.get('episodes',0)for x in rows);log_counts_agree=all(by_update.get(u,0)==x['episodes']for u,x in metrics_by.items())and closed==expected_closed
 complete=normal and len(rows)==meta['updates']
 if complete:require(log_counts_agree and closed==c['episodes']and failed==st['failed'],'normal closed episode/run accounting differs')
 return {'status':'verified_complete_run_accounting'if complete else'partial_snapshot_logs_unknown_tail','closed_log_rows_selected_prefix':closed,'failed_start_rows_selected_prefix':failed,'inflight_slots':len(n),'actual_checkpoint_closed_episodes':c['episodes'],'actual_checkpoint_started':st['started'],'actual_checkpoint_failed_starts':st['failed'],'log_closed_matches_observed_metrics':log_counts_agree,'snapshot_missing_closed_rows':c['episodes']-closed,'post_selected_episode_rows_excluded':post,'duplicate_seed_rows':0,'episode_identity_digest':digest.hexdigest(),'full_process_coverage':complete,'unknown_tail_not_fabricated':not complete}

def analyze(root,manifest,release,selection,*,metadata_reader=model_metadata):
 require(manifest.get('call_terminal')is True and manifest['call_id']==CID and manifest['attempt']==ATTEMPT,'actual own terminal M only; live inspection forbidden')
 require(manifest['release_sha256']==BOUND and SHA(P/'release.bound.json')==BOUND and release['output_volume_id']==VOLUME and release['science_plan_sha256']==PLAN_SHA,'fixed Mrelease/source differs')
 require(selection['root_selected']is True and selection['same_attempt']is True and(selection['call_id'],selection['attempt'])==(CID,ATTEMPT),'exact selected single M attempt required')
 chosen=selection['selected_file'];require(Path(chosen).name==chosen and chosen in manifest['files']and chosen.endswith('.pt'),'single selected body required')
 require(all(not n.endswith('.pt')or n==chosen for n in manifest['files']),'no older/donor body reads')
 allowed={'config.json','latest.json','selected-checkpoint.manifest.json','initialization-proof.json','first-transition-gpu-gate.json','status.json','summary.json','final.json','metrics.jsonl','episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl',chosen}
 require(set(manifest['files'])<=allowed,'unrecognized inspection input')
 for n,ref in manifest['files'].items():
  f=root/n;require(Path(n).name==n and not f.is_symlink()and f.is_file()and f.stat().st_size==ref['bytes']and SHA(f)==ref['sha256'],'artifact/hash differs: '+n)
 require(0<manifest['files'][chosen]['bytes']<=256*1024**2 and sum(x['bytes']for n,x in manifest['files'].items()if n in('metrics.jsonl','episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl'))<=512*1024**2,'M body/log bounds exceeded')
 require(selection['selected_sha256']==manifest['files'][chosen]['sha256'],'selected model SHA differs')
 latest=json.loads((root/'latest.json').read_bytes());require(latest['parent_model_sha256']is None and latest['sha256']==selection['selected_sha256'],'fresh latest identity differs')
 require((root/'selected-checkpoint.manifest.json').read_bytes()==(root/'latest.json').read_bytes(),'immutable checkpoint manifest differs from latest');match=re.fullmatch(r'checkpoints/ckpt-([0-9]{7})\.pt',latest['checkpoint']);require(match is not None and int(match[1])==manifest['selected_update']and latest['bytes']==manifest['files'][chosen]['bytes'],'immutable checkpoint update/bytes differ')
 normal=(root/'final.json').exists()or(root/'final.pt').exists();require(selection['normal_final_present']is normal,'normal-presence witness differs')
 final=None;normal_closed_result=False
 if normal:
  needed=('final.json','final.pt','initialization-proof.json','first-transition-gpu-gate.json','status.json','summary.json','episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl')
  require(all(n in manifest['files']for n in needed)and chosen=='final.pt'and selection['kind']=='normal_final','present/invalid final cannot fallback')
  final=json.loads((root/'final.json').read_bytes());require(final['final_sha256']==selection['selected_sha256']and final['latest']==latest,'normal final/latest differs');normal_closed_result=isinstance(final.get('result'),dict)
  if not normal_closed_result:require(final.get('status')in('failed_with_checkpoint','output_censored_with_checkpoint')and final.get('dose_complete')is False,'failed normal final must identify its error/censor class, no latest fallback')
 else:require(selection['kind']=='sole_atomic_latest'and chosen==Path(latest['checkpoint']).name and manifest.get('normal_final_absence_witnessed')is True,'only root-selected sole atomic latest')
 meta=metadata_reader(root/chosen,release)  # exactly one CPU weights_only load; no parent/model initialization
 require(meta['typed_M_fullstate_validated']is True and(meta['base_parameters'],meta['aux_parameters'])==(16253584,74052),'typed M model/aux state required')
 require(meta['updates']==manifest['selected_update']and 0<meta['updates']<=763 and meta['env_steps']==meta['updates']*131072 and meta['spec_sha256']==B.SPEC_SHA,'fresh positive own M dose/schema differs')
 B.marker(meta['strategy'],0);cfg=meta['recipe_config'];require(all(CAN(cfg.get(k))==CAN(v)for k,v in release['spec']['ppo_config'].items()),'PPO recipe changed');require(all(cfg.get(k)==v for k,v in release['spec']['model_config'].items()),'M actor/critic architecture differs')
 require((cfg['init_seed'],cfg['sample_seed'],cfg['env_seed'],cfg['input_arch'])==(release['spec']['init_seed'],release['spec']['policy_seed'],release['spec']['env_seed'],'reward_choices_public_handselect_v1'),'fresh M seed/input identity differs');require(all(meta['rng_present'].values()),'full checkpoint RNG absent')
 stream=meta['seed_stream'];lo,hi=release['seed_range'];require(stream['prefix']==release['spec']['env_seed_namespace']and lo<=stream['next']<=stream['issued_until']<=hi,'M stream escaped fence')
 config=json.loads((root/'config.json').read_bytes());proof=json.loads((root/'initialization-proof.json').read_bytes())if'initialization-proof.json'in manifest['files']else None;fresh=check_fresh(proof,config,release)if proof else{'status':'unknown_missing_initialization_proof'}
 if'first-transition-gpu-gate.json'in manifest['files']:require(json.loads((root/'first-transition-gpu-gate.json').read_bytes())==meta['gate'],'exported firstgate differs from checkpoint')
 ctx={'start_u':0,'start_n':0,'end_u':763,'end_n':100007936};allrows=B.rows_checked([json.loads(x)for x in(root/'metrics.jsonl').read_text().splitlines()if x.strip()],ctx);rows=[x for x in allrows if x['update']<=meta['updates']];post=[x['update']for x in allrows if x['update']>meta['updates']];missing=list(range(len(rows)+1,meta['updates']+1));coverage=not missing
 if normal_closed_result:require(coverage and not post and final['result']['env_steps']==meta['env_steps']and final['result']['updates']==meta['updates']and final['dose_complete']is(meta['updates']==763),'normal requires exact full closed checkpoint log prefix')
 observed=sum(x['strategic_forward_kl']['minibatches']for x in rows if'strategic_forward_kl'in x);known=coverage and all('strategic_forward_kl'in x and x['strategic_forward_kl']['minibatches']==math.ceil(x['learning_samples_update']/cfg['minibatch'])*cfg['epochs']for x in rows)
 for key in('base_adam','aux_adam'):
  a=meta[key];require(a['states_without_step']==0 and len(a['per_state_steps'])==a['initialized_states']<=a['parameter_slots'],'actual Adam state map incomplete')
  if known:require(all(0<v<=observed for v in a['per_state_steps'].values()),'actual fresh Adam clocks exceed observed steps')
 if known:require(meta['aux_adam']['initialized_states']==4 and all(v==observed for v in meta['aux_adam']['per_state_steps'].values()),'aux Adam must match actual minibatch steps; never assumed32/update')
 runs=episode_accounting(root,manifest,meta,rows,normal_closed_result and not post,release['spec']['env_seed_namespace']);meta['inflight_slots']=len(meta.pop('inflight')['slot']);meta['recipe_config']={k:cfg[k]for k in set(release['spec']['ppo_config'])|set(release['spec']['model_config'])|{'init_seed','sample_seed','env_seed','input_arch'}}
 return {'schema':'task116-terminal-safe-inspection-v1','status':'normal_full_M_inspected'if normal_closed_result and meta['updates']==763 else'actual_resource_censored_or_partial_M_inspected','actual_checkpoint_at_target':meta['updates']==763,'selected_sha256':selection['selected_sha256'],'call_id':CID,'attempt':ATTEMPT,'actual_metadata':meta,'fresh_initialization':fresh,'coverage':{'selected_prefix_complete':coverage,'missing_update_rows':missing,'post_selected_log_rows_excluded':post,'normal_full_closed_log':normal_closed_result and coverage and not post},'optimizer_validation':{'expected_exact_minibatch_steps':observed if known else None,'observed_minibatch_steps':observed,'base_initialized_state_map_is_actual':True,'base_missing_slots_not_fabricated':meta['base_adam']['parameter_slots']-meta['base_adam']['initialized_states'],'base_activation_clock_not_assumed_uniform':True,'aux_dense_steps_verified':known},'per_run_accounting':runs,'windows':{'all':B.describe(rows,ctx,1,meta['updates']),'first100':B.describe(rows,ctx,1,100),'last100':B.describe(rows,ctx,max(1,meta['updates']-99),meta['updates'])},'no_forward_native_modelinit_provider_or_new_game':True,'scientific_or_successor_acceptance':False}


def main():
 p=argparse.ArgumentParser(description=__doc__)
 for k in('input-binding','input-binding-sha256','once-authority','once-authority-sha256','output'):p.add_argument('--'+k,required=True)
 a=p.parse_args();require(SHA(a.input_binding)==a.input_binding_sha256 and SHA(a.once_authority)==a.once_authority_sha256,'exact external binding/authority required')
 binding=json.loads(Path(a.input_binding).read_bytes());auth=json.loads(Path(a.once_authority).read_bytes())
 require(auth.get('action')=='TASK116_ONE_TERMINAL_SELECTED_MODEL_METADATA_INSPECTION'and type(auth.get('expires'))in(int,float)and time.time()<auth['expires']and auth.get('allow_once_training_inspection')is True and auth['input_binding_sha256']==a.input_binding_sha256 and auth['helper_pins_sha256']==SHA(P/'pins.json')and auth['output']==a.output,'root exact once M helper authority required')
 for n,h in json.loads((P/'pins.json').read_bytes())['files'].items():
  q=P/n;require(q.resolve().is_relative_to(P)and not q.is_symlink()and SHA(q)==h,'frozen helper/source differs')
 def bound(k):
  ref=binding[k];f=Path(ref['path']);require(f.is_file()and not f.is_symlink()and f.stat().st_size<=2*1024**2 and SHA(f)==ref['sha256'],'bound metadata differs:'+k);return json.loads(f.read_bytes())
 release=bound('release');selection=bound('selection');manifest=bound('manifest');terminal=bound('provider_terminal')
 require(SHA(binding['release']['path'])==BOUND and binding['science_plan_sha256']==PLAN_SHA and manifest['provider_terminal_receipt_sha256']==binding['provider_terminal']['sha256'],'M release/science/terminal receipt identity differs')
 require(terminal['call_id']==CID and terminal['provider_terminal_verified']is True and terminal['observation_class']in('FunctionCall_returned','FunctionCall_verified_terminal_exception','SDK_verified_terminal_cancel'),'actual Mprovider terminal only')
 require((terminal.get('result',{}).get('status')if terminal['observation_class']=='FunctionCall_returned'else terminal.get('status'))in CLASSES,'live/unknown not terminal')
 if selection['kind']=='sole_atomic_latest':require(type(terminal.get('observed_at'))in(int,float)and terminal['observed_at']>=HARD_DEADLINE and manifest.get('normal_final_absence_witnessed')is True,'hardstop absentfinal proof required')
 require(auth['selection_sha256']==binding['selection']['sha256']and auth['provider_terminal_receipt_sha256']==binding['provider_terminal']['sha256'],'root actual selected tuple differs')
 target=Path(a.output);require(target.is_absolute()and str(target).startswith('/data/raven/'),'Raven isolated output only');claim=target.with_suffix(target.suffix+'.once');require(not target.exists()and not claim.exists(),'prior inspection attempt; no retry')
 with claim.open('x')as f:f.write(json.dumps({'input_binding_sha256':a.input_binding_sha256,'authority_sha256':a.once_authority_sha256}));f.flush();os.fsync(f.fileno())
 try:
  result=analyze(Path(binding['input_dir']),manifest,release,selection)
  result.update(input_binding_sha256=a.input_binding_sha256,once_authority_sha256=a.once_authority_sha256)
  with target.open('x')as f:f.write(json.dumps(result,indent=2,allow_nan=False)+'\n')
 except Exception as error:
  with target.open('x')as f:f.write(json.dumps({'status':'failed_after_once_claim_no_retry','error_type':type(error).__name__})+'\n')
  raise
 print(json.dumps({'status':result['status'],'output_sha256':SHA(target)}))
if __name__=='__main__':main()
