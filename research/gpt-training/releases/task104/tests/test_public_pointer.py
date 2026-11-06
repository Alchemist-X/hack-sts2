import copy
import pytest,torch
from sts2train.features import (public_hand_slots,PUBLIC_HAND_INPUT_ARCH,REWARD_INPUT_ARCH,
                                ModelSpec,check_spec_compat,SpecMismatch)
from sts2train.model import Policy,CRITIC_ARCH,synthetic_batch,param_count
from sts2train.ppo import TrainConfig,config_dict,save_checkpoint,load_checkpoint,restore_checkpoint,validate_input_checkpoint
from sts2train.transition_aux import TransitionHead
ARCH=dict(d=192,s=384,depth=3,heads=6,ff_mult=4,aux_heads=True,deck_counts=True,critic_arch=CRITIC_ARCH)

def observation(spec,hand=((2,0),(3,0),(4,0)),options=None,rows=16):
    if options is None:options=hand
    x=synthetic_batch(spec,1,rows=rows);g=spec.cols['global_i'];h=spec.cols['hand'];o=spec.cols['prompt_options'];c=spec.cols['cand']
    x['global_i'][:]=0;x['hand'][:]=0;x['prompt_options'][:]=0;x['cand'][:]=0
    for pointer in ('hand_slot','target_slot','potion_slot','option_index'):x['cand'][:,:,c[pointer]]=-1
    for name,value in dict(phase=spec.enum_names['RunPhase'].index('Combat'),decision_kind=spec.enum_names['RunPhase'].index('Prompt'),prompt_kind=spec.enum_names['ChoicePromptKind'].index('HandSelect'),prompt_pile=spec.enum_names['PileType'].index('Hand'),is_combat=1,hand_n=len(hand),prompt_option_n=len(options)).items():x['global_i'][:,g[name]]=value
    for i,(identity,upgrade) in enumerate(hand):x['hand'][0,i,h['card_id']]=identity;x['hand'][0,i,h['upgrade']]=upgrade
    for i,(identity,upgrade) in enumerate(options):
        x['prompt_options'][0,i,o['item_id']]=identity;x['prompt_options'][0,i,o['upgrade']]=upgrade;x['prompt_options'][0,i,o['kind_of_item']]=spec.enum_names['PromptItemKind'].index('card');x['prompt_options'][0,i,o['ordinal']]=i
        x['cand'][0,i,c['kind']]=spec.enum_names['RunActionKind'].index('PromptSelect');x['cand'][0,i,c['A']]=i;x['cand'][0,i,c['option_index']]=i;x['cand'][0,i,c['card_id']]=identity;x['cand'][0,i,c['hand_slot']]=-1
    n=len(options);x['cand'][0,n,c['kind']]=spec.enum_names['RunActionKind'].index('PromptConfirm');x['cand_n'][:]=n+1
    return x

def slots(spec,x):return public_hand_slots(spec,x)[0,:int(x['cand_n'][0])].tolist()

def test_full_list_duplicates_order_maps_and_does_not_mutate(specs):
    _,s=specs;x=observation(s,((2,0),(2,0),(3,1)));x['hand'][0,0,s.cols['hand']['cost_current']]=1;x['hand'][0,1,s.cols['hand']['cost_current']]=2
    before={k:v.clone() for k,v in x.items()};assert slots(s,x)==[0,1,2,-1]
    for k,v in before.items():assert torch.equal(v,x[k]),k
    assert x['global_i'][0,s.cols['global_i']['phase']]!=s.enum_names['RunPhase'].index('Prompt')

def test_filtered_unique_and_ambiguous_upgrade_match(specs):
    _,s=specs
    x=observation(s,((2,0),(2,1),(3,0)),((2,1),));assert slots(s,x)==[1,-1]
    x=observation(s,((2,0),(2,0),(3,0)),((2,0),(3,0)));assert slots(s,x)==[-1,2,-1]
    x['hand'][0,0,s.cols['hand']['cost_current']]=99
    assert slots(s,x)==[-1,2,-1]  # cost is public but absent from option identity; never guess it.

