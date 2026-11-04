from copy import deepcopy
from dataclasses import replace
import tempfile
import unittest
from pathlib import Path

from modal_research.adapters import Job, Outcome
from modal_research.contracts import digest, validate_spec
from modal_research.controller import Controller
from modal_research.fixture import FixtureBackend, artifact, decision, spec
from modal_research.lineage import FROZEN_HASHES, admit_worker, artifact_identity
from modal_research.native_worker import Installation, run
from modal_research.store import Store


class PureLineageTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.now = 1000
        self.frozen = spec(self.now)
        self.job = Job("train-test:1", "train-test", 1, "train", 1100, 100, {
            "study_id": "pure-study", "spec": self.frozen, "config_hash": digest(self.frozen),
            "checkpoint": None, "start_decisions": 0, "target_decisions": 10, "start_cursor": 0,
        })

    def facts(self, frozen=None):
        frozen = frozen or self.frozen
        return {key: frozen[key] for key in (
            *FROZEN_HASHES, "character", "ascension", "training_protocol", "evaluation_policy",
            "rollout_shape", "model_config", "ppo_config",
        )}

    def test_fresh_plan_has_no_checkpoint_and_records_random_root(self):
        admitted = admit_worker(self.job, self.root, self.facts())
        self.assertIsNone(admitted.checkpoint_dir)
        self.assertIsNone(admitted.identity["ancestor_checkpoint"])
        self.assertEqual(admitted.identity["root_initialization"], {"kind": "random", "init_seed": 1})

    def test_impure_spec_cannot_be_rehashed_into_a_valid_job(self):
        for field, value in {"start_mode": "pool", "start_saves": "/tmp/old.save", "save_mix": 0.2,
                             "harvest": 1, "teacher": "legacy", "action_labels": "/data/labels",
                             "pretrained": "/data/old.pt", "trajectory_data": "/data/trajectory",
                             "initialization": "resume", "rollout_loop_breaker": True}.items():
            with self.subTest(field=field):
                frozen = deepcopy(self.frozen)
                frozen["training_protocol"][field] = value
                job = replace(self.job, payload={**self.job.payload, "spec": frozen, "config_hash": digest(frozen)})
                with self.assertRaises(ValueError):
                    admit_worker(job, self.root, self.facts(frozen))
        for field, value in {"policy": "sample", "loop_breaker": True, "search": True,
                             "human_intervention": True, "start_mode": "snapshot"}.items():
            with self.subTest(field=field):
                frozen = deepcopy(self.frozen)
                frozen["evaluation_policy"][field] = value
                with self.assertRaises(ValueError):
                    validate_spec(frozen)

    def test_native_launcher_rejects_untrusted_paths_before_installation_or_import(self):
        fake_installation = Installation("invalid", "invalid", self.root / "missing", self.root / "missing",
                                         self.root, self.root, self.root)
        for override in ({"checkpoint": "/tmp/old.pt"}, {"checkpoint": "auto"}, {"start_cursor": 3},
                         {"checkpoint_path": "/tmp/foreign.pt"}, {"start_decisions": 4}):
            with self.subTest(override=override):
                with self.assertRaisesRegex(ValueError, "fresh|checkpoint|worker request"):
                    run(replace(self.job, payload={**self.job.payload, **override}), fake_installation)
        with self.assertRaises(ValueError):
            admit_worker(self.job, self.root, {**self.facts(), "engine_hash": "b" * 64})

    def test_milestones_must_be_whole_rollouts(self):
        frozen = deepcopy(self.frozen)
        frozen["rollout_shape"] = {"envs": 2048, "rollout": 64}
        frozen["evaluation_milestones"] = [10_000_000, 25_000_000, 50_000_000]
        frozen["cumulative_decision_target"] = 50_000_000
        with self.assertRaisesRegex(ValueError, "full rollout"):
            validate_spec(frozen)
        frozen["evaluation_milestones"] = [10_092_544, 25_034_752, 50_069_504]
        frozen["cumulative_decision_target"] = 50_069_504
        validate_spec(frozen)

    def test_ledger_attested_checkpoint_and_bytes_required(self):
        store = Store(self.root / "state.db", "pure-study", "fixture", clock=lambda: self.now)
        config_hash = store.register(self.frozen)
        store.approve(10, "fixture-approval")
        store.ingest("bootstrap", "bootstrap", {"fixture": True})
        provider = FixtureBackend(self.root / "provider.db")
        controller = Controller(store, provider, self.root / "artifacts")
        controller.tick()
        controller.tick()
        remote_id, research = provider.jobs()[-1]
        provider.complete(remote_id, Outcome("succeeded", 20, decision(
            research, "NEW_EXPERIMENT", experiment_spec={"experiment_id": self.frozen["experiment_id"],
                                                         "config_hash": config_hash}, reserve_budget_usd=1,
        )))
        controller.tick()
        controller.tick()
        remote_id, first_train = provider.jobs()[-1]
        result = artifact(controller.artifacts, first_train, 5, "segment")
        provider.complete(remote_id, Outcome("succeeded", 20, result))
        controller.tick()
        controller.tick()
        _, resumed = provider.jobs()[-1]
        with store.transaction() as db:
            accepted, _ = controller._accepted_checkpoint(db, resumed)
        admitted = admit_worker(resumed, controller.artifacts, self.facts(), accepted)
        self.assertEqual(admitted.identity["ancestor_checkpoint"], accepted.reference)
        self.assertEqual(admitted.checkpoint_manifest["identity"], artifact_identity(first_train))
        for bad_proof in (None, replace(accepted, study_id="another-study"),
                          replace(accepted, experiment_id="other-capacity"),
                          replace(accepted, identity={**accepted.identity, "data_lineage": "historical"})):
            with self.assertRaises(ValueError):
                admit_worker(resumed, controller.artifacts, self.facts(), bad_proof)
        (admitted.checkpoint_dir / "model.json").write_text("modified")
        with self.assertRaisesRegex(ValueError, "checksum"):
            admit_worker(resumed, controller.artifacts, self.facts(), accepted)

    def test_capacity_holdout_sizes_and_fixture_gate(self):
        operational = Store(self.root / "operation.db", "pure-study")
        with self.assertRaisesRegex(ValueError, "fixture evaluation"):
            operational.register(self.frozen)
        formal = {**self.frozen, "eval_protocol": "silent-a10-capacity-v1"}
        with self.assertRaisesRegex(ValueError, "256 dev and 1000 blind"):
            operational.register(formal)
        formal.update(dev_seed_count=256, blind_seed_count=1000)
        operational.register(formal)

    def test_capacity_specs_can_share_comparison_seed_schedule(self):
        store = Store(self.root / "state.db", "pure-study", "fixture")
        first = store.register(self.frozen)
        second = {**self.frozen, "experiment_id": "other-capacity"}
        self.assertNotEqual(store.register(second), first)
        self.assertEqual(len(store.status()["experiments"]), 2)


if __name__ == "__main__":
    unittest.main()
