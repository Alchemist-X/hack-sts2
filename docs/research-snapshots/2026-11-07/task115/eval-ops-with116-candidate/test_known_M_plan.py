import unittest,sqlite3,json
from fresh_admission import current_shared_liabilities
from project_obligations import M_PLAN,PROJECT_APPROVAL,PLATFORM_POLICY
B='6fc6f2544c6196546fe31b9796f73b3aca1aac57b7c2766c0d9454fe9dc1c8a6'
class KnownM(unittest.TestCase):
 def setup_db(self):
  d=sqlite3.connect(':memory:');d.execute('create table events(id text primary key,payload text)');return d
 def reader(self,db):return {'total_remaining_commitment_cents':1297,'first_remaining_allocations':{'final_dev':222,'global_blind_protection':1075}}
 def plan(self):return {'slot_allocations':{'train':3992,'final_dev':744,'shared_blind_increment':1026},'full_shared_blind_quote_cents':2101,'original_total_maximum_cents':5762,'active_plan_commitment_cents':5762,'existing_global_blind_base_cents':1075,'project_budget_approval_ref':PROJECT_APPROVAL,'project_cap_cents':60000,'platform_admission_policy':PLATFORM_POLICY,'actual_cost':None,'root_authority_sha256':'a'*64,'bound_release_sha256':B}
 def test_missing_and_wrongbound_refused(self):
  d=self.setup_db()
  with self.assertRaises(ValueError):current_shared_liabilities(d,self.reader)
  p=self.plan();p['bound_release_sha256']='b'*64;d.execute('insert into events values(?,?)',(M_PLAN,json.dumps(p)))
  with self.assertRaises(ValueError):current_shared_liabilities(d,self.reader)
 def test_legal_train_terminal_keeps_Mdev_and_blindgap(self):
  d=self.setup_db();p=self.plan();d.execute('insert into events values(?,?)',(M_PLAN,json.dumps(p)))
  self.assertEqual(current_shared_liabilities(d,self.reader)['M116_active_commitment_cents'],5762)
  terminal={'plan_event':M_PLAN,'slot':'train','completed_allocation_cents':3992,'active_component_cents':0,'actual_cost':None,'provider_terminal_verified':True,'call_terminal':True,'financial_terminal_status':'completed','training_root_authority_sha256':p['root_authority_sha256'],'training_bound_release_sha256':B,'completion_authority_sha256':'c'*64,'terminal_proof_sha256':'d'*64,'provider_terminal_receipt_sha256':'e'*64}
  d.execute('insert into events values(?,?)',('operator-task116-slot-train-terminal-history-v1',json.dumps(terminal)))
  q=current_shared_liabilities(d,self.reader);self.assertEqual(q['M116_active_allocations'],{'final_dev':744,'shared_blind_increment':1026});self.assertEqual(q['total_direct_commitments_cents'],3067)
if __name__=='__main__':unittest.main()
