import unittest,sqlite3,json
from project_obligations import *
class Tests(unittest.TestCase):
 def setUp(self):
  self.db=sqlite3.connect(':memory:');self.db.execute('CREATE TABLE events(id TEXT PRIMARY KEY,payload TEXT)');self.s=lambda db:{'total_remaining_commitment_cents':2926,'first_remaining_allocations':{'segment1_train':1629,'final_dev':222,'global_blind_protection':1075}}
  self.p={'slot_allocations':{'train':3992,'final_dev':744,'shared_blind_increment':1026},'full_shared_blind_quote_cents':2101,'original_total_maximum_cents':5762,'active_plan_commitment_cents':5762,'existing_global_blind_base_cents':1075,'project_budget_approval_ref':PROJECT_APPROVAL,'project_cap_cents':60000,'platform_admission_policy':PLATFORM_POLICY,'actual_cost':None,'root_authority_sha256':'a'*64,'bound_release_sha256':'b'*64}
 def put(self):self.db.execute('INSERT INTO events VALUES (?,?)',(M_PLAN,json.dumps(self.p)))
 def test_existing_S_and_new_M_both_visible_blind_only_gap(self):
  self.put();d=shared_liabilities(self.db,s115_reader=self.s);self.assertEqual(d['total_direct_commitments_cents'],8688);self.assertEqual(d['shared_blind_base_in_S115_cents']+d['shared_blind_increment_in_M_cents'],2101)
 def test_no_M_no_fake_M_liability(self):self.assertEqual(shared_liabilities(self.db,s115_reader=self.s)['total_direct_commitments_cents'],2926)
 def test_unknown_M_payload_rejected(self):
  self.p['slot_allocations']['shared_blind_increment']=2101;self.put()
  with self.assertRaises(ValueError):shared_liabilities(self.db,s115_reader=self.s)
 def test_old4214_and1075_floor_not_sufficient(self):
  self.p.update(slot_allocations={'train':3992,'final_dev':222,'shared_blind_increment':0},full_shared_blind_quote_cents=1075,original_total_maximum_cents=4214,active_plan_commitment_cents=4214);self.put()
  with self.assertRaises(ValueError):shared_liabilities(self.db,s115_reader=self.s)
 def test_verified_M_train_terminal_only_retires_its_slot(self):
  self.put();p={'plan_event':M_PLAN,'slot':'train','completed_allocation_cents':3992,'active_component_cents':0,'actual_cost':None,'provider_terminal_verified':True,'call_terminal':True,'financial_terminal_status':'failed','training_root_authority_sha256':'a'*64,'training_bound_release_sha256':'b'*64,'completion_authority_sha256':'c'*64,'terminal_proof_sha256':'d'*64,'provider_terminal_receipt_sha256':'e'*64};self.db.execute('INSERT INTO events VALUES (?,?)',('operator-task116-slot-train-terminal-history-v1',json.dumps(p)));d=shared_liabilities(self.db,s115_reader=self.s);self.assertEqual(d['M116_active_commitment_cents'],1770);self.assertEqual(d['total_direct_commitments_cents'],4696)
if __name__=='__main__':unittest.main()
