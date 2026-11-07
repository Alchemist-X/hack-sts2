"""Task089 optional training-only state and diagnostics. No evaluator or provider authority."""
import hashlib
import copy
import json
import math
import numpy as np
import torch
from .transition_aux import TransitionHead, CHANNELS, SCALES, COEFFICIENT, masked_loss

VERSION = 'task089-public-nextframe-v1'


def contract(spec):
    return dict(version=VERSION, channels=list(CHANNELS), scales=SCALES.tolist(), coefficient=COEFFICIENT,
                seed=89, hidden_width=64, loss='SmoothL1-beta1-perchannel-mean-div4',
                mask='same-combat-public-proxy-v1-owned-terminal-hp-only', encoder_version=spec.encoder_version,
                spec_sha256=hashlib.sha256(json.dumps(spec.to_json(),sort_keys=True,separators=(',',':')).encode()).hexdigest())


def create(spec, model, cfg, device):
    coef = float(getattr(cfg, 'transition_coef', 0.))
    if coef == 0:
        return None, None
    if coef != COEFFICIENT or getattr(cfg,'compile','off') != 'off' or cfg.amp or getattr(cfg,'tf32',False) or cfg.epochs != 1:
        raise ValueError('Task089 supports only fixed .1 eager FP32 epochs1 candidate')
    head=TransitionHead(model.s,model.d,seed=89).to(device)
    opt=torch.optim.Adam(head.parameters(),lr=cfg.lr,eps=1e-5)
    head.contract=contract(spec)
    head.last_metrics={}
    return head,opt


def weights_hash(state):
    digest=hashlib.sha256()
    for key,value in sorted(state.items()):
        value=value.detach().cpu().contiguous()
        digest.update(key.encode());digest.update(str(value.dtype).encode())
        digest.update(str(tuple(value.shape)).encode());digest.update(value.numpy().tobytes())
    return digest.hexdigest()


def sync_optimizer(base,opt):
    if len(base.param_groups)!=1 or len(opt.param_groups)!=1:
        raise ValueError('Task089 expects one base and one head optimizer group')
    for key,value in base.param_groups[0].items():
        if key!='params':opt.param_groups[0][key]=value


def pack(head,opt,counts):
    if head is None:return None
    return dict(contract=head.contract,head_weights_sha256=weights_hash(head.state_dict()),head={k:v.detach().cpu().clone() for k,v in head.state_dict().items()},
                optimizer=copy.deepcopy(opt.state_dict()),counts=copy.deepcopy(counts))


def validate_adam_state(saved,head,updates,base_optimizer):
    """Strict new-head Adam contract, checked before loading any partial optimizer state."""
    if not isinstance(saved,dict) or set(saved)!={'state','param_groups'}:
        raise ValueError('Task089 malformed head Adam state')
    groups=saved['param_groups'];states=saved['state'];params=list(head.parameters())
    if not isinstance(groups,list) or len(groups)!=1 or not isinstance(states,dict):
        raise ValueError('Task089 malformed head Adam group')
    group=groups[0];ids=group.get('params')
    if not isinstance(ids,list) or len(ids)!=len(params) or len(set(ids))!=len(ids):
        raise ValueError('Task089 malformed head Adam parameter identity')
    if updates==0:
        if states:raise ValueError('Task089 initial checkpoint has advanced head Adam state')
        return
    if updates<0 or set(states)!=set(ids):
        raise ValueError('Task089 checkpoint missing initialized optimizer moments')
    required={'step','exp_avg','exp_avg_sq'} | ({'max_exp_avg_sq'} if group.get('amsgrad',False) else set())
    steps=[]
    for key,param in zip(ids,params):
        state=states[key]
        if not isinstance(state,dict) or set(state)!=required:
            raise ValueError('Task089 head Adam moment fields differ')
        step=state['step']
        if (not torch.is_tensor(step) or step.shape!=torch.Size([]) or step.dtype!=torch.float32
                or not bool(torch.isfinite(step)) or float(step)<=0 or not float(step).is_integer()):
            raise ValueError('Task089 invalid head Adam step')
        steps.append(float(step))
        for name in required-{'step'}:
            moment=state[name]
            if (not torch.is_tensor(moment) or moment.shape!=param.shape or moment.dtype!=param.dtype
                    or not bool(torch.isfinite(moment).all())):
                raise ValueError('Task089 invalid head Adam moment shape dtype or value')
            if name in ('exp_avg_sq','max_exp_avg_sq') and not bool((moment>=0).all()):
                raise ValueError('Task089 negative head Adam squared moment')
        if group.get('amsgrad',False) and not bool((state['max_exp_avg_sq']>=state['exp_avg_sq']).all()):
            raise ValueError('Task089 inconsistent head Adam maximum moment')
    if len(set(steps))!=1:
        raise ValueError('Task089 head Adam steps are not synchronized')
    if steps[0] < updates:
        raise ValueError('Task089 head Adam step precedes completed updates')
    base_groups=base_optimizer.get('param_groups',[]) if isinstance(base_optimizer,dict) else []
    base_states=base_optimizer.get('state',{}) if isinstance(base_optimizer,dict) else {}
    if len(base_groups)!=1 or not base_states or bool(base_groups[0].get('amsgrad',False))!=bool(group.get('amsgrad',False)):
        raise ValueError('Task089 head/base Adam contract differs')
    base_steps=[]
    for state in base_states.values():
        value=state.get('step') if isinstance(state,dict) else None
        if (not torch.is_tensor(value) or value.shape!=torch.Size([]) or not bool(torch.isfinite(value))
                or float(value)<=0 or not float(value).is_integer()):
            raise ValueError('Task089 base Adam step unavailable')
        base_steps.append(float(value))
    if max(base_steps)!=steps[0]:
        raise ValueError('Task089 head/base Adam step differs')


