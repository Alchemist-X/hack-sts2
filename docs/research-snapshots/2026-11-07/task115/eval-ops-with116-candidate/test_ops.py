import unittest,json,copy,sqlite3,hashlib,time,tempfile
from pathlib import Path
from unittest.mock import patch
import ops_common as o
from fresh_admission import financial_view,quote_cents
from claim_slot import claim_in_transaction
from plan_liabilities import liabilities
from first_slot_budget import PLAN,SLOTS,TRAIN_AUTH,BOUND,terminal_id
class Tests(unittest.TestCase):
 def values(self,u=5309):
  r=json.loads((Path(__file__).parent/'release.DISABLED-DRAFT.json').read_text());n=495845376+(u-3783)*131072;full=u==5309
  term={'classification':'full200M_terminal'if full else'resource_censored_terminal','final_stop':True,'further_training_authorized':False,'midpoint_evaluation':False,'nominal_primary_observed':full}
  r.update(status='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT',checkpoint_sha256='a'*64,checkpoint_size=36000000,checkpoint_path='115/attempt/run/final.pt',checkpoint_steps=n,checkpoint_updates=u,model_volume_id='vo-model',root_scope_sha256='1'*64,root_terminal_decision_sha256='2'*64,accepted_training_source_proof_sha256='3'*64,independent_final_acceptance_sha256='4'*64,accepted_checkpoint_manifest_sha256='5'*64,accepted_training_receipt_sha256='6'*64,not_after=10000,terminal_evaluation=term)
  keys=('checkpoint_sha256','checkpoint_size','checkpoint_path','checkpoint_steps','checkpoint_updates','model_volume_id','accepted_checkpoint_manifest_sha256','accepted_training_receipt_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256','root_terminal_decision_sha256')
  a={'action':'TASK115_EXACT_FINAL_FORMALDEV','phase':'metadata','allow_metadata':True,'max_submissions':1,'future_components_acceptance_sha256':o.FUTURE,'science_plan_sha256':o.SCIENCE,'slot':'final_dev','existing_plan_event':PLAN,'existing_slot_cents':222,'new_pledge_cents':0,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'expires':9000,'minimum_gross_cents':0,'minimum_net_cents':0,'accepted_actual_runtime_pins_sha256':'7'*64,'ops_file_sha256':{n:o.H((Path(__file__).parent/n).read_bytes())for n in ('fresh_admission.py','plan_liabilities.py')},**{k:r[k]for k in keys}}
  p={'schema':'task115-accepted-final-eval-projection-v1','status':'ROOT_AND_INDEPENDENT_ACCEPTED_TERMINAL','call_terminal':True,'call_id':'fc-one','attempt':'f'*32,'selected_checkpoint_rule':'normal_final','terminal_evaluation':term,'science_plan_sha256':o.SCIENCE,**{k:r[k]for k in keys}}
  s={'status':'ROOT_AND_INDEPENDENT_ACCEPTED_OPERATIONAL_EVIDENCE_CHAIN','call_id':'fc-one','attempt':'f'*32,'parent_donor_sha256':r['parent_donor_sha256'],'beta':0.,'learning_overlay':{'ppo.py':'a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14','strategic_kl.py':'ed4ebfd9b0b0e622646f68bf34dfa3b56f2a7907f3cdbd15e34f3ba20b07c6f1'},'actual_runtime_pins_sha256':'7'*64,**{k:r[k]for k in ('checkpoint_sha256','checkpoint_steps','checkpoint_updates','checkpoint_path','model_volume_id')}}
  d={'study_donor_checkpoint_sha256':r['parent_donor_sha256'],'reason':None if full else'resource budget decision','actual_terminal_accepted':True,'science_plan_sha256':o.SCIENCE,'checkpoint_sha256':r['checkpoint_sha256'],'checkpoint_steps':n,'checkpoint_updates':u,'terminal_evaluation':term,'censor_class':None if full else'budget'}
  a.update(forward_admission_policy_sha256='6241b01cbd1238cbdc569e80af4e8e525077f0c2ae41342404c00fbda13432eb',project_cap_cents=60000,platform_admission_policy='UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT');a['ops_file_sha256']['project_obligations.py']=o.H((Path(__file__).parent/'project_obligations.py').read_bytes())
  return a,r,p,s,d
 def test_full_and_explicit_censored_stop(self):
  o.validate(*self.values(5309),phase='metadata',now=100)
  for reason in ('time','budget','provider','byte_limit','safety'):
   v=self.values(4546);v[-1]['censor_class']=reason;o.validate(*v,phase='metadata',now=100)
  v=self.values(4546);v[-1]['reason']=''
  with self.assertRaises(ValueError):o.validate(*v,phase='metadata',now=100)
 def test_donor_live_midpoint_fake_dose_and_missing_stop_refused(self):
  for mutate in (lambda a,r,p,s,d:r.update(checkpoint_updates=3783),lambda a,r,p,s,d:r['terminal_evaluation'].update(further_training_authorized=True),lambda a,r,p,s,d:r.update(checkpoint_steps=42),lambda a,r,p,s,d:d.update(actual_terminal_accepted=False),lambda a,r,p,s,d:d.update(censor_class='quality'),lambda a,r,p,s,d:s.update(checkpoint_sha256='b'*64)):
   v=self.values(4546);mutate(*v)
   with self.assertRaises(ValueError):o.validate(*v,phase='metadata',now=100)
 def test_disabled_draft_has_no_terminal(self):
  a,r,p,s,d=self.values();r=json.loads((Path(__file__).parent/'release.DISABLED-DRAFT.json').read_text())
  with self.assertRaises((ValueError,TypeError)):o.validate(a,r,p,s,d,phase='metadata',now=100)
 def test_no_double_pledge_expiry_wrong_phase(self):
  for k,v in [('new_pledge_cents',222),('existing_slot_cents',250),('expires',99),('max_submissions',2)]:
   values=self.values();values[0][k]=v
   with self.assertRaises(ValueError):o.validate(*values,phase='metadata',now=100)
  with self.assertRaises(ValueError):o.validate(*self.values(),phase='deploy',now=100)
 def db(self):
  db=sqlite3.connect(':memory:');db.execute('CREATE TABLE events(id TEXT PRIMARY KEY,kind TEXT,payload TEXT)')
  db.execute('INSERT INTO events VALUES(?,?,?)',(PLAN,'original',json.dumps({'root_authority_sha256':TRAIN_AUTH,'bound_release_sha256':BOUND,'original_total_maximum_cents':2926,'slot_allocations':SLOTS})))
  retire={'plan_event':PLAN,'slot':'segment1_train','completed_allocation_cents':1629,'active_component_cents':0,'training_root_authority_sha256':TRAIN_AUTH,'training_bound_release_sha256':BOUND,'call_terminal':True,'actual_cost':None,'provider_terminal_verified':True,'financial_terminal_status':'completed','completion_authority_sha256':'c'*64}
  db.execute('INSERT INTO events VALUES(?,?,?)',(terminal_id('segment1_train'),'terminal',json.dumps(retire)));return db
 def admission(self,a):
  return {'status':'PASS','root_eval_authority_sha256':'a'*64,'bound_release_sha256':'b'*64,'observed_at':100,'funding_source_sha256':a['ops_file_sha256']['fresh_admission.py'],'liabilities_source_sha256':a['ops_file_sha256']['plan_liabilities.py'],'existing_plan_event':PLAN,'slot':'final_dev','existing_slot_cents':222,'slot_unconsumed':True,'new_pledge_cents':0,'quote_cents':222,'global_blind_protected_cents':1075,'archive_commitment_cents':1000,'old_unknown_retained':True,'no_other_paid_active':True,'project_room_after_all_existing_holds_cents':0,'platform_room_after_all_existing_holds_cents':None,'project_cap_cents':60000,'platform_admission_policy':'UNKNOWN_USER_DIRECTED_PLATFORM_ENFORCEMENT','platform_caps_historical_only':True,'project_obligations_source_sha256':a['ops_file_sha256']['project_obligations.py'],'allowed_M_concurrency_only':True}
 def test_claim_once_original_slot_no_new_financial_row(self):
  a,r,*_=self.values();db=self.db();g=self.admission(a);g['authoritative_ledger_state_sha256']=o.H(db.serialize())
  def append(db,key,kind,payload,now):db.execute('INSERT INTO events VALUES(?,?,?)',(key,kind,json.dumps(payload)))
  p=claim_in_transaction(db,append,a,r,g,'a'*64,'b'*64,now=101);self.assertEqual(p['new_pledge_cents'],0);self.assertEqual(liabilities(db)['total_remaining_commitment_cents'],1297);self.assertEqual(db.execute('SELECT COUNT(*) FROM events').fetchone()[0],3)
  g['authoritative_ledger_state_sha256']=o.H(db.serialize())
  with self.assertRaises(ValueError):claim_in_transaction(db,append,a,r,g,'a'*64,'b'*64,now=102)
 def test_fresh_proof_stale_active_blind_archive_or_old_fee_omission(self):
  a,r,*_=self.values()
  for k,v in [('observed_at',-100),('quote_cents',223),('no_other_paid_active',False),('global_blind_protected_cents',0),('archive_commitment_cents',0),('old_unknown_retained',False)]:
   g=self.admission(a);g[k]=v
   with self.assertRaises(ValueError):o.check_admission(g,a,r,'a'*64,'b'*64,now=101)
 def test_budget_all_remaining_slots_once_quote_actual_function16(self):
  db=self.db();s=liabilities(db);b={'spent_and_reserved_cents':(10000,3705),'modal_spent_and_reserved_cents':(0,520),'platform_snapshot_cents':{'gross_limit':20000,'net_limit':20000}}
  shared={'S115':s,'M116_active_commitment_cents':5762,'M116_active_allocations':{'train':3992,'final_dev':744,'shared_blind_increment':1026},'shared_blind_increment_in_M_cents':1026,'total_direct_commitments_cents':s['total_remaining_commitment_cents']+5762};v=financial_view(b,s,{'remaining_commitment_cents':0},gross=10000,net=7000,shared=shared)
  self.assertEqual(v['existing_plan_commitments_cents'],7059);self.assertEqual(v['project_room_after_all_existing_holds_cents'],38236);self.assertIsNone(v['platform_room_after_all_existing_holds_cents'])
  q=quote_cents({'gpu_hour_cost_h100':'3.95','cpu_hour_cost':'.0473','mem_gib_hour_cost':'.008','volume_storage_gib_month_cost':'.09','egress_gib_cost':'.04'});self.assertEqual(q,222)
 def test_second_unknown_train_liability_is_not_lost_or_double_blind(self):
  db=self.db();event={'original_total_maximum_cents':1629,'active_plan_commitment_cents':1629,'slot_allocations':{'segment2_train':1629},'existing_protected_commitment_cents':1297,'existing_protected_plan_event':PLAN,'actual_cost':None,'root_authority_sha256':'a'*64,'bound_release_sha256':'b'*64}
  db.execute('INSERT INTO events VALUES(?,?,?)',('operator-task115-second-segment-budget-commitment-v1','second',json.dumps(event)))
  s=liabilities(db);self.assertEqual(s['total_remaining_commitment_cents'],2926);self.assertEqual(s['first_remaining_allocations']['final_dev'],222);self.assertEqual(s['first_remaining_allocations']['global_blind_protection'],1075)
 def test_imports_never_call_sdk(self):
  import sys,importlib
  class NoSDK:
   def __getattr__(self,n):raise AssertionError('SDK on import')
  with patch.dict(sys.modules,{'modal':NoSDK()}):
   for n in ('create_metadata','deploy_and_spawn','fresh_admission','claim_slot'):importlib.import_module(n)
if __name__=='__main__':unittest.main()
