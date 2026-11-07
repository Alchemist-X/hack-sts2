"""Synthetic M metadata/source chain, privateSQLite and fakeSDK; no weights/games."""
import copy,json,sqlite3,types,sys,time,hashlib,dataclasses,datetime
from pathlib import Path
import pytest
import ops_common as o
import fresh_admission as f
from claim_slot import claim_in_transaction
from first_slot_budget import PLAN as SPLAN,TRAIN_AUTH as SAUTH,BOUND as SBOUND,SLOTS,terminal_id
from plan_liabilities import SECOND,liabilities
from project_obligations import M_PLAN,PROJECT_APPROVAL,PLATFORM_POLICY,shared_liabilities
P=Path(__file__).parent

def values(u=763):
 r=json.loads((P/'release.DISABLED-DRAFT.json').read_bytes())
 # Read only public accepted release metadata/spec, never model or seeds.
 train=json.loads((P.parent/'ops/release.bound.json').read_bytes());spec=train['spec'];cfg=train['first_update_gate_context']['config_hash']
 term=dict(classification='full100M_terminal'if u==763 else'resource_censored_terminal',final_stop=True,further_training_authorized=False,midpoint_evaluation=False,nominal_primary_observed=u==763)
 r.update(status='ROOT_BOUND_ACCEPTED_FINAL_ENDPOINT',spec=spec,spec_sha256=cfg,checkpoint_sha256='a'*64,checkpoint_size=196000000,checkpoint_path='SYNTHETIC-M/attempt/run/final.pt',checkpoint_steps=u*131072,checkpoint_updates=u,model_volume_id='vo-vUK4QYJsqFt1QTrRerH4Of',output_volume_id='vo-FakeOutput',claim_dict_id='di-FakeClaim',not_after=2000000000,root_scope_sha256='b'*64,accepted_training_receipt_sha256='c'*64,accepted_checkpoint_manifest_sha256='d'*64,training_runtime_pins_sha256='e'*64,root_terminal_decision_sha256='f'*64,accepted_training_source_proof_sha256='1'*64,independent_final_acceptance_sha256='2'*64,terminal_evaluation=term,first_update_gate_context=train['first_update_gate_context'])
 keys=('checkpoint_sha256','checkpoint_size','checkpoint_path','checkpoint_steps','checkpoint_updates','model_volume_id','accepted_checkpoint_manifest_sha256','accepted_training_receipt_sha256','accepted_training_source_proof_sha256','independent_final_acceptance_sha256','root_terminal_decision_sha256')
 a=json.loads((P/'root-authorization.DISABLED-TEMPLATE.json').read_bytes());a.update(allowed_parallel_S2_call_id='fc-01M6SSSTZJRPZSWJNQDBMZTFC6',allowed_parallel_S2_bound_release_sha256='bb6322814aa1590d8669d2ac9219c6107b4bbd93047fc10b83d6e17f80175ab1',phase='deploy',allow_deploy=True,allow_consume_existing_slot=True,expires=2000000000,accepted_actual_runtime_pins_sha256='e'*64,minimum_gross_cents=0,minimum_net_cents=0,task117_inventory_status='ROOT_VERIFIED_NOT_LAUNCHED',ops_file_sha256={n:o.H((P/n).read_bytes())for n in ['fresh_admission.py','plan_liabilities.py','project_obligations.py']},**{k:r[k]for k in keys})
 proof=dict(schema='task116-accepted-final-eval-projection-v1',status='ROOT_AND_INDEPENDENT_ACCEPTED_TERMINAL',call_terminal=True,call_id='fc-01M6SREBKE87QMT2VQKEMC0Q6T',attempt='a'*32,selected_checkpoint_rule='normal_final',science_plan_sha256=o.SCIENCE,terminal_evaluation=term,**{k:r[k]for k in keys})
 source=dict(status='ROOT_AND_INDEPENDENT_ACCEPTED_OPERATIONAL_EVIDENCE_CHAIN',call_id=proof['call_id'],attempt=proof['attempt'],fresh_random=True,parent_donor_sha256=None,training_root_authority_sha256=o.TRAIN_AUTH,training_bound_release_sha256=o.TRAIN_BOUND,training_spec_sha256=cfg,actual_runtime_pins_sha256='e'*64,learning_overlay={'ppo.py':'a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14','strategic_kl.py':'ed4ebfd9b0b0e622646f68bf34dfa3b56f2a7907f3cdbd15e34f3ba20b07c6f1'},**{k:r[k]for k in ['checkpoint_sha256','checkpoint_steps','checkpoint_updates','checkpoint_path','model_volume_id']})
 decision=dict(actual_terminal_accepted=True,science_plan_sha256=o.SCIENCE,censor_class=None if u==763 else'budget',reason=None if u==763 else'explicit final resource stop',**{k:r[k]for k in ['checkpoint_sha256','checkpoint_steps','checkpoint_updates','terminal_evaluation']})
 return a,r,proof,source,decision

