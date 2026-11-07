"""Task115 preparation: one bounded beta0 continuation segment; no scheduler/evaluation/successor."""
from pathlib import Path
import hashlib,json,os,time,shutil
from tempfile import TemporaryDirectory

MAX_CHECKPOINT_BYTES=128*1024**2
MAX_PUBLICATIONS=32
MAX_LOG_BYTES=256*1024**2
MAX_OUTPUT_BYTES=1024**3
ROLES={'model','optimizer','scheduler','rng','seed_cursor','metrics','history','metrics_raw','transition_aux'}
def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
def sha(path):
 with Path(path).open('rb') as handle:return hashlib.file_digest(handle,'sha256').hexdigest()
def atomic_json(path,body):
 path=Path(path);tmp=path.with_name(path.name+'.tmp')
 with tmp.open('wb') as handle:handle.write(canonical(body));handle.flush();os.fsync(handle.fileno())
 os.replace(tmp,path)
def verify_parent(path,expected):
 path=Path(path)
 if path.is_symlink() or not path.is_file() or path.stat().st_size!=expected['bytes'] or sha(path)!=expected['model_sha256']:raise ValueError('exact109 final bytes differ')
 return path
def verify_resume(ck,expected):
 import torch
 if (type(ck)is not dict or ck.get('env_steps')!=expected['decisions'] or ck.get('updates')!=expected['updates']
     or type(ck.get('optimizer'))is not dict or not ck['optimizer'].get('state') or not ck['optimizer'].get('param_groups')
     or type(ck.get('rng'))is not dict or not torch.is_tensor(ck['rng'].get('torch'))
     or type(ck['rng'].get('cuda'))is not list or len(ck['rng']['cuda'])!=1
     or not torch.is_tensor(ck['rng']['cuda'][0]) or not ck['rng'].get('numpy')
     or type(ck.get('transition_aux'))is not dict):raise ValueError('complete model/base+aux Adam/RNG parent required')
 if ck.get('strategic_forward_kl',{}).get('beta')!=0.0 or ck.get('strategic_forward_kl',{}).get('method')!='task114-map-rest-forward-kl-uniform-v1':raise ValueError('only unchanged114 control continuation permitted')
 cfg=ck['config']
 if (cfg.get('ent_coef'),cfg.get('init_seed'),cfg.get('sample_seed'))!=(0.02,1457924769,759982036):raise ValueError('parent recipe or RNG seed differs')
 stream=ck['seed_stream']
 if stream['prefix']!=expected['namespace'] or stream['next']!=expected['cursor']:raise ValueError('exact parent stream differs')
 # Do not reuse lookahead that was issued before the accepted snapshot.
 issued=stream.get('issued_until')
 if type(issued)is not int or issued<stream['next'] or issued!=expected['issued']:raise ValueError('invalid parent issuance highwater')
 for index,optimizer in enumerate((ck['optimizer'],ck['transition_aux'].get('optimizer',{}))):
  states=optimizer.get('state',{})
  if not states:raise ValueError('initialized base and aux Adam moments required')
  steps=[]
  for value in states.values():
   step=value.get('step')
   if torch.is_tensor(step):step=step.item()
   if type(step)not in (int,float) or not 0<step<=expected['adam_step']:raise ValueError('invalid actual per-parameter Adam offset')
   steps.append(step)
  if max(steps)!=expected['adam_step'] or min(steps)!=expected['adam_step']:raise ValueError('actual base/aux Adam maximum differs')
 return issued

LOG_NAMES=('metrics.jsonl','episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl')
class OutputCensored(RuntimeError):pass

def log_bytes(run):return sum((Path(run)/n).stat().st_size for n in LOG_NAMES if (Path(run)/n).exists())
def enforce_logs(run):
 if log_bytes(run)>MAX_LOG_BYTES:raise OutputCensored('full log byte cap; no tail trimming')
