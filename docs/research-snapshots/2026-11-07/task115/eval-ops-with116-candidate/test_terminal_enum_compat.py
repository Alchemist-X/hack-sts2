import unittest,json
import test_ops
from first_slot_budget import remaining,terminal_id
class EnumCompatibility(unittest.TestCase):
 def test_actual_resource_censored_terminal_retains_original_final_and_blind(self):
  db=test_ops.Tests().db();row=db.execute("SELECT payload FROM events WHERE id=?",(terminal_id('segment1_train'),)).fetchone();payload=json.loads(row[0]);payload['financial_terminal_status']='resource_censored_with_checkpoint';db.execute("UPDATE events SET payload=? WHERE id=?",(json.dumps(payload),terminal_id('segment1_train')));state=remaining(db)
  self.assertEqual(state['remaining_allocations'],{'final_dev':222,'global_blind_protection':1075});self.assertEqual(state['remaining_commitment_cents'],1297);self.assertIsNone(payload['actual_cost'])
if __name__=='__main__':unittest.main()
