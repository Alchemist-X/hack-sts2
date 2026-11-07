"""Single detached formaldev256 evaluation; no training/controller/blind/automatic retry."""
from pathlib import Path
from uuid import uuid4
import hashlib,json,re,time
SOURCE=Path('/sts2-study');MODEL=Path('/sts2-model');OUTPUT=Path('/sts2-evaluation')
def validate_terminal_binding(release,*,bound=False):
 u=release['checkpoint_updates'];n=release['checkpoint_steps']
 if type(u)is not int or not 3783<u<=5309 or type(n)is not int or n!=495845376+(u-3783)*131072:raise ValueError('actual advanced terminal dose required; no donor or fabricated counters')
 terminal=release['terminal_evaluation']
 if type(terminal)is not dict or set(terminal)!={'classification','final_stop','further_training_authorized','midpoint_evaluation','nominal_primary_observed'}:raise ValueError('exact final-only decision required')
 if terminal['final_stop']is not True or terminal['further_training_authorized']is not False or terminal['midpoint_evaluation']is not False:raise ValueError('one stopped terminal endpoint only; no midpoint evaluation')
 full=u==5309
 if terminal['classification']!=('full200M_terminal'if full else'resource_censored_terminal')or terminal['nominal_primary_observed']is not full:raise ValueError('actual dose/full200M classification differs')
 if bound:
  for key in ('root_terminal_decision_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256'):
   if type(release.get(key))is not str or not re.fullmatch('[0-9a-f]{64}',release[key]):raise ValueError('actual independently accepted terminal and root stop decision required')
 return {'parent_steps':495845376,'parent_updates':3783,'actual_steps':n,'actual_updates':u,'additional_steps':n-495845376,'additional_updates':u-3783,**terminal}

def validate_release(release,*,bound=False):
 if (release['source_code_commit']!='53dd1d94616a6c1f32c177d8c45e6686571c4188' or release['bundle_sha256']!='b2b81e4053200c6d76b3d3a285b08b01a476b14abe96eadeba76783790ffb88b'
     or release['dev_sha256']!='ee1be1ba52dd1fa597f600df91fbca46f0f291c6c59c4dedb49e81f561c9e340'
     or release['model_spec_sha256']!='63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
     or release['science_plan_sha256']!='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
     or release['arm']!='control_continuation'or release['release_id']!='task115-final-only-dev256'
     or release['training_runtime_pins_sha256']!='02d40c6ed54f222e1d13c2f4509831ae305e0648f93c4df1b00099bda856e9c1'
     or release['parent_donor_sha256']!='7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'
     or release['timeout_seconds']!=900 or release['budget_cap_cents']!=222):raise ValueError('fixed115 source/dev/lineage/resource identity differs')
 if hashlib.sha256(json.dumps(release['spec'],sort_keys=True,separators=(',',':')).encode()).hexdigest()!='bf347e2040b12fc1c838010e794d2b5cdf20905f6e73a2bd9b1b5ee5f094dcd4':raise ValueError('exact104 recipe provenance differs')
 validate_terminal_binding(release,bound=bound)
 if bound:
  if release['status']!='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT':raise ValueError('disabled draft has no execution authority')
  for key in ('checkpoint_sha256','accepted_training_receipt_sha256','accepted_checkpoint_manifest_sha256','root_scope_sha256'):
   if type(release.get(key))is not str or not re.fullmatch('[0-9a-f]{64}',release[key]):raise ValueError('actual accepted final proof required')
  if type(release['checkpoint_size'])is not int or not 0<release['checkpoint_size']<=134217728 or type(release['checkpoint_path'])is not str or not release['checkpoint_path']or Path(release['checkpoint_path']).is_absolute()or'..'in Path(release['checkpoint_path']).parts or type(release['not_after'])is not int:raise ValueError('actual final publication binding required')
  for key,prefix in [('source_volume_id','vo-'),('model_volume_id','vo-'),('output_volume_id','vo-'),('claim_dict_id','di-')]:
   if type(release.get(key))is not str or not re.fullmatch(prefix+'[A-Za-z0-9]+',release[key]):raise ValueError('real metadata IDs required')
  if len({release[k]for k in ('source_volume_id','model_volume_id','output_volume_id')})!=3:raise ValueError('RO inputs and independent output must differ')
 return release

def execute(modal,release,volume,claims,release_sha,absolute_deadline):
 from eval_worker import atomic_json,run,canonical
 validate_release(release,bound=True)
 if release_sha!=hashlib.sha256(canonical(release)).hexdigest() or type(absolute_deadline)is not int or not time.time()<absolute_deadline<=min(time.time()+900,release['not_after']):raise ValueError('exact release/deadline required')
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
 for filename in ('function_app.py','eval_worker.py'):
  path=Path(__file__).parent/filename
  with path.open('rb')as h:actual=hashlib.file_digest(h,'sha256').hexdigest()
  if actual!=release['overlay_sha256'][filename]:raise ValueError('exact evaluation wrapper bytes differ')
  image=image.add_local_file(path,remote_path='/root/'+filename,copy=False)
 claims=modal.Dict.from_id(release['claim_dict_id']).hydrate();app=modal.App(release['app_name'])
 @app.function(name='evaluate_once',image=image,gpu='H100!',cpu=(16,16),memory=(65536,65536),volumes={str(SOURCE):source.with_mount_options(read_only=True),str(MODEL):model.with_mount_options(read_only=True),str(OUTPUT):output},timeout=900,startup_timeout=80,serialized=True,retries=0,min_containers=0,max_containers=1,buffer_containers=0,scaledown_window=2,single_use_containers=True,block_network=True)
 def evaluate_once(release_sha,absolute_deadline):return execute(modal,release,output,claims,release_sha,absolute_deadline)
 return app,evaluate_once