def install_log_bound(writer_class,run):
 original=writer_class.write
 def bounded(self,row):
  from sts2train.runlog import _clean
  size=len((json.dumps(_clean(row),allow_nan=False)+'\n').encode())
  if log_bytes(run)+size>MAX_LOG_BYTES:raise OutputCensored('full log byte cap before write')
  original(self,row)
 writer_class.write=bounded
 return lambda:setattr(writer_class,'write',original)

def used_bytes(root):return sum(p.stat().st_size for p in Path(root).rglob('*') if p.is_file())
class Publisher:
 def __init__(self,run,volume,parent_sha,*,durable=None,target_env_steps):
  self.target_env_steps=target_env_steps;self.source=Path(run);self.run=Path(durable) if durable is not None else self.source;self.run.mkdir(parents=True,exist_ok=True);self.volume=volume;self.parent_sha=parent_sha;self.latest=None;self.count=0
 def release_older(self):
  if self.latest is None:return
  keep=self.run/self.latest['checkpoint']
  for old in (self.run/'checkpoints').glob('ckpt-*.pt'):
   if old!=keep:
    old.unlink();old.with_suffix('.manifest.json').unlink(missing_ok=True)
  self.volume.commit()
 def sync_logs(self):
  enforce_logs(self.source)
  if used_bytes(self.run)+log_bytes(self.source)+MAX_CHECKPOINT_BYTES+65536>MAX_OUTPUT_BYTES:raise OutputCensored('output cap before full log snapshot')
  for name in (*LOG_NAMES,'config.json','status.json','summary.json'):
   source=self.source/name
   if source.exists():
    if source.stat().st_size>MAX_LOG_BYTES:raise OutputCensored('metadata/log file exceeds bound')
    tmp=self.run/(name+'.tmp');shutil.copyfile(source,tmp)
    with tmp.open('rb') as h:os.fsync(h.fileno())
    os.replace(tmp,self.run/name)
  self.volume.commit()
 def publish(self,path):
  path=Path(path)
  if path.parent!=self.source/'checkpoints' or path.is_symlink() or not path.is_file():raise ValueError('only own atomic native checkpoint')
  if not 0<path.stat().st_size<=MAX_CHECKPOINT_BYTES or self.count>=MAX_PUBLICATIONS:raise OutputCensored('checkpoint byte/publication cap')
  if self.source!=self.run:
   enforce_logs(self.source)
   peak=used_bytes(self.run)+path.stat().st_size+log_bytes(self.source)+MAX_CHECKPOINT_BYTES+65536
   if peak>MAX_OUTPUT_BYTES:
    self.release_older()
    peak=used_bytes(self.run)+path.stat().st_size+log_bytes(self.source)+MAX_CHECKPOINT_BYTES+65536
    if peak>MAX_OUTPUT_BYTES:raise OutputCensored('output cap including temporary copies/final reserve')
   target=self.run/'checkpoints'/path.name;target.parent.mkdir(exist_ok=True)
   if target.exists() and sha(target)!=sha(path):raise ValueError('immutable own checkpoint differs')
   if not target.exists():
    tmp=target.with_suffix('.tmp');shutil.copyfile(path,tmp)
    with tmp.open('rb') as h:os.fsync(h.fileno())
    os.replace(tmp,target)
   path=target
   self.sync_logs()
  body={'format':'direct-native-checkpoint-v1','checkpoint':str(path.relative_to(self.run)),'sha256':sha(path),'bytes':path.stat().st_size,'parent_model_sha256':self.parent_sha,'published_at':time.time()}
  self.volume.commit()  # complete atomic payload durable before any new pointer
  atomic_json(path.with_suffix('.manifest.json'),body)
  atomic_json(self.run/'latest.json',body);self.volume.commit()
  self.latest=body;self.count+=1
  # Native CheckpointManager keeps3 payloads; remove only now-orphan own manifests.
  for old in sorted((self.run/'checkpoints').glob('ckpt-*.pt'))[:-3]:old.unlink()
  for item in (self.run/'checkpoints').glob('ckpt-*.manifest.json'):
   if not item.with_name(item.name.replace('.manifest.json','.pt')).exists():item.unlink()
  self.volume.commit()
  return body
 def finish(self,result=None,error=None):
  if self.latest is None:raise RuntimeError('no durable checkpoint produced')
  try:self.sync_logs()
  except OutputCensored:
   self.release_older();self.sync_logs()
  final=self.run/'final.pt';tmp=self.run/'final.pt.tmp'
  shutil.copyfile(self.run/self.latest['checkpoint'],tmp)
  with tmp.open('rb') as handle:os.fsync(handle.fileno())
  os.replace(tmp,final);self.volume.commit()
  body={'final_checkpoint':'final.pt','final_sha256':sha(final),'status':('output_censored_with_checkpoint' if isinstance(error,OutputCensored) else 'failed_with_checkpoint') if error else 'completed'if result and result.get('env_steps')==self.target_env_steps else 'resource_censored_with_checkpoint','latest':self.latest,'checkpoint_publications':self.count,'requested_env_steps':self.target_env_steps,'dose_complete':bool(result and result.get('env_steps')==self.target_env_steps),'continuation_automatic':False,'result':result,'error_type':None if error is None else type(error).__name__,'finished_at':time.time(),'no_evaluation':True,'no_successor':True}
  atomic_json(self.run/'final.json',body);self.volume.commit();return body

