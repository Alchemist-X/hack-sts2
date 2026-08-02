"""Merge recorder part directories belonging to one resumed STS2 run."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from .canonical import build_canonical
from .session import load_manifest


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
        if (
            other.run.start_time == manifest.run.start_time
            and other.run.seed == manifest.run.seed
            and other.run.character == manifest.run.character
        ):
            found.append((other.part, candidate))
    return [path for _, path in sorted(found)]


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
