"""Mandatory first actual CUDA4096 gate for strict Task089 worker training.

CPU diagnostic helpers are not GPU receipts. No optimizer step, environment, provider or science choice.
"""
import hashlib
import json
import math
import os
from pathlib import Path
import time
import numpy as np
import torch

VERSION='task089-cuda4096-pre-adam0-v1'
KIND='actual_cuda_first4096_pre_adam0'
KEY='first_transition_gate'
FILENAME='first-transition-gpu-gate.json'
CONTEXT_KEYS={'experiment_id','config_hash','code_commit','engine_hash','dependency_hash','schema_hash','first_job_key'}


def required(cfg,formal_gate):return bool(getattr(cfg,'transition_coef',0)) and formal_gate


def validate_context(context):
    if not isinstance(context,dict) or set(context)!=CONTEXT_KEYS:
        raise ValueError('transition GPU gate requires exact public job/source context')
    for key,value in context.items():
        if not isinstance(value,str) or not value or len(value)>192:
            raise ValueError('invalid transition GPU gate context')
    for key in ('config_hash','engine_hash','dependency_hash','schema_hash'):
        if len(context[key])!=64 or any(c not in '0123456789abcdef' for c in context[key]):
            raise ValueError('invalid transition GPU gate context digest')
    if len(context['code_commit'])!=40 or any(c not in '0123456789abcdef' for c in context['code_commit']):
        raise ValueError('invalid transition GPU gate source commit')


def validate_config(cfg):
    if (cfg.envs,cfg.rollout,cfg.minibatch,cfg.epochs,cfg.device,cfg.compile,cfg.amp,cfg.tf32,
            cfg.start_mode,cfg.bootstrap_time_limits,cfg.transition_coef) != (
            2048,64,4096,1,'cuda','off',False,False,'stream',True,.1):
        raise ValueError('transition GPU gate requires frozen CUDA FP324096 recipe')