def test_full_equal_length_wrong_order_unknown_not_filtered_fallback(specs):
    _,s=specs;x=observation(s,((2,0),(3,0)),((3,0),(2,0)));assert slots(s,x)==[-1,-1,-1]

@pytest.mark.parametrize('bad',['inactive','phase_prompt_inactive','nonhand','noncombat','otherkind','ordinal','foreign','unknown_id','negative_upgrade','handcount','promptcount','candidatecount','extra_hand_padding','impossible_multiplicity'])
def test_unproven_snapshots_unknown(specs,bad):
    _,s=specs;x=observation(s,((2,0),(3,0),(4,0)),((2,0),));g=s.cols['global_i'];h=s.cols['hand'];o=s.cols['prompt_options']
    if bad=='inactive':x['global_i'][0,g['decision_kind']]=s.enum_names['RunPhase'].index('Combat')
    elif bad=='phase_prompt_inactive':x['global_i'][0,g['phase']]=8;x['global_i'][0,g['decision_kind']]=1
    elif bad=='nonhand':x['global_i'][0,g['prompt_pile']]=s.enum_names['PileType'].index('Draw')
    elif bad=='noncombat':x['global_i'][0,g['is_combat']]=0
    elif bad=='otherkind':x['global_i'][0,g['prompt_kind']]=s.enum_names['ChoicePromptKind'].index('HandUpgrade')
    elif bad=='ordinal':x['prompt_options'][0,0,o['ordinal']]=2
    elif bad=='foreign':x['prompt_options'][0,0,o['item_id']]=7
    elif bad=='unknown_id':x['hand'][0,0,h['card_id']]=1;x['prompt_options'][0,0,o['item_id']]=1
    elif bad=='negative_upgrade':x['hand'][0,0,h['upgrade']]=-1
    elif bad=='handcount':x['global_i'][0,g['hand_n']]=11
    elif bad=='promptcount':x['global_i'][0,g['prompt_option_n']]=11
    elif bad=='candidatecount':x['cand_n'][:]=17
    elif bad=='extra_hand_padding':x['hand'][0,4,h['card_id']]=8
    else:x=observation(s,((2,0),(3,0),(4,0)),((2,0),(2,0)))
    assert (public_hand_slots(s,x)==-1).all()

def test_native_true_handcount_cap_allowed_candidate_clipped_cap_unknown(specs):
    _,s=specs;hand=tuple((2+i,0) for i in range(10));x=observation(s,hand)
    assert slots(s,x)==list(range(10))+[-1] # true hand_n=10, not clamped.
    x=observation(s,rows=192);x['cand_n'][:]=192
    assert (public_hand_slots(s,x)==-1).all() # cand_n=min(nativelegal,192), cannot prove no truncation.

def test_candidate_rows_index_bounds_card_identity_and_padding(specs):
    _,s=specs;x=observation(s);c=s.cols['cand'];x['cand'][0,8]=x['cand'][0,0]
    assert public_hand_slots(s,x)[0,8]==-1
    x['cand'][0,0,c['A']]=2;assert public_hand_slots(s,x)[0,0]==-1
    x['cand'][0,1,c['card_id']]=7;assert public_hand_slots(s,x)[0,1]==-1
    x['cand'][0,2,c['option_index']]=99;assert public_hand_slots(s,x)[0,2]==-1

def test_default_old_mode_weights_outputs_rng_and_later_aux_exact(specs,original):
    old,new=specs
    torch.manual_seed(104);base=original.Policy(old,**ARCH);head=TransitionHead(384,192);rng=torch.get_rng_state().clone()
    torch.manual_seed(104);off=Policy(old,**ARCH);offhead=TransitionHead(384,192)
    assert torch.equal(rng,torch.get_rng_state()) and off.arch==base.arch and param_count(off)==param_count(base)==2941072
    for k,v in base.state_dict().items():assert torch.equal(v,off.state_dict()[k]),k
    for k,v in head.state_dict().items():assert torch.equal(v,offhead.state_dict()[k])
    x=observation(old);base.eval();off.eval()
    with torch.no_grad():a=base(x,aux=True,return_transition_features=True);b=off(x,aux=True,return_transition_features=True)
    assert torch.equal(a[0],b[0]) and torch.equal(a[1],b[1])
    for k,v in a[2].items():assert torch.equal(v,b[2][k])
    torch.manual_seed(104);on=Policy(new,**ARCH);onhead=TransitionHead(384,192)
    assert torch.equal(rng,torch.get_rng_state()) and param_count(on)==param_count(base)
    for k,v in base.state_dict().items():assert torch.equal(v,on.state_dict()[k])
    assert 'input_arch' not in config_dict(TrainConfig(**ARCH))