def fixture_db(path):
 db=sqlite3.connect(path);db.row_factory=sqlite3.Row;db.execute('create table events(id text primary key,kind text,payload text)')
 def put(k,p):db.execute('insert into events values(?,?,?)',(k,'fixture',json.dumps(p)))
 put(SPLAN,dict(root_authority_sha256=SAUTH,bound_release_sha256=SBOUND,original_total_maximum_cents=2926,slot_allocations=SLOTS))
 put(terminal_id('segment1_train'),dict(plan_event=SPLAN,slot='segment1_train',completed_allocation_cents=1629,active_component_cents=0,training_root_authority_sha256=SAUTH,training_bound_release_sha256=SBOUND,call_terminal=True,actual_cost=None,provider_terminal_verified=True,financial_terminal_status='completed',completion_authority_sha256='a'*64))
 put(SECOND,dict(original_total_maximum_cents=1629,active_plan_commitment_cents=1629,slot_allocations={'segment2_train':1629},existing_protected_commitment_cents=1297,existing_protected_plan_event=SPLAN,actual_cost=None,root_authority_sha256='a'*64,bound_release_sha256='b'*64))
 put(M_PLAN,dict(slot_allocations=dict(train=3992,final_dev=744,shared_blind_increment=1026),full_shared_blind_quote_cents=2101,original_total_maximum_cents=5762,active_plan_commitment_cents=5762,existing_global_blind_base_cents=1075,project_budget_approval_ref=PROJECT_APPROVAL,project_cap_cents=60000,platform_admission_policy=PLATFORM_POLICY,actual_cost=None,root_authority_sha256=o.TRAIN_AUTH,bound_release_sha256=o.TRAIN_BOUND))
 put('operator-task116-slot-train-terminal-history-v1',dict(plan_event=M_PLAN,slot='train',completed_allocation_cents=3992,active_component_cents=0,actual_cost=None,provider_terminal_verified=True,call_terminal=True,financial_terminal_status='resource_censored_with_checkpoint',training_root_authority_sha256=o.TRAIN_AUTH,training_bound_release_sha256=o.TRAIN_BOUND,completion_authority_sha256='c'*64,terminal_proof_sha256='d'*64,provider_terminal_receipt_sha256='e'*64))
 db.commit();return db

def admission(a,db):
 return dict(status='PASS',root_eval_authority_sha256='a'*64,bound_release_sha256='b'*64,observed_at=100,funding_source_sha256=a['ops_file_sha256']['fresh_admission.py'],liabilities_source_sha256=a['ops_file_sha256']['plan_liabilities.py'],project_obligations_source_sha256=a['ops_file_sha256']['project_obligations.py'],existing_plan_event=o.PLAN,slot='final_dev',existing_slot_cents=744,slot_unconsumed=True,new_pledge_cents=0,quote_cents=744,global_blind_protected_cents=1075,archive_commitment_cents=1000,old_unknown_retained=True,inventory_within_authorized_scope=True,project_cap_cents=60000,platform_admission_policy=PLATFORM_POLICY,platform_caps_historical_only=True,platform_room_after_all_existing_holds_cents=None,project_room_after_all_existing_holds_cents=0,allowed_S2_concurrency_only=True,shared_blind_increment_cents=1026,M_active_allocations={'final_dev':744,'shared_blind_increment':1026},authoritative_ledger_state_sha256=o.H(db.serialize()))

