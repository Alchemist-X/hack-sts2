"""Necessary synthetic/typed CPU checks only; no real Model construction/forward/native/provider."""
import copy,importlib.util,json,os,sys,tempfile,time,types,unittest
from pathlib import Path
from unittest.mock import patch
P=Path(__file__).parent;sys.path.insert(0,str(P/'frozen_source'))
import function_app as app
import runtime_worker as worker
import m_profile
from sts2train.ppo import resolve_config
from sts2train.evaluate import parse_mode  # preload pure definitions before fake native package seam
S=importlib.util.spec_from_file_location('m_contract',P/'profile-overlay/transition_contract.py');contract=importlib.util.module_from_spec(S);S.loader.exec_module(contract)
class Volume:
 def __init__(self,root):self.root=Path(root);self.commits=0
 def reload(self):pass
 def commit(self):
  for f in Path('/proc/self/fd').glob('*'):
   try:target=Path(os.readlink(f))
   except OSError:continue
   if self.root in target.parents:raise AssertionError('output FD open at commit')
  p=self.root/'latest.json'
  if p.exists():
   x=json.loads(p.read_bytes());assert worker.sha(self.root/x['checkpoint'])==x['sha256']
  self.commits+=1
class Tests(unittest.TestCase):
 def release(self):return json.loads((P/'release.DISABLED-DRAFT.json').read_bytes())
 def pub(self,temp):
  src=Path(temp)/'scratch';dst=Path(temp)/'output';(src/'checkpoints').mkdir(parents=True);dst.mkdir();return src,dst,worker.Publisher(src,Volume(dst),None,durable=dst,target_env_steps=100007936)
 def test_M_typed_profile_rejects_oldM_andS(self):
  spec=self.release()['spec'];m_profile.validate_spec(spec);contract.validate_recipe(spec)
  old=copy.deepcopy(spec);old['model_config']['critic_arch']='';old['actual_param_count']=16106128
  with self.assertRaises(ValueError):m_profile.validate_spec(old)
  bad=copy.deepcopy(spec);bad['model_config'].update(d=192,s=384,depth=3,heads=6);bad['actual_param_count']=2941072
  with self.assertRaises(ValueError):contract.validate_recipe(bad)
  self.assertEqual(contract.HEAD_PARAMS,74052);self.assertEqual(contract.total_params(spec),16327636)
 def test_M_firstgate_capacity_stub_and_hash_closure(self):
  import torch
  q=importlib.util.spec_from_file_location('sts2train.M_gate116',P/'profile-overlay/transition_gpu_gate.py');gate=importlib.util.module_from_spec(q);q.loader.exec_module(gate)
  device=types.SimpleNamespace(type='cuda',index=0)
  class T:
   dtype=torch.float32
   def __init__(self,n):self.device=device;self.n=n
   def numel(self):return self.n
   def is_floating_point(self):return True
  actor=types.SimpleNamespace(critic_arch='state_legal_candidates_detached_mean_v1',parameters=lambda:iter([T(16253584)]));head=types.SimpleNamespace(parameters=lambda:iter([T(74052)]));updater=types.SimpleNamespace(model=actor,transition_head=head)
  fake=types.SimpleNamespace(float32=torch.float32,is_tensor=lambda x:isinstance(x,T),cuda=types.SimpleNamespace(is_available=lambda:True,get_device_name=lambda d:'SYNTHETIC_ONLY_NO_GPU'),get_default_dtype=lambda:torch.float32,get_float32_matmul_precision=lambda:'highest',backends=types.SimpleNamespace(cuda=types.SimpleNamespace(matmul=types.SimpleNamespace(allow_tf32=False)),cudnn=types.SimpleNamespace(allow_tf32=False)),is_autocast_enabled=lambda k:False,__version__='synthetic')
  with patch.object(gate,'torch',fake):
   facts,_=gate._cuda_runtime(updater,({'x':T(1)},T(4096)));self.assertEqual(facts['device_name'],'SYNTHETIC_ONLY_NO_GPU')
   actor.parameters=lambda:iter([T(2941072)])
   with self.assertRaises(ValueError):gate._cuda_runtime(updater,({'x':T(1)},T(4096)))
  expected=__import__('hashlib').sha256((P/'profile-overlay/transition_gpu_gate.py').read_bytes()).hexdigest();self.assertEqual(contract.science_files(self.release()['spec'])['code/sts2train/transition_gpu_gate.py'],expected)
 def test_real_config_fresh_current_recipe_and_no25mgate(self):
  options=m_profile.run_options(self.release()['spec'],'/scratch',21600,0);cfg=resolve_config(options)
  self.assertEqual((cfg.d,cfg.s,cfg.depth,cfg.heads),(384,768,6,8));self.assertEqual(cfg.max_env_steps,100007936);self.assertEqual(cfg.rng_scheme,'decoupled');self.assertEqual(cfg.start_mode,'stream');self.assertEqual(cfg.eval_at,());self.assertEqual(cfg.eval_every,0);self.assertFalse(cfg.rollout_loop_breaker);self.assertEqual(cfg.transition_coef,.1);self.assertEqual(cfg.ent_coef,.02);self.assertFalse(cfg.amp);self.assertFalse(cfg.tf32);self.assertTrue(cfg.bootstrap_time_limits);self.assertEqual(cfg.max_hours,21300/3600)
  with self.assertRaises(TimeoutError):m_profile.run_options(self.release()['spec'],'/x',350,0)
 def test_large_checkpoint_closed_publication_and_full_log_retention(self):
  with tempfile.TemporaryDirectory()as temp:
   s,d,p=self.pub(temp)
   # Sparse payload exceeds old S128MiB cap, below new256MiB; no model loaded.
   ck=s/'checkpoints/ckpt-0000001.pt'
   with ck.open('wb')as f:f.truncate(129*1024**2)
   (s/'metrics.jsonl').write_bytes(b'untouched full log\n');p.publish(ck);p.finish({'env_steps':131072,'updates':1})
   final=json.loads((d/'final.json').read_bytes());self.assertEqual(final['status'],'resource_censored_with_checkpoint');self.assertFalse(final['dose_complete']);self.assertEqual((d/'metrics.jsonl').read_bytes(),b'untouched full log\n');self.assertEqual((d/'final.pt').stat().st_size,129*1024**2)
 def test_log_and_publication_bound_keep_valid_latest(self):
  with tempfile.TemporaryDirectory()as temp:
   s,d,p=self.pub(temp);ck=s/'checkpoints/ckpt-0000001.pt';ck.write_bytes(b'ck');p.publish(ck)
   with patch.object(worker,'MAX_PUBLICATIONS',1):
    with self.assertRaises(worker.OutputCensored):p.publish(ck)
   (s/'metrics.jsonl').write_bytes(b'full123')
   with patch.object(worker,'MAX_LOG_BYTES',4):
    with self.assertRaises(worker.OutputCensored):p.sync_logs()
   self.assertEqual(worker.sha(d/p.latest['checkpoint']),p.latest['sha256'])
 def test_actual_fresh_worker_entry_no_resume_firstgate_in_training(self):
  release=self.release();seen={};guard=types.SimpleNamespace(add=lambda x,**kw:seen.update(ab=x),record=lambda:{'holdouts':True})
  class Writer:
   def write(self,row):pass
  def fake_train(cfg,**kw):
   seen.update(cfg=cfg,kwargs=kw);out=Path(cfg.out);(out/'checkpoints').mkdir();(out/'metrics.jsonl').write_text('full fresh log\n');p=out/'checkpoints/ckpt-0000763.pt';p.write_bytes(b'new M complete checkpoint');kw['checkpoint_ready'](p);return{'last_checkpoint':str(p),'env_steps':100007936,'updates':763,'stop_reason':'max_env_steps'}
  modules={'modal_research.native_worker':types.SimpleNamespace(Installation=lambda *a:types.SimpleNamespace(engine=a[2]),_validate_seed_manifests=lambda *a:guard,_verified_seed_path=lambda *a:None),'modal_research.transition_contract':types.SimpleNamespace(validate_recipe=lambda s:contract.validate_recipe(s)),'sts2train.ppo':types.SimpleNamespace(resolve_config=resolve_config,train=fake_train),'sts2train.runlog':types.SimpleNamespace(JsonlWriter=Writer),'sts2vec':types.SimpleNamespace(load=lambda *a:types.SimpleNamespace(schema_json=lambda:'{}'),protocol=types.SimpleNamespace(validate_seedset=lambda x:x,checksum=lambda x:release['ab_content_sha256']))}
  with tempfile.TemporaryDirectory()as temp:
   root=Path(temp);bundle=root/'bundle';(bundle/'code/modal_research').mkdir(parents=True);(bundle/'code/sts2train').mkdir();(bundle/'code/sts2train/base.txt').write_text('frozen source');(bundle/'bundle.json').write_bytes(worker.canonical({'files':{'code/sts2train/base.txt':worker.sha(bundle/'code/sts2train/base.txt')}}));release['bundle_sha256']=worker.sha(bundle/'bundle.json');release['spec']['schema_hash']=__import__('hashlib').sha256(b'{}').hexdigest();contract.BASE_ENV=dict(contract.BASE_ENV,schema_hash=release['spec']['schema_hash']);oldpath=list(sys.path)
   try:
    with patch.dict(sys.modules,modules):result=worker.run(release,bundle=bundle,output=root/'output',volume=Volume(root/'output'),absolute_deadline=21600,clock=lambda:0,patch_dir=P/'learning-overlay',profile_path=P/'profile-overlay/transition_contract.py',gate_path=P/'profile-overlay/transition_gpu_gate.py',train_path=P/'train-seeds.json',ab_path=P/'seeds128.json')
   finally:sys.path[:]=oldpath;contract.BASE_ENV['schema_hash']='caddf802fff5f221bca659831c148ea7e5b268e014bcbe2b4e517d3e3bb98f96'
   self.assertEqual(result['status'],'completed');self.assertTrue(result['dose_complete']);self.assertIsNone(seen['kwargs']['resume']);self.assertIsNone(seen['kwargs']['resume_path']);self.assertTrue(seen['kwargs']['strict_resume']);self.assertTrue(seen['kwargs']['formal_transition_gpu_gate']);self.assertEqual(seen['kwargs']['strategic_kl_beta'],0.);self.assertEqual(seen['kwargs']['seed_range'],(0,2**27));self.assertEqual(seen['ab']['role'],'ab');self.assertEqual((seen['cfg'].d,seen['cfg'].depth),(384,6));self.assertEqual((root/'output/final.pt').read_bytes(),b'new M complete checkpoint')
 def test_gate_failure_without_checkpoint_preserves_diagnostic(self):
  release=self.release();seen={};guard=types.SimpleNamespace(add=lambda x,**kw:seen.update(ab=x),record=lambda:{'holdouts':True})
  class Writer:
   def write(self,row):pass
  def fake_train(cfg,**kw):
   seen.update(cfg=cfg,kwargs=kw);out=Path(cfg.out);(out/'metrics.jsonl').write_text('full failure log\n');(out/worker.GATE_NAME).write_bytes(worker.canonical({'kind':'SYNTHETIC_TEST_ONLY','passed':False,'error_code':'GATE_REJECTED','samples':4096}));raise RuntimeError('synthetic firstgate failure')
  modules={'modal_research.native_worker':types.SimpleNamespace(Installation=lambda *a:types.SimpleNamespace(engine=a[2]),_validate_seed_manifests=lambda *a:guard,_verified_seed_path=lambda *a:None),'modal_research.transition_contract':types.SimpleNamespace(validate_recipe=lambda s:contract.validate_recipe(s)),'sts2train.ppo':types.SimpleNamespace(resolve_config=resolve_config,train=fake_train),'sts2train.runlog':types.SimpleNamespace(JsonlWriter=Writer),'sts2vec':types.SimpleNamespace(load=lambda *a:types.SimpleNamespace(schema_json=lambda:'{}'),protocol=types.SimpleNamespace(validate_seedset=lambda x:x,checksum=lambda x:release['ab_content_sha256']))}
  with tempfile.TemporaryDirectory()as temp:
   root=Path(temp);bundle=root/'bundle';(bundle/'code/modal_research').mkdir(parents=True);(bundle/'code/sts2train').mkdir();(bundle/'code/sts2train/base.txt').write_text('frozen source');(bundle/'bundle.json').write_bytes(worker.canonical({'files':{'code/sts2train/base.txt':worker.sha(bundle/'code/sts2train/base.txt')}}));release['bundle_sha256']=worker.sha(bundle/'bundle.json');release['spec']['schema_hash']=__import__('hashlib').sha256(b'{}').hexdigest();contract.BASE_ENV=dict(contract.BASE_ENV,schema_hash=release['spec']['schema_hash']);oldpath=list(sys.path)
   try:
    with patch.dict(sys.modules,modules),self.assertRaises(RuntimeError):worker.run(release,bundle=bundle,output=root/'output',volume=Volume(root/'output'),absolute_deadline=21600,clock=lambda:0,patch_dir=P/'learning-overlay',profile_path=P/'profile-overlay/transition_contract.py',gate_path=P/'profile-overlay/transition_gpu_gate.py',train_path=P/'train-seeds.json',ab_path=P/'seeds128.json')
   finally:sys.path[:]=oldpath;contract.BASE_ENV['schema_hash']='caddf802fff5f221bca659831c148ea7e5b268e014bcbe2b4e517d3e3bb98f96'
   final=json.loads((root/'output/final.json').read_bytes());self.assertEqual(final['status'],'failed_without_new_checkpoint');self.assertFalse(final['dose_complete']);self.assertEqual(final['first_update_gate_receipt'],worker.GATE_NAME);self.assertEqual(final['first_update_gate_receipt_sha256'],worker.sha(root/'output'/worker.GATE_NAME));self.assertFalse((root/'output/final.pt').exists());self.assertEqual((root/'output/metrics.jsonl').read_text(),'full failure log\n');self.assertFalse(json.loads((root/'output'/worker.GATE_NAME).read_bytes())['passed'])
 def test_once_and_disabled_release_never_load_old_checkpoint(self):
  r=self.release();app.validate_release(r)
  with self.assertRaises(ValueError):app.validate_release(r,bound=True)
  r.update(source_volume_id='vo-original',output_volume_id='vo-new',claim_dict_id='di-once',not_after=int(time.time())+21600,budget_cap_cents=5000,root_scope_sha256='a'*64,science_plan_sha256='b'*64,funding_receipt_sha256='c'*64)
  sdk=types.SimpleNamespace(current_function_call_id=lambda:'fc-new');claims=types.SimpleNamespace(put=lambda *a,**kw:True);digest=__import__('hashlib').sha256(worker.canonical(r)).hexdigest()
  with tempfile.TemporaryDirectory()as temp,patch.object(app,'OUTPUT',Path(temp)),patch.object(worker,'run',return_value={'status':'completed'})as run:
   app.execute(sdk,r,Volume(temp),claims,digest,int(time.time())+21600);self.assertEqual(run.call_count,1);self.assertNotIn('parent',run.call_args.kwargs)
   claims.put=lambda *a,**kw:False;run.reset_mock();app.execute(sdk,r,Volume(temp),claims,digest,int(time.time())+21600);run.assert_not_called()
  r['parent_checkpoint']='oldM.pt'
  with self.assertRaises(ValueError):app.validate_release(r)
if __name__=='__main__':unittest.main()
