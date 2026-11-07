"""Only new final-only metadata and115 binding seams; no SDK/model/native/statistics."""
from pathlib import Path
import ast,copy,importlib.util,json,sys,types,unittest
from unittest.mock import patch
D=Path(__file__).resolve().parent;R=D.parents[1]
spec=importlib.util.spec_from_file_location('candidate_function_app',D/'function_app.py');app=importlib.util.module_from_spec(spec);spec.loader.exec_module(app)
spec=importlib.util.spec_from_file_location('candidate_eval_worker',D/'eval_worker.py');worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)
class NoSDK:
 def __getattr__(self,name):raise AssertionError('SDK side effect before actual terminal binding: '+name)
class ReachedModelSpec(Exception):pass
class FakeModelSpec:
 @staticmethod
 def from_json(_):raise ReachedModelSpec()
class Tests(unittest.TestCase):
 def bound(self,u=5309):
  r=json.loads((D/'release.DISABLED-DRAFT.json').read_bytes());full=u==5309
  r.update(status='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT',checkpoint_steps=495845376+(u-3783)*131072,checkpoint_updates=u,checkpoint_sha256='a'*64,checkpoint_size=123,checkpoint_path='SYNTHETIC/final.pt',accepted_training_receipt_sha256='b'*64,accepted_checkpoint_manifest_sha256='c'*64,root_scope_sha256='d'*64,root_terminal_decision_sha256='e'*64,accepted_training_source_proof_sha256='f'*64,independent_final_acceptance_sha256='1'*64,model_volume_id='vo-SYNTHETICMODEL',output_volume_id='vo-SYNTHETICOUTPUT',claim_dict_id='di-SYNTHETICCLAIM',not_after=2000000000,terminal_evaluation={'classification':'full200M_terminal'if full else'resource_censored_terminal','final_stop':True,'further_training_authorized':False,'midpoint_evaluation':False,'nominal_primary_observed':full})
  return r
 def test_exact114_collect_replay_source_and_transport_AST_reuse(self):
  original=D/'preimage114'
  def get(p,n):return next(x for x in ast.parse(p.read_text()).body if isinstance(x,ast.FunctionDef)and x.name==n)
  for file,names in [('eval_worker.py',['canonical','sha','atomic_json','check_source','capture_replay','collect','sha_json']),('function_app.py',['execute','declare'])]:
   for n in names:self.assertEqual(ast.dump(get(D/file,n),include_attributes=False),ast.dump(get(original/file,n),include_attributes=False),n)
 def test_unknown_draft_cannot_declare_or_execute_before_SDK(self):
  r=json.loads((D/'release.DISABLED-DRAFT.json').read_bytes())
  with patch.object(app,'__name__','function_app'):
   with self.assertRaisesRegex(ValueError,'actual advanced terminal dose'):app.declare(NoSDK(),r)
  with patch.dict(sys.modules,{'eval_worker':worker}):
   with self.assertRaises(ValueError):app.execute(NoSDK(),r,NoSDK(),NoSDK(),'a'*64,2000000000)
 def test_full200M_and_stopped_resource_censored_actual_endpoints(self):
  for u in (5309,4546,4200):
   r=self.bound(u);app.validate_release(r,bound=True);b=app.validate_terminal_binding(r,bound=True)
   self.assertEqual(b['additional_updates'],u-3783);self.assertEqual(b['additional_steps'],(u-3783)*131072);self.assertIs(b['nominal_primary_observed'],u==5309)
 def test_no_donor_fake_counter_or_beyond_target_substitution(self):
  for u,n in [(3783,495845376),(3782,495714304),(5310,695992320),(5309,695861247),(True,695861248)]:
   r=self.bound();r.update(checkpoint_updates=u,checkpoint_steps=n)
   with self.assertRaises(ValueError):app.validate_release(r,bound=True)
 def test_live_midpoint_cannot_be_eval_and_censored_cannot_claim_full_primary(self):
  for u in (4546,5309):
   for k,v in [('final_stop',False),('further_training_authorized',True),('midpoint_evaluation',True),('nominal_primary_observed',u!=5309),('classification','midpoint')]:
    r=self.bound(u);r['terminal_evaluation'][k]=v
    with self.assertRaises(ValueError):app.validate_release(r,bound=True)
 def test_actual_proofs_publication_and_lineage_required(self):
  for key in ('root_terminal_decision_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256','checkpoint_sha256','accepted_training_receipt_sha256','accepted_checkpoint_manifest_sha256','root_scope_sha256','checkpoint_path','checkpoint_size','model_volume_id','output_volume_id','claim_dict_id'):
   r=self.bound();r[key]=None
   with self.assertRaises(ValueError):app.validate_release(r,bound=True)
  for k,v in [('checkpoint_path','../escape.pt'),('checkpoint_path','/absolute.pt'),('science_plan_sha256','0'*64),('parent_donor_sha256','0'*64),('training_runtime_pins_sha256','0'*64),('arm','strategic_forward_kl'),('release_id','midpoint-dev'),('budget_cap_cents',600)]:
   r=self.bound();r[k]=v
   with self.assertRaises(ValueError):app.validate_release(r,bound=True)
 def test_actual_checkpoint_dose_and_beta_before_ModelSpec_native_boundary(self):
  fake=types.ModuleType('sts2train.features');fake.ModelSpec=FakeModelSpec;fake.PUBLIC_HAND_INPUT_ARCH='SYNTHETIC'
  with patch.dict(sys.modules,{'sts2train.features':fake}):
   r=self.bound(4546);ck={'env_steps':r['checkpoint_steps'],'updates':r['checkpoint_updates'],'spec':{},'strategic_forward_kl':{'method':'task114-map-rest-forward-kl-uniform-v1','beta':0.,'phase_weights':{'Map':.5,'RestSite':.5}}}
   with self.assertRaises(ReachedModelSpec):worker.check_model_spec(ck,r,None,None)
   bad=copy.deepcopy(ck);bad['strategic_forward_kl']['beta']=.02
   with self.assertRaises(ValueError):worker.check_model_spec(bad,r,None,None)
   bad=copy.deepcopy(ck);bad['updates']-=1
   with self.assertRaises(ValueError):worker.check_model_spec(bad,r,None,None)
if __name__=='__main__':unittest.main()