def test_full_censored_and_disabled_same_call_guards():
 for u in (763,400):o.validate(*values(u),phase='deploy',now=100)
 v=values();v[2]['call_id']='fc-Foreign'
 with pytest.raises(ValueError):o.validate(*v,phase='deploy',now=100)
 for mutate in [lambda a,r,p,s,d:a.update(task117_inventory_status=None),lambda a,r,p,s,d:p.update(call_terminal=False),lambda a,r,p,s,d:r.update(checkpoint_size=256*1024**2+1),lambda a,r,p,s,d:d.update(actual_terminal_accepted=False),lambda a,r,p,s,d:a.update(new_pledge_cents=744)]:
  v=values();mutate(*v)
  with pytest.raises(ValueError):o.validate(*v,phase='deploy',now=100)
 a,r,p,s,d=values();r=json.loads((P/'release.DISABLED-DRAFT.json').read_bytes())
 with pytest.raises((ValueError,TypeError)):o.validate(a,r,p,s,d,phase='deploy',now=100)

@pytest.mark.parametrize('S2,M,backlog,allow',[(1,0,0,True),(0,0,0,True),(2,0,0,False),(1,1,0,False),(1,0,1,False),(None,0,0,False)])
def test_actual_inventory_S2_only(S2,M,backlog,allow):
 ids={'115segment2':f.S2_FUNCTION,'116M':f.M_FUNCTION,'old':'fu-old'}
 stats={k:dict(num_running_inputs=0,num_total_runners=0,backlog=0)for k in ids};stats['115segment2'].update(num_running_inputs=S2,num_total_runners=S2,backlog=backlog);stats['116M'].update(num_running_inputs=M,num_total_runners=M)
 if allow:assert f.inventory_view(ids,stats)
 else:
  with pytest.raises(ValueError):f.inventory_view(ids,stats)

def test_privateSQLite_funding_and_once_claim_do_not_double744(tmp_path):
 db=fixture_db(tmp_path/'private.sqlite');state=liabilities(db);shared=f.current_shared_liabilities(db,liabilities)
 v=f.financial_view({'spent_and_reserved_cents':(20000,3705),'modal_spent_and_reserved_cents':(0,3705)},state,{'remaining_commitment_cents':0},gross=20000,net=17000,shared=shared)
 assert v['existing_plan_commitments_cents']==1297+1629+744+1026 and v['project_room_after_all_existing_holds_cents']==60000-20000-3705-1000-(1297+1629+744+1026)
 a,r,*_=values();g=admission(a,db);before=list(db.execute('select * from events order by id'))
 out=claim_in_transaction(db,lambda db,k,t,p,n:db.execute('insert into events values(?,?,?)',(k,t,json.dumps(p))),a,r,g,'a'*64,'b'*64,now=101)
 assert out['existing_slot_cents']==744 and out['new_pledge_cents']==0 and out['actual_cost']is None
 assert shared==f.current_shared_liabilities(db,liabilities)and len(list(db.execute('select * from events')))==len(before)+1
 g['authoritative_ledger_state_sha256']=o.H(db.serialize())
 with pytest.raises(ValueError):claim_in_transaction(db,lambda *x:None,a,r,g,'a'*64,'b'*64,now=102)

def test_quote_uses_M32cpu3600_plus300_and_original744():
 rates=dict(gpu_hour_cost_h100='3.95',cpu_hour_cost='.0473',mem_gib_hour_cost='.008',volume_storage_gib_month_cost='.09',egress_gib_cost='.04')
 assert f.quote_cents(rates)==744

def test_fakeSDK_declare_all_M_profile_files_and_resources(monkeypatch):
 import function_app as app
 a,r,*_=values();added=[];decor=[]
 class V:
  def hydrate(self):return self
  def with_mount_options(self,**kw):return self
  def _get_metadata(self):return types.SimpleNamespace(version=2)
 class I:
  def add_local_file(self,p,**kw):added.append(str(kw['remote_path']));return self
 class A:
  def __init__(self,*a):pass
  def function(self,**kw):decor.append(kw);return lambda f:f
 api=types.ModuleType('modal_proto.api_pb2');api.VOLUME_FS_VERSION_V2=2;monkeypatch.setitem(sys.modules,'modal_proto',types.ModuleType('modal_proto'));monkeypatch.setitem(sys.modules,'modal_proto.api_pb2',api)
 sdk=types.SimpleNamespace(Volume=types.SimpleNamespace(from_id=lambda _:V()),Image=types.SimpleNamespace(from_id=lambda _:I()),Dict=types.SimpleNamespace(from_id=lambda _:V()),App=A)
 app.declare(sdk,r);assert len(added)==5 and decor[0]['cpu']==(32,32)and decor[0]['timeout']==3600 and decor[0]['retries']==0
