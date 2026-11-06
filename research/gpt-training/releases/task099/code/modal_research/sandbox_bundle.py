"""Prepare a single immutable Sandbox input bundle from reviewed local bytes."""

from __future__ import annotations

import argparse
import ast
import json
import shutil
import subprocess
from pathlib import Path

from .artifact_adapter import verify_bundle
from .contracts import canonical, digest, validate_spec
from .manifests import file_digest
from .native_worker import Installation

SOURCES = {
    "sts2train": ("train", ("__init__", "cycles", "episodes", "evaluate", "features", "gae", "heads",
                            "isolation", "loopbreak", "model", "objective", "ppo", "progress",
                            "runlog", "seedstream", "startpool")),
    "sts2vec": ("vec", ("__init__", "env", "errors", "evaluate", "loader", "policies", "pool", "protocol", "schema")),
    "modal_research": (None, ("__init__", "adapters", "artifact_adapter", "broker", "contracts", "controller",
                              "lineage", "manifests", "modal_provider", "native_worker", "sandbox_provider")),
}


def prepare(repo, spec, seed_index, engine, dependencies, destination):
    validate_spec(spec)
    seed_index, engine, dependencies, destination = map(Path, (seed_index, engine, dependencies, destination))
    if file_digest(engine) != spec["engine_hash"] or file_digest(dependencies) != spec["dependency_hash"]:
        raise ValueError("native engine or dependencies differ from the freeze")
    index = json.loads(seed_index.read_bytes())
    if index["registration_fields"]["eval_protocol"] != spec["eval_protocol"]:
        raise ValueError("seed isolation protocol differs from the frozen experiment")
    seeds = {}
    for role in ("train", "dev", "blind"):
        entry = index["manifests"][role]
        sha = spec[f"{role}_seed_manifest_hash"]
        relative = Path("sealed" if role == "blind" else role) / f"{sha}.json"
        path = seed_index.parent / relative
        if entry["file"] != relative.as_posix() or entry["file_sha256"] != sha or file_digest(path) != sha:
            raise ValueError("seed input differs from the frozen experiment")
        seeds[sha] = path
    if set(seeds) != {spec[f"{role}_seed_manifest_hash"] for role in ("train", "dev", "blind")}:
        raise ValueError("expected three distinct frozen seed roles")

    destination.mkdir(parents=False, mode=0o700, exist_ok=False)
    code = destination / "code"
    from .transition_contract import positive
    extras = {"sts2train": ("transition_aux", "transition_state", "transition_vector", "transition_gpu_gate"),
              "modal_research": ("transition_contract",)}
    if spec.get('model_config', {}).get('critic_arch') == 'state_legal_candidates_detached_mean_v1':
        extras['sts2train'] += ('critic_diagnostics',)
    content = {}
    def source_bytes(package, group, name):
        source = Path("sim/tools") / group / package if group else Path("sim/tools") / package
        path = source / f"{name}.py"
        return subprocess.check_output(
            ["git", "-C", str(repo), "show", f"{spec['code_commit']}:{path.as_posix()}"],
            timeout=10, stderr=subprocess.DEVNULL)
    for package, (group, names) in SOURCES.items():
        for name in names:
            content[package, name] = source_bytes(package, group, name)
    available = []
    for package, names in extras.items():
        group = SOURCES[package][0]
        for name in names:
            try:
                content[package, name] = source_bytes(package, group, name)
                available.append(True)
            except subprocess.CalledProcessError:
                available.append(False)
    if positive(spec) or any(available):
        if not all(available):
            raise ValueError("transition bundle dependencies must exist together at the exact commit")
    else:
        # Old immutable source commits retain their inventory only when none of
        # their base modules imports a missing new dependency. No HEAD fallback.
        transition_dependencies = {name for names in extras.values() for name in names}
        for raw in content.values():
            for node in ast.walk(ast.parse(raw)):
                if isinstance(node, ast.Import):
                    names = [x.name.split(".")[-1] for x in node.names]
                elif isinstance(node, ast.ImportFrom):
                    names = ([node.module.split(".")[-1]] if node.module else []) + [x.name for x in node.names]
                else:
                    continue
                if transition_dependencies.intersection(names):
                    raise ValueError("legacy source imports an absent transition dependency")
    for (package, name), raw in content.items():
        target = code / package
        target.mkdir(parents=True, exist_ok=True)
        (target / f"{name}.py").write_bytes(raw)
    lib = destination / "lib"
    lib.mkdir()
    shutil.copyfile(engine, lib / "Sts2Sim.Vec.so")
    shutil.copyfile(dependencies, destination / "dependencies.lock")
    target_seeds = destination / "seeds"
    target_seeds.mkdir()
    for sha, path in seeds.items():
        shutil.copyfile(path, target_seeds / f"{sha}.json")
    manifest = destination / "bundle.json"
    manifest.write_text(canonical({
        "version": 1, "code_commit": spec["code_commit"], "image_digest": spec["image_digest"],
        "files": {path.relative_to(destination).as_posix(): file_digest(path)
                  for path in sorted(destination.rglob("*")) if path.is_file()},
    }), encoding="utf-8")
    checksum = file_digest(manifest)
    installation = Installation(spec["code_commit"], spec["image_digest"], lib / "Sts2Sim.Vec.so",
                                destination / "dependencies.lock", target_seeds, destination, destination)
    verify_bundle(installation, spec, destination, checksum)
    return {"config_hash": digest(spec), "bundle_hash": checksum, "volume_path": f"/bundles/{digest(spec)}"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("repo", "spec", "seed-index", "engine", "dependencies", "destination"):
        parser.add_argument(f"--{key}", type=Path, required=True)
    args = parser.parse_args()
    result = prepare(args.repo, json.loads(args.spec.read_bytes()), args.seed_index,
                     args.engine, args.dependencies, args.destination)
    print(canonical(result))


if __name__ == "__main__":
    main()
