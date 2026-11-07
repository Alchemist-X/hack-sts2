"""Task116 typed M final-dev boundary only; no model/native/SDK imports."""
import hashlib,json
MODEL={'d':384,'s':768,'depth':6,'heads':8,'ff_mult':4,'aux_heads':True,'deck_counts':True,'critic_arch':'state_legal_candidates_detached_mean_v1'}
BASE_PARAMS=16253584;HEAD_PARAMS=74052
MODEL_SPEC_SHA='63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
PROFILE={'modal_research/transition_contract.py':'cebd63e6468138637b076e431242d4fe54b1adec2fbb0f71e6681d02d8684686','sts2train/transition_gpu_gate.py':'663a19669c7f34583a279cb44e1029055db437ed020225eb83e6cc4420589b8b'}
PPO={'acq_deck_coef':.003,'acq_relic_coef':.01,'acq_shaping':False,'acq_upgrade_coef':.004,'amp':False,'aux_bootstrap':True,'aux_coef':.1,'clip':.2,'compile':'off','cycle_cost':.01,'ent_coef':.02,'epochs':1,'floor_step_cap':80,'gamma':1.,'grad_ckpt':False,'lam':.99,'lr':.0003,'max_grad_norm':.5,'max_rows':192,'minibatch':4096,'objective':'protocol','step_cap':1200,'tf32':False,'transition_coef':.1,'vf_coef':.5,'win_bonus':1.}
def validate_spec(spec):
 if spec['model_config']!=MODEL or spec['actual_param_count']!=BASE_PARAMS or spec['ppo_config']!=PPO or spec['rollout_shape']!={'envs':2048,'rollout':64}or spec['input_arch']!='reward_choices_public_handselect_v1':raise ValueError('exact current-recipe M required; S checkpoint is not M')
 if not isinstance(spec['env_seed_namespace'],str)or not spec['env_seed_namespace'].startswith('MODA10PH116')or spec['env_seed_namespace']=='MODA10PH104FRESH1':raise ValueError('fresh independent M namespace116 only')
 if spec['parent_id']is not None or spec['cumulative_decision_target']!=100007936 or spec['training_protocol']['initialization']!='random':raise ValueError('fresh116M recipe provenance only')
 return spec

def validate_checkpoint_profile(ck):
 if ck.get('params')!=BASE_PARAMS or any(ck.get('config',{}).get(k)!=v for k,v in MODEL.items())or any(ck.get('arch',{}).get(k)!=v for k,v in MODEL.items()):raise ValueError('exact M actor/config required; no S or other width checkpoint')
 return True
