import json
import tempfile
import unittest
from pathlib import Path

from modal_research.store import Store, TERMINAL_MODAL_PROBES_056

AUDIT = json.loads(Path('/private/tmp/sts2-task054/modal-probe-audit.json').read_text())
STATUS = json.loads(Path('/private/tmp/sts2-task054/status-full.json').read_text())
STATUS_ROWS = {row['id']: row for row in STATUS['finished_probes_cost_unknown']}


class ProbeBudget056(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store = Store(Path(self.tmp.name) / 'operation.sqlite3', 'study', mode='fixture', clock=lambda: 2000.0)
        self.store.approve('100.00', 'user100')
        self.store.record_platform(gross_limit='42.50', net_limit='30.00', credit='27.66',
                                   gross_used='6.30', net_spent='0', reference='metered', observed_at=1999.0)
        with self.store.transaction() as db:
            db.execute("CREATE TABLE broker_jobs (job_id TEXT PRIMARY KEY,state TEXT,receipt TEXT,provider_id TEXT,input_id TEXT,lease_until REAL,safe_after REAL,seed_begin INTEGER,seed_end INTEGER,reusable_tail INTEGER,send_authorized_at REAL,send_not_after REAL,send_finished_at REAL,cancel_attempts INTEGER,error TEXT)")
            for key, kind, state, reserve, vendor, broker in (
                ('S-running','train','running',730,'modal','running'),
                ('old-done','train','done',407,'modal','finished'),
                ('unknown-probe','access_probe','external_reserved',250,'modal',None),
            ):
                db.execute("INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,deadline,state) VALUES (?,?,?,?,?,?,?,?)",
                           (key,key,1,kind,'{}',reserve,3000,state))
                db.execute('INSERT INTO job_billing VALUES (?,?,?)',(key,vendor,'binding'))
                if broker:
                    db.execute('INSERT INTO broker_jobs(job_id,state) VALUES (?,?)',(key,broker))
            for item in AUDIT['probes']:
                if item['id'] not in TERMINAL_MODAL_PROBES_056:
                    continue
                key = item['id']
                result = STATUS_ROWS[key]
                payload = {'category': item['category']}
                if item['child_receipts']:
                    payload['authorized_jobs'] = [child['job_id'] for child in item['child_receipts']]
                outcome = {k:result[k] for k in ('status','report_ref')}
                outcome['details'] = result['details']
                db.execute("""INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,deadline,state,
                                               actual,remote_id,submit_started,outcome)
                              VALUES (?,?,1,'access_probe',?,?,3000,'external_finished_cost_unknown',NULL,?,?,?)""",
                           (key,key,json.dumps(payload),item['reserve'],item['remote_id'],item['submit_started'],json.dumps(outcome)))
                db.execute('INSERT INTO job_billing VALUES (?,?,?)',(key,'modal',item['reference']))

    def optin(self):
        with self.store.transaction() as db:
            db.execute("INSERT INTO budget_policy VALUES (1,'terminal_modal_history_v1','user-optin',2000)")

    def test_exact_nine_release_and_shared_views(self):
        self.optin()
        with self.store.transaction() as db:
            self.assertEqual(set(self.store.terminal_modal_probe_ids(db)),set(TERMINAL_MODAL_PROBES_056))
            self.assertEqual(self.store.ledger(db),(630,980))  # S $7.30 + unknown $2.50
            self.assertEqual(self.store.modal_ledger(db),(0,980))
            self.assertEqual(self.store.terminal_modal_historical_reserve(db),1827)
            evidence=self.store.budget_evidence(db,db.execute('SELECT * FROM study').fetchone())
            self.assertEqual(evidence['spent_and_reserved_cents'],[630,980])
            self.assertEqual(evidence['modal_spent_and_reserved_cents'],[630,980])
            self.assertEqual(evidence['historical_terminal_modal_reserve_cents'],1827)
        status=self.store.status()
        self.assertEqual(status['platform_reserved_usd'],9.8)
        self.assertEqual(status['historical_terminal_modal_reserve_usd'],18.27)
        self.assertEqual(status['reserved_usd'],9.8)

    def test_default_policy_still_holds_every_probe(self):
        with self.store.transaction() as db:
            self.assertEqual(self.store.terminal_modal_probe_ids(db),())
            self.assertEqual(self.store.ledger(db),(0,2807))
            self.assertEqual(self.store.modal_ledger(db),(0,2807))

    def test_mismatched_proof_and_active_probe_remain_held(self):
        self.optin()
        with self.store.transaction() as db:
            db.execute("UPDATE jobs SET outcome=json_set(outcome,'$.details.report_sha256','bad') WHERE id='reservation_capacity_l_018'")
            db.execute("UPDATE jobs SET state='external_reserved' WHERE id='reservation_gpu_smoke_007'")
            self.assertEqual(len(self.store.terminal_modal_probe_ids(db)),7)
            self.assertEqual(self.store.modal_ledger(db),(0,1430))

    def test_repeat_budget_reads_create_no_job_or_change_history(self):
        self.optin()
        with self.store.transaction() as db:
            before=[tuple(row) for row in db.execute('SELECT * FROM jobs ORDER BY id')]
            billing=[tuple(row) for row in db.execute('SELECT * FROM job_billing ORDER BY job_id')]
            for _ in range(3):
                self.assertEqual(self.store.modal_ledger(db),(0,980))
                self.assertEqual(self.store.ledger(db),(630,980))
                self.assertEqual(self.store.terminal_modal_historical_reserve(db),1827)
            self.assertEqual(before,[tuple(row) for row in db.execute('SELECT * FROM jobs ORDER BY id')])
            self.assertEqual(billing,[tuple(row) for row in db.execute('SELECT * FROM job_billing ORDER BY job_id')])


if __name__ == '__main__':
    unittest.main()
