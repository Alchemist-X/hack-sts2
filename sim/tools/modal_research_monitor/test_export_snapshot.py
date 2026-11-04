import json
from pathlib import Path
import tempfile
import unittest
from .export_snapshot import export, formal_arm


class SnapshotTests(unittest.TestCase):
    def fixture(self, root):
        (root / 'research').mkdir()
        (root / 'status.json').write_text(json.dumps({'approved_budget':100,'spent_usd':0,'spend_complete':False,'reserved_usd':7,'platform_snapshot':{'gross_used_usd':0.64},'experiments':[{'id':'silent-a10-capacity-s-formal-v1','decisions':0,'stage':'train','target':50069504,'config_hash':'a'*64}],'active_jobs':[{'id':'train-1:1','kind':'train','state':'running'}]}))
        (root / 'research' / 'plan.json').write_text(json.dumps({'capacity_comparison':{'new_decisions_each':50069504}}))


    def test_telemetry_allowlist_and_history(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            telemetry={'job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64,'grant_fence':1,'receipt_fence':1,'receipt_status':'running','observed_at':'2026-11-04T21:00:10Z','checkpoint_at':'2026-11-04T21:00:00Z','manifest_hash':'b'*64,'identity':{'job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'metadata':{'result_label':'capacity','termination':'checkpoint','decisions':131072},'metrics':{'last_completed_update':{'approx_kl':0.01,'entropy':1.5,'value_loss':0.1,'progress_mean':0.3,'env_steps_per_s':2000,'wins':1,'episodes':10,'secret':'NEVER_EXPORT'}}}
            inp=root/'telemetry.json';inp.write_text(json.dumps(telemetry));out=root/'snapshot.json'
            self.assertEqual(formal_arm('silent-a10-capacity-s-formal-v1'),'S')
            self.assertIsNone(formal_arm('silent-a10-pull-loop-acceptance'))
            self.assertIsNone(formal_arm('capacity-S-fresh'))
            before=export(root,out)
            self.assertEqual(before['streams']['formal']['arms']['S']['stage'],'deployed')
            grant=root/'active-grant.json';grant.write_text(json.dumps({'schema_version':1,'volume_id':'vo-private','job_key':'train-1:1','launch_token':'private','packet_hash':'c'*64,'fence':1,'identity':{'job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'deadline':9999999999}))
            a=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(a['streams']['formal']['current_task'],'运行中遥测，未终态验收')
            self.assertEqual(a['streams']['formal']['arms']['S']['stage'],'real')
            self.assertEqual(a['data_at'],'2026-11-04T21:00:00Z')
            self.assertEqual(a['streams']['formal']['arms']['S']['points'][0]['values']['win_rate'],0.1)
            self.assertNotIn('NEVER_EXPORT',out.read_text())
            telemetry['metadata']['decisions']=262144;telemetry['observed_at']='2026-11-04T21:05:10Z';telemetry['checkpoint_at']='2026-11-04T21:05:00Z';inp.write_text(json.dumps(telemetry))
            b=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(b['version'],a['version']+1)
            self.assertEqual(len(b['streams']['formal']['arms']['S']['points']),2)
            telemetry['receipt_fence']=2;inp.write_text(json.dumps(telemetry))
            with self.assertRaises(ValueError):export(root,out,telemetry_input=inp,active_grant=grant)
            grant.write_text(json.dumps({'schema_version':1,'active':False}))
            c=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(len(c['streams']['formal']['arms']['S']['points']),2)
            grant.write_text(json.dumps({'schema_version':1,'volume_id':'vo-private','job_key':'train-1:1','launch_token':'private','packet_hash':'c'*64,'fence':1,'identity':{'job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'deadline':9999999999}))
            telemetry['receipt_status']='succeeded';inp.write_text(json.dumps(telemetry))
            d=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(len(d['streams']['formal']['arms']['S']['points']),2)


if __name__=='__main__':unittest.main()