def run_options(output,absolute_deadline,now,target_env_steps):
 budget=min(8700,absolute_deadline-now-300)
 if budget<60:raise TimeoutError('insufficient bounded training and final-save margin')
 return dict(out=str(output),device='cuda',threads=32,torch_threads=32,max_hours=budget/3600,max_env_steps=target_env_steps,max_updates=0,eval_every=0,eval_at=(),no_best=True,baseline_steps=0,ckpt_every=0,ckpt_every_min=5,keep_ckpts=3)

def run(release,*,bundle,parent,output,volume,absolute_deadline,patch_dir,ab_path,clock=time.time):
 """Imports unchanged native/PPO only after exact source/parent bytes are verified."""
 import sys
 from function_app import check_bundle
 bundle=Path(bundle);check_bundle(bundle,release['bundle_sha256'])
 # Exact source stays RO. Copy only code into private scratch, then apply pinned two-file learning overlay.
 code_work=TemporaryDirectory(prefix='sts2-114-code-')
 code=Path(code_work.name)/'code';shutil.copytree(bundle/'code',code)
 patch_dir=Path(patch_dir)
 for name,expected in release['learning_overlay'].items():
  source=patch_dir/name
  if source.is_symlink() or sha(source)!=expected:raise ValueError('learning overlay differs')
  target=code/'sts2train'/name;shutil.copyfile(source,target)
 sys.path.insert(0,str(code))
 os.environ.update(STS2VEC_AOT_LIB=str(bundle/'lib/Sts2Sim.Vec.so'),DOTNET_gcServer='1',DOTNET_PROCESSOR_COUNT='32',OMP_NUM_THREADS='32',MKL_NUM_THREADS='32',OPENBLAS_NUM_THREADS='32')
 model_path=verify_parent(parent,release['parent'])
 from modal_research.native_worker import Installation,_validate_seed_manifests,_verified_seed_path,trainer_config_matches
 from modal_research.artifact_adapter import load_checkpoint
 from modal_research.transition_contract import validate_checkpoint
 from sts2train.ppo import resolve_config,train
 spec=release['spec'];ck,_=load_checkpoint(model_path);validate_checkpoint(ck,spec)
 issued=verify_resume(ck,release['parent'])
 segment=release['segment'];start,end=segment['seed_range']
 if type(start)is not int or type(end)is not int or start<max(issued,release['old_reserved_end']) or end-start!=2**27:raise ValueError('one authorized seed grant must skip all old issued/reserved indices')
 installation=Installation(spec['code_commit'],spec['image_digest'],bundle/'lib/Sts2Sim.Vec.so',bundle/'dependencies.lock',bundle/'seeds',Path(parent).parent.parent,Path(output))
 guard=_validate_seed_manifests(*[_verified_seed_path(installation,spec[k]) for k in ('train_seed_manifest_hash','dev_seed_manifest_hash','blind_seed_manifest_hash')],spec)
 from sts2vec import protocol as PG
 ab_path=Path(ab_path)
 if sha(ab_path)!=release['ab_manifest_sha256']:raise ValueError('frozen113 AB bytes differ')
 ab=PG.validate_seedset(json.loads(ab_path.read_bytes()))
 if ab.get('role')!='ab' or PG.checksum(ab)!=release['ab_content_sha256']:raise ValueError('AB role/content differs')
 guard.add(ab,path=str(ab_path))
 import sts2vec
 if hashlib.sha256(sts2vec.load(installation.engine).schema_json().encode()).hexdigest()!=spec['schema_hash']:raise ValueError('frozen native schema differs')
 durable=Path(output);durable.mkdir(parents=True,exist_ok=False)
 work=TemporaryDirectory(prefix='sts2-direct8h-')
 out=Path(work.name)/'run';out.mkdir()
 publisher=Publisher(out,volume,release['parent']['model_sha256'],durable=durable,target_env_steps=segment['target_env_steps'])
 # A committed untouched full parent is always available, including pre-first-update/cap failure.
 initial=out/'checkpoints'/f"ckpt-{release['parent']['updates']:07d}.pt";initial.parent.mkdir();shutil.copyfile(model_path,initial);publisher.publish(initial)
 atomic_json(durable/'resume-proof.json',{'parent_model_sha256':release['parent']['model_sha256'],'base_max_adam_step':release['parent']['adam_step'],'aux_adam_step':release['parent']['adam_step'],'base_initialized_states':len(ck['optimizer']['state']),'base_uninitialized_parameters':sum(len(g['params']) for g in ck['optimizer']['param_groups'])-len(ck['optimizer']['state']),'parent_next':ck['seed_stream']['next'],'parent_issued':issued,'new_seed_begin':start,'new_seed_end':end,'isolation':guard.record(),'strategic_kl_beta':0.0,'segment_index':segment['index'],'requested_env_steps':segment['target_env_steps'],'fresh_episodes':True,'old_inflight_restored':False,'strict_resume':True});volume.commit()
 def log(text):
  encoded=(str(text)+'\n').encode()
  if log_bytes(out)+len(encoded)>MAX_LOG_BYTES:raise OutputCensored('full train log byte cap before write')
  with (out/'train.log').open('ab') as h:h.write(encoded)
  enforce_logs(out)
  print(text,flush=True)
 from sts2train.runlog import JsonlWriter
 restore_logs=install_log_bound(JsonlWriter,out)
 try:
  options=run_options(out,absolute_deadline,clock(),segment['target_env_steps'])
  cfg=resolve_config(options,ck['config'])
  invariants={k:ck['config'][k] for k in ck['config'] if k not in options}
  if not trainer_config_matches(cfg.__dict__,invariants):raise ValueError('non-runtime parent recipe changed')
  result=train(cfg,log=log,resume=ck,resume_path=model_path,given=options,strict_resume=True,isolation_guard=guard,checkpoint_ready=publisher.publish,seed_range=(start,end),strategic_kl_beta=0.0,formal_transition_gpu_gate=True,transition_gate_context=ck.get('first_transition_gate',{}).get('context'),critic_diagnostics=True)
  last=Path(result['last_checkpoint'])
  if publisher.latest is None or publisher.latest['sha256']!=sha(last):publisher.publish(last)
  return publisher.finish({'reason':result.get('stop_reason'),'env_steps':result.get('env_steps'),'updates':result.get('updates')})
 except BaseException as exc:
  if publisher.latest is not None:publisher.finish(error=exc)
  else:atomic_json(durable/'final.json',{'status':'failed_without_new_checkpoint','error_type':type(exc).__name__});volume.commit()
  if isinstance(exc,OutputCensored):return {'status':'output_censored_with_checkpoint','reason':str(exc)}
  raise
 finally:
  restore_logs();work.cleanup();code_work.cleanup()
