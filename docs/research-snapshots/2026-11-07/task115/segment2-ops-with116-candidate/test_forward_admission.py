"""Actual prospective entrypoint, real Store private SQLite and fake SDK only."""
import ast,contextlib,dataclasses,io,json,runpy,sqlite3,sys,tempfile,types
from pathlib import Path
import pytest
from modal_research.store import Store
from first_slot_budget import TRAIN_AUTH,BOUND,PLAN,SLOTS,terminal_id
from project_obligations import M_PLAN,PROJECT_APPROVAL,PLATFORM_POLICY
P=Path(__file__).parent
MBOUND='6fc6f2544c6196546fe31b9796f73b3aca1aac57b7c2766c0d9454fe9dc1c8a6'
@dataclasses.dataclass
class Stats:
 num_running_inputs:int=0
 backlog:int=0
 num_total_runners:int=0

def setup(tmp,*,gross=20000,retired=False):
 st=Store(tmp/'fixture.sqlite','privateS115withM116',mode='fixture',clock=lambda:1794010000.)
 st.approve('600.00',PROJECT_APPROVAL)
 st.record_platform(gross_limit='200.00',net_limit='200.00',credit='0',gross_used=f'{gross/100:.2f}',net_spent=f'{gross/100:.2f}',reference='historical-platform200',observed_at=1794009999.,cycle_start='2026-11-01')
 def ev(db,key,p):Store.append_event(db,key,'synthetic_accounting',p,1794010000.)
 with st.transaction()as db:
  db.execute("INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,actual,state,deadline)VALUES('unknown','unknown',1,'research','{}',3705,NULL,'failed',1)")
  ev(db,PLAN,dict(root_authority_sha256=TRAIN_AUTH,bound_release_sha256=BOUND,original_total_maximum_cents=2926,slot_allocations=SLOTS))
  ev(db,terminal_id('segment1_train'),dict(plan_event=PLAN,slot='segment1_train',completed_allocation_cents=1629,active_component_cents=0,training_root_authority_sha256=TRAIN_AUTH,training_bound_release_sha256=BOUND,call_terminal=True,actual_cost=None,provider_terminal_verified=True,financial_terminal_status='completed',completion_authority_sha256='a'*64))
  m=dict(slot_allocations=dict(train=3992,final_dev=744,shared_blind_increment=1026),full_shared_blind_quote_cents=2101,original_total_maximum_cents=5762,active_plan_commitment_cents=5762,existing_global_blind_base_cents=1075,project_budget_approval_ref=PROJECT_APPROVAL,project_cap_cents=60000,platform_admission_policy=PLATFORM_POLICY,actual_cost=None,root_authority_sha256='c'*64,bound_release_sha256=MBOUND);ev(db,M_PLAN,m)
  if retired:
   for slot,amount in [('train',3992),('final_dev',744)]:
    ev(db,'operator-task116-slot-'+slot.replace('_','-')+'-terminal-history-v1',dict(plan_event=M_PLAN,slot=slot,completed_allocation_cents=amount,active_component_cents=0,actual_cost=None,provider_terminal_verified=True,call_terminal=True,financial_terminal_status='failed',training_root_authority_sha256='c'*64,training_bound_release_sha256=MBOUND,completion_authority_sha256='e'*64,terminal_proof_sha256='f'*64,provider_terminal_receipt_sha256='a'*64))
 return st

