"""One targeted stdlib regression; original fixture reused, no original suite executed."""
import unittest
from pathlib import PurePosixPath
import collect_terminal as c
import accepted_fixture
class Tests(unittest.TestCase):
 def test_normal_unchanged_and_verified_exception_null_raw_preserved(self):
  f=accepted_fixture.Tests();f.setUp()
  normal=c.select_metadata(f.r,f.sel,f.term,f.read,f.probe)
  self.assertEqual(normal['selected_remote'],'run/final.pt');self.assertEqual(f.term['result']['status'],'completed')
  del f.files['run/final.json'];f.final_pt=False
  f.sel.update(kind='sole_atomic_latest',normal_final_present=False,selected_file=PurePosixPath(f.latest['checkpoint']).name)
  f.term.update(observation_class='FunctionCall_verified_terminal_exception',status='timed_out',result=None,observed_at=getattr(c,'HARD_DEADLINE',0))
  chosen=c.select_metadata(f.r,f.sel,f.term,f.read,f.probe)
  self.assertTrue(chosen['normal_final_absence_witnessed']);self.assertEqual(chosen['selected_remote'],'run/'+f.latest['checkpoint']);self.assertIsNone(f.term['result'])
if __name__=='__main__':unittest.main()
