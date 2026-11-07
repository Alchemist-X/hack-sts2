"""Two small stdlib115 interface fixtures, no NumPy/RNG/model/game/actual results."""
import unittest
import analyze115 as a
from paired_statistics import paired_deltas,validate_statistics_parameters

class Tests(unittest.TestCase):
 def facts(self,steps,updates,kind):
  model={'checkpoint_file_sha256':'1'*64,'decisions':steps,'updates':updates}
  d={'science_plan_sha256':a.PLAN_SHA,'study_donor_checkpoint_sha256':a.BASE_SHA,'actual_terminal_accepted':True,'checkpoint_sha256':model['checkpoint_file_sha256'],'checkpoint_steps':steps,'checkpoint_updates':updates,'terminal_evaluation':{'classification':'full200M_terminal'if kind=='full200M'else'resource_censored_terminal','final_stop':True,'further_training_authorized':False,'midpoint_evaluation':False,'nominal_primary_observed':kind=='full200M'},'censor_class':'budget','reason':'synthetic explicit resource stop, no second segment'}
  return d,model
 def test_full_primary_censored_description_and_no_midpoint(self):
  d,m=self.facts(a.TARGET_STEPS,a.TARGET_UPDATES,'full200M');full=a.terminal_scope(d,m)
  self.assertIsNotNone(a.comparison_scope(full,{'synthetic_receipt':True})['planned_200M_primary'])
  d,m=self.facts(595853312,4546,'resource_censored');short=a.terminal_scope(d,m);out=a.comparison_scope(short,{'synthetic_receipt':True})
  self.assertIsNone(out['planned_200M_primary']);self.assertIsNotNone(out['resource_censored_actual_dose_descriptive'])
  d['terminal_evaluation']['further_training_authorized']=True
  with self.assertRaises(ValueError):a.terminal_scope(d,m)
  d,m=self.facts(a.BASE_STEPS,a.BASE_UPDATES,'resource_censored')
  with self.assertRaises(ValueError):a.terminal_scope(d,m)
 def test_fixed256_failures_kept_missing_null_rejected_seed_only(self):
  keys=['synthetic'+str(i)for i in range(256)];x=[dict(key=k,progress=.2,result='error'if i==0 else'loss')for i,k in enumerate(keys)];y=[dict(key=k,progress=.1,result='stuck'if i==0 else'loss')for i,k in enumerate(keys)]
  self.assertEqual(len(paired_deltas(x,y,keys)),256)
  with self.assertRaises(ValueError):paired_deltas(x[:-1],y,keys)
  x[0]['progress']=None
  with self.assertRaises(ValueError):paired_deltas(x,y,keys)
  self.assertEqual(validate_statistics_parameters()['seed'],115114)

if __name__=='__main__':unittest.main()