def entry(st,tmp,monkeypatch,gross,mstats=None):
 # Real script gets only private local filesystem/Store and explicit SDK facts.
 args=types.ModuleType('ops_common');args.inputs=lambda a,b:({'segment_scope_sha256':'a'*64,'first_parent_proof_sha256':'b'*64},{'segment':{'seed_range':[1099783413606,1099917631334]},'old_reserved_end':1099783413606})
 monkeypatch.setitem(sys.modules,'ops_common',args);monkeypatch.setattr(sys,'argv',[str(P/'funding_snapshot.py'),'a'*64,'b'*64])
 rates=dict(gpu_hour_cost_h100='3.95',cpu_hour_cost='.04716',mem_gib_hour_cost='.007992',cpu_hour_cost_sandbox='.04716',mem_gib_hour_cost_sandbox='.007992',volume_storage_gib_month_cost='.09',egress_gib_cost='.04')
 import datetime
 summary=types.SimpleNamespace(metered_cost=gross/100,billed_cost=gross/100,start=datetime.datetime(2026,11,1))
 ws=types.SimpleNamespace(billing=types.SimpleNamespace(summary=lambda:summary,rates=lambda:rates))
 modal=types.ModuleType('modal');modal.Workspace=types.SimpleNamespace(from_context=lambda:types.SimpleNamespace(hydrate=lambda:ws));modal.Function=types.SimpleNamespace(from_id=lambda k:types.SimpleNamespace(hydrate=lambda:types.SimpleNamespace(get_current_stats=lambda:(mstats if k=='fu-yOQraWLl74vW59NtASwaVu'and mstats is not None else Stats(1,0,1)if k=='fu-yOQraWLl74vW59NtASwaVu'else Stats()))))
 monkeypatch.setitem(sys.modules,'modal',modal)
 original=sqlite3.connect
 def connect(path,*a,**kw):
  if str(path).startswith('file:/var/lib/sts2-modal-production/operation.sqlite3'):path='file:'+str(st.path)+'?mode=ro'
  return original(path,*a,**kw)
 monkeypatch.setattr(sqlite3,'connect',connect)
 mk=tempfile.mkdtemp;monkeypatch.setattr(tempfile,'mkdtemp',lambda **kw:mk(prefix=kw['prefix'],dir=tmp))
 import subprocess
 def systemctl(args,**kwargs):
  assert args[0]=='systemctl';return types.SimpleNamespace(stdout='disabled'if args[1]=='is-enabled'else'inactive')
 monkeypatch.setattr(subprocess,'run',systemctl)
 out=io.StringIO()
 with contextlib.redirect_stdout(out):runpy.run_path(str(P/'funding_snapshot.py'),run_name='__main__')
 return json.loads(out.getvalue())

def test_actual_funding_entry_counts_M_and_single_S2_with_unknown_platform(tmp_path,monkeypatch):
 st=setup(tmp_path);f=entry(st,tmp_path,monkeypatch,20000)
 assert f['status']=='PASS' and f['project_cap_cents']==60000 and f['platform_admission_policy']==PLATFORM_POLICY
 assert f['prior_direct_commitments_cents']==1297+5762
 assert f['project_room_after_cents']==60000-20000-3705-1000-1297-5762-1629
 assert f['platform_room_after_cents']is None and f['platform_snapshot']['gross_limit']==20000
 assert f['existing_direct_obligations']['shared_blind_base_in_S115_cents']==1075 and f['existing_direct_obligations']['shared_blind_increment_in_M_cents']==1026
 with st.transaction()as db:assert tuple(db.execute("SELECT reserve,actual FROM jobs WHERE id='unknown'").fetchone())==(3705,None)

def test_actual_entry_M_priority_blocks_unaffordable_S2(tmp_path,monkeypatch):
 st=setup(tmp_path,gross=48000);f=entry(st,tmp_path,monkeypatch,48000)
 assert 60000-48000-3705-1000-1297-1629>=0 # would pass if M silently omitted
 assert f['project_room_after_cents']<0 and f['status']=='BLOCKED_FUNDS_OR_ACTIVE'

def test_actual_entry_only_verified_M_retirement_releases_train_dev(tmp_path,monkeypatch):
 st=setup(tmp_path,retired=True);f=entry(st,tmp_path,monkeypatch,20000)
 assert f['prior_direct_commitments_cents']==1297+1026
 assert f['existing_direct_obligations']['M116_active_allocations']=={'shared_blind_increment':1026}

@pytest.mark.parametrize('bad',['missingM','badM','missingS1terminal','wrongproject'])
def test_actual_entry_rejects_unknown_or_malformed_obligations(tmp_path,monkeypatch,bad):
 st=setup(tmp_path)
 with st.transaction()as db:
  if bad=='missingM':db.execute('DELETE FROM events WHERE id=?',(M_PLAN,))
  elif bad=='badM':db.execute('UPDATE events SET payload=? WHERE id=?',(json.dumps({'slot_allocations':{}}),M_PLAN))
  elif bad=='missingS1terminal':db.execute('DELETE FROM events WHERE id=?',(terminal_id('segment1_train'),))
  else:db.execute('UPDATE study SET execution_cap=30000')
 with pytest.raises((AssertionError,ValueError)):entry(st,tmp_path,monkeypatch,20000)

