import copy,json
import numpy as np
import pytest,torch
from sts2train.model import Policy,CRITIC_ARCH,legal_candidate_mean,param_count,synthetic_batch,arch_of,model_arch
from sts2train.ppo import (TrainConfig,config_dict,save_checkpoint,load_checkpoint,validate_critic_checkpoint,
                          restore_checkpoint,make_checkpoint,ppo_loss)
from sts2train.transition_aux import TransitionHead
ARCH=dict(d=192,s=384,depth=3,heads=6,ff_mult=4,aux_heads=True,deck_counts=True)

def pair(spec,original):
    torch.manual_seed(991);base=original.Policy(spec,**ARCH);after=torch.get_rng_state().clone()
    torch.manual_seed(991);model=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH)
    assert torch.equal(after,torch.get_rng_state())
    return base,model

def cfg(on=True):return TrainConfig(**ARCH,critic_arch=CRITIC_ARCH if on else '',amp=False,tf32=False,compile='off',epochs=1)

def test_initial_mapped_outputs_parameters_and_rng_exact(spec,original):
    base,m=pair(spec,original);base.eval();m.eval()
    assert param_count(base)==2904208 and param_count(m)==2941072
    assert set(m.state_dict())==set(base.state_dict())|{'value_candidate_proj.weight'}
    for k,v in base.state_dict().items():assert torch.equal(v,m.state_dict()[k]),k
    assert not m.value_candidate_proj.weight.any() and m.value_candidate_proj.bias is None
    x=synthetic_batch(spec,2,rows=3)
    with torch.no_grad():a=base(x,aux=True,return_transition_features=True);b=m(x,aux=True,return_transition_features=True)
    for i in (0,1):assert torch.equal(a[i],b[i])
    for k in a[2]:assert torch.equal(a[2][k],b[2][k])
    for aa,bb in zip(a[3],b[3]):assert torch.equal(aa,bb)

def test_off_is_exact_legacy_and_preserves_later_transition_rng(spec,original):
    torch.manual_seed(992);base=original.Policy(spec,**ARCH);h0=TransitionHead(384,192);rng=torch.get_rng_state().clone()
    torch.manual_seed(992);off=Policy(spec,**ARCH);h1=TransitionHead(384,192)
    assert torch.equal(rng,torch.get_rng_state()) and off.arch==base.arch
    for k,v in base.state_dict().items():assert torch.equal(v,off.state_dict()[k])
    for k,v in h0.state_dict().items():assert torch.equal(v,h1.state_dict()[k])
    assert 'critic_arch' not in config_dict(cfg(False)) and 'critic_arch' not in model_arch(cfg(False))
    torch.manual_seed(992);positive=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);h2=TransitionHead(384,192)
    assert torch.equal(rng,torch.get_rng_state())
    for k,v in h0.state_dict().items():assert torch.equal(v,h2.state_dict()[k])

def test_legal_pool_mask_empty_finite_and_permutation():
    h=torch.tensor([[[1.,3.],[5.,7.],[float('nan'),float('inf')]],[[9.,8.],[7.,6.],[5.,4.]]])
    out=legal_candidate_mean(h,torch.tensor([2,0]))
    assert torch.equal(out,torch.tensor([[3.,5.],[0.,0.]])) and torch.isfinite(out).all()
    assert torch.equal(out,legal_candidate_mean(h[:,[1,0,2]],torch.tensor([2,0])))

def test_value_grad_scope_detached_candidates_and_existing_state(spec):
    m=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH)
    with torch.no_grad():m.value_candidate_proj.weight.fill_(.01)
    x=synthetic_batch(spec,2,rows=3);_,v,(state,h2)=m(x,return_transition_features=True);state.retain_grad();h2.retain_grad()
    v.sum().backward()
    assert m.value_candidate_proj.weight.grad is not None and m.value_candidate_proj.weight.grad.abs().sum()>0
    assert state.grad is not None and state.grad.abs().sum()>0 and h2.grad is None
    candidate_prefixes=('c_kind','c_num','c_hand','c_tgt','c_pot','c_opt','c_card','c_state','c_l2','q_state','c_bias','c_copies')
    for name,p in m.named_parameters():
        if name.split('.')[0] in candidate_prefixes:assert p.grad is None,name
    assert m.state_mlp[0].weight.grad is not None and m.state_mlp[0].weight.grad.abs().sum()>0

def macro(spec):
    x=synthetic_batch(spec,1,rows=3);c=spec.cols['cand'];choice=spec.cols['reward_choices']
    x['cand_n'][:]=2;x['cand'][:]=0
    kind=spec.enum_names['RunActionKind'].index('TakeRewardCard')
    x['cand'][:,:,c['kind']]=kind;x['cand'][0,0,c['option_index']]=0;x['cand'][0,1,c['option_index']]=1
    x['reward_choices'][:]=0;x['reward_choices'][0,0,choice['card_id']]=1;x['reward_choices'][0,1,choice['card_id']]=2
    return x

