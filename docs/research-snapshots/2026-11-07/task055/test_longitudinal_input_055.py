import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from modal_research.agent_runner import _research_input
from modal_research.contracts import canonical
from modal_research.controller import Controller
from modal_research.fixture import spec
from modal_research.store import Store


S = 'silent-a10-capacity-s-formal-v1'


class LongitudinalInputTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.store = Store(self.root / 'ledger', 'research-test', mode='fixture', clock=lambda: 1000)
        self.store.approve(20, 'approval')
        self.store.record_platform(gross_limit=40, net_limit=30, credit=30, gross_used=0,
                                   net_spent=0, reference='platform')
        self.spec = spec(1000, experiment_id=S)
        self.config_hash = self.store.register(self.spec)
        with self.store.transaction() as db:
            db.execute("UPDATE experiments SET stage='await_decision',decisions=25034752,target=25034752 WHERE id=?", (S,))
        self.controller = Controller(self.store, SimpleNamespace(tool_enabled=False), self.root / 'artifacts')

    def add_eval(self, number, *, decisions=10092544, experiment=S, config_hash=None,
                 protocol=None, dev_hash=None, state='done', outcome=True, origin=None):
        config_hash = self.config_hash if config_hash is None else config_hash
        protocol = self.spec['eval_protocol'] if protocol is None else protocol
        dev_hash = self.spec['dev_seed_manifest_hash'] if dev_hash is None else dev_hash
        event_id, job_id = f'result-fixture-{number}', f'eval-fixture-{number}:1'
        eval_hash, train_hash = format(number, '064x'), format(number + 1000, '064x')
        result = {
            'experiment_id': experiment,
            'checkpoint': {'job_key': job_id, 'generation': f'g-{number}', 'manifest_hash': eval_hash},
            'manifest': {'identity': {'kind': 'eval', 'experiment_id': experiment, 'config_hash': config_hash,
                                      'eval_protocol': protocol, 'dev_seed_manifest_hash': dev_hash},
                         'metadata': {'decisions': decisions, 'termination': 'completed'}},
            'metrics': {'games': 1, 'wins': 0, 'win_rate': 0, 'win_rate_ci95': [0, 0.79],
                        'progress_mean': 0.1, 'progress_ci95': [0.1, 0.1], 'progress_games': 1,
                        'replayable': 1, 'results': {'loss': 1, 'crashed': 0, 'stuck': 0},
                        'raw_seed': 'should-not-leak'},
            'weights': 'should-not-leak',
        }
        job_payload = {'config_hash': config_hash, 'start_decisions': decisions,
                       'checkpoint': {'job_key': 'train-source:1', 'generation': 'train-generation',
                                      'manifest_hash': train_hash}}
        accepted_outcome = {key: result[key] for key in ('checkpoint', 'manifest', 'metrics')}
        with self.store.transaction() as db:
            db.execute('INSERT INTO jobs(id,logical_key,attempt,kind,experiment_id,payload,reserve,actual,state,deadline,outcome) '
                       'VALUES (?,?,1,\'eval\',?,?,0,0,?,2000,?)',
                       (job_id, job_id, experiment, canonical(job_payload), state,
                        canonical(accepted_outcome) if outcome else None))
            self.store.append_event(db, event_id, 'result', result, 1000,
                                    origin=(job_id, origin if origin is not None else experiment))
        return event_id, train_hash, eval_hash

    def test_s_25m_input_includes_only_verified_earlier_s_dev(self):
        prior_id, train_hash, eval_hash = self.add_eval(1)
        self.add_eval(2, decisions=25034752)
        self.add_eval(3, config_hash='a' * 64)
        self.add_eval(4, protocol='other-protocol')
        self.add_eval(5, dev_hash='b' * 64)
        self.add_eval(6, state='failed')
        self.add_eval(7, outcome=False)
        self.store.register(spec(1000, experiment_id='other-experiment'))
        self.add_eval(8, origin='other-experiment')
        with self.store.transaction() as db:
            experiment = db.execute('SELECT * FROM experiments WHERE id=?', (S,)).fetchone()
            history = self.controller._longitudinal_results(db, experiment, self.spec)
            state = db.execute('SELECT * FROM study').fetchone()
            # A current result event is the scheduling trigger; capture the packet without dispatch.
            current = db.execute("SELECT * FROM events WHERE id='result-fixture-2'").fetchone()
            captured = {}
            self.controller._create_job = lambda db, logical, attempt, kind, payload, reserve, deadline, event=None: captured.update(payload)
            self.controller._schedule_analysis(db, state, current)
        self.assertEqual([x['event_id'] for x in history], [prior_id])
        item = history[0]
        self.assertEqual(item['comparison_kind'], 'same_experiment_longitudinal_dev')
        self.assertEqual(item['evaluated_checkpoint_manifest_hash'], train_hash)
        self.assertEqual(item['evaluation_manifest_hash'], eval_hash)
        self.assertEqual(item['headline']['progress_games'], 1)
        self.assertEqual(item['headline']['results']['crashed'], 0)
        self.assertNotIn('raw_seed', item['headline'])
        self.assertEqual(captured['longitudinal_dev_history'], history)
        self.assertIn('not matched cross-capacity', captured['longitudinal_dev_history_basis'])
        self.assertEqual(captured['research_plan_context']['baseline_decisions'], 10092544)
        self.assertIn('Training curves are diagnostic', captured['research_plan_context']['guidance'])
        packet = json.loads(_research_input(captured)[0].rsplit('\n', 1)[-1])
        self.assertEqual(packet['longitudinal_dev_history'], history)
        self.assertNotIn('weights', canonical(packet['longitudinal_dev_history']))
        self.assertNotIn('raw_seed', canonical(packet['longitudinal_dev_history']))

    def test_history_is_bounded_to_eight_most_recent(self):
        for n in range(1, 11):
            self.add_eval(n, decisions=n)
        with self.store.transaction() as db:
            experiment = db.execute('SELECT * FROM experiments WHERE id=?', (S,)).fetchone()
            history = self.controller._longitudinal_results(db, experiment, self.spec)
        self.assertEqual([x['decisions'] for x in history], list(range(10, 2, -1)))


if __name__ == '__main__':
    unittest.main()