def validate_receipt(report,expected_context=None):
    """A structural check under exact worker/checkpoint hashes, not an independent hardware attestation."""
    if not isinstance(report,dict):raise ValueError('transition GPU gate receipt absent')
    expected={'version','context','kind','passed','rollout_health','runtime','optimizer_steps_before',
        'head_optimizer_steps_before','sample_indices_sha256','base_weights_before_sha256','head_weights_before_sha256',
        'gate_source_sha256','p95_abs_delta_logp','p99_abs_delta_logp','min_signed_delta_logp','max_signed_delta_logp',
        'positive_deltas','negative_deltas','zero_deltas','samples','finite','max_abs_delta_logp','mean_approx_kl',
        'max_abs_delta_limit','mean_approx_kl_limit','learner_train_mode','learner_grad_enabled','bf16_autocast',
        'same_candidate_mask','finite_transition_prediction','actor_dtype','transition_prediction_dtype',
        'finite_loss','finite_gradients','base_grad_norm','head_grad_norm','combined_grad_norm','loss','loss_dtype',
        'learner_forward_calls','base_gradient_tensors','head_gradient_tensors','transition_valid_per_channel'}
    if set(report)!=expected:raise ValueError('transition GPU gate public fields differ')
    if (report.get('version')!=VERSION or report.get('kind')!=KIND or report.get('passed') is not True
            or report.get('samples')!=4096 or report.get('optimizer_steps_before')!=0
            or report.get('head_optimizer_steps_before')!=0 or report.get('finite') is not True
            or report.get('finite_gradients') is not True or report.get('finite_loss') is not True
            or report.get('finite_transition_prediction') is not True
            or report.get('learner_train_mode') is not True or report.get('learner_grad_enabled') is not True
            or report.get('same_candidate_mask') is not True or report.get('bf16_autocast') is not False
            or report.get('max_abs_delta_limit')!=1e-3 or report.get('mean_approx_kl_limit')!=1e-6):
        raise ValueError('transition GPU gate receipt is not a successful actual first learner gate')
    if any(type(report[k]) is not int for k in ('samples','optimizer_steps_before','head_optimizer_steps_before','learner_forward_calls')):
        raise ValueError('transition GPU gate integer witness fields differ')
    for key,limit in [('max_abs_delta_logp',1e-3),('mean_approx_kl',1e-6)]:
        value=report.get(key)
        if type(value) not in (int,float) or not math.isfinite(value) or not 0<=value<=limit:
            raise ValueError('transition GPU gate parity bound differs')
    for key in ('positive_deltas','negative_deltas','zero_deltas'):
        if type(report[key]) is not int or not 0<=report[key]<=4096:
            raise ValueError('transition GPU gate delta counts differ')
    if sum(report[k] for k in ('positive_deltas','negative_deltas','zero_deltas'))!=4096:
        raise ValueError('transition GPU gate delta denominator differs')
    if (type(report['base_gradient_tensors']) is not int or not 0<report['base_gradient_tensors']<1000
            or report['head_gradient_tensors']!=4):raise ValueError('transition GPU gate gradient scope differs')
    counts=report['transition_valid_per_channel']
    if type(counts) is not list or len(counts)!=4 or any(type(n) is not int or not 0<=n<=4096 for n in counts):
        raise ValueError('transition GPU gate channel coverage differs')
    for key in ('p95_abs_delta_logp','p99_abs_delta_logp','min_signed_delta_logp','max_signed_delta_logp'):
        if type(report[key]) not in (int,float) or not math.isfinite(report[key]):
            raise ValueError('transition GPU gate nonfinite delta distribution')
    runtime=report.get('runtime',{})
    if (runtime.get('device_type')!='cuda' or runtime.get('default_dtype')!='torch.float32'
            or runtime.get('float32_matmul_precision')!='highest' or runtime.get('cuda_matmul_tf32') is not False
            or runtime.get('cudnn_tf32') is not False or runtime.get('ambient_cuda_autocast') is not False
            or not isinstance(runtime.get('device_name'),str) or not runtime['device_name']):
        raise ValueError('transition GPU gate runtime is not strict actual CUDA FP32')
    for key in ('sample_indices_sha256','base_weights_before_sha256','head_weights_before_sha256','gate_source_sha256'):
        value=report.get(key)
        if not isinstance(value,str) or len(value)!=64 or any(c not in '0123456789abcdef' for c in value):
            raise ValueError('transition GPU gate provenance digest absent')
    if report['gate_source_sha256']!=hashlib.sha256(Path(__file__).read_bytes()).hexdigest():
        raise ValueError('transition GPU gate source witness differs')
    validate_context(report.get('context'))
    if expected_context is not None:
        validate_context(expected_context)
        if any(report['context'][k]!=v for k,v in expected_context.items() if k!='first_job_key'):
            raise ValueError('transition GPU gate belongs to another config/source')
    for key in ('base_grad_norm','head_grad_norm','combined_grad_norm','loss'):
        value=report.get(key)
        if type(value) not in (int,float) or not math.isfinite(value) or (key!='loss' and value<0):
            raise ValueError('transition GPU gate nonfinite health receipt')
    if any(report.get(k)!='torch.float32' for k in ('actor_dtype','transition_prediction_dtype','loss_dtype')):
        raise ValueError('transition GPU gate learner arithmetic dtype differs')
    if report.get('learner_forward_calls')!=1:raise ValueError('transition GPU gate did not use one learner forward')
    validate_rollout(report.get('rollout_health'))
    return report


def prepare(cfg,strict_resume,resume,context,formal_gate=False):
    if not formal_gate:
        if context is not None:raise ValueError('public formal gate context without mandatory formal gate')
        return None
    if not strict_resume or not required(cfg,formal_gate):
        raise ValueError('formal transition GPU gate requires strict positive training')
    validate_config(cfg);validate_context(context)
    if resume is None:return None
    return validate_receipt(resume.get(KEY),context)


