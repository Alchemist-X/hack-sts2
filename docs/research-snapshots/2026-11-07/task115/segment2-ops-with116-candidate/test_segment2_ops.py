import unittest,tempfile,json,types,sqlite3,hashlib
from pathlib import Path
import ops_common as o
from first_slot_budget import *
from plan_liabilities import liabilities,SECOND
class Gates(unittest.TestCase):
 def setUp(self):
  self.t=tempfile.TemporaryDirectory();self.old=o.W;o.W=Path(self.t.name)
  self.r={'parent':{'decisions':595853312,'updates':4546,'model_sha256':'a'*64,'cursor':1099649200000,'issued':1099649204096,'namespace':'MODA10PH104FRESH1','adam_step':145472,'bytes':36000000},'parent_volume_id':'vo-HbcQICsESY3s4xORGRnIbS','parent_path':'fixed/run/final.pt','parent_evidence_sha256':'b'*64,'segment':{'index':2,'target_env_steps':695861248,'target_updates':5309,'beta':0,'seed_range':[1099783413606,1099917631334]},'old_reserved_end':1099783413606}
  self.p={'status':'accepted_full_endpoint','provider_terminal_verified':True,'model_body_hash_verified':True,'call_id':'fc-01M6SFGP9W1T6AGQZP477J8PCZ','parent':self.r['parent'],'parent_volume_id':self.r['parent_volume_id'],'parent_path':self.r['parent_path'],'parent_metadata_evidence_sha256':'b'*64,'model_body_sha256':'a'*64,'root_final_acceptance_sha256':'c'*64,'independent_final_acceptance_sha256':'d'*64,'provider_terminal_receipt_sha256':'e'*64,'actual_prior_reserved_end':1099783413606}
 def tearDown(self):o.W=self.old;self.t.cleanup()
 def save(self):
  b=o.CAN(self.p).encode();(o.W/'first-terminal-accepted.json').write_bytes(b);return o.H(b)
 def test_accepted_synthetic_tuple_pass(self):self.assertEqual(o.accepted_parent(self.save(),self.r),self.p)
 def test_null_actual_parent_blocks(self):
  self.p['parent']=None
  with self.assertRaises(AssertionError):o.accepted_parent(self.save(),self.r)
 def test_partial_live_unknown_blocks(self):
  for k in ['provider_terminal_verified','model_body_hash_verified']:
   p=dict(self.p);self.p[k]=False
   with self.assertRaises(AssertionError):o.accepted_parent(self.save(),self.r)
   self.p=p
 def test_wrong_dose_or_old_grant_blocks(self):
  self.r['segment']['seed_range']=[1099649195878,1099783413606]
  with self.assertRaises(AssertionError):o.accepted_parent(self.save(),self.r)
 def test_s2_accounting_only_adds_train_not_protected_again(self):
  c=sqlite3.connect(':memory:');c.execute('CREATE TABLE events(id TEXT PRIMARY KEY,payload TEXT)')
  orig={'root_authority_sha256':TRAIN_AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':2926,'slot_allocations':SLOTS};c.execute('INSERT INTO events VALUES (?,?)',(PLAN,canonical(orig)))
  proof={'slot':'segment1_train','arm_id':'segment1','call_id':'fc-ORIGINAL','call_terminal':True,'provider_terminal_verified':True,'status':'completed','observation_class':'FunctionCall_returned','provider_terminal_receipt_sha256':'a'*64,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND};ps=digest(proof)
  auth={'action':TERMINAL_AUTH_ACTION,'slot':'segment1_train','call_id':'fc-ORIGINAL','terminal_proof_sha256':ps,'provider_terminal_receipt_sha256':'a'*64,'provider_terminal_verified':True,'terminal_status':'completed','training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'original_plan_event':PLAN,'allocation_cents':1629}
  e=completion_payload('segment1_train',proof,ps,auth,'b'*64);c.execute('INSERT INTO events VALUES (?,?)',(terminal_id('segment1_train'),canonical(e)));self.assertEqual(liabilities(c)['total_remaining_commitment_cents'],1297)
  second={'original_total_maximum_cents':1629,'active_plan_commitment_cents':1629,'slot_allocations':{'segment2_train':1629},'existing_protected_commitment_cents':1297,'existing_protected_plan_event':PLAN,'actual_cost':None,'root_authority_sha256':'c'*64,'bound_release_sha256':'d'*64};c.execute('INSERT INTO events VALUES (?,?)',(SECOND,canonical(second)));self.assertEqual(liabilities(c)['total_remaining_commitment_cents'],2926)
  second['original_total_maximum_cents']=2926;c.execute('UPDATE events SET payload=? WHERE id=?',(canonical(second),SECOND))
  with self.assertRaises(ValueError):liabilities(c)
if __name__=='__main__':unittest.main()
