import json
import copy
from pathlib import Path
import tempfile
import unittest
from .export_snapshot import export, formal_arm, training_metric_points, merge_metric_point


class SnapshotTests(unittest.TestCase):
    def fixture(self, root):
        (root / 'research').mkdir()
        (root / 'status.json').write_text(json.dumps({'approved_budget':100,'spent_usd':0,'spend_complete':False,'reserved_usd':7,'platform_snapshot':{'gross_used_usd':0.64},'experiments':[{'id':'silent-a10-capacity-s-formal-v1','decisions':0,'stage':'train','target':50069504,'config_hash':'a'*64}],'active_jobs':[{'id':'train-1:1','kind':'train','state':'running'}],'finished_probes_cost_unknown':[{'id':'reservation_runtime_gpu_015','status':'runtime_gpu_verified','reserved_usd':2.0,'details':{'actual_cost_usd':None}}]}))
        (root / 'research' / 'plan.json').write_text(json.dumps({'capacity_comparison':{'new_decisions_each':50069504}}))


    def test_telemetry_allowlist_and_history(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            telemetry={'job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64,'grant_fence':1,'receipt_fence':1,'receipt_status':'running','observed_at':'2026-11-04T21:00:10Z','checkpoint_at':'2026-11-04T21:00:00Z','manifest_hash':'b'*64,'identity':{'kind':'train','job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'metadata':{'result_label':'capacity','termination':'checkpoint','decisions':262144},'metrics':{'update_history':[{'update':1,'env_steps':65536,'at':'2026-11-04T20:55:00Z','entropy':1.7},{'update':2,'env_steps':131072,'entropy':1.5}], 'last_completed_update':{'env_steps':131072,'approx_kl':0.01,'entropy':1.5,'value_loss':0.1,'progress_mean':0.3,'env_steps_per_s':2000,'wins':1,'episodes':10,'secret':'NEVER_EXPORT'}}}
            inp=root/'telemetry.json';inp.write_text(json.dumps(telemetry));out=root/'snapshot.json'
            self.assertEqual(formal_arm('silent-a10-capacity-s-formal-v1'),'S')
            self.assertIsNone(formal_arm('silent-a10-pull-loop-acceptance'))
            self.assertIsNone(formal_arm('capacity-S-fresh'))
            before=export(root,out)
            self.assertEqual(before['streams']['formal']['arms']['S']['stage'],'deployed')
            self.assertEqual(before['streams']['engineering']['current_task'],'GPU 闭环验收通过')
            self.assertEqual(before['budget']['actual_status'],'unknown')
            out.unlink();(root/'snapshot.json.lineage.json').unlink()
            grant=root/'active-grant.json';grant.write_text(json.dumps({'schema_version':1,'volume_id':'vo-private','job_key':'train-1:1','launch_token':'private','packet_hash':'c'*64,'fence':1,'identity':{'kind':'train','job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'deadline':9999999999}))
            a=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(a['streams']['formal']['current_task'],'训练中 S；遥测未验收，指标可滞后一批')
            self.assertEqual(a['streams']['formal']['arms']['S']['decisions'],262144)
            self.assertEqual([p['decisions'] for p in a['streams']['formal']['arms']['S']['points']],[65536,131072])
            self.assertEqual(a['streams']['formal']['arms']['S']['points'][0]['at'],'2026-11-04T20:55:00Z')
            self.assertIsNone(a['streams']['formal']['arms']['S']['points'][-1]['at'])
            self.assertEqual(a['streams']['formal']['arms']['S']['stage'],'real')
            self.assertEqual(a['data_at'],'2026-11-04T21:00:00Z')
            self.assertEqual(a['streams']['formal']['arms']['S']['points'][-1]['values']['win_rate'],0.1)
            self.assertNotIn('NEVER_EXPORT',out.read_text())
            telemetry['metadata']['decisions']=393216;telemetry['metrics']['last_completed_update']['env_steps']=262144;telemetry['observed_at']='2026-11-04T21:05:10Z';telemetry['checkpoint_at']='2026-11-04T21:05:00Z';inp.write_text(json.dumps(telemetry))
            b=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(b['version'],a['version']+1)
            self.assertEqual(len(b['streams']['formal']['arms']['S']['points']),3)
            self.assertEqual(b['streams']['formal']['arms']['S']['points'][-1]['decisions'],262144)
            self.assertEqual(b['streams']['formal']['arms']['S']['decisions'],393216)
            telemetry['metadata']['decisions']=524288;telemetry['metrics'].pop('last_completed_update');inp.write_text(json.dumps(telemetry))
            missing=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(missing['streams']['formal']['arms']['S']['decisions'],524288)
            self.assertEqual(len(missing['streams']['formal']['arms']['S']['points']),3)
            telemetry['receipt_fence']=2;inp.write_text(json.dumps(telemetry))
            with self.assertRaises(ValueError):export(root,out,telemetry_input=inp,active_grant=grant)
            grant.write_text(json.dumps({'schema_version':1,'active':False}))
            c=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(len(c['streams']['formal']['arms']['S']['points']),3)
            grant.write_text(json.dumps({'schema_version':1,'volume_id':'vo-private','job_key':'train-1:1','launch_token':'private','packet_hash':'c'*64,'fence':1,'identity':{'kind':'train','job_key':'train-1:1','experiment_id':'silent-a10-capacity-s-formal-v1','config_hash':'a'*64},'deadline':9999999999}))
            telemetry['receipt_fence']=1;telemetry['receipt_status']='succeeded';telemetry['metadata']['termination']='milestone';inp.write_text(json.dumps(telemetry))
            d=export(root,out,telemetry_input=inp,active_grant=grant)
            self.assertEqual(len(d['streams']['formal']['arms']['S']['points']),3)





    def test_older_accepted_head_cannot_mask_live_checkpoint(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            status=json.loads((root/'status.json').read_text())
            exp=status['experiments'][0]
            exp['decisions']=131072
            exp['checkpoint']=json.dumps({'job_key':'train-accepted:1','generation':'accepted-gen','manifest_hash':'b'*64})
            (root/'status.json').write_text(json.dumps(status))
            identity={'kind':'train','job_key':'train-1:1','experiment_id':exp['id'],'config_hash':exp['config_hash']}
            grant={'schema_version':1,'volume_id':'vo-Test','job_key':'train-1:1','launch_token':'private',
                   'packet_hash':'c'*64,'fence':1,'identity':identity,'deadline':9999999999}
            grant_path=root/'grant.json';grant_path.write_text(json.dumps(grant))
            telemetry={'job_key':'train-1:1','experiment_id':exp['id'],'config_hash':exp['config_hash'],
                       'grant_fence':1,'receipt_fence':1,'receipt_status':'running',
                       'observed_at':'2026-11-04T21:05:10Z','checkpoint_at':'2026-11-04T21:05:00Z',
                       'manifest_hash':'d'*64,'identity':identity,
                       'metadata':{'decisions':262144,'result_label':'capacity','termination':'checkpoint'},
                       'metrics':{'update_history':[{'update':1,'env_steps':131072,'at':'2026-11-04T21:01:00Z','entropy':1.1,'value_loss':0.03}], 'last_completed_update':{'update':2,'env_steps':196608,'entropy':0.8}}}
            tele_path=root/'telemetry.json';tele_path.write_text(json.dumps(telemetry))
            accepted={'schema_version':1,'items':[{'experiment_id':exp['id'],'config_hash':exp['config_hash'],
                       'job_key':'train-accepted:1','manifest_hash':'b'*64,'checkpoint_at':'2026-11-04T21:00:00Z',
                       'metadata':{'decisions':131072,'result_label':'capacity','termination':'segment'},
                       'metrics':{'last_completed_update':{'update':1,'env_steps':131072,'entropy':1.0}}}]}
            accepted_path=root/'accepted.json';accepted_path.write_text(json.dumps(accepted))
            result=export(root,root/'snapshot.json',telemetry_input=tele_path,active_grant=grant_path,accepted_metrics=accepted_path)
            arm=result['streams']['formal']['arms']['S']
            self.assertEqual(arm['decisions'],262144)
            self.assertEqual([p['decisions'] for p in arm['points']],[131072,196608])
            self.assertEqual(arm['points'][0]['at'],'2026-11-04T21:01:00Z')
            self.assertEqual(arm['points'][0]['values']['value_loss'],0.03)
            self.assertEqual(result['data_at'],'2026-11-04T21:05:00Z')

    def test_summary_merges_without_losing_true_row_time_or_x(self):
        points=training_metric_points({'update_history':[{'update':1,'env_steps':131072,
            'at':'2026-11-04T21:01:00Z','entropy':1.2,'value_loss':0.03}],
            'last_completed_update':{'update':1,'env_steps':131072,'decisions':999999,
                                      'at':None,'entropy':1.1}},131072)
        self.assertEqual(len(points),1)
        self.assertEqual(points[0]['decisions'],131072)
        self.assertEqual(points[0]['at'],'2026-11-04T21:01:00Z')
        self.assertEqual(points[0]['values']['entropy'],1.1)
        self.assertEqual(points[0]['values']['value_loss'],0.03)
        later=training_metric_points({'last_completed_update':{'update':1,'env_steps':131072,'entropy':1.05}},131072)[0]
        merged=merge_metric_point(points[0],later)
        self.assertEqual(merged['at'],'2026-11-04T21:01:00Z')
        self.assertEqual(merged['values']['value_loss'],0.03)
        self.assertEqual(merged['values']['entropy'],1.05)

    def test_development_evaluation_is_separate_series(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            status=json.loads((root/'status.json').read_text())
            status['active_jobs']=[];status['experiments'][0]['stage']='await_decision'
            status['experiments'][0]['decisions']=10092544
            (root/'status.json').write_text(json.dumps(status))
            exp=status['experiments'][0]
            projection={'schema_version':1,'items':[{'experiment_id':exp['id'],'config_hash':exp['config_hash'],
                'job_key':'eval-final:1','manifest_hash':'b'*64,'checkpoint_at':None,
                'metadata':{'decisions':10092544,'result_label':'capacity','termination':'completed'},
                'metrics':{'games':256,'wins':0,'act_reached':{'1':232,'2':24},
                           'progress_mean':0.258338875,'win_rate':0.0,'win_rate_ci95':[0,0.0148],
                           'progress_ci95':[0.245,0.271]}}]}
            path=root/'evals.json';path.write_text(json.dumps(projection))
            result=export(root,root/'snapshot.json',accepted_evaluations=path)
            arm=result['streams']['formal']['arms']['S']
            self.assertEqual(arm['points'],[])
            self.assertEqual(len(arm['evaluations']),1)
            values=arm['evaluations'][0]['values']
            self.assertEqual(values['act1_reach'],232/256)
            self.assertEqual(values['act2_reach'],24/256)
            self.assertIsNone(values['act3_reach'])
            self.assertEqual(values['win_rate'],0.0)
            self.assertIsNone(arm['evaluations'][0]['at'])

    def test_sparse_stdout_history_only_real_points(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            status=json.loads((root/'status.json').read_text())
            status['active_jobs']=[]
            exp=status['experiments'][0]
            exp['stage']='await_decision';exp['decisions']=10092544;exp['target']=10092544
            exp['checkpoint']=json.dumps({'job_key':'train-final:1','generation':'gen-final','manifest_hash':'b'*64})
            (root/'status.json').write_text(json.dumps(status))
            history={'schema_version':1,'experiment_id':exp['id'],'config_hash':exp['config_hash'],
                     'job_key':'train-final:1','terminal':{'generation':'gen-final','manifest_hash':'b'*64,'updates':77,'decisions':10092544},
                     'points':[{'update':1,'env_steps':131072,'at':None,'source':'stdout','metrics':{'entropy':1.4}},
                               {'update':10,'env_steps':1310720,'at':None,'source':'stdout','metrics':{'entropy':1.2}},
                               {'update':77,'env_steps':10092544,'at':None,'source':'terminal_metrics','metrics':{'entropy':0.8,'value_loss':0.02}}]}
            source=root/'history.json';source.write_text(json.dumps(history))
            out=root/'snapshot.json';result=export(root,out,history=source)
            pts=result['streams']['formal']['arms']['S']['points']
            self.assertEqual([p['decisions'] for p in pts],[131072,1310720,10092544])
            self.assertTrue(all(p['at'] is None for p in pts))
            self.assertIsNone(pts[0]['values']['value_loss'])
            history['terminal']['manifest_hash']='c'*64;source.write_text(json.dumps(history))
            stale=export(root,out,history=source)
            self.assertEqual(len(stale['streams']['formal']['arms']['S']['points']),3)

    def test_accepted_final_metric_and_await_decision(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            status=json.loads((root/'status.json').read_text())
            status['active_jobs']=[]
            exp=status['experiments'][0]
            exp['stage']='await_decision'
            exp['decisions']=10092544
            exp['target']=10092544
            exp['checkpoint']=json.dumps({'job_key':'train-final:1','generation':'gen-final','manifest_hash':'b'*64})
            (root/'status.json').write_text(json.dumps(status))
            projection={'schema_version':1,'items':[{'experiment_id':exp['id'],'config_hash':exp['config_hash'],'job_key':'train-final:1','manifest_hash':'b'*64,'checkpoint_at':'2026-11-04T21:20:00Z','metadata':{'decisions':10092544,'result_label':'capacity','termination':'milestone'},'metrics':{'last_completed_update':{'env_steps':9961472,'entropy':0.5,'approx_kl':0.01}}}]}
            accepted=root/'accepted.json';accepted.write_text(json.dumps(projection))
            out=root/'snapshot.json'
            result=export(root,out,accepted_metrics=accepted)
            arm=result['streams']['formal']['arms']['S']
            self.assertEqual(arm['decisions'],10092544)
            self.assertEqual(arm['points'][0]['decisions'],9961472)
            self.assertEqual(result['streams']['formal']['status'],'pending')
            self.assertEqual(result['streams']['formal']['current_task'],'等待研究决策 S')
            projection['items'][0]['manifest_hash']='c'*64;accepted.write_text(json.dumps(projection))
            stale=export(root,out,accepted_metrics=accepted)
            self.assertEqual(len(stale['streams']['formal']['arms']['S']['points']),1)
            projection['items'][0]['manifest_hash']='b'*64
            projection['items'][0]['metadata']['termination']='checkpoint'
            accepted.write_text(json.dumps(projection))
            recovered=export(root,out,accepted_metrics=accepted)
            self.assertEqual(recovered['streams']['formal']['arms']['S']['decisions'],10092544)

    def test_three_independent_arms_and_waiting_state(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fixture(root)
            status=json.loads((root/'status.json').read_text())
            status['experiments']=[];status['active_jobs']=[]
            grants=[];items=[]
            for index,arm in enumerate('SML',1):
                experiment_id=f'silent-a10-capacity-{arm.lower()}-formal-v1'
                config_hash=str(index)*64
                job_key=f'train-{arm.lower()}:1'
                status['experiments'].append({'id':experiment_id,'config_hash':config_hash,'decisions':0,'target':50069504,'stage':'train'})
                status['active_jobs'].append({'id':job_key,'kind':'train','state':'running'})
                identity={'kind':'train','job_key':job_key,'experiment_id':experiment_id,'config_hash':config_hash}
                grants.append({'schema_version':1,'volume_id':'vo-Test','job_key':job_key,'launch_token':arm*64,'packet_hash':str(index)*64,'fence':index,'identity':identity,'deadline':9999999999})
                items.append({'job_key':job_key,'experiment_id':experiment_id,'config_hash':config_hash,'grant_fence':index,'receipt_fence':index,'receipt_status':'running','observed_at':f'2026-11-04T21:0{index}:10Z','checkpoint_at':f'2026-11-04T21:0{index}:00Z','manifest_hash':'a'*64,'identity':identity,'metadata':{'result_label':'capacity','termination':'checkpoint','decisions':index*262144},'metrics':{'last_completed_update':{'env_steps':index*262144-131072,'entropy':0.1*index}}})
            (root/'status.json').write_text(json.dumps(status))
            grant=root/'grants.json';grant.write_text(json.dumps({'schema_version':2,'grants':grants}))
            telemetry=root/'telemetry.json';telemetry.write_text(json.dumps({'schema_version':2,'items':items}))
            out=root/'snapshot.json'
            result=export(root,out,telemetry_input=telemetry,active_grant=grant)
            self.assertEqual(result['streams']['formal']['status'],'active')
            self.assertEqual(result['streams']['formal']['current_task'],'训练中 L/M/S；遥测未验收，指标可滞后一批')
            for index,arm in enumerate('SML',1):
                a=result['streams']['formal']['arms'][arm]
                self.assertEqual(a['decisions'],index*262144)
                self.assertEqual(a['points'][0]['decisions'],index*262144-131072)
            self.assertEqual(result['data_at'],'2026-11-04T21:03:00Z')
            # A later checkpoint for M does not erase S/L histories.
            items[1]['metadata']['decisions']+=131072
            items[1]['metrics']['last_completed_update']['env_steps']+=131072
            telemetry.write_text(json.dumps({'schema_version':2,'items':[items[1]]}))
            second=export(root,out,telemetry_input=telemetry,active_grant=grant)
            self.assertEqual(len(second['streams']['formal']['arms']['S']['points']),1)
            self.assertEqual(len(second['streams']['formal']['arms']['M']['points']),2)
            self.assertEqual(len(second['streams']['formal']['arms']['L']['points']),1)
            self.assertEqual(second['data_at'],'2026-11-04T21:03:00Z')
            # No active jobs with await_decision must not say training is running.
            status['active_jobs']=[]
            for exp in status['experiments']:exp['stage']='await_decision'
            (root/'status.json').write_text(json.dumps(status))
            grant.write_text(json.dumps({'schema_version':1,'active':False}))
            waiting=export(root,out,telemetry_input=telemetry,active_grant=grant)
            self.assertEqual(waiting['streams']['formal']['status'],'pending')
            self.assertEqual(waiting['streams']['formal']['current_task'],'等待研究决策 L/M/S')
            status['experiments'][1]['config_hash']='f'*64
            (root/'status.json').write_text(json.dumps(status))
            isolated=export(root,out,telemetry_input=telemetry,active_grant=grant)
            self.assertEqual(isolated['streams']['formal']['arms']['M']['points'],[])
            self.assertEqual(len(isolated['streams']['formal']['arms']['S']['points']),1)


if __name__=='__main__':unittest.main()