def diagnose(updater,args):
    """Pure learner health/parity core usable on CPU tests, explicitly NOT hardware evidence.

    Reuses the exact learner loss and one forward. autograd.grad leaves .grad and Adam untouched.
    """
    from .ppo import _logp_gate_from_outputs
    head=updater.transition_head
    if head is None:raise ValueError('transition head absent')
    previous=getattr(head,'_gate_observer',None)
    previous_metrics=head.last_metrics
    observations={};calls=[]
    def observer(logp_all,newlogp,value,heads,pred):
        calls.append(1)
        observations.update(_logp_gate_from_outputs(updater.model,args[2],logp_all,newlogp,value,heads,False))
        observations['finite_transition_prediction']=bool(torch.isfinite(pred.detach()).all())
        observations['actor_dtype']=str(logp_all.dtype)
        observations['transition_prediction_dtype']=str(pred.dtype)
    head._gate_observer=observer
    try:
        with torch.enable_grad():
            loss,stats=updater.loss(*args,compiled=False)
            finite_loss=bool(torch.isfinite(loss.detach())) and bool(torch.isfinite(stats.detach()).all())
            if not finite_loss:raise FloatingPointError('transition GPU gate nonfinite learner loss')
            params=list(updater.model.parameters())+list(head.parameters())
            grads=torch.autograd.grad(loss,params,allow_unused=True)
        finite=all(g is None or bool(torch.isfinite(g).all()) for g in grads)
        split=len(list(updater.model.parameters()))
        def norm(gs):
            terms=[g.detach().float().norm() for g in gs if g is not None]
            return float(torch.stack(terms).norm()) if terms else 0.
        bn,hn,gn=norm(grads[:split]),norm(grads[split:]),norm(grads)
        finite=finite and all(math.isfinite(v) for v in (bn,hn,gn))
        result={**observations,'kind':'CPU_or_CUDA_unit_diagnostics_not_hardware_receipt',
                'finite_loss':finite_loss,'finite_gradients':finite,'base_grad_norm':bn,'head_grad_norm':hn,
                'combined_grad_norm':gn,'loss':float(loss.detach()),'loss_dtype':str(loss.dtype),'learner_forward_calls':len(calls),
                'base_gradient_tensors':sum(g is not None for g in grads[:split]),
                'head_gradient_tensors':sum(g is not None for g in grads[split:]),
                'transition_valid_per_channel':head.last_metrics['count'].detach().cpu().tolist()}
        result['passed']=bool(result.get('passed') and result['finite_transition_prediction'] and finite and len(calls)==1)
        return result
    finally:
        head.last_metrics=previous_metrics
        if previous is None:delattr(head,'_gate_observer')
        else:head._gate_observer=previous


def _cuda_runtime(updater,args):
    model,head=updater.model,updater.transition_head
    params=list(model.parameters())+list(head.parameters())
    if not params or any(p.device.type!='cuda' or p.dtype!=torch.float32 for p in params):
        raise ValueError('actual CUDA FP32 parameters required; CPU diagnostics cannot be a GPU gate')
    device=params[0].device
    tensors=list(args[0].values())+list(args[1:])
    if any(not torch.is_tensor(t) or t.device!=device or (t.is_floating_point() and t.dtype!=torch.float32) for t in tensors):
        raise ValueError('actual CUDA learner batch must share the model device')
    from .model import checked_critic_arch, CRITIC_ARCH
    marker = checked_critic_arch(getattr(model, 'critic_arch', ''))
    expected_base_params = 16253584 if marker == CRITIC_ARCH else 16106128
    if (sum(p.numel() for p in model.parameters())!=expected_base_params or sum(p.numel() for p in head.parameters())!=74052):
        raise ValueError('transition GPU gate requires the exact frozen M and new head')
    if not torch.cuda.is_available():raise ValueError('CUDA runtime unavailable')
    facts={'device_type':'cuda','device_index':device.index,'device_name':torch.cuda.get_device_name(device),
        'default_dtype':str(torch.get_default_dtype()),'float32_matmul_precision':torch.get_float32_matmul_precision(),
        'cuda_matmul_tf32':torch.backends.cuda.matmul.allow_tf32,'cudnn_tf32':torch.backends.cudnn.allow_tf32,
        'ambient_cuda_autocast':torch.is_autocast_enabled('cuda'),'torch_version':str(torch.__version__)}
    if (facts['default_dtype']!='torch.float32' or facts['float32_matmul_precision']!='highest'
            or facts['cuda_matmul_tf32'] or facts['cudnn_tf32'] or facts['ambient_cuda_autocast']
            or os.environ.get('TORCH_ALLOW_TF32_CUBLAS_OVERRIDE')=='1'):
        raise ValueError('effective CUDA arithmetic is not strict FP32')
    return facts,device


