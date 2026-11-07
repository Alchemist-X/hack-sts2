"""One actual stopped115 final-checkpoint formaldev256 using unchanged53dd native evaluation."""
from pathlib import Path
from tempfile import TemporaryDirectory
import hashlib,json,os,shutil,time

def canonical(x):return json.dumps(x,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
def sha(path):
 with Path(path).open('rb')as h:return hashlib.file_digest(h,'sha256').hexdigest()
def atomic_json(path,body):
 path=Path(path);tmp=path.with_name(path.name+'.tmp')
 with tmp.open('wb')as h:h.write(canonical(body));h.flush();os.fsync(h.fileno())
 os.replace(tmp,path)
def check_source(bundle,release):
 raw=(bundle/'bundle.json').read_bytes()
 if hashlib.sha256(raw).hexdigest()!=release['bundle_sha256']:raise ValueError('immutable53dd bundle differs')
 manifest=json.loads(raw)
 for name,expected in manifest['files'].items():
  # Only the fixed dev manifest is read; blind/train commitments remain opaque.
  if name.startswith('seeds/') and name!='seeds/'+release['dev_sha256']+'.json':continue
  p=bundle/name
  if p.is_symlink() or not p.is_file() or sha(p)!=expected:raise ValueError('source/native/dev bytes differ')
 if manifest['files'].get('seeds/'+release['dev_sha256']+'.json')!=release['dev_sha256']:raise ValueError('fixeddev absent')
def verify_input(path,release):
 if path.is_symlink() or not path.is_file() or path.stat().st_size!=release['checkpoint_size'] or sha(path)!=release['checkpoint_sha256']:raise ValueError('exact stopped115 checkpoint bytes differ')
def capture_replay(module,out,keys):
 original=module.replay;calls=[]
 def traced(seedset,actions_log,**kwargs):
  if calls:raise ValueError('only original single mandatory replay allowed')
  result=original(seedset,actions_log,**kwargs);calls.append(result)
  atomic_json(out/'replay-audit.json',{'protocol':'original-same-actions-replay-v1','keys':keys,'results':result,'action_counts':[len(a)for a in actions_log]})
  return result
 module.replay=traced
 return calls,lambda:setattr(module,'replay',original)
def collect(out,seedset,report):
 keys=[g['character']+'-'+g['seed']for g in seedset['games']]
 files={p.stem.removesuffix('.summary'):p for p in (out/'games').glob('*.summary.json')}
 if len(keys)!=256 or len(set(keys))!=256 or set(files)!=set(keys):raise ValueError('complete frozen dev256 membership required')
 audit=json.loads((out/'replay-audit.json').read_bytes())
 if audit['keys']!=keys or len(audit['results'])!=256 or len(audit['action_counts'])!=256:raise ValueError('complete original replay evidence required')
 rows=[]
 for i,(key,game)in enumerate(zip(keys,seedset['games'])):
  s=json.loads(files[key].read_bytes());p=audit['results'][i]
  if (s.get('key'),s.get('seed'),s.get('character'),s.get('ascension'),s.get('content'))!=(key,game['seed'],'SILENT',10,'ported_only'):raise ValueError('perseed identity differs')
  if (s.get('replayable')is not True or s.get('illegal')!=0 or len(s.get('history_actions',[]))!=audit['action_counts'][i]
      or p['ended']!=(s['result']!='error') or p['act']!=s['act'] or p['floor']!=s['floor']
      or f"{p['state_hash']:016x}"!=s['final_state_hash'] or p['result']!=s['result']):raise ValueError('original replay verification failed')
  rows.append({'key':key,**{k:s[k]for k in ('result','progress','act','floor','steps','end_kind','end_status','replayable')}})
 metrics={**report['headline'],'result_label':'capacity'}
 if (metrics.get('games')!=256 or metrics.get('progress_games')!=256 or metrics.get('replayable')!=256 or metrics.get('illegal_total')!=0
     or metrics.get('wins')!=sum(r['result']=='win'for r in rows)
     or any(metrics['results'].get(k,0)for k in ('error','crashed','aborted'))):raise ValueError('formalcomplete/error-free denominator required')
 atomic_json(out/'metrics.json',metrics);atomic_json(out/'per_seed.json',rows)
 return metrics

def check_model_spec(ck,release,library,schema):
 from sts2train.features import ModelSpec,PUBLIC_HAND_INPUT_ARCH
 if (ck.get('env_steps'),ck.get('updates'))!=(release['checkpoint_steps'],release['checkpoint_updates']):raise ValueError('exact final dose differs')
 marker=ck.get('strategic_forward_kl',{})
 if marker.get('method')!='task114-map-rest-forward-kl-uniform-v1'or marker.get('beta')!={'control_continuation':0.}[release['arm']]or marker.get('phase_weights')!={'Map':.5,'RestSite':.5}:raise ValueError('exact115 beta0 checkpoint marker differs')
 spec=ModelSpec.from_json(ck['spec'])
 if (spec.input_arch!=PUBLIC_HAND_INPUT_ARCH or sha_json(spec.to_json())!=release['model_spec_sha256']
     or spec.to_json()!=ModelSpec.from_schema(schema,input_arch=PUBLIC_HAND_INPUT_ARCH).to_json()
     or hashlib.sha256(library.schema_json().encode()).hexdigest()!=release['spec']['schema_hash']):raise ValueError('exact ModelSpec/native input contract differs')
 return spec

def run(release,*,bundle,checkpoint,durable,volume):
 import sys
 bundle=Path(bundle);checkpoint=Path(checkpoint);durable=Path(durable);check_source(bundle,release);verify_input(checkpoint,release)
 sys.path.insert(0,str(bundle/'code'))
 os.environ.update(STS2VEC_AOT_LIB=str(bundle/'lib/Sts2Sim.Vec.so'),DOTNET_gcServer='1',DOTNET_PROCESSOR_COUNT='16',OMP_NUM_THREADS='8',MKL_NUM_THREADS='8',OPENBLAS_NUM_THREADS='8')
 from modal_research.artifact_adapter import load_checkpoint
 from modal_research.transition_contract import validate_checkpoint
 from modal_research.native_worker import _evaluate_checkpoint
 from sts2train.features import ModelSpec,PUBLIC_HAND_INPUT_ARCH
 from sts2vec import load,schema_of
 import importlib
 vec=importlib.import_module('sts2vec.evaluate')
 from sts2vec.protocol import validate_seedset
 devpath=bundle/'seeds'/f"{release['dev_sha256']}.json";seedset=validate_seedset(json.loads(devpath.read_bytes()))
 if (seedset['role'],seedset['ascension'],seedset['content'],len(seedset['games']))!=('dev',10,'ported_only',256)or any(g['character']!='SILENT'for g in seedset['games']):raise ValueError('fixeddev protocol differs')
 with TemporaryDirectory(prefix='sts2-dev256-')as temp:
  root=Path(temp);local=root/'final.pt';shutil.copyfile(checkpoint,local);verify_input(local,release);local.chmod(0o400)
  ck,_=load_checkpoint(local)  # safe preflight; no unsafe input or fallback
  validate_checkpoint(ck,release['spec'])
  library=load();schema=schema_of(library);spec=check_model_spec(ck,release,library,schema)
  out=root/'evaluation';out.mkdir()
  atomic_json(out/'preflight.json',{'checkpoint_sha256':release['checkpoint_sha256'],'checkpoint_bytes':release['checkpoint_size'],'checkpoint_steps':release['checkpoint_steps'],'checkpoint_updates':release['checkpoint_updates'],'source_code_commit':release['source_code_commit'],'bundle_sha256':release['bundle_sha256'],'dev_sha256':release['dev_sha256'],'model_spec_sha256':release['model_spec_sha256'],'input_arch':PUBLIC_HAND_INPUT_ARCH,'critic_arch':release['spec']['model_config']['critic_arch'],'engine_sha256':release['spec']['engine_hash'],'schema_sha256':release['spec']['schema_hash'],'precision':'strict-FP32-TF32-off-native-context','policy':'greedy','search':False,'loop_breaker':False,'games':256,'step_cap':1200,'floor_step_cap':80,'max_rows':192,'threads':8,'repeats':1,'training':False,'blind':False,'arm':release['arm'],'science_plan_sha256':release['science_plan_sha256'],'strategic_forward_kl':ck['strategic_forward_kl'],'terminal_evaluation':release['terminal_evaluation'],'root_terminal_decision_sha256':release['root_terminal_decision_sha256']})
  del ck
  keys=[g['character']+'-'+g['seed']for g in seedset['games']];calls,restore=capture_replay(vec,out,keys)
  status='failed';error=None
  try:
   # Original code safely re-reads these exact owned, hashed, read-only bytes.
   # It performs the unmodified greedy fresh evaluation AND mandatory replay.
   report=_evaluate_checkpoint(devpath,local,out,8)
   if len(calls)!=1:raise ValueError('original replay did not execute exactly once')
   metrics=collect(out,seedset,report);status='completed'
  except BaseException as exc:error=type(exc).__name__
  finally:
   restore()
   durable.mkdir(parents=True,exist_ok=False)
   if out.exists():
    if sum(p.stat().st_size for p in out.rglob('*')if p.is_file())>512*1024**2:raise RuntimeError('explicit evaluation output exceeds512MiB')
    shutil.copytree(out,durable/'evaluation')
   payload={str(p.relative_to(durable)):{'sha256':sha(p),'size':p.stat().st_size}for p in durable.rglob('*')if p.is_file()}
   volume.commit()
   atomic_json(durable/'ready.json',{'status':status,'error_type':error,'checkpoint_sha256':release['checkpoint_sha256'],'checkpoint_steps':release['checkpoint_steps'],'checkpoint_updates':release['checkpoint_updates'],'dev_sha256':release['dev_sha256'],'protocol':'native-original-greedy-dev256-replay','files':payload,'finished_at':time.time(),'terminal_evaluation':release['terminal_evaluation'],'science_plan_sha256':release['science_plan_sha256'],'root_terminal_decision_sha256':release['root_terminal_decision_sha256']});volume.commit()
  if status!='completed':raise RuntimeError('formal eval incomplete: '+str(error))
  return metrics

def sha_json(x):return hashlib.sha256(canonical(x)).hexdigest()
