import unittest
from fresh_admission import inventory_view,M_FUNCTION,S2_FUNCTION
class Inventory(unittest.TestCase):
 def test_exact_M_one_allowed_S2_quiet(self):
  ids={'116M':M_FUNCTION,'115segment2':S2_FUNCTION,'old':'fu-old'};s={k:{'num_running_inputs':0,'num_total_runners':0,'backlog':0}for k in ids};s['116M'].update(num_running_inputs=1,num_total_runners=1);self.assertTrue(inventory_view(ids,s))
  for key,field,value in [('116M','backlog',1),('115segment2','num_running_inputs',1),('old','num_total_runners',1),('116M','num_total_runners',None)]:
   changed={k:dict(v)for k,v in s.items()};changed[key][field]=value
   with self.assertRaises(ValueError):inventory_view(ids,changed)
  with self.assertRaises(ValueError):inventory_view({**ids,'116M':'fu-wrong'},s)
if __name__=='__main__':unittest.main()
