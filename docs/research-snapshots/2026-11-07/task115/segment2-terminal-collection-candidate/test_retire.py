import unittest,sqlite3,json
import retire_segment2 as s
from first_slot_budget import *
from plan_liabilities import liabilities,SECOND
class Tests(unittest.TestCase):
 def test_retire_idempotent_preserves_other_events(self):
  c=sqlite3.connect(':memory:');c.execute('CREATE TABLE events(id TEXT PRIMARY KEY,payload TEXT)')
  orig={'root_authority_sha256':TRAIN_AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':2926,'slot_allocations':SLOTS};c.execute('INSERT INTO events VALUES (?,?)',(PLAN,canonical(orig)))
  proof={'slot':'segment1_train','arm_id':'segment1','call_id':'fc-ORIGINAL','call_terminal':True,'provider_terminal_verified':True,'status':'completed','observation_class':'FunctionCall_returned','provider_terminal_receipt_sha256':'a'*64,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND};ps=digest(proof)
  auth={'action':TERMINAL_AUTH_ACTION,'slot':'segment1_train','call_id':'fc-ORIGINAL','terminal_proof_sha256':ps,'provider_terminal_receipt_sha256':'a'*64,'provider_terminal_verified':True,'terminal_status':'completed','training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'original_plan_event':PLAN,'allocation_cents':1629}
  e=completion_payload('segment1_train',proof,ps,auth,'b'*64);c.execute('INSERT INTO events VALUES (?,?)',(terminal_id('segment1_train'),canonical(e)));self.assertEqual(liabilities(c)['total_remaining_commitment_cents'],1297)
  second={'original_total_maximum_cents':1629,'active_plan_commitment_cents':1629,'slot_allocations':{'segment2_train':1629},'existing_protected_commitment_cents':1297,'existing_protected_plan_event':PLAN,'actual_cost':None,'root_authority_sha256':s.TRAIN_AUTH,'bound_release_sha256':s.BOUND};c.execute('INSERT INTO events VALUES (?,?)',(SECOND,canonical(second)));self.assertEqual(liabilities(c)['total_remaining_commitment_cents'],2926)
  before=list(c.execute('SELECT * FROM events ORDER BY id'))
  proof={'slot':'segment2_train','arm_id':'segment2','call_id':s.CID,'attempt':s.ATTEMPT,'call_terminal':True,'provider_terminal_verified':True,'status':'completed','observation_class':'FunctionCall_returned','provider_terminal_receipt_sha256':'a'*64,'training_root_authority_sha256':s.TRAIN_AUTH,'training_bound_release_sha256':s.BOUND}
  ps=digest(proof);auth={'action':TERMINAL_AUTH_ACTION,**{k:v for k,v in proof.items()if k not in ('status','observation_class')},'terminal_proof_sha256':ps,'terminal_status':'completed','original_plan_event':SECOND,'allocation_cents':1629}
  entry=s.completion_payload(proof,ps,auth,'b'*64)
  append=lambda db,i,k,p,t:db.execute('INSERT INTO events VALUES (?,?)',(i,canonical(p)))
  after=s.retire(c,entry,append);self.assertEqual(after['S115']['total_remaining_commitment_cents'],1297)
  s.retire(c,entry,append)
  self.assertEqual(before,list(c.execute('SELECT * FROM events WHERE id!=? ORDER BY id',(s.SECOND_TERMINAL,))))
  proof['status']='live'
  with self.assertRaises(ValueError):s.completion_payload(proof,ps,auth,'b'*64)
if __name__=='__main__':unittest.main()
