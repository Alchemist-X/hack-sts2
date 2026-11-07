"""Single detached formaldev256 evaluation; no training/controller/blind/automatic retry."""
from pathlib import Path
from uuid import uuid4
import hashlib,json,re,time
SOURCE=Path('/sts2-study');MODEL=Path('/sts2-model');OUTPUT=Path('/sts2-evaluation')
def validate_terminal_binding(release,*,bound=False):
 u=release['checkpoint_updates'];n=release['checkpoint_steps']
 if type(u)is not int or not 0<u<=763 or type(n)is not int or n!=u*131072:raise ValueError('actual positive116M terminal dose required')
 t=release['terminal_evaluation'];full=u==763
 if set(t)!={'classification','final_stop','further_training_authorized','midpoint_evaluation','nominal_primary_observed'}or t['final_stop']is not True or t['further_training_authorized']is not False or t['midpoint_evaluation']is not False:raise ValueError('final stopped M only; no midpoint evaluation')
 if t['classification']!=('full100M_terminal'if full else'resource_censored_terminal')or t['nominal_primary_observed']is not full:raise ValueError('M actual/full100M scope differs')
 if bound:
  for key in ('root_terminal_decision_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256','training_runtime_pins_sha256'):
   if type(release.get(key))is not str or re.fullmatch('[0-9a-f]{64}',release[key])is None:raise ValueError('actual independent/root stop evidence required')
 return {'actual_steps':n,'actual_updates':u,'parent_steps':0,'parent_updates':0,**t}

def validate_release(release,*,bound=False):
 from m_eval_profile import validate_spec,MODEL_SPEC_SHA,PROFILE
 if (release['source_code_commit']!='53dd1d94616a6c1f32c177d8c45e6686571c4188'or release['bundle_sha256']!='b2b81e4053200c6d76b3d3a285b08b01a476b14abe96eadeba76783790ffb88b'or release['dev_sha256']!='ee1be1ba52dd1fa597f600df91fbca46f0f291c6c59c4dedb49e81f561c9e340'or release['model_spec_sha256']!=MODEL_SPEC_SHA or release['profile_overlay']!=PROFILE):raise ValueError('fixed M source/schema/profile differs')
 if release['arm']!='fresh_M_current_recipe'or release['release_id']!='task116-final-only-M-dev256'or release['timeout_seconds']!=3600 or release['budget_cap_cents']!=744 or release['project_cap_cents']!=60000 or release['output_limit_bytes']!=1024**3:raise ValueError('M identity/resource bounds differ')
 if release.get('science_plan_sha256')!='edf98387857cec63590d6c4f840ef5b3ed6d748b0c188aec4f4548993c8a6871':raise ValueError('frozen116science required')
 validate_spec(release['spec'])
 actual=hashlib.sha256(json.dumps(release['spec'],sort_keys=True,separators=(',',':')).encode()).hexdigest()
 if release.get('spec_sha256')!=actual:raise ValueError('exact M recipe spec bytes differ')
 validate_terminal_binding(release,bound=bound)
 if bound:
  if release['status']!='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT':raise ValueError('disabled/no actual accepted M endpoint')
  for key in ('checkpoint_sha256','accepted_training_receipt_sha256','accepted_checkpoint_manifest_sha256','root_scope_sha256'):
   if type(release.get(key))is not str or re.fullmatch('[0-9a-f]{64}',release[key])is None:raise ValueError('actual M proof required')
  if type(release['checkpoint_size'])is not int or not 0<release['checkpoint_size']<=256*1024**2 or type(release['checkpoint_path'])is not str or not release['checkpoint_path']or Path(release['checkpoint_path']).is_absolute()or'..'in Path(release['checkpoint_path']).parts or type(release['not_after'])is not int:raise ValueError('actual M file cap/location required')
  for k,prefix in [('source_volume_id','vo-'),('model_volume_id','vo-'),('output_volume_id','vo-'),('claim_dict_id','di-')]:
   if type(release.get(k))is not str or re.fullmatch(prefix+'[A-Za-z0-9]+',release[k])is None:raise ValueError('real bound metadata only')
  if len({release[k]for k in ('source_volume_id','model_volume_id','output_volume_id')})!=3:raise ValueError('RO inputs and new V2 output differ')
  context=release.get('first_update_gate_context')
  if type(context)is not dict or set(context)!={'experiment_id','config_hash','code_commit','engine_hash','dependency_hash','schema_hash','first_job_key'}or context['config_hash']!=release['spec_sha256']or any(context[k]!=release['spec'][k]for k in ('experiment_id','code_commit','engine_hash','dependency_hash','schema_hash')):raise ValueError('actualMfirstCUDAgate context bound to exact source/config required')
  if type(context['first_job_key'])is not str or not context['first_job_key']:raise ValueError('first CUDA causal job required')
 return release