def test_public_macro_detail_reaches_value_after_weights_learn(spec,original):
    base,m=pair(spec,original);base.eval();m.eval();x=macro(spec);y={k:v.clone() for k,v in x.items()}
    y['reward_choices'][0,0,spec.cols['reward_choices']['upgrade']]=1
    with torch.no_grad():
        s0,_=m.encode(x);s1,_=m.encode(y);assert torch.equal(s0,s1)
        a=m(x,return_transition_features=True);b=m(y,return_transition_features=True)
        assert not torch.equal(legal_candidate_mean(a[2][1],x['cand_n']),legal_candidate_mean(b[2][1],y['cand_n']))
        m.value_candidate_proj.weight.fill_(.01)
        assert not torch.equal(m(x)[1],m(y)[1])
        assert torch.equal(base(x)[1],base(y)[1])
        assert torch.equal(m(x)[0],base(x)[0])

def test_candidate_permutation_and_padding_leave_value_unchanged(spec):
    m=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH).eval()
    with torch.no_grad():m.value_candidate_proj.weight.fill_(.01)
    x=macro(spec);y={k:v.clone() for k,v in x.items()};y['cand'][:,[0,1]]=y['cand'][:,[1,0]]
    z={k:v.clone() for k,v in x.items()};z['cand'][:,2,:]=999
    with torch.no_grad():
        assert torch.equal(m(x)[1],m(y)[1]);assert torch.equal(m(x)[1],m(z)[1])
        z['cand_n'][:]=0;_,v,(state,_)=m(z,return_transition_features=True)
        assert torch.isfinite(v).all() and torch.equal(v,m.value_head(state).squeeze(-1))

@pytest.mark.parametrize('marker',[None,False,1,'unknown'])
def test_wrong_arch_type_rejected(spec,marker):
    with pytest.raises(ValueError):Policy(spec,**ARCH,critic_arch=marker)
    with pytest.raises(ValueError):arch_of({**ARCH,'critic_arch':marker})

def checkpoint(m,spec,on=True):
    return {'format':2,'model':copy.deepcopy(m.state_dict()),'spec':spec.to_json(),'arch':m.arch,'config':config_dict(cfg(on))}

def test_safe_checkpoint_roundtrip_and_wrong_arch_fail_closed(spec,original,tmp_path):
    from modal_research.artifact_adapter import load_checkpoint as safe_load
    base,m=pair(spec,original);p=tmp_path/'critic.pt';save_checkpoint(p,m,spec,cfg(),{})
    raw,_=safe_load(p);assert validate_critic_checkpoint(raw)==CRITIC_ARCH
    loaded,_,_=load_checkpoint(p);assert loaded.arch==m.arch
    for k,v in m.state_dict().items():assert torch.equal(v,loaded.state_dict()[k])
    bad=copy.deepcopy(raw);bad['arch'].pop('critic_arch')
    with pytest.raises(ValueError):validate_critic_checkpoint(bad)
    bad=copy.deepcopy(raw);bad['config'].pop('critic_arch')
    with pytest.raises(ValueError):validate_critic_checkpoint(bad)
    bad=copy.deepcopy(raw);bad['model'].pop('value_candidate_proj.weight')
    with pytest.raises(ValueError):validate_critic_checkpoint(bad)
    with pytest.raises(ValueError):restore_checkpoint(raw,Policy(spec,**ARCH),None,None,'cpu')
    with pytest.raises(ValueError):restore_checkpoint(checkpoint(base,spec,False),m,None,None,'cpu')
    with pytest.raises(RuntimeError):base.load_state_dict(raw['model'],strict=True)
    with pytest.raises(ValueError):save_checkpoint(tmp_path/'bad.pt',m,spec,cfg(False),{})

@pytest.mark.parametrize('bad',['shape','dtype','nan'])
def test_projection_schema_rejected(spec,bad):
    m=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);ck=checkpoint(m,spec)
    if bad=='shape':ck['model']['value_candidate_proj.weight']=torch.zeros(1)
    elif bad=='dtype':ck['model']['value_candidate_proj.weight']=torch.zeros(192,192,dtype=torch.float64)
    else:ck['model']['value_candidate_proj.weight'][0,0]=float('nan')
    with pytest.raises(ValueError):validate_critic_checkpoint(ck)

def test_off_full_loss_and_gradients_match_original(spec,original):
    torch.manual_seed(993);base=original.Policy(spec,**ARCH)
    torch.manual_seed(993);off=Policy(spec,**ARCH)
    x=synthetic_batch(spec,2,rows=3);act=torch.tensor([0,1])
    with torch.no_grad():lg,v=base(x);lp=torch.log_softmax(lg,-1).gather(1,act[:,None]).squeeze(1)
    args=(x,act,lp,torch.tensor([1.,-1.]),torch.tensor([.2,.4]),v.detach())
    aux=(torch.tensor([0.,1.]),torch.ones(2),torch.tensor([.2,.6]),torch.ones(2))
    la,sa=ppo_loss(base,*args,clip=.2,vf_coef=.5,ent_coef=.02,aux=aux,aux_coef=.1)
    lb,sb=ppo_loss(off,*args,clip=.2,vf_coef=.5,ent_coef=.02,aux=aux,aux_coef=.1)
    assert torch.equal(la,lb) and torch.equal(sa,sb);la.backward();lb.backward()
    for (na,pa),(nb,pb) in zip(base.named_parameters(),off.named_parameters()):
        assert na==nb and ((pa.grad is None and pb.grad is None) or torch.equal(pa.grad,pb.grad)),na