def restore(ck,head,opt,counts):
    saved=ck.get('transition_aux')
    oldcoef=float((ck.get('config') or {}).get('transition_coef',0.))
    if head is None:
        if saved is not None or oldcoef != 0:raise ValueError('Task089 positive checkpoint cannot resume disabled')
        return counts
    if oldcoef != COEFFICIENT or not isinstance(saved,dict) or saved.get('contract')!=head.contract:
        raise ValueError('Task089 resume requires exact existing transition contract')
    if not saved.get('optimizer') or not isinstance(saved.get('counts'),dict):
        raise ValueError('Task089 checkpoint missing head optimizer or cumulative counts')
    validate_adam_state(saved['optimizer'],head,int(ck.get('updates',0)),ck.get('optimizer'))
    if weights_hash(saved['head'])!=saved.get('head_weights_sha256'):
        raise ValueError('Task089 head weight hash differs')
    head.load_state_dict(saved['head'],strict=True)
    opt.load_state_dict(saved['optimizer'])
    for value in head.state_dict().values():
        if not torch.isfinite(value).all():raise ValueError('nonfinite Task089 head checkpoint')
    for state in opt.state.values():
        for value in state.values():
            if torch.is_tensor(value) and not torch.isfinite(value).all():
                raise ValueError('nonfinite Task089 head optimizer checkpoint')
    if int(ck.get('updates',0)) > 0 and len(opt.state) != len(list(head.parameters())):
        raise ValueError('Task089 positive checkpoint missing initialized optimizer moments')
    return copy.deepcopy(saved['counts'])


def prediction_metrics(pred,target,mask):
    with torch.no_grad():
        mask=mask.bool();safe=torch.where(mask,target,torch.zeros_like(target));n=mask.sum(0)
        denom=n.clamp_min(1)
        return {'count':n.detach(),'mae':((pred-safe).abs()*mask).sum(0)/denom,
                'zero_baseline_mae':(safe.abs()*mask).sum(0)/denom,
                'loss':masked_loss(pred,target,mask).detach()}


def add_counts(total, row):
    """Bounded fixed-shape numeric tree; never seed/runkey/observation data."""
    for key,value in row.items():
        if isinstance(value,dict):add_counts(total.setdefault(key,{}),value)
        elif isinstance(value,list):
            dst=total.setdefault(key,[0]*len(value))
            if len(dst)!=len(value):raise ValueError('transition metric shape changed')
            total[key]=[a+int(b) for a,b in zip(dst,value)]
        else:total[key]=total.get(key,0)+int(value)
    return total


def coverage(spec,before,actions,y,mask,counts):
    result=dict(counts)
    result['samples']=len(actions)
    result['nonzero_per_channel']=((y!=0)&mask).sum(0).tolist()
    result['by_action_kind']={}
    names=spec.enum_names['RunActionKind'];col=spec.cols['cand']['kind']
    for name in ('PlayCard','UsePotion','EndTurn'):
        index=names.index(name)
        if hasattr(before,'selected_kind'):
            selected=before.selected_kind==index
        else:
            selected=np.array([0<=a<len(before.cand[i]) and a<before.cand_n[i]
                               and before.cand[i,a,col]==index for i,a in enumerate(actions)])
        result['by_action_kind'][name]={'samples':int(selected.sum()),
            'valid_per_channel':mask[selected].sum(0).tolist(),
            'nonzero_per_channel':((y[selected]!=0)&mask[selected]).sum(0).tolist()}
    return result


def summarize_rows(rows):
    """Weight per-minibatch MAE by valid count; no misleading average of sparse means."""
    if not rows:return {}
    counts=torch.stack([r['count'] for r in rows]).sum(0)
    den=counts.clamp_min(1)
    mae=sum(r['mae']*r['count'] for r in rows)/den
    zero=sum(r['zero_baseline_mae']*r['count'] for r in rows)/den
    return {'timing':'first_exposure_minibatch_pre_update_epochs1_not_rollout_holdout',
            'learner_label_count':counts.cpu().tolist(),'mae':mae.cpu().tolist(),
            'zero_baseline_mae':zero.cpu().tolist(),
            'loss_minibatch_mean':float(torch.stack([r['loss'] for r in rows]).mean().cpu())}


def gradient_scope(model,head,base_loss,transition_loss):
    """Bounded diagnostic only, never called by runtime training loop."""
    mp=list(model.parameters());hp=list(head.parameters());params=mp+hp
    bg=torch.autograd.grad(base_loss,params,retain_graph=True,allow_unused=True)
    ag=torch.autograd.grad(transition_loss,params,retain_graph=True,allow_unused=True)
    norm=lambda gs:float(torch.stack([g.float().square().sum() for g in gs if g is not None]).sum().sqrt()) if any(g is not None for g in gs) else 0.
    dot=sum(float((a*b).sum()) for a,b in zip(bg[:len(mp)],ag[:len(mp)]) if a is not None and b is not None)
    bn,an=norm(bg[:len(mp)]),norm(ag[:len(mp)])
    hn=norm(ag[len(mp):]);combined=math.sqrt(max(0.,bn*bn+an*an+hn*hn+2*dot))
    return {'base_model_grad_norm':bn,'transition_model_grad_norm':an,
            'transition_head_grad_norm':hn,'combined_grad_norm':combined,
            'base_clip_scale_at_0p5':min(1.,.5/bn) if bn else 1.,
            'combined_clip_scale_at_0p5':min(1.,.5/combined) if combined else 1.,
            'base_head_grad_norm':norm(bg[len(mp):]),
            'shared_gradient_cosine':dot/(bn*an) if bn*an else None}
