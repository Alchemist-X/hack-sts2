"""Run-directory plumbing for long training runs: atomic writes, the per-update metrics log, checkpoint rotation.

Run directory layout (``sts2train train --out RUN_DIR``)::

    config.json            resolved TrainConfig + parameter count, device, library / encoder version, sessions
    status.json            rewritten (atomically) after every update: an external watcher's single source of truth
    metrics.jsonl          one JSON object per PPO update (append-only; a resume drops rows past the checkpoint)
    evals.jsonl            one row per protocol evaluation; the run directories live in evals/u<update>/
    checkpoints/ckpt-<update>.pt   the last K checkpoints (tmp file + rename); best.pt = best eval progress so far
                                   (unless --no-best); pinned-<update>.pt = the weights of a --eval-at evaluation
    cycles.jsonl           one row per UI revisit cycle closed in a rollout (sts2train.cycles)
    cycle_episodes.jsonl   per update: the finished episodes' slot, start entry and cycle count (aligned lists)
    summary.json           written when a session ends
    train.log              the CLI's log lines with timestamps (all sessions)
"""

from __future__ import annotations

import json
import math
import os
import re
import shutil
from pathlib import Path

import numpy as np

CKPT_RE = re.compile(r"^ckpt-(\d+)\.pt$")


def _clean(v):
    """JSON-safe copy: numpy scalars/arrays -> python, NaN/inf -> None, tuples -> lists, Paths -> str."""
    if isinstance(v, dict):
        return {str(k): _clean(x) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [_clean(x) for x in v]
    if isinstance(v, np.ndarray):
        return _clean(v.tolist())
    if isinstance(v, np.generic):
        v = v.item()
    if isinstance(v, float) and not math.isfinite(v):
        return None
    if isinstance(v, Path):
        return str(v)
    return v


def _fsync_dir(d: Path) -> None:
    try:
        fd = os.open(d, os.O_RDONLY)
    except OSError:
        return
    try:
        os.fsync(fd)
    except OSError:
        pass
    finally:
        os.close(fd)


def atomic_write_bytes(path: str | os.PathLike, data: bytes) -> None:
    """Write `data` to `path` through `<path>.tmp` + fsync + rename: readers see the old or the new file, never half."""
    path = Path(path)
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "wb") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)
    _fsync_dir(path.parent)


def atomic_write_json(path: str | os.PathLike, obj, indent: int | None = 1) -> None:
    atomic_write_bytes(path, (json.dumps(_clean(obj), indent=indent, allow_nan=False) + "\n").encode("utf-8"))


def atomic_torch_save(obj, path: str | os.PathLike) -> None:
    import torch

    path = Path(path)
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "wb") as f:
        torch.save(obj, f)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)
    _fsync_dir(path.parent)


def read_jsonl(path: str | os.PathLike) -> list[dict]:
    """Rows of a JSONL file; a torn last line (killed mid-write) is ignored."""
    rows = []
    p = Path(path)
    if not p.exists():
        return rows
    with open(p, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return rows


class JsonlWriter:
    """Append-only JSONL log, one flushed line per `write`. `keep_upto=u` first drops rows whose `update` > u (a
    resumed run re-does those updates), rewriting the file atomically."""

    def __init__(self, path: str | os.PathLike, *, keep_upto: int | None = None, key: str = "update"):
        self.path = Path(path)
        if keep_upto is not None and self.path.exists():
            rows = [r for r in read_jsonl(self.path) if int(r.get(key, 0) or 0) <= keep_upto]
            atomic_write_bytes(self.path, "".join(json.dumps(_clean(r), allow_nan=False) + "\n" for r in rows)
                               .encode("utf-8"))
        self._f = open(self.path, "a", encoding="utf-8")

    def write(self, row: dict) -> None:
        self._f.write(json.dumps(_clean(row), allow_nan=False) + "\n")
        self._f.flush()

    def close(self) -> None:
        self._f.close()


MetricsWriter = JsonlWriter


class CheckpointManager:
    """`checkpoints/ckpt-<update:07d>.pt`, written atomically; keeps the newest `keep`; `best.pt` is never pruned."""

    def __init__(self, run_dir: str | os.PathLike, keep: int = 3):
        self.dir = Path(run_dir) / "checkpoints"
        self.keep = max(1, int(keep))

    def path_for(self, update: int) -> Path:
        return self.dir / f"ckpt-{int(update):07d}.pt"

    def list(self) -> list[tuple[int, Path]]:
        if not self.dir.is_dir():
            return []
        out = []
        for p in self.dir.iterdir():
            m = CKPT_RE.match(p.name)
            if m:
                out.append((int(m.group(1)), p))
        return sorted(out)

    def latest(self) -> Path | None:
        ck = self.list()
        return ck[-1][1] if ck else None

    def save(self, state: dict, update: int) -> Path:
        self.dir.mkdir(parents=True, exist_ok=True)
        path = self.path_for(update)
        atomic_torch_save(state, path)
        self.prune()
        return path

    def prune(self) -> None:
        for _, p in self.list()[:-self.keep]:
            try:
                p.unlink()
            except FileNotFoundError:
                pass

    def mark_best(self, path: Path) -> Path:
        best = self.dir / "best.pt"
        tmp = best.with_name(best.name + ".tmp")
        shutil.copyfile(path, tmp)
        os.replace(tmp, best)
        return best

    def pin(self, path: Path, update: int) -> Path:
        """Copy checkpoint `path` to ``pinned-<update:07d>.pt`` (a fixed-endpoint eval's weights): never pruned,
        never taken by `latest`."""
        dst = self.dir / f"pinned-{int(update):07d}.pt"
        if Path(path).resolve() != dst.resolve():
            tmp = dst.with_name(dst.name + ".tmp")
            shutil.copyfile(path, tmp)
            os.replace(tmp, dst)
        return dst
