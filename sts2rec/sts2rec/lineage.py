"""Merge recorder part directories belonging to one resumed STS2 run."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from .canonical import build_canonical
from .errors import ManifestError
from .session import Manifest, load_manifest


def _lineage_key(manifest: Manifest) -> tuple[float, str, str]:
    return (manifest.run.start_time, manifest.run.seed, manifest.run.character)


def discover_lineage(session_dir: str | Path) -> list[Path]:
    """Find sibling parts with the same start_time and seed, ordered by part."""
    anchor = Path(session_dir)
    manifest = load_manifest(anchor)
    found: list[tuple[int, Path]] = []
    for candidate in anchor.parent.iterdir():
        if not candidate.is_dir():
            continue
        try:
            other = load_manifest(candidate)
        except Exception:
            continue
        if _lineage_key(other) == _lineage_key(manifest):
            found.append((other.part, candidate))
    return [path for _, path in sorted(found)]


def group_lineages(root: str | Path) -> list[list[tuple[Path, Manifest]]]:
    """Group every session directory under ``root`` into resume lineages.

    Same matching rule as :func:`discover_lineage` (start_time, seed and
    character), but one manifest read per directory instead of one scan per
    anchor. Each lineage is ordered by part (then directory name); lineages
    are ordered by start_time, seed, character. Directories whose manifest
    cannot be loaded are skipped.
    """
    groups: dict[tuple[float, str, str], list[tuple[int, str, Path, Manifest]]] = {}
    for candidate in sorted(Path(root).iterdir()):
        if not candidate.is_dir():
            continue
        try:
            manifest = load_manifest(candidate)
        except ManifestError:
            continue
        groups.setdefault(_lineage_key(manifest), []).append(
            (manifest.part, candidate.name, candidate, manifest)
        )
    return [
        [(path, manifest) for _, _, path, manifest in sorted(members, key=lambda m: (m[0], m[1]))]
        for _, members in sorted(groups.items(), key=lambda item: item[0])
    ]


def build_lineage(session_dir: str | Path) -> dict[str, Any]:
    """Build one canonical trajectory across every discovered resume part."""
    parts = discover_lineage(session_dir)
    trajectories = [build_canonical(path) for path in parts]
    steps: list[dict[str, Any]] = []
    prelude_events: list[dict[str, Any]] = []
    for path, trajectory in zip(parts, trajectories):
        part = trajectory["meta"]["part"]
        prelude_events.extend(
            {**event, "part": part, "run_id": path.name}
            for event in trajectory["prelude_events"]
        )
        for local_index, step in enumerate(trajectory["steps"]):
            merged = {**step, "step_idx": len(steps)}
            merged["info"] = {
                **step["info"],
                "part": part,
                "run_id": path.name,
                "part_step_idx": local_index,
            }
            steps.append(merged)
    anchor_meta = dict(trajectories[0]["meta"])
    terminal_meta = trajectories[-1]["meta"]
    anchor_meta.update(
        {
            "run_id": parts[0].name,
            "parts": [path.name for path in parts],
            "part_count": len(parts),
            "result": terminal_meta["result"],
            "incomplete": terminal_meta["incomplete"],
        }
    )
    return {"meta": anchor_meta, "prelude_events": prelude_events, "steps": steps}
