"""Task115 disabled preparation: exactly one authorized continuation segment, never a successor launcher."""
from pathlib import Path
from uuid import uuid4
import hashlib,json,os,re,time
SOURCE=Path('/sts2-study');PARENT=Path('/sts2-parent');OUTPUT=Path('/sts2-overnight')

def check_bundle(root,expected):
 root=Path(root);manifest=root/'bundle.json'
 if manifest.is_symlink() or hashlib.sha256(manifest.read_bytes()).hexdigest()!=expected:raise ValueError('exact104 bundle manifest differs')
 files=json.loads(manifest.read_bytes())['files'];actual={}
 for p in root.rglob('*'):
  if p.is_symlink() or not(p.is_dir() or p.is_file()):raise ValueError('unsafe source bundle')
  if p.is_file() and p!=manifest:
   with p.open('rb') as h:actual[p.relative_to(root).as_posix()]=hashlib.file_digest(h,'sha256').hexdigest()
 if actual!=files:raise ValueError('exact104 source/dependency/native members differ')

def validate_release(release,*,bound=False):
 if release['source_code_commit']!='53dd1d94616a6c1f32c177d8c45e6686571c4188' or release['training_seconds']!=8700 or release['timeout_seconds']!=9000 or release['project_cap_cents']!=60000:raise ValueError('fixed115 time/source differs')
 if release['science_plan_sha256']!='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb':raise ValueError('exact root-frozen115 science required')
 if release['image_id']!='im-SiAUSyA568b1WIQLQ1Uljr':raise ValueError('fixed existing image differs')
 if (release['spec']['code_commit']!=release['source_code_commit'] or hashlib.sha256(json.dumps(release['spec'],sort_keys=True,separators=(',',':')).encode()).hexdigest()!='bf347e2040b12fc1c838010e794d2b5cdf20905f6e73a2bd9b1b5ee5f094dcd4' or release['bundle_sha256']!='b2b81e4053200c6d76b3d3a285b08b01a476b14abe96eadeba76783790ffb88b'):raise ValueError('exact historical source/spec/bundle differs')
 if release['learning_overlay']!={'ppo.py':'a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14','strategic_kl.py':'ed4ebfd9b0b0e622646f68bf34dfa3b56f2a7907f3cdbd15e34f3ba20b07c6f1'}:raise ValueError('unchanged114 learning overlay required')
 segment=release['segment'];parent=release['parent'];index=segment['index'];plan=release['continuation']
 if type(index)is not int or index not in (1,2) or segment['beta']!=0.0 or segment['additional_steps']!=100007936 or segment['target_env_steps']!=495845376+index*100007936 or segment['target_updates']!=3783+index*763 or parent['decisions']!=segment['target_env_steps']-100007936 or parent['updates']!=segment['target_updates']-763:raise ValueError('exact two-stage cumulative dose differs')
 if parent['namespace']!='MODA10PH104FRESH1' or not parent['cursor']<=parent['issued'] or parent['decisions']!=parent['updates']*131072:raise ValueError('strict parent stream/counters required')
 start,end=segment['seed_range']
 if any(type(v)is not int for v in (start,end,release['old_reserved_end'])) or end-start!=2**27 or release['old_reserved_end']<1099649195878+(index-1)*2**27 or start<max(parent['issued'],release['old_reserved_end']):raise ValueError('fresh bounded disjoint grant required')
 if plan!={'initial_parent_sha256':'7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c','initial_env_steps':495845376,'initial_updates':3783,'total_additional_steps':200015872,'final_env_steps':695861248,'final_updates':5309,'max_segments':2,'second_segment_automatic':False,'midpoint_dev':False,'quality_branch':False,'second_segment_requires_new_actual_parent_and_separate_once_authority':True,'incomplete_or_unfunded':'resource_censored; one final dev at actual available checkpoint, no full200M claim'}:raise ValueError('no automatic second segment or midpoint quality branch allowed')
 if index==1 and parent['model_sha256']!=plan['initial_parent_sha256']:raise ValueError('exact accepted114 control required')

 if bound:
  for key in ('root_selection_sha256','science_plan_sha256','segment_authority_sha256','funding_receipt_sha256','parent_evidence_sha256'):
   if re.fullmatch('[0-9a-f]{64}',release.get(key)or'')is None:raise ValueError('separate root selection/science/funding/segment once authority required')
  if type(release.get('budget_cap_cents'))is not int or not 0<release['budget_cap_cents']<=60000:raise ValueError('fresh bounded cost admission required')
  for key,prefix in [('source_volume_id','vo-'),('parent_volume_id','vo-'),('output_volume_id','vo-'),('claim_dict_id','di-')]:
   if type(release.get(key))is not str or re.fullmatch(prefix+'[A-Za-z0-9]+',release[key])is None:raise ValueError('real metadata IDs required')
  if len({release[k] for k in ('source_volume_id','parent_volume_id','output_volume_id')})!=3:raise ValueError('RO inputs and RW output must differ')
 return release

