"""Two terminal synthetic fixtures; metadata_reader injected, no Torch/body/native/provider access."""
import copy,json,tempfile,unittest
from pathlib import Path
import analyze_training as a

class Tests(unittest.TestCase):
 def fixture(self,p,normal,local_updates,missing_tail=0):
  release=json.loads((Path(__file__).resolve().parents[1]/'candidate/release.segment1.DISABLED-DRAFT.json').read_text());start=3783;n0=495845376;parent=release['parent'];lo,hi=release['segment']['seed_range']
  guards=[dict(id=role,role=role,sha256=release['ab_content_sha256']if role=='ab'else'opaque-'+role,games=count)for role,count in [('dev',256),('blind',1000),('ab',128)]]
  prior=dict(actual_accepted=True,checkpoint_sha256=parent['model_sha256'],updates=start,env_steps=n0,base_adam={'per_state_steps':{'0':121056,'1':121056},'initialized_states':2,'parameter_slots':3},aux_adam={'per_state_steps':{'0':121056},'initialized_states':1,'parameter_slots':1},required_guard_sets=guards,required_guard_roles=['dev','blind','ab'])
  marker=dict(method=a.METHOD,beta=0.,phase_weights={'Map':.5,'RestSite':.5},direction='KL(Ulegal||policy)',old_global_entropy_unchanged=.02)
  rows=[]
  for i in range(1,local_updates-missing_tail+1):
   phase=dict(frames=2,KL_sum=1.,normalized_entropy_sum=1.,near_deterministic_count=1)
   rows.append(dict(update=start+i,env_steps=n0+i*a.ROLLOUT,env_steps_update=a.ROLLOUT,session_update=i,learning_samples_update=a.ROLLOUT,episodes=1,wins=0,starts_update=1,strategic_forward_kl=dict(marker,minibatches=32,phases={'Map':phase,'RestSite':phase}),critic_diagnostics={'status':'ok','act':{'1':{'n':a.ROLLOUT},'2':{'n':0},'3':{'n':0}}},**{k:0 for k in a.HEALTH},**{k:1. for k in('t_env_s','t_infer_s','t_update_s','t_ckpt_s','t_total_s')}))
  u=start+local_updates;steps=n0+local_updates*a.ROLLOUT;name='final.pt'if normal else f'ckpt-{u:07d}.pt';(p/name).write_bytes(b'synthetic-not-a-model')
  latest=dict(checkpoint=f'checkpoints/ckpt-{u:07d}.pt',sha256=a.sha(p/name),bytes=(p/name).stat().st_size,parent_model_sha256=parent['model_sha256'])
  proof=dict(parent_model_sha256=parent['model_sha256'],strict_resume=True,fresh_episodes=True,old_inflight_restored=False,strategic_kl_beta=0.,segment_index=1,requested_env_steps=595853312,parent_next=parent['cursor'],parent_issued=parent['issued'],base_max_adam_step=121056,aux_adam_step=121056,new_seed_begin=lo,new_seed_end=hi,isolation={'checked_sets':guards,'roles':['dev','blind','ab']})
  config=dict(strategic_forward_kl=marker,sessions=[dict(update_start=start,adam_step=121056,dropped_at_start=2048,rng_restored={k:'ok'for k in('numpy','torch','cuda')})])
  def write(n,v): (p/n).write_text(json.dumps(v))
  write('latest.json',latest);write('config.json',config);write('resume-proof.json',proof);(p/'metrics.jsonl').write_text(''.join(json.dumps(r)+'\n'for r in rows))
  if normal:
   write('final.json',dict(final_sha256=latest['sha256'],latest=latest,result={'env_steps':steps,'updates':u},dose_complete=local_updates==763))
   for n in('episodes.jsonl','cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl'):(p/n).write_text('')
   for n in('status.json','summary.json'):write(n,{})
  manifest=dict(call_terminal=True,segment_index=1,call_id='synthetic-call',attempt='synthetic-attempt',release_sha256=__import__('hashlib').sha256(a.canonical(release)).hexdigest(),learning_overlay=a.OVERLAY,files={f.name:{'bytes':f.stat().st_size,'sha256':a.sha(f)}for f in p.iterdir()})
  selected=dict(root_selected=True,same_attempt=True,call_id='synthetic-call',attempt='synthetic-attempt',selected_file=name,selected_sha256=latest['sha256'],normal_final_present=normal,kind='normal_final'if normal else'sole_atomic_latest')
  def opt(states,slots):return dict(initialized_states=states,parameter_slots=slots,per_state_steps={str(i):121056+local_updates*32 for i in range(states)},states_without_step=0,min=121056+local_updates*32,max=121056+local_updates*32)
  cfg=dict(release['spec']['ppo_config'],init_seed=1457924769,sample_seed=759982036,env_seed=818190218,input_arch='reward_choices_public_handselect_v1',critic_arch='state_legal_candidates_detached_mean_v1')
  meta=dict(env_steps=steps,updates=u,strategy=marker,spec_sha256=a.SPEC_SHA,recipe_config=cfg,rng_present={k:True for k in('numpy','torch','cuda')},seed_stream={'prefix':'MODA10PH104FRESH1','next':lo+100,'issued_until':lo+200},inflight_slots=2048,base_adam=opt(2,3),aux_adam=opt(1,1))
  return manifest,release,prior,selected,meta
 def test_normal_segment1_full_and_dynamic_segment2_context(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,parent,s,meta=self.fixture(p,True,763);calls=[]
   out=a.analyze(p,m,r,parent,s,metadata_reader=lambda path:(calls.append(path)or meta));self.assertEqual(len(calls),1);self.assertTrue(out['coverage']['normal_full_closed_log']);self.assertEqual(out['optimizer_validation']['expected_exact_delta'],24416);self.assertEqual(out['optimizer_validation']['optimizers']['base_adam']['uninitialized_slots_not_fabricated'],1);self.assertEqual(out['windows']['last100']['observed_updates'],100)
   uniform=copy.deepcopy(parent)
   for key in('base_adam','aux_adam'):uniform[key].update(per_state_steps=None,uniform_initialized_step=121056)
   rows=[json.loads(x)for x in(p/'metrics.jsonl').read_text().splitlines()]
   actual=a.optimizer_validation(meta,uniform,rows,True);self.assertEqual(actual['optimizers']['base_adam']['expected_uniform_initialized_step'],145472);self.assertEqual(actual['optimizers']['base_adam']['identity_map_validation'],'unknown_parent_state_IDs')
   r2=copy.deepcopy(r);r2['segment'].update(index=2,target_env_steps=695861248,target_updates=5309);r2['parent'].update(model_sha256='2'*64,decisions=595853312,updates=4546,adam_step=145472);p2=copy.deepcopy(parent);p2.update(checkpoint_sha256='2'*64,env_steps=595853312,updates=4546)
   self.assertEqual(a.segment_context(r2,p2)['start_u'],4546)
 def test_hard_latest_missing_callback_proof_unknown_and_no_donor_fallback(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,parent,s,meta=self.fixture(p,False,102,missing_tail=1);del m['files']['resume-proof.json'];(p/'resume-proof.json').unlink();config=json.loads((p/'config.json').read_text());config['sessions']=[];(p/'config.json').write_text(json.dumps(config));m['files']['config.json']={'bytes':(p/'config.json').stat().st_size,'sha256':a.sha(p/'config.json')};calls=[]
   out=a.analyze(p,m,r,parent,s,metadata_reader=lambda path:(calls.append(path)or meta));self.assertEqual(len(calls),1);self.assertFalse(out['coverage']['selected_prefix_complete']);self.assertEqual(out['coverage']['missing_selected_update_rows'],[3885]);self.assertIsNone(out['optimizer_validation']['expected_exact_delta']);self.assertEqual(out['resume_evidence']['status'],'unknown_proof_fields');self.assertIsNone(out['resume_evidence']['discarded_parent_inflight_slots'])
   bad=copy.deepcopy(meta);bad.update(updates=3783,env_steps=495845376)
   with self.assertRaises(ValueError):a.analyze(p,m,r,parent,s,metadata_reader=lambda path:bad)
   (p/s['selected_file']).write_bytes(b'invalid chosen model');read=[]
   with self.assertRaises(ValueError):a.analyze(p,m,r,parent,s,metadata_reader=lambda path:read.append(path))
   self.assertEqual(read,[])

if __name__=='__main__':unittest.main()
