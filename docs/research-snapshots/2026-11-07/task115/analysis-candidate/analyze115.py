"""One terminal115 comparison against frozen114control; no model/native/provider APIs."""
import argparse
from pathlib import Path
from endpoint_validation import sha,read,location,checked,exclusive,endpoint,DEV_SHA
from paired_statistics import paired_deltas,bootstrap_contrasts
from validation import descriptive_pairs
CODE=Path(__file__).resolve().parent
PLAN_SHA='548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb'
BASE_SHA='7f4de995ae7a0434078e73ef96f76ae3b27a77e746054c7b4ef61822ee543a6c'
BASE_STEPS,BASE_UPDATES=495845376,3783
TARGET_STEPS,TARGET_UPDATES=695861248,5309

def terminal_scope(decision,model):
    """Actual accepted stop, never a midpoint or zero-step donor re-evaluation."""
    if decision['science_plan_sha256']!=PLAN_SHA or decision['study_donor_checkpoint_sha256']!=BASE_SHA:raise ValueError('fixed science/donor differs')
    t=decision['terminal_evaluation']
    if decision['actual_terminal_accepted']is not True or set(t)!={'classification','final_stop','further_training_authorized','midpoint_evaluation','nominal_primary_observed'}or t['final_stop']is not True or t['further_training_authorized']is not False or t['midpoint_evaluation']is not False:raise ValueError('accepted terminal/no successor required; midpoint is not terminal')
    s,u=model['decisions'],model['updates']
    if type(s)is not int or type(u)is not int or not BASE_STEPS<s<=TARGET_STEPS or not BASE_UPDATES<u<=TARGET_UPDATES or s-BASE_STEPS!=(u-BASE_UPDATES)*131072:raise ValueError('actual dose must advance own donor by full rollout updates within frozen cap')
    if model['checkpoint_file_sha256']==BASE_SHA:raise ValueError('donor re-evaluation forbidden')
    for k,v in {'checkpoint_sha256':model['checkpoint_file_sha256'],'checkpoint_steps':s,'checkpoint_updates':u}.items():
        if decision.get(k)!=v:raise ValueError('terminal decision/model identity differs')
    full=(s,u)==(TARGET_STEPS,TARGET_UPDATES)
    kind='full200M'if full else'resource_censored'
    if t['classification']!=('full200M_terminal'if full else'resource_censored_terminal')or t['nominal_primary_observed']is not full:raise ValueError('full200M and actual censored scope cannot be relabeled')
    if not full and (decision.get('censor_class')not in ('time','budget','provider','byte_limit','safety')or not isinstance(decision.get('reason'),str)or not decision['reason']):raise ValueError('resource censor reason required; no quality midpoint substitution')
    return {'kind':kind,'full200M_primary_observed':full,'baseline_steps':BASE_STEPS,'actual_terminal_steps':s,'actual_terminal_updates':u,'new_steps':s-BASE_STEPS,'new_updates':u-BASE_UPDATES,'planned_new_steps':200015872,'terminal_decision':decision}

def comparison_scope(scope,receipt):
    return {'planned_200M_primary':receipt if scope['full200M_primary_observed']else None,'resource_censored_actual_dose_descriptive':None if scope['full200M_primary_observed']else receipt,'primary_interpretation':'Full200M paired endpoint comparison'if scope['full200M_primary_observed']else'Planned200M primary unobserved. Actual-dose paired CI is descriptive and cannot judge200M plateau.'}