def execute(modal,release,volume,claims,release_sha,absolute_deadline):
 from runtime_worker import atomic_json,run
 validate_release(release,bound=True)
 expected=hashlib.sha256(json.dumps(release,sort_keys=True,separators=(',',':'),allow_nan=False).encode()).hexdigest()
 if release_sha!=expected or type(absolute_deadline)is not int or not time.time()<absolute_deadline<=min(time.time()+9000,release['not_after']):raise ValueError('exact single-call release/deadline required')
 call=modal.current_function_call_id();attempt=uuid4().hex
 if not re.fullmatch('fc-[A-Za-z0-9]+',call):raise ValueError('durable function call required')
 volume.reload();root=OUTPUT/release['release_id']/attempt;root.mkdir(parents=True,exist_ok=False)
 handle={'call_id':call,'attempt':attempt,'segment_index':release['segment']['index'],'release_sha256':release_sha,'absolute_deadline':absolute_deadline}
 atomic_json(root/'handle.json',handle);atomic_json(root/'release.json',release);volume.commit()
 # Modal server-side atomic claim, not a cross-container filesystem lock.
 admitted=claims.put(release['release_id'],handle,skip_if_exists=True)
 if admitted is not True:
  atomic_json(root/'entry-status.json',{'status':'not_admitted_no_training','handle':handle});volume.commit()
  return {'status':'not_admitted_no_training','attempt':attempt}
 atomic_json(root/'entry-status.json',{'status':'running','handle':handle});volume.commit()
 try:
  result=run(release,bundle=SOURCE/release['bundle_path'],parent=PARENT/release['parent_path'],output=root/'run',volume=volume,absolute_deadline=absolute_deadline,patch_dir=Path('/root/learning-overlay'),ab_path=Path('/root/seeds128.json'))
  atomic_json(root/'entry-status.json',{'status':result.get('status','completed'),'handle':handle,'final':'run/final.json'});volume.commit()
  return {'status':result.get('status','completed'),'attempt':attempt,'final':str((root/'run/final.json').relative_to(OUTPUT))}
 except BaseException as exc:
  atomic_json(root/'entry-status.json',{'status':'failed','error_type':type(exc).__name__,'handle':handle});volume.commit()
  raise

def declare(modal,release):
 if __name__!='function_app':raise ValueError('declare fixed serialized module function_app only')
 validate_release(release,bound=True)
 source=modal.Volume.from_id(release['source_volume_id']).hydrate();parent=modal.Volume.from_id(release['parent_volume_id']).hydrate();output=modal.Volume.from_id(release['output_volume_id']).hydrate()
 from modal_proto.api_pb2 import VOLUME_FS_VERSION_V2
 if output._get_metadata().version!=VOLUME_FS_VERSION_V2:raise ValueError('independent output must be V2')
 image=modal.Image.from_id(release['image_id'])
 for filename in ('function_app.py','runtime_worker.py'):
  p=Path(__file__).parent/filename
  with p.open('rb')as h:actual=hashlib.file_digest(h,'sha256').hexdigest()
  if actual!=release['overlay_sha256'][filename]:raise ValueError('direct wrapper bytes differ')
  image=image.add_local_file(p,remote_path='/root/'+filename,copy=False)
 for name,expected in release['learning_overlay'].items():
  p=Path(__file__).parent/'learning-overlay'/name
  if hashlib.sha256(p.read_bytes()).hexdigest()!=expected:raise ValueError('learning bytes differ')
  image=image.add_local_file(p,remote_path='/root/learning-overlay/'+name,copy=False)
 ab=Path(__file__).parent/'seeds128.json'
 if hashlib.sha256(ab.read_bytes()).hexdigest()!=release['ab_manifest_sha256']:raise ValueError('AB bytes differ')
 image=image.add_local_file(ab,remote_path='/root/seeds128.json',copy=False)
 claims=modal.Dict.from_id(release['claim_dict_id']).hydrate();app=modal.App(release['app_name'])
 @app.function(name='train_arm',image=image,gpu='H100!',cpu=(32,32),memory=(65536,65536),volumes={str(SOURCE):source.with_mount_options(read_only=True),str(PARENT):parent.with_mount_options(read_only=True),str(OUTPUT):output},timeout=9000,startup_timeout=80,serialized=True,retries=0,min_containers=0,max_containers=1,buffer_containers=0,scaledown_window=2,single_use_containers=True,block_network=True)
 def train_arm(release_sha,absolute_deadline):return execute(modal,release,output,claims,release_sha,absolute_deadline)
 return app,train_arm
