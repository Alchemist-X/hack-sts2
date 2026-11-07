import unittest,json
from collect_terminal import select_metadata,CID,BOUND,CAN,ATTEMPT,OUTPUT_VOLUME
class Tests(unittest.TestCase):
 def setUp(self):
  self.r={'release_id':'fixed','parent':{'model_sha256':'b7b510d48141fd86afa3c3ec6789a30417bb188d1ec1e63adb2e68431b1edc1a','updates':4546,'decisions':595853312},'segment':{'target_updates':5309,'target_env_steps':695861248,'index':2,'beta':0},'output_volume_id':OUTPUT_VOLUME}
  self.at=ATTEMPT;self.prefix='fixed/'+self.at
  self.latest={'parent_model_sha256':'b7b510d48141fd86afa3c3ec6789a30417bb188d1ec1e63adb2e68431b1edc1a','bytes':36,'checkpoint':'checkpoints/ckpt-0004575.pt','sha256':'b'*64}
  self.final={'final_checkpoint':'final.pt','final_sha256':'b'*64,'latest':self.latest}
  self.files={'handle.json':{'call_id':CID,'attempt':self.at,'release_sha256':BOUND},'run/latest.json':self.latest,'run/checkpoints/ckpt-0004575.manifest.json':self.latest,'run/final.json':self.final}
  self.sel={'root_selected':True,'same_attempt':True,'call_id':CID,'attempt':self.at,'normal_final_present':True,'kind':'normal_final','selected_file':'final.pt','selected_sha256':'b'*64,'selected_bytes':36}
  self.final_pt=True
  self.term={'call_id':CID,'provider_terminal_verified':True,'observation_class':'FunctionCall_returned','result':{'status':'completed','attempt':self.at}}
 def read(self,n,cap):
  key=n[len(self.prefix)+1:]
  if key not in self.files:raise FileNotFoundError(key)
  raw=CAN(self.files[key]).encode();assert len(raw)<=cap;return raw
 def probe(self,n):return {'bytes':36,'type':'file'}if self.final_pt else None
 def test_normal_final_exact(self):self.assertEqual(select_metadata(self.r,self.sel,self.term,self.read,self.probe)['selected_remote'],'run/final.pt')
 def test_resource_censored_is_provider_terminal(self):
  self.term['result']['status']='resource_censored_with_checkpoint';self.assertEqual(select_metadata(self.r,self.sel,self.term,self.read,self.probe)['selected_update'],4575)
 def test_present_invalid_final_not_bypassed(self):
  self.final['final_sha256']='c'*64;self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0004575.pt')
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
 def test_rootsolelatest_absence_witness(self):
  del self.files['run/final.json'];self.final_pt=False;self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0004575.pt');self.term.update(observation_class='FunctionCall_verified_terminal_exception',status='timed_out');self.term.pop('result')
  result=select_metadata(self.r,self.sel,self.term,self.read,self.probe);self.assertTrue(result['normal_final_absence_witnessed']);self.assertEqual(result['selected_basename'],'ckpt-0004575.pt')
 def test_lone_finalpayload_cannot_bypass(self):
  del self.files['run/final.json'];self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0004575.pt')
  with self.assertRaisesRegex(AssertionError,'lone finalpayload'):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
 def test_zero_advance_parent_refused(self):
  del self.files['run/final.json'];self.final_pt=False;self.latest['checkpoint']='checkpoints/ckpt-0004546.pt';self.files['run/checkpoints/ckpt-0004546.manifest.json']=self.latest;self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0004546.pt')
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
 def test_writer_final_with_live_or_unknown_is_not_terminal(self):
  self.term['result']['status']='live_unfinished'
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
  self.term['result']['status']='completed';self.term['observation_class']='GetTimeoutError'
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
 def test_other_call_attempt_or_corruptmanifest_refused(self):
  self.term['result']['attempt']='1'*32
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
  self.term['result']['attempt']=self.at;self.files['run/checkpoints/ckpt-0004575.manifest.json']={**self.latest,'bytes':37}
  with self.assertRaises(AssertionError):select_metadata(self.r,self.sel,self.term,self.read,self.probe)
if __name__=='__main__':unittest.main()
