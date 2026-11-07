"""Task116 fresh M current-recipe bounded training; no old checkpoint/scheduler/evaluation/successor."""
from pathlib import Path
import hashlib,json,os,time,shutil
from tempfile import TemporaryDirectory

MAX_CHECKPOINT_BYTES=256*1024**2
MAX_PUBLICATIONS=75
MAX_LOG_BYTES=512*1024**2
MAX_OUTPUT_BYTES=4*1024**3
ROLES={'model','optimizer','scheduler','rng','seed_cursor','metrics','history','metrics_raw','transition_aux'}
def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
def sha(path):
 with Path(path).open('rb') as handle:return hashlib.file_digest(handle,'sha256').hexdigest()
def atomic_json(path,body):
 path=Path(path);tmp=path.with_name(path.name+'.tmp')
 with tmp.open('wb') as handle:handle.write(canonical(body));handle.flush();os.fsync(handle.fileno())
 os.replace(tmp,path)
GATE_NAME='first-transition-gpu-gate.json'
MAX_GATE_BYTES=128*1024
SIDE_NAMES=('config.json','status.json','summary.json',GATE_NAME)
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
  if used_bytes(self.run)+sum((self.source/n).stat().st_size for n in (*LOG_NAMES,*SIDE_NAMES)if(self.source/n).exists())+MAX_CHECKPOINT_BYTES+65536>MAX_OUTPUT_BYTES:raise OutputCensored('output cap before full log snapshot')
  for name in (*LOG_NAMES,*SIDE_NAMES):
   source=self.source/name
   if source.exists():
    if source.is_symlink()or source.stat().st_size>(MAX_GATE_BYTES if name==GATE_NAME else MAX_LOG_BYTES):raise OutputCensored('metadata/log/gate file exceeds bound or unsafe')
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
  body={'final_checkpoint':'final.pt','final_sha256':sha(final),'status':('output_censored_with_checkpoint' if isinstance(error,OutputCensored) else 'failed_with_checkpoint') if error else 'completed'if result and result.get('env_steps')==self.target_env_steps else 'resource_censored_with_checkpoint','latest':self.latest,'checkpoint_publications':self.count,'requested_env_steps':self.target_env_steps,'dose_complete':bool(result and result.get('env_steps')==self.target_env_steps),'continuation_automatic':False,'result':result,'error_type':None if error is None else type(error).__name__,'finished_at':time.time(),'no_evaluation':True,'no_successor':True,'first_update_gate_receipt':GATE_NAME if(self.run/GATE_NAME).exists()else None,'first_update_gate_receipt_sha256':sha(self.run/GATE_NAME)if(self.run/GATE_NAME).exists()else None}
  atomic_json(self.run/'final.json',body);self.volume.commit();return body

