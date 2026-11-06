"""Read-only standard-library verification; no training runtime imports."""
from pathlib import Path
import ast
import csv
import hashlib
import json


def main():
    directory = Path(__file__).resolve().parent
    root = directory.parents[1]
    manifest = json.loads((directory / "source-manifest.json").read_text())
    seen = set()
    for entry in manifest["files"]:
        relative = Path(entry["path"])
        assert not relative.is_absolute() and ".." not in relative.parts, relative
        assert entry["path"] not in seen, entry["path"]
        seen.add(entry["path"])
        path = root / relative
        assert not path.is_symlink(), path
        data = path.read_bytes()
        assert hashlib.sha256(data).hexdigest() == entry["sha256"], path
        assert len(data) == entry["bytes"], path
        if path.suffix == ".py":
            ast.parse(data, filename=str(relative))
    for path in directory.rglob("*.py"):
        ast.parse(path.read_bytes(), filename=str(path.relative_to(root)))
    summary = json.loads((directory / "progress.json").read_text())
    points = summary["task099"]["points"]
    with (directory / "learning-curve.csv").open(newline="") as handle:
        rows = list(csv.DictReader(handle))
    assert len(points) == len(rows) == 5
    for point, row in zip(points, rows):
        for name, value in row.items():
            parsed = float(value) if name == "mean_protocol_progress" else int(value)
            assert point[name] == parsed, (name, point, row)
    assert points[-1]["decisions"] == 200015872
    assert all(p["games"] == 256 and p["wins"] == 0 for p in points)
    assert summary["task104"]["quality_evaluation"] is None
    assert summary["goal"]["achieved"] is False and summary["goal"]["blind_used"] is False
    for task in ("task099", "task104"):
        recipe = json.loads((directory / "releases" / task / "recipe.json").read_text())
        sampling = recipe["sampling"]
        assert sampling["envs"] * sampling["rollout"] == sampling["rollout_batch"] == 131072
        assert sampling["rollout_batch"] // sampling["minibatch"] == 32
        assert recipe["ppo_config"]["acq_shaping"] is False
    print(f"PASS: {len(seen)} manifested files; Python syntax; five-point curve; recipe consistency")


if __name__ == "__main__":
    main()
