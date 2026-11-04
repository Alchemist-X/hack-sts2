import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from modal_research.artifact_adapter import ArtifactMirror, HISTORY_LIMIT, _metric_history
from modal_research.contracts import canonical
from modal_research.manifests import file_digest, publish, verify


class MetricHistoryTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)

    def test_real_rows_only_with_torn_line_duplicates_bad_numbers_and_fence(self):
        path = self.root / "metrics.jsonl"
        rows = [
            {"update": 1, "env_steps": 10, "time": "2026-11-04T12:00:00+00:00", "entropy": 1.2,
             "secret": "do not copy"},
            {"update": 1, "env_steps": 11, "entropy": 1.1},  # duplicate
            {"update": 2, "env_steps": 20, "time": "local time", "entropy": float("nan"),
             "value_loss": float("inf"), "approx_kl": 0.02},
            {"update": 3, "env_steps": 31, "entropy": 0.5},  # beyond checkpoint decisions
            {"update": 4, "env_steps": 40, "entropy": 0.4},  # beyond checkpoint update
        ]
        with path.open("wb") as handle:
            for row in rows:
                handle.write((json.dumps(row) + "\n").encode())
            handle.write(b'{"update":2,"env_steps":21')  # interrupted append
        history = _metric_history(path, 3, 30)
        self.assertEqual([(r["update"], r["env_steps"]) for r in history["rows"]], [(1, 10), (2, 20)])
        self.assertEqual(history["rows"][0]["at"], "2026-11-04T12:00:00+00:00")
        self.assertIsNone(history["rows"][1]["at"])
        self.assertNotIn("secret", canonical(history))
        self.assertNotIn("entropy", history["rows"][1])
        self.assertNotIn("value_loss", history["rows"][1])
        self.assertEqual(history["rows"][1]["approx_kl"], 0.02)

    def test_bounded_history_marks_actual_omissions_without_inventing_rows(self):
        path = self.root / "metrics.jsonl"
        with path.open("w") as handle:
            for update in range(1, 800):
                row = {"update": update, "env_steps": update * 131072, "time": "2026-11-04T12:00:00Z",
                       "entropy": 0.75, "value_loss": 0.125, "progress_mean": 0.1,
                       "gpu_mem_peak_mb": 50000, "env_steps_per_s": 2000.0}
                handle.write(json.dumps(row) + "\n")
        history = _metric_history(path, 799, 799 * 131072)
        self.assertLessEqual(len(canonical(history).encode()), HISTORY_LIMIT)
        self.assertEqual(len(history["rows"]) + history["omitted_rows"], 799)
        self.assertEqual(history["rows"][-1]["update"], 799)
        self.assertGreater(history["omitted_rows"], 0)

    def test_legacy_and_new_training_manifests_verify_and_hash_history(self):
        identity = {"kind": "train", "job_key": "job:1"}
        metadata = {"resume_mode": "fresh_episodes", "decisions": 10, "updates": 1, "cursor": 1}
        names = {role: role + ".json" for role in
                 ("model", "optimizer", "scheduler", "rng", "seed_cursor", "metrics")}
        for role, name in names.items():
            (self.root / name).write_text(role)
        old_sha = publish(self.root, identity, metadata, names)
        old = verify(self.root, old_sha, identity)
        self.assertEqual(set(old["files"]), set(names))
        new_root = self.root / "new"
        new_root.mkdir()
        for role, name in names.items():
            (new_root / name).write_text(role)
        history = {"version": 1, "series": "train", "omitted_rows": 0,
                   "rows": [{"update": 1, "env_steps": 10, "at": None, "entropy": 0.5}]}
        (new_root / "history.json").write_text(canonical(history))
        sha = publish(new_root, identity, metadata, {**names, "history": "history.json"})
        manifest = verify(new_root, sha, identity)
        self.assertEqual(manifest["files"]["history"]["sha256"], file_digest(new_root / "history.json"))
        self.assertEqual(manifest["files"]["history"]["size"], (new_root / "history.json").stat().st_size)
        (new_root / "history.json").write_text("corrupt")
        with self.assertRaisesRegex(ValueError, "checksum"):
            verify(new_root, sha, identity)

        eval_root = self.root / "eval"
        eval_root.mkdir()
        for name in ("metrics.json", "per_seed.json", "history.json"):
            (eval_root / name).write_text("{}")
        with self.assertRaisesRegex(ValueError, "role set"):
            publish(eval_root, {"kind": "eval"}, {"decisions": 10},
                    {"metrics": "metrics.json", "per_seed": "per_seed.json", "history": "history.json"})

    def test_oversized_history_rejected_before_hash_or_remote_read(self):
        identity = {"kind": "train", "job_key": "job:1"}
        metadata = {"resume_mode": "fresh_episodes", "decisions": 10, "updates": 1, "cursor": 1}
        roles = ("model", "optimizer", "scheduler", "rng", "seed_cursor", "metrics")
        names = {role: role + ".json" for role in roles}
        for role, name in names.items():
            (self.root / name).write_text(role)
        history_path = self.root / "history.json"
        history_path.write_bytes(b"x" * (HISTORY_LIMIT + 1))
        original_digest = file_digest

        def hash_without_history(path):
            self.assertNotEqual(Path(path).name, "history.json")
            return original_digest(path)

        with patch("modal_research.manifests.file_digest", side_effect=hash_without_history):
            with self.assertRaisesRegex(ValueError, "history artifact exceeds"):
                publish(self.root, identity, metadata, {**names, "history": "history.json"})

        history_path.write_text("{}")
        sha = publish(self.root, identity, metadata, {**names, "history": "history.json"})
        history_path.write_bytes(b"x" * (HISTORY_LIMIT + 1))
        with patch("modal_research.manifests.file_digest", side_effect=hash_without_history):
            with self.assertRaisesRegex(ValueError, "history artifact exceeds"):
                verify(self.root, sha, identity)

        remote_files = {"history": {"name": "history.json", "size": HISTORY_LIMIT + 1,
                                    "sha256": "a" * 64}}
        remote_files.update({role: {"name": name, "size": 1, "sha256": "b" * 64}
                             for role, name in names.items()})
        remote_ready = canonical({"version": 1, "identity": identity, "metadata": metadata,
                                  "files": remote_files}).encode()
        reads = []

        def read_file(name):
            reads.append(name)
            if name.endswith("ready.json"):
                return iter([remote_ready])
            raise AssertionError("oversized history must not be read")

        mirror_root = self.root / "mirror"
        mirror_root.mkdir()
        mirror = ArtifactMirror(mirror_root, SimpleNamespace(read_file=read_file))
        job = SimpleNamespace(payload={"spec": {"actual_param_count": 1}})
        reference = {"generation": "gen1", "manifest_hash": hashlib.sha256(remote_ready).hexdigest()}
        with patch("modal_research.artifact_adapter.artifact_identity", return_value=identity):
            with self.assertRaisesRegex(ValueError, "byte bound"):
                mirror.copy(job, reference)
        self.assertEqual(reads, ["job:1/gen1/ready.json"])

    def test_exact_resume_accepts_old_and_new_history_role_sets(self):
        identity = {"kind": "train", "job_key": "job:1"}
        metadata = {"resume_mode": "exact", "decisions": 10, "updates": 1, "cursor": 1}
        roles = ("model", "optimizer", "scheduler", "rng", "seed_cursor", "metrics", "environment")
        for include_history in (False, True):
            root = self.root / ("exact-new" if include_history else "exact-old")
            root.mkdir()
            names = {role: role + ".json" for role in roles}
            if include_history:
                names["history"] = "history.json"
            for role, name in names.items():
                (root / name).write_text(role)
            sha = publish(root, identity, metadata, names)
            self.assertEqual(set(verify(root, sha, identity)["files"]), set(names))


if __name__ == "__main__":
    unittest.main()
