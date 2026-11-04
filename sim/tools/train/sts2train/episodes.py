"""``<run>/episodes.jsonl``: one row per training episode, and the hook registry other modules attach fields with.

Rows (``EpisodeLog.write``; one JSON object per line, appended once per update, flushed):

    {"update": 12, "session": 1, "slot": 3, "seed_text": "SP0-SILENT-1041", "n": 1041, "result": "death",
     "act": 1, "floor": 9, "steps": 212, "progress": 0.156863, "return": 0.156863, "end_kind": 1, "cycles": 0,
     ...hook fields}

- ``update``: the PPO update whose rollout the episode ended in; ``session``: the trainer session (1 = fresh run).
- ``seed_text`` / ``n``: the start's seed text and stream index (``--start-mode stream``); in pool mode the pool
  entry's name (its fresh seed text, or ``<save file>#copy<c>``) and ``n`` null.
- ``result``: ``win`` / ``death`` (terminal), ``trunc`` (step cap), ``stall`` (floor step cap: FLOOR_STALL),
  ``error`` (SIM_ERROR / CONTENT_GAP), and two rows that are not finished episodes: ``dropped`` (in flight at the
  checkpoint a session resumed from: it never finishes; ``steps`` = its decisions so far, ``act`` / ``floor`` /
  ``progress`` null; written by the resumed session with ``update`` = the checkpoint's update) and ``start_failed``
  (a stream seed whose template / first observation failed).
- ``progress``: protocol v0.2 progress at the end (what ``sts2vec eval`` would score); ``return``: the objective's
  summed reward (unshaped).
- ``cycles``: the verified UI revisit cycles (``sts2train.cycles``: rest-site / shop prompt -> cancel back to the same
  observation) the episode closed, counted with or without ``--cycle-cost`` (null on ``dropped`` / ``start_failed``
  rows); the per-cycle rows (path, room, floor) are in ``cycles.jsonl``.

A resume in the same run directory drops the rows past the checkpoint's update and that update's ``dropped`` rows
(the resumed session writes them again), like metrics.jsonl, so repeated resumes never duplicate a row. Size: ~170
bytes per row (about 110 KB per update at 2,048 envs x 64 steps); ``--no-episodes-log`` turns the file off.

Hooks (``EPISODE_HOOKS``, a process-wide registry; ``train(..., episode_hooks=[...])`` adds per-call ones): a hook is
``fn(episode) -> dict | None`` and is called once per finished episode, in the rollout step the episode ended in
(right after ``EpisodeRewards.step``, before that slot's next episode advances), with the finished-episode dict of
``ProgressTracker.finished`` / ``EpisodeRewards.step``: ``slot``, ``win``, ``floor``, ``act``, ``steps``, ``end_kind``,
``progress`` (+ the other protocol fields), ``return``, ``start_entry``, ``start_floor``, ``start_act``,
``start_progress``, ``progress_gain``, plus ``seed_text``, ``seed_n``, ``update`` (the update being collected) and
``t`` (the rollout step). The returned fields are merged into the episode's row (``fields``; a hook must not reuse the
base keys). Example (a per-slot loop counter that a rollout-side monitor keeps)::

    from sts2train.episodes import EPISODE_HOOKS
    EPISODE_HOOKS.register("ui_cycles", lambda e: {"ui_cycles": monitor.pop_slot(e["slot"])})

Hooks are not called for ``dropped`` / ``start_failed`` rows.
"""

from __future__ import annotations

from typing import Callable, Mapping

from .runlog import JsonlWriter

END_RESULT = {2: "trunc", 3: "error", 4: "error", 5: "stall"}     # EndKind (sim/Sts2Sim.Vec/Status.cs) -> result
BASE_KEYS = ("update", "session", "slot", "seed_text", "n", "result", "act", "floor", "steps", "progress", "return",
             "end_kind", "cycles")

EpisodeHook = Callable[[dict], "Mapping | None"]


def episode_result(e: dict) -> str:
    """win / death / trunc / stall / error of a finished-episode dict (``end_kind``: EndKind)."""
    k = int(e.get("end_kind", 0))
    if k == 1:
        return "win" if e.get("win") else "death"
    return END_RESULT.get(k, "win" if e.get("win") else "other")


class EpisodeHooks:
    """Named per-episode field callbacks (module docstring)."""

    def __init__(self):
        self._hooks: dict[str, EpisodeHook] = {}

    def register(self, name: str, fn: EpisodeHook) -> None:
        if not callable(fn):
            raise TypeError(f"episode hook {name!r} is not callable")
        self._hooks[str(name)] = fn

    def unregister(self, name: str) -> None:
        self._hooks.pop(str(name), None)

    def names(self) -> list[str]:
        return list(self._hooks)

    def __bool__(self) -> bool:
        return bool(self._hooks)

    def fields(self, e: dict, extra=()) -> dict:
        """The merged fields of every hook (registered ones first, then ``extra`` callables) for episode ``e``."""
        out: dict = {}
        for fn in (*self._hooks.values(), *extra):
            got = fn(e)
            if got:
                bad = [k for k in got if k in BASE_KEYS]
                if bad:
                    raise ValueError(f"episode hook {fn!r} returned base keys {bad}")
                out.update(got)
        return out


EPISODE_HOOKS = EpisodeHooks()


def _r(x, nd: int = 6):
    return None if x is None else round(float(x), nd)


class EpisodeLog:
    """``episodes.jsonl`` writer (module docstring). ``keep_upto`` = the update a session resumed from (None = new
    file / keep everything)."""

    def __init__(self, path, *, keep_upto: int | None = None, enabled: bool = True):
        self.enabled = bool(enabled)
        self.path = path
        self.rows = 0
        self._w = None
        if self.enabled:
            def resumed_drops(r):       # this update's dropped rows: the resumed session writes them again
                return int(r.get("update", 0) or 0) == keep_upto and r.get("result") == "dropped"

            self._w = JsonlWriter(path, keep_upto=keep_upto, drop=None if keep_upto is None else resumed_drops)

    def _write(self, row: dict) -> None:
        if self._w is not None:
            self._w.write(row)
            self.rows += 1

    def write_update(self, update: int, session: int, episodes: list[dict]) -> None:
        if self._w is None:
            return
        for e in episodes:
            row = {"update": update, "session": session, "slot": e["slot"], "seed_text": e.get("seed_text"),
                   "n": e.get("seed_n"), "result": episode_result(e), "act": e.get("act"), "floor": e.get("floor"),
                   "steps": e.get("steps"), "progress": _r(e.get("progress")), "return": _r(e.get("return")),
                   "end_kind": e.get("end_kind"), "cycles": int(e.get("cycles", 0))}
            row.update(e.get("fields") or {})
            self._write(row)

    def write_special(self, update: int, session: int, rows: list[dict], result: str) -> None:
        """``dropped`` / ``start_failed`` rows: {slot, seed_text, n, steps?, reason?} each."""
        if self._w is None:
            return
        for r in rows:
            row = {"update": update, "session": session, "slot": r.get("slot"), "seed_text": r.get("seed_text"),
                   "n": r.get("n"), "result": result, "act": None, "floor": None, "steps": r.get("steps"),
                   "progress": None, "return": None, "end_kind": None, "cycles": None}
            if r.get("reason"):
                row["reason"] = r["reason"]
            self._write(row)

    def close(self) -> None:
        if self._w is not None:
            self._w.close()
            self._w = None
