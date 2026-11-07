"""Only M shape/byte/resource/source differences; no constructors/weights/native/SDK."""
import ast,copy,json,hashlib,sys,types,unittest
from pathlib import Path
from unittest.mock import patch
import function_app as app
import eval_worker as worker
import m_eval_profile as m
D=Path(__file__).parent
class NoSDK:
 def __getattr__(self,n):raise AssertionError('SDK must not be reached')
class Tests(unittest.TestCase):
 def bound(self,u=763):
  r=json.loads((D/'release.DISABLED-DRAFT.json').read_bytes());spec=json.loads((D/'spec.PROVISIONAL-FIXTURE.json').read_bytes());spec['absolute_deadline']=2000000000
  h=hashlib.sha256(json.dumps(spec,sort_keys=True,separators=(',',':')).encode()).hexdigest();full=u==763
  r.update(training_runtime_pins_sha256='2'*64,status='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT',spec=spec,spec_sha256=h,checkpoint_sha256='a'*64,checkpoint_size=196000000,checkpoint_path='SYNTHETIC-M/final.pt',checkpoint_steps=u*131072,checkpoint_updates=u,model_volume_id='vo-MODEL',output_volume_id='vo-OUTPUT',claim_dict_id='di-ONCE',not_after=2000000000,root_scope_sha256='b'*64,accepted_training_receipt_sha256='c'*64,accepted_checkpoint_manifest_sha256='d'*64,root_terminal_decision_sha256='e'*64,accepted_training_source_proof_sha256='f'*64,independent_final_acceptance_sha256='1'*64,terminal_evaluation={'classification':'full100M_terminal'if full else'resource_censored_terminal','final_stop':True,'further_training_authorized':False,'midpoint_evaluation':False,'nominal_primary_observed':full},first_update_gate_context={k:spec[k]for k in ('experiment_id','code_commit','engine_hash','dependency_hash','schema_hash')})
  r['first_update_gate_context'].update(config_hash=h,first_job_key='synthetic116first');return r
 def test_full_M196MB_and_censored_scope_not_S128MB(self):
  for u in (763,400):app.validate_release(self.bound(u),bound=True)
  for size in (0,256*1024**2+1):
   r=self.bound();r['checkpoint_size']=size
   with self.assertRaises(ValueError):app.validate_release(r,bound=True)
 def test_S_shape_or_count_or_head_profile_rejected(self):
  spec=self.bound()['spec'];m.validate_spec(spec)
  for k,v in [('actual_param_count',2941072),('model_config',{**m.MODEL,'d':192}),('input_arch','reward_choices_v1')]:
   s=copy.deepcopy(spec);s[k]=v
   with self.assertRaises(ValueError):m.validate_spec(s)
  ck={'params':m.BASE_PARAMS,'config':m.MODEL,'arch':m.MODEL};self.assertTrue(m.validate_checkpoint_profile(ck));ck['params']=2941072
  with self.assertRaises(ValueError):m.validate_checkpoint_profile(ck)
 def test_disabled_no_actual_model_no_declare_or_execute(self):
  r=json.loads((D/'release.DISABLED-DRAFT.json').read_bytes())
  with patch.object(app,'__name__','function_app'):
   with self.assertRaises((ValueError,TypeError)):app.declare(NoSDK(),r)
  with self.assertRaises((ValueError,TypeError)):app.execute(NoSDK(),r,NoSDK(),NoSDK(),'a'*64,2000000000)
 def test_scope_bound_context_wrong_Sgate_source_or_live_endpoint(self):
  for key,value in [('timeout_seconds',900),('budget_cap_cents',222),('model_spec_sha256','0'*64),('science_plan_sha256','0'*64),('checkpoint_updates',764),('checkpoint_steps',100000000),('profile_overlay',{'sts2train/transition_gpu_gate.py':'cd4f'*16})]:
   r=self.bound();r[key]=value
   with self.assertRaises(ValueError):app.validate_release(r,bound=True)
  r=self.bound(400);r['terminal_evaluation']['further_training_authorized']=True
  with self.assertRaises(ValueError):app.validate_release(r,bound=True)
 def test_exact_two_profiles_and_original_protocol_seams(self):
  for rel,pin in m.PROFILE.items():self.assertEqual(hashlib.sha256((D/'profile-overlay'/Path(rel).name).read_bytes()).hexdigest(),pin)
  original=D/'preimage115'
  def fn(path,name):return next(n for n in ast.parse(path.read_text()).body if isinstance(n,ast.FunctionDef)and n.name==name)
  for n in ('canonical','sha','atomic_json','check_source','capture_replay','collect','sha_json'):self.assertEqual(ast.dump(fn(D/'eval_worker.py',n)),ast.dump(fn(original/'eval_worker.py',n)),n)
  self.assertEqual(ast.dump(fn(D/'function_app.py','execute')),ast.dump(fn(original/'function_app.py','execute')).replace('900','3600'))
 def test_static_resource_and_no_protocol_or_model_forward_rewrite(self):
  tree=ast.parse((D/'function_app.py').read_text());dec=next(n for n in ast.walk(tree)if isinstance(n,ast.Call)and isinstance(n.func,ast.Attribute)and n.func.attr=='function')
  kw={k.arg:ast.literal_eval(k.value)for k in dec.keywords if k.arg in ('cpu','memory','timeout','retries','min_containers','max_containers')}
  self.assertEqual(kw,{'cpu':(32,32),'memory':(65536,65536),'timeout':3600,'retries':0,'min_containers':0,'max_containers':1})
  code=(D/'eval_worker.py').read_text();self.assertIn('report=_evaluate_checkpoint(devpath,local,out,8)',code);self.assertIn('weights_only', (D/'README.md').read_text());self.assertNotIn('evaluate_model(',code)
if __name__=='__main__':unittest.main()