def test_positive_ppo_with_transition_still_one_policy_forward(spec):
    m=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);head=TransitionHead(384,192)
    x=synthetic_batch(spec,2,rows=3);act=torch.tensor([0,1])
    with torch.no_grad():lg,v=m(x);lp=torch.log_softmax(lg,-1).gather(1,act[:,None]).squeeze(1)
    calls=[];hook=m.register_forward_hook(lambda *args:calls.append(1))
    loss,stats=ppo_loss(m,x,act,lp,torch.tensor([1.,-1.]),torch.tensor([.2,.4]),v,clip=.2,vf_coef=.5,ent_coef=.02,
                        transition=(head,torch.ones(2,4),torch.ones(2,4,dtype=torch.bool),.1))
    loss.backward();hook.remove()
    assert len(calls)==1 and torch.isfinite(loss) and torch.isfinite(stats).all()
    assert m.value_candidate_proj.weight.grad is not None
    assert all(p.grad is not None and torch.isfinite(p.grad).all() for p in head.parameters())

def optimizer_checkpoint(spec):
    m=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);opt=torch.optim.Adam(m.parameters(),lr=3e-4,eps=1e-5)
    x=synthetic_batch(spec,2,rows=3);m(x)[1].sum().backward();opt.step()
    ck=checkpoint(m,spec);ck['optimizer']=copy.deepcopy(opt.state_dict());ck['updates']=1
    return m,opt,ck

def test_optimizer_roundtrip_and_valid_empty_initial(spec):
    m,opt,ck=optimizer_checkpoint(spec);new=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);newopt=torch.optim.Adam(new.parameters(),lr=3e-4,eps=1e-5)
    restore_checkpoint(ck,new,newopt,None,'cpu')
    p=new.value_candidate_proj.weight
    assert newopt.state[p]['step']==1 and torch.equal(newopt.state[p]['exp_avg'],opt.state[m.value_candidate_proj.weight]['exp_avg'])
    empty=checkpoint(new,spec);empty['optimizer']=torch.optim.Adam(new.parameters()).state_dict();empty['updates']=0
    restore_checkpoint(empty,new,newopt,None,'cpu')

@pytest.mark.parametrize('bad',['missing_state','missing_moment','shape','dtype','nan','bad_step','bool_step','vector_step'])
def test_optimizer_partial_projection_state_rejected_before_restore(spec,bad):
    m,opt,ck=optimizer_checkpoint(spec);idx=ck['optimizer']['param_groups'][0]['params'][-1];st=ck['optimizer']['state'][idx]
    if bad=='missing_state':ck['optimizer']['state'].pop(idx)
    elif bad=='missing_moment':st.pop('exp_avg')
    elif bad=='shape':st['exp_avg']=torch.zeros(1)
    elif bad=='dtype':st['exp_avg']=st['exp_avg'].double()
    elif bad=='nan':st['exp_avg'][0,0]=float('nan')
    elif bad=='bool_step':st['step']=torch.tensor(True)
    elif bad=='vector_step':st['step']=torch.tensor([1.])
    else:st['step']=torch.tensor(.5)
    target=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);before=copy.deepcopy(target.state_dict());to=torch.optim.Adam(target.parameters())
    with pytest.raises(ValueError):restore_checkpoint(ck,target,to,None,'cpu')
    for k,v in before.items():assert torch.equal(v,target.state_dict()[k])
    assert not to.state

@pytest.mark.parametrize('bad',['duplicate','reordered','bool_id','unbound_state'])
def test_newcritic_optimizer_id_bindings_rejected(spec,bad):
    m,opt,ck=optimizer_checkpoint(spec);ids=ck['optimizer']['param_groups'][0]['params']
    if bad=='duplicate':ids[-1]=ids[-2]
    elif bad=='reordered':ids[-1],ids[-2]=ids[-2],ids[-1]
    elif bad=='bool_id':ids[1]=True
    else:ck['optimizer']['state'][99999]=copy.deepcopy(ck['optimizer']['state'][ids[-1]])
    target=Policy(spec,**ARCH,critic_arch=CRITIC_ARCH);before=copy.deepcopy(target.state_dict());to=torch.optim.Adam(target.parameters())
    with pytest.raises(ValueError):restore_checkpoint(ck,target,to,None,'cpu')
    for k,v in before.items():assert torch.equal(v,target.state_dict()[k])
    assert not to.state
