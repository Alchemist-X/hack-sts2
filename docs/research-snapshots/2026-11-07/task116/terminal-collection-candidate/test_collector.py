import copy,json,unittest
import collect_terminal as a
class Tests(unittest.TestCase):
 def setUp(self):
  self.r={'release_id':'fixed','output_volume_id':a.VOLUME};self.prefix='fixed/'+a.ATTEMPT
  self.latest={'parent_model_sha256':None,'bytes':196000000,'checkpoint':'checkpoints/ckpt-0000029.pt','sha256':'b'*64}
  self.final={'final_checkpoint':'final.pt','final_sha256':'b'*64,'latest':self.latest}
  self.files={'handle.json':dict(call_id=a.CID,attempt=a.ATTEMPT,release_sha256=a.BOUND,absolute_deadline=a.HARD_DEADLINE),'run/latest.json':self.latest,'run/checkpoints/ckpt-0000029.manifest.json':self.latest,'run/final.json':self.final}
  self.sel=dict(root_selected=True,same_attempt=True,call_id=a.CID,attempt=a.ATTEMPT,normal_final_present=True,kind='normal_final',selected_file='final.pt',selected_sha256='b'*64,selected_bytes=196000000)
  self.final_pt=True;self.term=dict(call_id=a.CID,provider_terminal_verified=True,observation_class='FunctionCall_returned',result={'status':'completed','attempt':a.ATTEMPT},observed_at=a.HARD_DEADLINE)
 def read(self,n,cap):
  key=n[len(self.prefix)+1:]
  if key not in self.files:raise FileNotFoundError(key)
  raw=a.CAN(self.files[key]).encode();assert len(raw)<=cap;return raw
 def probe(self,n):return {'bytes':self.latest['bytes'],'type':'file'}if self.final_pt else None
 def run_select(self):return a.select_metadata(self.r,self.sel,self.term,self.read,self.probe)
 def absent(self):
  del self.files['run/final.json'];self.final_pt=False;self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0000029.pt')
 def test_normal_M196MB_and_sole_latest(self):
  self.assertEqual(self.run_select()['selected_remote'],'run/final.pt');self.absent();out=self.run_select();self.assertTrue(out['normal_final_absence_witnessed']);self.assertEqual(out['selected_update'],29)
 def test_lone_or_invalid_final_never_fallback(self):
  for lone in ('pt','json','corrupt'):
   self.setUp()
   if lone=='pt':del self.files['run/final.json']
   elif lone=='json':self.final_pt=False
   else:self.final['final_sha256']='c'*64
   self.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file='ckpt-0000029.pt')
   with self.assertRaises(AssertionError):self.run_select()
 def test_live_unknown_beforehardstop_refused(self):
  self.term['result']['status']='live_unfinished'
  with self.assertRaises(AssertionError):self.run_select()
  self.term['result']['status']='completed';self.term['observation_class']='GetTimeoutError'
  with self.assertRaises(AssertionError):self.run_select()
  self.term['observation_class']='FunctionCall_returned';self.absent();self.term['observed_at']=a.HARD_DEADLINE-1
  with self.assertRaises(AssertionError):self.run_select()
 def test_wrong_attempt_volume_release_or_manifest_refused(self):
  for kind in ('attempt','volume','handle','immutable'):
   self.setUp()
   if kind=='attempt':self.term['result']['attempt']='a'*32
   elif kind=='volume':self.r['output_volume_id']='vo-other'
   elif kind=='handle':self.files['handle.json']['release_sha256']='c'*64
   else:self.files['run/checkpoints/ckpt-0000029.manifest.json']={**self.latest,'bytes':196000001}
   with self.assertRaises(AssertionError):self.run_select()
 def test_zero_future_and_overcap_refused(self):
  for u,size in ((0,196000000),(764,196000000),(29,268435457)):
   self.setUp();name=f'checkpoints/ckpt-{u:07d}.pt';self.latest.update(checkpoint=name,bytes=size);self.files['run/'+name.replace('.pt','.manifest.json')]=self.latest;self.sel['selected_bytes']=size
   with self.assertRaises(AssertionError):self.run_select()
if __name__=='__main__':unittest.main()