HEALTH_FIELDS={'rollout_decisions','learning_samples','sim_error','content_gap','illegal',
               'cand_over_max_rows','prompt_over_max_rows','native_candidate_overflow','native_table_overflow'}


def validate_rollout(health):
    if not isinstance(health,dict) or set(health)!=HEALTH_FIELDS or any(type(v) is not int for v in health.values()):
        raise ValueError('transition GPU gate lacks typed rollout health')
    if (health['rollout_decisions']!=2048*64 or health['learning_samples']!=2048*64
            or any(v!=0 for k,v in health.items() if k not in ('rollout_decisions','learning_samples'))):
        raise ValueError('transition GPU gate rollout is incomplete or unhealthy')


def run(updater,args,indices,context,path,rollout_health):
    """Only actual CUDA path can mint this receipt. Persist before giving updater permission to step."""
    from .ppo import optimizer_step
    from .transition_state import weights_hash as state_hash
    from .runlog import atomic_write_json
    validate_context(context)
    indices=np.asarray(indices)
    base={'version':VERSION,'context':dict(context),'kind':'gpu_gate_refused','passed':False,
          'optimizer_steps_before':int(optimizer_step(updater.opt)),
          'head_optimizer_steps_before':int(optimizer_step(updater.transition_opt)),
          'rollout_health':dict(rollout_health) if isinstance(rollout_health,dict) else {}}
    try:
        validate_rollout(rollout_health)
        if (args is None or indices.shape!=(4096,) or indices.dtype.kind not in 'iu' or args[1].numel()!=4096
                or len(np.unique(indices))!=4096 or np.any(indices<0) or np.any(indices>=2048*64)):
            raise ValueError('transition GPU gate needs actual unique first4096 learner indices')
        if (optimizer_step(updater.opt)!=0 or optimizer_step(updater.transition_opt)!=0
                or updater.opt.state or updater.transition_opt.state):
            raise ValueError('transition GPU gate must precede both first Adam steps')
        if any(p.grad is not None for p in list(updater.model.parameters())+list(updater.transition_head.parameters())):
            raise ValueError('transition GPU gate requires untouched initial gradients')
        if not updater.model.training:raise ValueError('transition GPU gate requires learner train mode')
        runtime,device=_cuda_runtime(updater,args)
        base.update(runtime=runtime,kind=KIND,optimizer_steps_before=0,head_optimizer_steps_before=0,
            sample_indices_sha256=hashlib.sha256(indices.astype('<i8',copy=False).tobytes()).hexdigest(),
            base_weights_before_sha256=state_hash(updater.model.state_dict()),
            head_weights_before_sha256=state_hash(updater.transition_head.state_dict()),
            gate_source_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
        with torch.random.fork_rng(devices=[device.index]):
            health=diagnose(updater,args)
        base.update(health);base['kind']=KIND
        if not base.get('passed'):raise RuntimeError('transition GPU gate parity or finite health failed')
        validate_receipt(base,context)
        atomic_write_json(Path(path),base)
        return base
    except Exception as error:
        base['passed']=False
        base['error_code']='NONFINITE' if isinstance(error,FloatingPointError) else 'GATE_REJECTED'
        atomic_write_json(Path(path),base)
        raise
