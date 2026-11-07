"""Task116 typed fresh-M admission/config only; no model/optimizer/native construction."""
import hashlib,json
MODEL={'d':384,'s':768,'depth':6,'heads':8,'ff_mult':4,'aux_heads':True,'deck_counts':True,'critic_arch':'state_legal_candidates_detached_mean_v1'}
BASE_PARAMS=16253584
HEAD_PARAMS=74052
TARGET=100007936
SPEC_SHA='63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
PPO={'acq_deck_coef':.003,'acq_relic_coef':.01,'acq_shaping':False,'acq_upgrade_coef':.004,'amp':False,'aux_bootstrap':True,'aux_coef':.1,'clip':.2,'compile':'off','cycle_cost':.01,'ent_coef':.02,'epochs':1,'floor_step_cap':80,'gamma':1.,'grad_ckpt':False,'lam':.99,'lr':.0003,'max_grad_norm':.5,'max_rows':192,'minibatch':4096,'objective':'protocol','step_cap':1200,'tf32':False,'transition_coef':.1,'vf_coef':.5,'win_bonus':1.}
def validate_spec(spec):
 if spec['model_config']!=MODEL or spec['actual_param_count']!=BASE_PARAMS or spec['ppo_config']!=PPO or spec['rollout_shape']!={'envs':2048,'rollout':64}:raise ValueError('exact current-recipe M required')
 if spec['input_arch']!='reward_choices_public_handselect_v1' or spec['parent_id']is not None or spec['cursor']!=0 or spec['cumulative_decision_target']!=TARGET:raise ValueError('fresh B0 M only; no historical checkpoint resume')
 if spec['training_protocol']!={'action_labels':None,'harvest':0,'initialization':'random','pretrained':None,'rollout_loop_breaker':False,'save_mix':0,'start_mode':'stream','start_saves':None,'teacher':None,'trajectory_data':None}:raise ValueError('fresh autonomous no saves/search/breaker/teacher')
 if not spec['env_seed_namespace'].startswith('MODA10PH116') or spec['env_seed_namespace']=='MODA10PH104FRESH1':raise ValueError('independent116 namespace required')
 return spec

def run_options(spec,output,deadline,now):
 validate_spec(spec);seconds=min(21300,deadline-now-300)
 if seconds<60:raise TimeoutError('insufficient fresh M training/final margin')
 return {**spec['model_config'],**spec['ppo_config'],**spec['rollout_shape'],'out':str(output),'device':'cuda','threads':32,'torch_threads':32,'characters':('SILENT',),'ascension':10,'start_mode':'stream','start_saves':None,'save_mix':0,'seed_stream_prefix':spec['env_seed_namespace'],'init_seed':spec['init_seed'],'sample_seed':spec['policy_seed'],'env_seed':spec['env_seed'],'rng_scheme':'decoupled','input_arch':spec['input_arch'],'bootstrap_time_limits':True,'rollout_loop_breaker':False,'loop_breaker':False,'max_hours':seconds/3600,'max_env_steps':TARGET,'max_updates':0,'eval_every':0,'eval_at':(),'no_best':True,'baseline_steps':0,'ckpt_every':0,'ckpt_every_min':5,'keep_ckpts':3}
