import ast,copy,importlib.util,json,random,time
from pathlib import Path
from types import SimpleNamespace
import numpy as np
import pytest
import torch
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('critic_diagnostics',ROOT/'code/sts2train/critic_diagnostics.py')
diag=importlib.util.module_from_spec(spec);spec.loader.exec_module(diag)
torch.set_num_threads(1)

def fixture(T=64,N=8):
    rng=np.random.default_rng(60062026)
    v=rng.normal(size=(T,N)).astype(np.float32)
    a=rng.normal(size=(T,N)).astype(np.float32)
    r=a+v
    valid=np.ones((T,N),bool);valid[2,1]=False
    phase=np.broadcast_to(np.arange(N)%3,(T,N)).copy()
    act=phase+1
    done=np.zeros((T,N),bool);done[3,0]=True;done[4,1]=True;done[-1,2]=True
    final=np.full((T,N),np.nan,np.float32);final[4,1]=.9
    last=np.ones(N,np.float32)
    return v,r,a,valid,phase,act,done,final,last,['Combat','Rewards','RestSite']

def test_valid_denominators_and_empty_groups():
    args=list(fixture());args[4][:]=0;args[5][:]=1
    out=diag.summarize(*args);n=int(args[3].sum())
    assert out['global']['n']==out['valid_n']==n
    for name in ['phase','act','window_position']:
        assert sum(row['n'] for row in out[name].values())==n
    for row in [out['phase']['1:Rewards'],out['act']['3']]:
        assert row['n']==0 and row['positive_adv_n']==0
        assert all(row[k] is None for k in diag.MOMENTS+diag.DERIVED)
    selected=args[3];v,r,a=args[:3];g=out['global']
    assert g['positive_adv_n']==int((a[selected]>0).sum())
    assert g['raw_adv_mean']==float(a[selected].astype(np.float64).mean())
    assert g['residual_mse']==float(np.mean((r[selected].astype(np.float64)-v[selected].astype(np.float64))**2))
    assert len(json.dumps({'critic_diagnostics':out},allow_nan=False).encode())<=diag.MAX_BYTES

def test_boundary_is_not_terminal_label():
    out=diag.summarize(*fixture());b=out['boundary']
    assert b['terminal_known'] is None and 'owned' in b['terminal_unavailable_reason']
    assert b['valid_done_n']==3 and b['valid_done_finite_final_bootstrap_n']==1
    assert b['valid_done_no_finite_final_n']==2
    assert b['last_step_continuing_n']==7 and b['last_step_finite_window_bootstrap_n']==7

def test_actual_raw_adv_not_float32_residual():
    args=list(fixture());args[0][:]=1e8;args[1][:]=1e8;args[2][:]=1
    g=diag.summarize(*args)['global']
    assert g['raw_adv_mean']==1 and g['residual_mean']==0

def test_nonfinite_keeps_valid_denominator_and_strict_json():
    args=list(fixture());args[0][0,0]=np.nan;args[0][2,1]=np.inf
    out=diag.summarize(*args)
    assert out['global']['n']==int(args[3].sum()) and out['global']['nonfinite_n']==1
    assert out['global']['oldV_mean'] is None
    assert out['status'].startswith('nonfinite_present')
    json.dumps(out,allow_nan=False)

def test_invalid_nan_not_counted_and_unknown_bucket():
    args=list(fixture());args[0][2,1]=np.nan;args[4][0,0]=999;args[5][0,0]=9
    out=diag.summarize(*args)
    assert out['status']=='ok' and out['global']['nonfinite_n']==0
    assert out['phase']['unknown']['n']==1 and out['act']['unknown']['n']==1

def test_empty_all_nonmutation_and_safe_failure():
    args=list(fixture());args[3][:]=False;before=[x.copy() if isinstance(x,np.ndarray) else copy.deepcopy(x) for x in args]
    out=diag.safe_summarize(*args)
    assert out['global']['n']==0 and out['global']['residual_mse'] is None
    for a,b in zip(args,before):
        if isinstance(a,np.ndarray):np.testing.assert_array_equal(a,b)
    assert diag.safe_summarize(np.zeros(3))['status']=='unavailable_diagnostic_error'

def test_bound_and_schema_refusal():
    args=list(fixture());args[-1]=['p'*2000]*32
    assert diag.summarize(*args)['status']=='unavailable_size_limit'
    args[-1]=['p']*33
    assert diag.summarize(*args)['status'].startswith('unavailable_schema')


def learner_input_block(path):
    """Compile exact PPO statements ret→minibatch input construction; no env/model forward."""
    tree=ast.parse(path.read_text());train=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='train')
    # Walk to the update-loop body, preserving exact source statements.
    node=next(n for n in ast.walk(train) if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='ret' for t in n.targets))
    parent=next(n for n in ast.walk(train) if isinstance(getattr(n,'body',None),list) and node in n.body)
    body=parent.body;start=body.index(node)
    end=next(i for i in range(start,len(body)) if isinstance(body[i],ast.Assign) and any(isinstance(t,ast.Name) and t.id=='d_mbs' for t in body[i].targets))
    return compile(ast.fix_missing_locations(ast.Module(body=copy.deepcopy(body[start:end+1]),type_ignores=[])),str(path),'exec')

@pytest.mark.parametrize('enabled',[False,True])
def test_logging_on_off_exact_optimizer_inputs_and_rng(enabled):
    args=list(fixture());v,ret,adv,valid,phase,act,done,final,last,names=args
    def execute(path,on):
        rng=np.random.default_rng(47);store=SimpleNamespace(values=v.copy(),valid=valid.copy(),dones=done.copy(),actions=np.zeros(v.shape,np.int64),logp=np.zeros(v.shape,np.float32))
        env=SimpleNamespace(prefetch=lambda:None)
        ns=dict(np=np,torch=torch,time=time,adv=adv.copy(),store=store,cfg=SimpleNamespace(start_mode='stream',minibatch=64,epochs=1),env=env,device='cpu',rng=rng,model=torch.nn.Identity(),critic_diagnostics=on,summarize_critic_diagnostics=diag.safe_summarize,diagnostic_phase=phase,diagnostic_act=act,final_values=final,last_v=last,spec=SimpleNamespace(enum_names={'RunPhase':names}))
        torch_before=torch.get_rng_state().clone();global_before=copy.deepcopy(np.random.get_state());python_before=random.getstate()
        exec(learner_input_block(path),ns)
        assert torch.equal(torch_before,torch.get_rng_state()) and random.getstate()==python_before
        for a,b in zip(global_before,np.random.get_state()):
            if isinstance(a,np.ndarray):np.testing.assert_array_equal(a,b)
            else:assert a==b
        return ns
    base=execute(ROOT/'reference_ppo.py',False);new=execute(ROOT/'code/sts2train/ppo.py',enabled)
    for key in ['ret','adv','idx_all','f_adv']:
        np.testing.assert_array_equal(base[key],new[key])
    for key in ['d_act','d_logp','d_adv','d_ret','d_val']:
        torch.testing.assert_close(base[key],new[key],rtol=0,atol=0)
    for a,b in zip(base['mbs'],new['mbs']):np.testing.assert_array_equal(a,b)
    assert base['rng'].bit_generator.state==new['rng'].bit_generator.state
    assert (new['critic_diagnostic_row'] is not None)==enabled
