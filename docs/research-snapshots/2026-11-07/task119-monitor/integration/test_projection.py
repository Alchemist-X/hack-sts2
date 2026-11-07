import json
from pathlib import Path
import tempfile
import unittest

from assemble import curate
from project_live import RUNS, latest, project


class ProjectionTests(unittest.TestCase):
    def observation(self, identifier):
        identity = RUNS[identifier]
        update = identity["baseline"] // 131072 + 3
        return {
            "at": int(identity["submitted"]) + 130.1,
            "call_id": identity["call_id"], "call_state": "live_unfinished",
            "checkpoint_update": update, "new_env_steps": 3 * 131072,
            "run/latest.json": {"body": {"checkpoint": f"checkpoints/ckpt-{update:07d}.pt", "sha256": "a" * 64}},
            "ignored_private_field": "/data/raven/opaque",
        }

    def test_projection_preserves_dose_basis_and_rejects_foreign_call(self):
        raw = self.observation("S115-S2")
        view = project(raw, "S115-S2")
        self.assertEqual(view["new_decisions"], 3 * 131072)
        self.assertEqual(view["decisions"], 595853312 + 3 * 131072)
        self.assertEqual(view["checkpoint_evidence"], "writer_metadata")
        self.assertNotIn("/data/", json.dumps(view))
        self.assertIsNone(view["progress_health"]["training_progress"])
        with self.assertRaisesRegex(ValueError, "foreign call"):
            project(raw, "M116")

    def test_reads_complete_observation_when_newest_is_partial(self):
        raw = self.observation("M116")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / f"observation-{int(raw['at'])}.json").write_text(json.dumps(raw))
            (root / f"observation-{int(raw['at']) + 1}.json").write_text("{")
            self.assertEqual(latest(root, "M116"), project(raw, "M116"))

    def test_curated_references_are_short_and_reject_unknown_sensitive_fields(self):
        value = curate({"path": "/Users/example/artifacts/modal-research/2026-11-04/research/task101/notes.json"})
        self.assertEqual(value, {"path": "research/task101/notes.json"})
        with self.assertRaises(ValueError):
            curate({"description": "path /data/raven/private"})
        with self.assertRaises(ValueError):
            curate({"token": "sensitive fixture"})


if __name__ == "__main__":
    unittest.main()