def test_hand_feature_routing_sensitive_finite_grad_no_new_parameters(specs):
    old,new=specs;torch.manual_seed(105);m=Policy(new,**ARCH).eval();x=observation(new,((2,0),(2,0)))
    h=new.cols['hand'];x['hand'][0,0,h['cost_current']]=0;x['hand'][0,1,h['cost_current']]=3
    before={k:v.clone() for k,v in x.items()};counts=x['cand_n'].clone()
    seen=[];hook=m.c_num.register_forward_pre_hook(lambda module,args:seen.append(args[0].detach().clone()))
    logits,value,(_,h2)=m(x,return_transition_features=True);hook.remove()
    assert not torch.equal(h2[0,0],h2[0,1])
    assert seen[0][0,:2,2].tolist()==[1.,1.]  # existing hand_ok, not new policy mask.
    assert seen[0][0,2,2]==0
    assert torch.isfinite(logits).all() and torch.isfinite(value).all()
    (logits[0,0]-logits[0,1]+value.sum()).backward()
    assert m.c_hand.weight.grad is not None and torch.isfinite(m.c_hand.weight.grad).all()
    assert m.card_mlp.l1.weight.grad is not None and torch.isfinite(m.card_mlp.l1.weight.grad).all()
    for k,v in before.items():assert torch.equal(v,x[k]),k
    assert torch.equal(counts,x['cand_n']) and (logits[0,int(counts[0]):]==-1e9).all()

def test_strict_modelspec_and_checkpoint_identity_roundtrip(specs,tmp_path):
    old,new=specs
    with pytest.raises(SpecMismatch):check_spec_compat(old,new)
    with pytest.raises(SpecMismatch):check_spec_compat(new,old)
    m=Policy(new,**ARCH);cfg=TrainConfig(**ARCH,input_arch=PUBLIC_HAND_INPUT_ARCH);p=tmp_path/'pointer.pt';save_checkpoint(p,m,new,cfg,{})
    model,ms,raw=load_checkpoint(p,library=new);assert ms.input_arch==model.arch['input_arch']==PUBLIC_HAND_INPUT_ARCH
    for k,v in m.state_dict().items():assert torch.equal(v,model.state_dict()[k])
    for field in ('spec','arch','config'):
        bad=copy.deepcopy(raw);bad[field].pop('input_arch')
        with pytest.raises(ValueError):validate_input_checkpoint(bad)
    with pytest.raises(ValueError):restore_checkpoint(raw,Policy(old,**ARCH),None,None,'cpu')
    with pytest.raises(ValueError):save_checkpoint(tmp_path/'bad.pt',m,new,TrainConfig(**ARCH),{})
    off=Policy(old,**ARCH);save_checkpoint(tmp_path/'off.pt',off,old,TrainConfig(**ARCH),{});_,_,legacy=load_checkpoint(tmp_path/'off.pt',library=old)
    assert validate_input_checkpoint(legacy)==REWARD_INPUT_ARCH
    with pytest.raises(ValueError):restore_checkpoint(legacy,m,None,None,'cpu')

@pytest.mark.parametrize('marker',[None,False,1,'bad'])
def test_unknown_input_arch_rejects(schema,marker):
    with pytest.raises(ValueError):ModelSpec.from_schema(schema,input_arch=marker)


