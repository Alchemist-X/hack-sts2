import unittest,sqlite3,json
import retire_train as m
from first_slot_budget import PLAN,SLOTS,TRAIN_AUTH,BOUND,canonical,digest
from plan_liabilities import SECOND
from project_obligations import M_PLAN,PROJECT_APPROVAL,PLATFORM_POLICY
class Test(unittest.TestCase):
 def setUp(self):
  self.db=sqlite3.connect(':memory:');self.db.execute('CREATE TABLE events(id TEXT PRIMARY KEY,payload TEXT)')
  self.put(PLAN,{'root_authority_sha256':TRAIN_AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':2926,'slot_allocations':SLOTS})
  self.put(M_PLAN,{'root_authority_sha256':m.TRAIN_AUTH,'bound_release_sha256':m.BOUND,'slot_allocations':{'train':3992,'final_dev':744,'shared_blind_increment':1026},'full_shared_blind_quote_cents':2101,'original_total_maximum_cents':5762,'active_plan_commitment_cents':5762,'existing_global_blind_base_cents':1075,'project_budget_approval_ref':PROJECT_APPROVAL,'project_cap_cents':60000,'platform_admission_policy':PLATFORM_POLICY,'actual_cost':None})
  self.put('unknown-hold',{'reserved_cents':3705,'actual_cost':None});self.put('archive',{'commitment_cents':1000,'actual_cost':None})
  self.proof={'slot':'train','arm_id':'M116','call_id':m.CID,'attempt':m.ATTEMPT,'training_root_authority_sha256':m.TRAIN_AUTH,'training_bound_release_sha256':m.BOUND,'call_terminal':True,'provider_terminal_verified':True,'status':'completed','observation_class':'FunctionCall_returned','provider_terminal_receipt_sha256':'a'*64}
  self.auth={'action':'ACCEPT_TASK116_EXACT_PROVIDER_TERMINAL_FOR_ACCOUNTING',**{k:v for k,v in self.proof.items()if k not in ('status','observation_class')},'terminal_proof_sha256':digest(self.proof),'terminal_status':'completed','original_plan_event':M_PLAN,'allocation_cents':3992}
 def put(self,k,p):self.db.execute('INSERT INTO events VALUES (?,?)',(k,canonical(p)))
 def entry(self):return m.completion_payload(self.proof,digest(self.proof),self.auth,'b'*64)
 def append(self,db,k,kind,p,ts):db.execute('INSERT INTO events VALUES (?,?)',(k,canonical(p)))
 def test_exact_slot_duplicate_and_preservation(self):
  before=list(self.db.execute('SELECT * FROM events ORDER BY id'));out=m.retire(self.db,self.entry(),self.append)
  self.assertEqual(out['M116_active_allocations'],{'final_dev':744,'shared_blind_increment':1026});self.assertEqual(out['S115']['total_remaining_commitment_cents'],2926);self.assertEqual(out['archive_not_included_cents'],1000)
  m.retire(self.db,self.entry(),self.append);self.assertEqual(before,list(self.db.execute('SELECT * FROM events WHERE id!=? ORDER BY id',(m.SECOND_TERMINAL,))))
  altered=self.entry();altered['completion_authority_sha256']='c'*64
  with self.assertRaises(ValueError):m.retire(self.db,altered,self.append)
 def test_missing_foreign_or_nonterminal(self):
  for key,value in [('call_id','fc-FOREIGN'),('provider_terminal_verified',False),('status','writer_final'),('attempt',None)]:
   old=self.proof[key];self.proof[key]=value
   with self.assertRaises(ValueError):self.entry()
   self.proof[key]=old
  self.db.execute('DELETE FROM events WHERE id=?',(M_PLAN,))
  with self.assertRaises((ValueError,TypeError)):m.retire(self.db,self.entry(),self.append)
if __name__=='__main__':unittest.main()
