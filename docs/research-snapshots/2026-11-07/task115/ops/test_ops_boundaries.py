import unittest,tempfile,json,hashlib,time,types,sys,runpy
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).parent
class Boundaries(unittest.TestCase):
 def setUp(self):
  self.t=tempfile.TemporaryDirectory();self.w=Path(self.t.name);self.m=types.ModuleType('ops_common');self.m.__file__=str(ROOT/'ops_common.py');exec((ROOT/'ops_common.py').read_text(),self.m.__dict__);self.m.W=self.w
  self.r={'science_plan_sha256':'548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb','segment_authority_sha256':'751b140b38e7fe77d2b5a6ac9fd50a20feacb449bf4de561d56894208e05e8e2','parent':{'model_sha256':'7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'},'segment':{'index':1,'seed_range':[1099649195878,1099783413606],'target_env_steps':595853312},'not_after':time.time()+5000}
  self.bind()
 def bind(self):
  raw=self.m.CAN(self.r).encode();(self.w/'release.bound.json').write_bytes(raw);self.b=self.m.H(raw);a={'bound_release_sha256':self.b,'science_plan_sha256':self.r['science_plan_sha256'],'authorized_segment':1,'max_submissions':1,'expires':time.time()+5000};raw=self.m.CAN(a).encode();(self.w/'root-authorization.json').write_bytes(raw);self.a=self.m.H(raw)
 def tearDown(self):self.t.cleanup()
 def test_exact_bound_pass(self):self.assertEqual(self.m.inputs(self.a,self.b)[1],self.r)
 def test_changed_parent_refused(self):
  self.r['parent']['model_sha256']='0'*64;self.bind()
  with self.assertRaises(AssertionError):self.m.inputs(self.a,self.b)
 def test_changed_seed_refused(self):
  self.r['segment']['seed_range'][0]-=1;self.bind()
  with self.assertRaises(AssertionError):self.m.inputs(self.a,self.b)
 def test_second_segment_refused(self):
  self.r['segment']['index']=2;self.bind()
  with self.assertRaises(AssertionError):self.m.inputs(self.a,self.b)
 def test_commitment_fresh_proof_generated_internally_bad_tuple_refused(self):
  (self.w/'funding_snapshot.py').write_bytes(b'private fake boundary')
  a=json.loads((self.w/'root-authorization.json').read_bytes());a['allow_dynamic_fresh_admission']=True;a['ops_file_sha256']={'funding_snapshot.py':self.m.H((self.w/'funding_snapshot.py').read_bytes())};raw=self.m.CAN(a).encode();(self.w/'root-authorization.json').write_bytes(raw);self.a=self.m.H(raw)
  fake=types.SimpleNamespace(stdout=json.dumps({'segment_scope_sha256':'751b140b38e7fe77d2b5a6ac9fd50a20feacb449bf4de561d56894208e05e8e2','allocations':{'segment1_train':1629,'final_dev':222,'global_blind_protection':1075},'root_authority_sha256':'wrong','bound_release_sha256':self.b,'funding_source_sha256':a['ops_file_sha256']['funding_snapshot.py']}).encode())
  with patch.dict(sys.modules,{'ops_common':self.m}),patch.object(sys,'argv',['record',self.a,self.b]),patch('subprocess.run',return_value=fake)as call:
   with self.assertRaises(AssertionError):runpy.run_path(str(ROOT/'record_commitment.py'))
   self.assertEqual(call.call_count,1);self.assertEqual(call.call_args.args[0][-2:],[self.a,self.b]);self.assertFalse((self.w/'commitment-receipt.json').exists())
 def test_existing_spawn_intent_blocks_before_sdk(self):
  (self.w/'spawn-intent.json').write_text('{}')
  with patch.dict(sys.modules,{'ops_common':self.m,'modal':types.ModuleType('modal')}),patch.object(sys,'argv',['spawn',self.a,self.b]):
   with self.assertRaisesRegex(AssertionError,'previous intent'):runpy.run_path(str(ROOT/'spawn_once.py'))
 def test_metadata_binds_only_authorized_fields_no_compute(self):
  (self.w/"release.bound.json").unlink()
  (self.w/'release.segment1.DISABLED-DRAFT.json').write_bytes((ROOT/'release.segment1.DISABLED-DRAFT.json').read_bytes())
  a={'allow_metadata_only':True,'compute_invocations_authorized':0,'expires':time.time()+500,'draft_release_sha256':'32414204c4d2c07a30e50cbf375ede035aa9f6d96f51f6def71424d415399265','release_binding':{'budget_cap_cents':1629,'science_plan_sha256':'548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb','source_volume_id':'vo-source','parent_volume_id':'vo-parent'}}
  raw=self.m.CAN(a).encode();(self.w/'root-metadata-authorization.json').write_bytes(raw);auth=self.m.H(raw)
  v=types.SimpleNamespace(object_id='vo-newoutput',hydrate=lambda:None,_get_metadata=lambda:types.SimpleNamespace(version=2),listdir=lambda *x,**kw:[]);v.hydrate=lambda:v
  d=types.SimpleNamespace(object_id='di-newclaims',len=lambda:0);d.hydrate=lambda:d
  fake=types.ModuleType('modal');fake.__version__='1.6.3';fake.Workspace=types.SimpleNamespace(from_context=lambda:types.SimpleNamespace(hydrate=lambda:types.SimpleNamespace(name='hycrpg')));fake.Volume=types.SimpleNamespace(from_name=lambda *x,**kw:v);fake.Dict=types.SimpleNamespace(from_name=lambda *x,**kw:d)
  with patch.dict(sys.modules,{'ops_common':self.m,'modal':fake}),patch.object(sys,'argv',['meta',auth]):runpy.run_path(str(ROOT/'create_metadata.py'))
  bound=json.loads((self.w/'release.bound.json').read_bytes());self.assertEqual(bound['output_volume_id'],'vo-newoutput');self.assertEqual(bound['claim_dict_id'],'di-newclaims');self.assertEqual(json.loads((self.w/'metadata-receipt.json').read_bytes())['compute_invocations'],0)
 def test_metadata_unapproved_binding_refused(self):
  (self.w/'release.segment1.DISABLED-DRAFT.json').write_bytes((ROOT/'release.segment1.DISABLED-DRAFT.json').read_bytes())
  a={'allow_metadata_only':True,'compute_invocations_authorized':0,'expires':time.time()+500,'draft_release_sha256':'32414204c4d2c07a30e50cbf375ede035aa9f6d96f51f6def71424d415399265','release_binding':{'timeout_seconds':99999}}
  raw=self.m.CAN(a).encode();(self.w/'root-metadata-authorization.json').write_bytes(raw)
  with patch.dict(sys.modules,{'ops_common':self.m,'modal':types.ModuleType('modal')}),patch.object(sys,'argv',['meta',self.m.H(raw)]):
   with self.assertRaises(AssertionError):runpy.run_path(str(ROOT/'create_metadata.py'))
   self.assertFalse((self.w/'metadata-intent.json').exists())
if __name__=='__main__':unittest.main()