def test_actual_precommit_transaction_recomputes_M_and_no_double_S2(tmp_path):
 from first_slot_budget import remaining
 from plan_liabilities import liabilities
 from project_obligations import shared_liabilities
 st=setup(tmp_path)
 # Execute the actual record_commitment transaction AST, not a helper reimplementation.
 tree=ast.parse((P/'record_commitment.py').read_text());node=next(n for n in tree.body if isinstance(n,ast.With)and ast.unparse(n.items[0].context_expr)=='s.transaction()')
 g=dict(s=st,remaining=remaining,shared_liabilities=shared_liabilities,liabilities=liabilities,PROJECT_APPROVAL=PROJECT_APPROVAL,PLATFORM_POLICY=PLATFORM_POLICY,M_PLAN=M_PLAN,Store=Store,AUTH='a'*64,BOUND='b'*64,E='operator-task115-second-segment-budget-commitment-v1',f={'allocations':{'segment2_train':1629},'gross_effective_cents':20000},W=tmp_path,H=lambda raw:__import__('hashlib').sha256(raw).hexdigest(),time=__import__('time'),r={'segment':{'seed_range':[1099783413606,1099917631334]}})
 (tmp_path/'fresh-precommit-funding.json').write_text('{}')
 exec(compile(ast.Module(body=[node],type_ignores=[]),'actual-record-transaction','exec'),g)
 with st.transaction()as db:
  ob=shared_liabilities(db,s115_reader=liabilities)
  assert ob['total_direct_commitments_cents']==1297+1629+5762
  assert db.execute("SELECT COUNT(*) FROM events WHERE id=?",(g['E'],)).fetchone()[0]==1
 with pytest.raises(AssertionError):exec(compile(ast.Module(body=[node],type_ignores=[]),'actual-record-transaction','exec'),g)


def test_precommit_transaction_rechecks_M_at_affordability_boundary(tmp_path):
 from first_slot_budget import remaining
 from plan_liabilities import liabilities
 from project_obligations import shared_liabilities
 st=setup(tmp_path,gross=48000)
 tree=ast.parse((P/'record_commitment.py').read_text());node=next(n for n in tree.body if isinstance(n,ast.With)and ast.unparse(n.items[0].context_expr)=='s.transaction()')
 g=dict(s=st,remaining=remaining,shared_liabilities=shared_liabilities,liabilities=liabilities,PROJECT_APPROVAL=PROJECT_APPROVAL,PLATFORM_POLICY=PLATFORM_POLICY,M_PLAN=M_PLAN,Store=Store,AUTH='a'*64,BOUND='b'*64,E='operator-task115-second-segment-budget-commitment-v1',f={'allocations':{'segment2_train':1629},'gross_effective_cents':48000},W=tmp_path,H=lambda raw:'a'*64,time=__import__('time'),r={'segment':{'seed_range':[1099783413606,1099917631334]}})
 with pytest.raises(AssertionError):exec(compile(ast.Module(body=[node],type_ignores=[]),'actual-record-transaction','exec'),g)
 with st.transaction()as db:assert db.execute('SELECT count(*) FROM events WHERE id=?',(g['E'],)).fetchone()[0]==0


@pytest.mark.parametrize('mstats,allowed',[(Stats(1,0,1),True),(Stats(2,0,1),False),(Stats(1,0,2),False),(Stats(1,1,1),False)])
def test_actual_entry_M_concurrency_bounds(tmp_path,monkeypatch,mstats,allowed):
 st=setup(tmp_path);f=entry(st,tmp_path,monkeypatch,20000,mstats)
 assert f['M116_concurrency_allowed']is allowed and (f['status']=='PASS')is allowed
 assert f['owned_function_stats']['M116']==dataclasses.asdict(mstats)
 assert f['M116_inventory_binding']['bound_release_sha256']==MBOUND

def test_actual_entry_unknown_M_stats_not_pass(tmp_path,monkeypatch):
 st=setup(tmp_path)
 with pytest.raises((AssertionError,TypeError)):entry(st,tmp_path,monkeypatch,20000,Stats(None,0,1))