def main():
    p=argparse.ArgumentParser(description=__doc__)
    for n in ('input-binding','input-binding-sha256','once-authority','once-authority-sha256','research-root','output'):p.add_argument('--'+n,required=True)
    a=p.parse_args();root=Path(a.research_root);out=Path(a.output);claim=out.with_suffix(out.suffix+'.once')
    if out.exists()or claim.exists():raise FileExistsError('prior once evidence; no automatic retry')
    if sha(a.input_binding)!=a.input_binding_sha256 or sha(a.once_authority)!=a.once_authority_sha256:raise ValueError('exact input/authority differs')
    b=read(a.input_binding);auth=read(a.once_authority);pins=CODE/'pins.json'
    if b['status']!='ROOT_BOUND_ACCEPTED_ACTUAL_TERMINAL'or b['science_plan_sha256']!=PLAN_SHA or auth.get('output')!=str(out)or auth.get('allow_once_analysis')is not True or auth.get('input_binding_sha256')!=a.input_binding_sha256 or auth.get('analysis_pins_sha256')!=sha(pins):raise ValueError('root exact terminal once authority required')
    for n,h in read(pins)['files'].items():
        if Path(n).name!=n or sha(CODE/n)!=h:raise ValueError('frozen code differs')
    if sha(root/'task115/science-plan.json')!=PLAN_SHA:raise ValueError('science freeze differs')
    dev=checked(b['dev_manifest'],root)
    if b['dev_manifest']['sha256']!=DEV_SHA or(dev['role'],dev['ascension'],dev['content'])!=('dev',10,'ported_only'):raise ValueError('original dev protocol differs')
    keys=sorted(g['character']+'-'+g['seed']for g in dev['games'])
    if len(keys)!=256 or len(set(keys))!=256 or any(g['character']!='SILENT'for g in dev['games']):raise ValueError('full fixed Silent256 required')
    if sha(CODE/'baseline-binding.json')!=b['baseline_binding_sha256']:raise ValueError('fixed114control baseline differs')
    baseline=read(CODE/'baseline-binding.json')
    for k in ('root_actual_acceptance','accepted_prior_analysis','accepted_prior_analysis_review'):checked(baseline[k],root,parse=False)
    if baseline['actual_model']['checkpoint_file_sha256']!=BASE_SHA:raise ValueError('only114control baseline allowed')
    base_rows,base_summary,base_validation=endpoint(baseline,root,keys,(BASE_STEPS,BASE_UPDATES))
    ref=b['endpoint'];decision=checked(b['terminal_decision'],root)
    if auth.get('terminal_decision_sha256')!=b['terminal_decision']['sha256']:raise ValueError('root terminal disposition differs')
    scope=terminal_scope(decision,ref['actual_model'])
    for role in ('execution_receipt','independent_acceptance'):
        if auth['accepted_'+role+'_sha256']['endpoint']!=ref[role]['sha256']:raise ValueError('root accepted actual endpoint proof differs')
    rows,summary,validation=endpoint(ref,root,keys,(ref['actual_model']['decisions'],ref['actual_model']['updates']))
    for role in ('ready','preflight'):
        meta=checked(ref[role],root)
        if meta.get('science_plan_sha256')!=PLAN_SHA or meta.get('root_terminal_decision_sha256')!=b['terminal_decision']['sha256']or meta.get('terminal_evaluation')!=decision['terminal_evaluation']:raise ValueError('formal evaluation did not bind this same final-only disposition')
        if role=='preflight'and(meta.get('arm')!='control_continuation'or meta.get('strategic_forward_kl',{}).get('beta')!=0):raise ValueError('unchanged beta0 continuation evaluation required')
    pairs={'endpoint_minus_baseline':paired_deltas(rows,base_rows,keys)}
    import numpy
    exclusive(claim,{'input_binding_sha256':a.input_binding_sha256,'authority_sha256':a.once_authority_sha256,'analysis_pins_sha256':sha(pins),'seed':115114,'one_shared_index':True,'actual_scope':scope['kind']})
    try:
        receipt=bootstrap_contrasts(pairs)
        result={'schema':'task115-one-terminal-paired-result-v1','status':'completed','science_plan_sha256':PLAN_SHA,'input_binding_sha256':a.input_binding_sha256,'authority_sha256':a.once_authority_sha256,'analysis_pins_sha256':sha(pins),'actual_dose_scope':scope,'endpoint':summary,'endpoint_validation':validation,'baseline114control':base_summary,'baseline_validation':base_validation,**comparison_scope(scope,receipt),'bootstrap_receipt':receipt,'paired_descriptive':descriptive_pairs(rows,base_rows,keys),'limits':['One own lineage and training replicate; exposeddev episodeCI excludes trainingseed variance.','No100M midpoint quality evaluation. Resource-censored actualdose cannot judge planned200M outcome.','Fresh-resume boundaries retained; no automaticextension, blind or goalproof.','Original formal export does not establish all native observation flags zero.'],'model_native_provider_calls':0}
        exclusive(out,result)
    except Exception as error:
        exclusive(out,{'status':'failed_after_once_claim_no_retry','error_type':type(error).__name__,'input_binding_sha256':a.input_binding_sha256});raise
    print({'status':'completed','output':str(out),'sha256':sha(out)})

if __name__=='__main__':main()