def test_public_cost_sensitivity_is_direct_hand_route_not_global_state(specs):
    old,new=specs
    torch.manual_seed(106);on=Policy(new,**ARCH).eval()
    off=Policy(old,**ARCH).eval();off.load_state_dict(on.state_dict(),strict=True)
    with torch.no_grad():on.c_state.weight.zero_();off.c_state.weight.zero_()
    x=observation(new,((2,0),(3,0)));y={k:v.clone() for k,v in x.items()}
    y['hand'][0,0,new.cols['hand']['cost_current']]=5
    with torch.no_grad():
        a=on(x,return_transition_features=True)[2][1];b=on(y,return_transition_features=True)[2][1]
        c=off(x,return_transition_features=True)[2][1];d=off(y,return_transition_features=True)[2][1]
    assert not torch.equal(a[0,0],b[0,0])
    assert torch.equal(c,d) # source prompt/card embedding unchanged, state route disabled only for this probe.


def test_actual_train_resume_compat_expression_uses_config(specs,schema,tmp_path,monkeypatch):
    import ast,inspect
    import sts2train.ppo as ppo
    old,new=specs;m=Policy(new,**ARCH);cfg=TrainConfig(**ARCH,input_arch=PUBLIC_HAND_INPUT_ARCH)
    path=tmp_path/'resume.pt';save_checkpoint(path,m,new,cfg,{})
    _,_,resume=load_checkpoint(path)
    monkeypatch.setattr(ppo,'load',lambda:object());monkeypatch.setattr(ppo,'schema_of',lambda _:schema)
    tree=ast.parse(inspect.getsource(ppo.train))
    node=next(n for n in ast.walk(tree) if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='compat' for t in n.targets))
    expression=compile(ast.Expression(node.value),'<actual-train-resume-compat>','eval')
    scope=dict(resume=resume,cfg=cfg,log=lambda _:None,resume_compat=ppo.resume_compat)
    assert eval(expression,scope)['model_inputs']=='identical'
    scope['cfg']=TrainConfig(**ARCH)
    with pytest.raises(ValueError):eval(expression,scope)
    # Baseline actual call still accepts its original default semantics.
    save_checkpoint(path,Policy(old,**ARCH),old,TrainConfig(**ARCH),{})
    scope['resume']=load_checkpoint(path)[2]
    assert eval(expression,scope)['model_inputs']=='identical'


def test_evaluate_consumers_use_validated_input_marker(specs,schema,tmp_path,monkeypatch):
    import ast,inspect
    import sts2train.evaluate as ev
    import sts2train.ppo as ppo
    _,new=specs;m=Policy(new,**ARCH);cfg=TrainConfig(**ARCH,input_arch=PUBLIC_HAND_INPUT_ARCH)
    path=tmp_path/'eval.pt';save_checkpoint(path,m,new,cfg,{})
    # Execute the exact checkpoint-load expression used by evaluate(), not an
    # independent mock loader; no native/env/seedset is accessed.
    tree=ast.parse(inspect.getsource(ev.evaluate))
    call=next(n for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='load_checkpoint')
    loaded=eval(compile(ast.Expression(call),'<actual-evaluate-load>','eval'),dict(load_checkpoint=ppo.load_checkpoint,checkpoint=path,device='cpu',schema_of=lambda _:schema,load=lambda:None))
    assert loaded[1].input_arch==PUBLIC_HAND_INPUT_ARCH
    # Run actual evaluate_model until its completed compatibility check, then
    # stop before any seedset read or environment construction.
    import sts2vec
    monkeypatch.setattr(sts2vec,'load',lambda:None);monkeypatch.setattr(sts2vec,'schema_of',lambda _:schema)
    class Checked(Exception):pass
    original=ev.check_spec_compat
    def stop(a,b,**kw):
        result=original(a,b,**kw);assert b.input_arch==PUBLIC_HAND_INPUT_ARCH
        raise Checked
    monkeypatch.setattr(ev,'check_spec_compat',stop)
    with pytest.raises(Checked):ev.evaluate_model(m,new,'never-read','never-created',weights_hash='test')


def test_out_of_vocabulary_identity_is_unknown(specs):
    _,s=specs;x=observation(s,((s.vocab['cards'],0),))
    assert slots(s,x)==[-1,-1]
