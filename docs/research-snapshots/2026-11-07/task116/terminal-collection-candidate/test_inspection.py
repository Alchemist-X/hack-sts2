"""Synthetic opaque body only; injected metadata reader never imports Torch/native/provider."""
import copy,json,tempfile,unittest
from pathlib import Path
import inspect_terminal as a
class Tests(unittest.TestCase):
 def fixture(self,p,normal=True,updates=3,missing=0):
  r=json.loads((a.P/'release.bound.json').read_bytes());ns=r['spec']['env_seed_namespace'];guard=json.loads((a.P/'required-guards.json').read_bytes())
  marker=dict(method=a.B.METHOD,beta=0.,phase_weights={'Map':.5,'RestSite':.5},direction='KL(Ulegal||policy)',old_global_entropy_unchanged=.02)
  def write(n,v): (p/n).write_bytes(a.CAN(v))
  rows=[]
  for u in range(1,updates-missing+1):
   phase=dict(frames=2,KL_sum=1.,normalized_entropy_sum=1.,near_deterministic_count=1)
   rows.append(dict(update=u,env_steps=u*131072,env_steps_update=131072,session_update=u,learning_samples_update=131072,episodes=1,wins=0,starts_update=1,strategic_forward_kl=dict(marker,minibatches=32,phases={'Map':phase,'RestSite':phase})))
  chosen='final.pt'if normal else f'ckpt-{updates:07d}.pt';(p/chosen).write_bytes(b'synthetic-opaque-Mbody-no-Torch');latest=dict(checkpoint=f'checkpoints/ckpt-{updates:07d}.pt',sha256=a.SHA(p/chosen),bytes=(p/chosen).stat().st_size,parent_model_sha256=None)
  write('latest.json',latest);write('selected-checkpoint.manifest.json',latest)
  write('config.json',dict(sessions=[dict(update_start=0,resumed_from=None,adam_step=0,dropped_at_start=0)]))
  proof=dict(mode='fresh_random',parent_checkpoint=None,original_M_checkpoint_loaded=False,game_state_resume=False,base_params=16253584,transition_params=74052,init_seed=r['spec']['init_seed'],sample_seed=r['spec']['policy_seed'],env_seed=r['spec']['env_seed'],namespace=ns,seed_range=r['seed_range'],isolation={'checked_sets':guard['required_guard_sets'],'roles':guard['required_guard_roles']})
  write('initialization-proof.json',proof);write('first-transition-gpu-gate.json',{'synthetic_only':True})
  (p/'metrics.jsonl').write_text(''.join(json.dumps(x)+'\n'for x in rows));(p/'episodes.jsonl').write_text(''.join(json.dumps(dict(update=x['update'],n=x['update']-1,seed_text=f"{ns}-SILENT-{x['update']-1}",result='death'))+'\n'for x in rows))
  if normal:
   write('final.json',dict(final_sha256=latest['sha256'],latest=latest,result={'env_steps':updates*131072,'updates':updates},dose_complete=updates==763))
   for n in ('cycles.jsonl','cycle_episodes.jsonl','train.log','evals.jsonl'):(p/n).write_text('')
   for n in ('status.json','summary.json'):write(n,{})
  def opt(n,slots):return dict(initialized_states=n,parameter_slots=slots,per_state_steps={str(i):updates*32-(1 if i==1 else 0)for i in range(n)},states_without_step=0,min=updates*32-1,max=updates*32)
  cfg=dict(r['spec']['ppo_config'],**r['spec']['model_config'],init_seed=r['spec']['init_seed'],sample_seed=r['spec']['policy_seed'],env_seed=r['spec']['env_seed'],input_arch=r['spec']['input_arch'])
  meta=dict(env_steps=updates*131072,updates=updates,spec_sha256=a.B.SPEC_SHA,strategy=marker,recipe_config=cfg,rng_present={k:True for k in('numpy','torch','cuda')},seed_stream=dict(prefix=ns,next=updates+2048,issued_until=updates+6144,started=updates+2048,failed=0,skipped=0,reserved_skipped=0),inflight=dict(slot=list(range(2048)),n=list(range(updates,updates+2048)),seed_text=[f'{ns}-SILENT-{n}'for n in range(updates,updates+2048)],steps=[1]*2048),counters={'episodes':updates,'dropped':0},base_parameters=16253584,aux_parameters=74052,gate={'synthetic_only':True},typed_M_fullstate_validated=True,base_adam=opt(2,3),aux_adam=opt(4,4))
  meta['aux_adam']['per_state_steps']={str(i):updates*32 for i in range(4)}
  m=dict(call_terminal=True,call_id=a.CID,attempt=a.ATTEMPT,release_sha256=a.BOUND,files={},selected_update=updates,normal_final_absence_witnessed=not normal)
  s=dict(root_selected=True,same_attempt=True,call_id=a.CID,attempt=a.ATTEMPT,selected_file=chosen,selected_sha256=latest['sha256'],normal_final_present=normal,kind='normal_final'if normal else'sole_atomic_latest')
  self.rehash(p,m);return m,r,s,meta
 def rehash(self,p,m):m['files']={f.name:dict(bytes=f.stat().st_size,sha256=a.SHA(f))for f in p.iterdir()if f.is_file()}
 def test_full763_actual_nonuniform_base_clocks_no_fake_slots(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p,updates=763);calls=[];out=a.analyze(p,m,r,s,metadata_reader=lambda *args:calls.append(args)or copy.deepcopy(meta));self.assertEqual(len(calls),1);self.assertEqual(out['status'],'normal_full_M_inspected');self.assertEqual(out['optimizer_validation']['expected_exact_minibatch_steps'],24416);self.assertEqual(out['optimizer_validation']['base_missing_slots_not_fabricated'],1);self.assertEqual(out['per_run_accounting']['status'],'verified_complete_run_accounting')
 def test_hardlatest_missing_callback_tail_explicit_unknown(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p,False,3,1);out=a.analyze(p,m,r,s,metadata_reader=lambda *args:meta);self.assertEqual(out['coverage']['missing_update_rows'],[3]);self.assertIsNone(out['optimizer_validation']['expected_exact_minibatch_steps']);self.assertEqual(out['per_run_accounting']['snapshot_missing_closed_rows'],1)
 def test_hash_before_reader_invalidfinal_and_identity_refused(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p);(p/s['selected_file']).write_bytes(b'corrupt');calls=[]
   with self.assertRaises(ValueError):a.analyze(p,m,r,s,metadata_reader=lambda *args:calls.append(args))
   self.assertEqual(calls,[]);self.rehash(p,m)
   with self.assertRaises(ValueError):a.analyze(p,m,r,s,metadata_reader=lambda *args:meta)
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p,False);(p/'final.json').write_text('{}');self.rehash(p,m)
   with self.assertRaises(ValueError):a.analyze(p,m,r,s,metadata_reader=lambda *args:meta)
 def test_failed_normal_final_keeps_samebody_missing_tail_unknown(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p,True,3,1);final=json.loads((p/'final.json').read_bytes());final.update(result=None,status='failed_with_checkpoint',dose_complete=False);(p/'final.json').write_bytes(a.CAN(final));self.rehash(p,m)
   out=a.analyze(p,m,r,s,metadata_reader=lambda *args:meta);self.assertFalse(out['coverage']['normal_full_closed_log']);self.assertEqual(out['coverage']['missing_update_rows'],[3]);self.assertEqual(s['selected_file'],'final.pt')
 def test_failed_final_complete_prefix_plus_postrow_never_full_process(self):
  with tempfile.TemporaryDirectory()as t:
   p=Path(t);m,r,s,meta=self.fixture(p,True,3);last=json.loads((p/'metrics.jsonl').read_text().splitlines()[-1]);last.update(update=4,env_steps=4*131072,session_update=4)
   with(p/'metrics.jsonl').open('a')as f:f.write(json.dumps(last)+'\n')
   final=json.loads((p/'final.json').read_bytes());final.update(result=None,status='failed_with_checkpoint',dose_complete=False);(p/'final.json').write_bytes(a.CAN(final));self.rehash(p,m)
   out=a.analyze(p,m,r,s,metadata_reader=lambda *args:meta);self.assertTrue(out['coverage']['selected_prefix_complete']);self.assertEqual(out['coverage']['post_selected_log_rows_excluded'],[4]);self.assertFalse(out['per_run_accounting']['full_process_coverage']);self.assertTrue(out['per_run_accounting']['unknown_tail_not_fabricated'])
 def test_wrongM_seed_or_adam_and_duplicateepisode_refused(self):
  for kind in ('M','seed','adam','duplicate'):
   with self.subTest(kind=kind),tempfile.TemporaryDirectory()as t:
    p=Path(t);m,r,s,meta=self.fixture(p)
    if kind=='M':meta['base_parameters']=2978260
    elif kind=='seed':meta['seed_stream']['prefix']='wrong-S'
    elif kind=='adam':meta['base_adam']['per_state_steps']['0']=9999
    else:
     with(p/'episodes.jsonl').open('a')as f:f.write((p/'episodes.jsonl').read_text().splitlines()[0]+'\n')
     self.rehash(p,m)
    with self.assertRaises(ValueError):a.analyze(p,m,r,s,metadata_reader=lambda *args:copy.deepcopy(meta))
if __name__=='__main__':unittest.main()