def test_project600_metadata_template_and_actual_declare_validation(tmp_path,monkeypatch):
 import function_app as app,ops_common as o,hashlib,time,copy
 r=json.loads((P/'release.segment2.DISABLED-TEMPLATE.json').read_bytes())
 r['parent']={'decisions':595853312,'updates':4546,'model_sha256':'a'*64,'cursor':1099649200000,'issued':1099649204096,'namespace':'MODA10PH104FRESH1','adam_step':145472,'bytes':36000000}
 app.validate_release(r)
 old=copy.deepcopy(r);old['project_cap_cents']=30000
 with pytest.raises(ValueError):app.validate_release(old)
 # Run actual metadata entry prefix, stopping BEFORE first SDK attribute/use.
 raw=(P/'release.segment2.DISABLED-TEMPLATE.json').read_bytes();(tmp_path/'release.segment2.DISABLED-TEMPLATE.json').write_bytes(raw)
 a={'allow_metadata_only':True,'compute_invocations_authorized':0,'expires':time.time()+60,'draft_release_sha256':hashlib.sha256(raw).hexdigest(),'release_binding':{'budget_cap_cents':1629,'project_cap_cents':60000,'science_plan_sha256':r['science_plan_sha256']},'first_parent_proof_sha256':'f'*64}
 ar=json.dumps(a).encode();(tmp_path/'root-metadata-authorization.json').write_bytes(ar)
 monkeypatch.setattr(o,'W',tmp_path);monkeypatch.setattr(o,'accepted_parent',lambda proof,rel:None);monkeypatch.setattr(sys,'argv',['create_metadata.py',hashlib.sha256(ar).hexdigest()]);monkeypatch.setitem(sys.modules,'modal',types.ModuleType('modal'))
 tree=ast.parse((P/'create_metadata.py').read_text());nodes=[]
 for n in tree.body:
  if isinstance(n,ast.Assert)and 'modal.__version__'in ast.unparse(n.test):break
  nodes.append(n)
 g={};exec(compile(ast.Module(body=nodes,type_ignores=[]),'actual-metadata-pre-SDK','exec'),g)
 assert g['r']['project_cap_cents']==60000
 a['release_binding']['project_cap_cents']=30000;ar=json.dumps(a).encode();(tmp_path/'root-metadata-authorization.json').write_bytes(ar);monkeypatch.setattr(sys,'argv',['create_metadata.py',hashlib.sha256(ar).hexdigest()])
 with pytest.raises(AssertionError):exec(compile(ast.Module(body=nodes,type_ignores=[]),'actual-metadata-pre-SDK','exec'),{})


def test_actual_declare_passes_complete_pinned_file_closure_and_decorator(monkeypatch):
 import function_app as app,hashlib
 r=json.loads((P/'release.segment2.DISABLED-TEMPLATE.json').read_bytes())
 r['parent']={'decisions':595853312,'updates':4546,'model_sha256':'a'*64,'cursor':1099649200000,'issued':1099649204096,'namespace':'MODA10PH104FRESH1','adam_step':145472,'bytes':36000000}
 for k in ('root_selection_sha256','science_plan_sha256','segment_authority_sha256','funding_receipt_sha256','parent_evidence_sha256'):
  if k!='science_plan_sha256':r[k]='a'*64
 r.update(budget_cap_cents=1629,source_volume_id='vo-FixtureSource',parent_volume_id='vo-FixtureParent',output_volume_id='vo-FixtureOutput',claim_dict_id='di-FixtureClaim')
 added=[];decorated=[]
 class V:
  def hydrate(self):return self
  def _get_metadata(self):return types.SimpleNamespace(version=2)
  def with_mount_options(self,**kw):return self
 class Image:
  def add_local_file(self,p,**kw):
   assert Path(p).is_file();added.append((Path(p).name,kw));return self
 class App:
  def __init__(self,name):self.name=name
  def function(self,**kwargs):
   decorated.append(kwargs);return lambda fn:fn
 api=types.ModuleType('modal_proto.api_pb2');api.VOLUME_FS_VERSION_V2=2
 monkeypatch.setitem(sys.modules,'modal_proto',types.ModuleType('modal_proto'));monkeypatch.setitem(sys.modules,'modal_proto.api_pb2',api)
 sdk=types.SimpleNamespace(Volume=types.SimpleNamespace(from_id=lambda _:V()),Image=types.SimpleNamespace(from_id=lambda _:Image()),Dict=types.SimpleNamespace(from_id=lambda _:V()),App=App)
 application,fn=app.declare(sdk,r)
 assert application.name==r['app_name']and callable(fn)and len(decorated)==1
 assert set(n for n,_ in added)=={'function_app.py','runtime_worker.py','ppo.py','strategic_kl.py','seeds128.json'}
 assert decorated[0]['max_containers']==1 and decorated[0]['retries']==0 and decorated[0]['timeout']==9000
 assert r['overlay_sha256']['function_app.py']==hashlib.sha256((P/'function_app.py').read_bytes()).hexdigest()
 bad=json.loads(json.dumps(r));bad['overlay_sha256']['function_app.py']='15d294ef01ffb1db2bdec8969d5d9b8361d6c39ac4c7fc3b8239457d36649213'
 with pytest.raises(ValueError,match='direct wrapper bytes'):app.declare(sdk,bad)