def execute(modal,release,volume,claims,release_sha,absolute_deadline):
 from eval_worker import atomic_json,run,canonical
 validate_release(release,bound=True)
 if release_sha!=hashlib.sha256(canonical(release)).hexdigest() or type(absolute_deadline)is not int or not time.time()<absolute_deadline<=min(time.time()+3600,release['not_after']):raise ValueError('exact release/deadline required')
 call=modal.current_function_call_id();attempt=uuid4().hex
 if not re.fullmatch('fc-[A-Za-z0-9]+',call):raise ValueError('durable Function handle required')
 volume.reload();root=OUTPUT/release['release_id']/attempt;root.mkdir(parents=True,exist_ok=False)
 handle={'call_id':call,'attempt':attempt,'release_sha256':release_sha,'absolute_deadline':absolute_deadline}
 atomic_json(root/'handle.json',handle);atomic_json(root/'release.json',release);volume.commit()
 admitted=claims.put(release['release_id'],handle,skip_if_exists=True)
 if admitted is not True:return {'status':'not_admitted_no_eval','attempt':attempt}
 atomic_json(root/'entry-status.json',{'status':'running','handle':handle});volume.commit()
 try:
  metrics=run(release,bundle=SOURCE/release['bundle_path'],checkpoint=MODEL/release['checkpoint_path'],durable=root/'result',volume=volume)
  atomic_json(root/'entry-status.json',{'status':'completed','handle':handle,'ready':'result/ready.json'});volume.commit()
  return {'status':'completed','attempt':attempt,'ready':str((root/'result/ready.json').relative_to(OUTPUT)),'metrics':metrics}
 except BaseException as exc:
  atomic_json(root/'entry-status.json',{'status':'failed','error_type':type(exc).__name__,'handle':handle});volume.commit();raise

def declare(modal,release):
 if __name__!='function_app':raise ValueError('fixed serialized module function_app required')
 validate_release(release,bound=True)
 source=modal.Volume.from_id(release['source_volume_id']).hydrate();model=modal.Volume.from_id(release['model_volume_id']).hydrate();output=modal.Volume.from_id(release['output_volume_id']).hydrate()
 from modal_proto.api_pb2 import VOLUME_FS_VERSION_V2
 if output._get_metadata().version!=VOLUME_FS_VERSION_V2:raise ValueError('new output V2 required')
 image=modal.Image.from_id(release['image_id'])
 for filename in ('function_app.py','eval_worker.py','m_eval_profile.py'):
  path=Path(__file__).parent/filename
  with path.open('rb')as h:actual=hashlib.file_digest(h,'sha256').hexdigest()
  if actual!=release['overlay_sha256'][filename]:raise ValueError('exact evaluation wrapper bytes differ')
  image=image.add_local_file(path,remote_path='/root/'+filename,copy=False)
 for rel,pin in release['profile_overlay'].items():
  path=Path(__file__).parent/'profile-overlay'/Path(rel).name
  if hashlib.sha256(path.read_bytes()).hexdigest()!=pin:raise ValueError('M profile bytes differ')
  image=image.add_local_file(path,remote_path='/root/profile-overlay/'+Path(rel).name,copy=False)
 claims=modal.Dict.from_id(release['claim_dict_id']).hydrate();app=modal.App(release['app_name'])
 @app.function(name='evaluate_once',image=image,gpu='H100!',cpu=(32,32),memory=(65536,65536),volumes={str(SOURCE):source.with_mount_options(read_only=True),str(MODEL):model.with_mount_options(read_only=True),str(OUTPUT):output},timeout=3600,startup_timeout=80,serialized=True,retries=0,min_containers=0,max_containers=1,buffer_containers=0,scaledown_window=2,single_use_containers=True,block_network=True)
 def evaluate_once(release_sha,absolute_deadline):return execute(modal,release,output,claims,release_sha,absolute_deadline)
 return app,evaluate_once
