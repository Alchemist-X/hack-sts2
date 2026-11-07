import unittest,tempfile,json,types,sys,runpy
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).parent
class Tests(unittest.TestCase):
 def setUp(self):
  self.t=tempfile.TemporaryDirectory();self.w=Path(self.t.name);self.m=types.ModuleType('ops_common');self.m.__file__=str(ROOT/'ops_common.py');exec((ROOT/'ops_common.py').read_text(),self.m.__dict__);self.m.W=self.w
  self.r=json.loads((ROOT/'release.DISABLED-DRAFT.json').read_bytes());self.r.update(source_volume_id='vo-NWLwwK5soAAAaKiLjay1Ek',output_volume_id='vo-NEW',claim_dict_id='di-NEW',not_after=self.m.time.time()+30000,funding_receipt_sha256='a'*64)
  self.bind()
 def bind(self):
  raw=self.m.CAN(self.r).encode();(self.w/'release.bound.json').write_bytes(raw);self.b=self.m.H(raw);self.a={'bound_release_sha256':self.b,'science_plan_sha256':self.m.SCIENCE,'project_cap_cents':60000,'platform_admission_policy':self.m.POLICY,'max_submissions':1,'total_commitment_cents':5762,'expires':self.m.time.time()+600};raw=self.m.CAN(self.a).encode();(self.w/'root-paid-authorization.json').write_bytes(raw);self.auth=self.m.H(raw)
 def tearDown(self):self.t.cleanup()
 def test_exact_fresh_tuple_pass(self):self.assertEqual(self.m.inputs(self.auth,self.b)[1],self.r)
 def test_old_project300_or_parent_forbidden(self):
  self.r['project_cap_cents']=30000;self.bind()
  with self.assertRaises(AssertionError):self.m.inputs(self.auth,self.b)
  self.r['project_cap_cents']=60000;self.r['parent_checkpoint']='old.pt';self.bind()
  with self.assertRaises(AssertionError):self.m.inputs(self.auth,self.b)
 def test_only_S115_parallel_allowed(self):
  self.m.validate_concurrency({'115segment1':{'num_running_inputs':1,'num_total_runners':1,'backlog':0},'old':{'num_running_inputs':0,'num_total_runners':0,'backlog':0}})
  with self.assertRaises(AssertionError):self.m.validate_concurrency({'other':{'num_running_inputs':1,'num_total_runners':1,'backlog':0}})
 def test_existing_spawn_intent_blocks_before_sdk(self):
  self.a['allow_commit_and_spawn']=True;raw=self.m.CAN(self.a).encode();(self.w/'root-paid-authorization.json').write_bytes(raw);auth=self.m.H(raw);(self.w/'spawn-intent.json').write_text('{}')
  with patch.dict(sys.modules,{'ops_common':self.m,'modal':types.ModuleType('modal')}),patch.object(sys,'argv',['spawn',auth,self.b]):
   with self.assertRaisesRegex(AssertionError,'previous intent'):runpy.run_path(str(ROOT/'spawn_once.py'))
 def test_funds_internal_execution_bad_tuple_refused_before_store(self):
  (self.w/'funding_snapshot.py').write_bytes(b'privatefakefresh')
  self.a.update(allow_dynamic_fresh_admission=True,allow_commit_and_spawn=True,ops_file_sha256={'funding_snapshot.py':self.m.H((self.w/'funding_snapshot.py').read_bytes())});raw=self.m.CAN(self.a).encode();(self.w/'root-paid-authorization.json').write_bytes(raw);auth=self.m.H(raw)
  fake=types.SimpleNamespace(stdout=json.dumps({'status':'PASS','root_authority_sha256':'wrong'}).encode())
  with patch.dict(sys.modules,{'ops_common':self.m}),patch.object(sys,'argv',['commit',auth,self.b]),patch('subprocess.run',return_value=fake)as call:
   with self.assertRaises(AssertionError):runpy.run_path(str(ROOT/'record_commitment.py'))
   self.assertEqual(call.call_count,1);self.assertEqual(call.call_args.args[0][-2:],[auth,self.b]);self.assertFalse((self.w/'commitment-receipt.json').exists())
 def test_metadata_fake_sdk_only_fixed_bind(self):
  (self.w/'release.bound.json').unlink();(self.w/'release.DISABLED-DRAFT.json').write_bytes((ROOT/'release.DISABLED-DRAFT.json').read_bytes())
  a={'allow_metadata_only':True,'compute_invocations_authorized':0,'expires':self.m.time.time()+600,'draft_release_sha256':'f19852a0740d32492cdb138e18e824ed6811354634e90c2e31f4b15b69f5e44e','release_binding':{'source_volume_id':'vo-NWLwwK5soAAAaKiLjay1Ek','not_after':self.m.time.time()+30000,'funding_receipt_sha256':'a'*64,'status':'BOUND_METADATA_ONLY'}};raw=self.m.CAN(a).encode();(self.w/'root-metadata-authorization.json').write_bytes(raw);auth=self.m.H(raw)
  v=types.SimpleNamespace(object_id='vo-NEW',_get_metadata=lambda:types.SimpleNamespace(version=2),listdir=lambda *x,**k:[]);v.hydrate=lambda:v;d=types.SimpleNamespace(object_id='di-NEW',len=lambda:0);d.hydrate=lambda:d
  fake=types.ModuleType('modal');fake.__version__='1.6.3';fake.Workspace=types.SimpleNamespace(from_context=lambda:types.SimpleNamespace(hydrate=lambda:types.SimpleNamespace(name='hycrpg')));fake.Volume=types.SimpleNamespace(from_name=lambda *x,**k:v);fake.Dict=types.SimpleNamespace(from_name=lambda *x,**k:d)
  with patch.dict(sys.modules,{'ops_common':self.m,'modal':fake}),patch.object(sys,'argv',['meta',auth]):runpy.run_path(str(ROOT/'create_metadata.py'))
  self.assertEqual(json.loads((self.w/'metadata-receipt.json').read_bytes())['compute_invocations'],0)
if __name__=='__main__':unittest.main()