def run(release,*,bundle,output,volume,absolute_deadline,patch_dir,profile_path,gate_path,train_path,ab_path,clock=time.time):
 import sys
 from function_app import check_bundle
 from m_profile import validate_spec,run_options,TARGET,BASE_PARAMS,HEAD_PARAMS
 bundle=Path(bundle);check_bundle(bundle,release['bundle_sha256']);spec=validate_spec(release['spec'])
 code_work=TemporaryDirectory(prefix='sts2-116-code-');code=Path(code_work.name)/'code';shutil.copytree(bundle/'code',code)
 for name,expected in release['learning_overlay'].items():
  source=Path(patch_dir)/name
  if source.is_symlink()or sha(source)!=expected:raise ValueError('unchanged beta0 learning overlay differs')
  shutil.copyfile(source,code/'sts2train'/name)
 if sha(profile_path)!=release['m_contract_sha256']:raise ValueError('M typed contract differs')
 shutil.copyfile(profile_path,code/'modal_research/transition_contract.py')
 if sha(gate_path)!=release['m_gate_sha256']:raise ValueError('M mandatory first GPU gate source differs')
 shutil.copyfile(gate_path,code/'sts2train/transition_gpu_gate.py');sys.path.insert(0,str(code))
 os.environ.update(STS2VEC_AOT_LIB=str(bundle/'lib/Sts2Sim.Vec.so'),DOTNET_gcServer='1',DOTNET_PROCESSOR_COUNT='32',OMP_NUM_THREADS='32',MKL_NUM_THREADS='32',OPENBLAS_NUM_THREADS='32')
 from modal_research.native_worker import Installation,_validate_seed_manifests,_verified_seed_path
 from modal_research.transition_contract import validate_recipe
 from sts2train.ppo import resolve_config,train
 validate_recipe(spec)
 installation=Installation(spec['code_commit'],spec['image_digest'],bundle/'lib/Sts2Sim.Vec.so',bundle/'dependencies.lock',bundle/'seeds',Path(output),Path(output))
 if sha(train_path)!=spec['train_seed_manifest_hash']:raise ValueError('fresh116 stream manifest differs')
 guard=_validate_seed_manifests(Path(train_path),*[_verified_seed_path(installation,spec[k])for k in ('dev_seed_manifest_hash','blind_seed_manifest_hash')],spec)
 from sts2vec import protocol as PG
 if sha(ab_path)!=release['ab_manifest_sha256']:raise ValueError('113holdout bytes differ')
 ab=PG.validate_seedset(json.loads(Path(ab_path).read_bytes()))
 if ab.get('role')!='ab'or PG.checksum(ab)!=release['ab_content_sha256']:raise ValueError('113holdout role/content differs')
 guard.add(ab,path=str(ab_path))
 import sts2vec
 if hashlib.sha256(sts2vec.load(installation.engine).schema_json().encode()).hexdigest()!=spec['schema_hash']:raise ValueError('original native schema differs')
 start,end=release['seed_range']
 if(start,end)!=(0,2**27):raise ValueError('bounded fresh116 namespace seed grant differs')
 durable=Path(output);durable.mkdir(parents=True,exist_ok=False);work=TemporaryDirectory(prefix='sts2-116-fresh-');out=Path(work.name)/'run';out.mkdir()
 publisher=Publisher(out,volume,None,durable=durable,target_env_steps=TARGET)
 atomic_json(durable/'initialization-proof.json',{'mode':'fresh_random','parent_checkpoint':None,'original_M_checkpoint_loaded':False,'base_params':BASE_PARAMS,'transition_params':HEAD_PARAMS,'source_base':release['source_code_commit'],'source_overlay':release['learning_overlay'],'m_contract_sha256':release['m_contract_sha256'],'init_seed':spec['init_seed'],'sample_seed':spec['policy_seed'],'env_seed':spec['env_seed'],'namespace':spec['env_seed_namespace'],'seed_range':[start,end],'isolation':guard.record(),'game_state_resume':False});volume.commit()
 def log(text):
  encoded=(str(text)+'\n').encode()
  if log_bytes(out)+len(encoded)>MAX_LOG_BYTES:raise OutputCensored('full M train log cap before write')
  with(out/'train.log').open('ab')as h:h.write(encoded)
  print(text,flush=True)
 from sts2train.runlog import JsonlWriter
 restore_logs=install_log_bound(JsonlWriter,out)
 try:
  options=run_options(spec,out,absolute_deadline,clock());cfg=resolve_config(options)
  result=train(cfg,log=log,resume=None,resume_path=None,given=options,strict_resume=True,isolation_guard=guard,checkpoint_ready=publisher.publish,seed_range=(start,end),strategic_kl_beta=0.0,formal_transition_gpu_gate=True,transition_gate_context=release['first_update_gate_context'],critic_diagnostics=True)
  last=Path(result['last_checkpoint'])
  if publisher.latest is None or publisher.latest['sha256']!=sha(last):publisher.publish(last)
  return publisher.finish({'reason':result.get('stop_reason'),'env_steps':result.get('env_steps'),'updates':result.get('updates')})
 except BaseException as exc:
  if publisher.latest is not None:publisher.finish(error=exc)
  else:
   publisher.sync_logs();atomic_json(durable/'final.json',{'status':'failed_without_new_checkpoint','error_type':type(exc).__name__,'dose_complete':False,'requested_env_steps':TARGET,'no_evaluation':True,'no_successor':True,'first_update_gate_receipt':GATE_NAME if(durable/GATE_NAME).exists()else None,'first_update_gate_receipt_sha256':sha(durable/GATE_NAME)if(durable/GATE_NAME).exists()else None});volume.commit()
  if isinstance(exc,OutputCensored):return{'status':'output_censored_with_checkpoint'if publisher.latest is not None else'output_censored_without_checkpoint','dose_complete':False}
  raise
 finally:restore_logs();work.cleanup();code_work.cleanup()
