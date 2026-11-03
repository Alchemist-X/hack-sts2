"""Training objectives on sts2vec, computed on the Python side from fields the env already exposes.

``protocol`` (default): per-step reward = the change of the STS2-Bench v0.2 progress metric (protocol §4.2) plus
``win_bonus`` at a Victory terminal. The progress of a state is ``sts2vec.protocol.compute`` -- the very function
``sts2vec eval`` (``sts2vec.evaluate``) scores games with -- on (act, floor) with the act's start floor
(``aux.total_floor - aux.act_floor``) and room count (``aux.act_rooms``); a finished episode is scored from the slot's
previous ``aux`` plus ``last_episode`` (win / act / floor) exactly like ``ProgressTracker``. The rewards of an episode
telescope: they sum to ``progress(final) - progress(first observation) (+ win_bonus on a win)``, and a fresh run
starts at progress 0 (act 1, no room cleared), so the return of a fresh episode is its protocol progress (+ bonus).
Truncated episodes (step cap, floor step cap) end with the progress of their last state, as the protocol scores
``capped`` / ``stuck`` games.

``win``: the env's own reward (1 at a Victory terminal, else 0; CONTRACTS.md D5).
``shaped``: LEGACY smoke objective: ``win`` + delta(total_floor) / ``shaped_floors`` per step (floor count, not the
protocol metric; it allows stall loops; kept for reproducing the 2026-11-02 smoke).
"""

from __future__ import annotations

import numpy as np
from sts2vec import protocol as PG

from .progress import ProgressTracker

OBJECTIVES = ("protocol", "win", "shaped")


def state_progress(act_index: int, total_floor: int, act_floor: int, act_rooms: int, ascension: int = 0) -> float:
    """Protocol progress of a live (unfinished) state from its aux fields: what ``sts2vec eval`` would score if the
    game ended here (as ``loss`` / ``capped`` / ``stuck``)."""
    act = int(act_index) + 1
    starts = {act: int(total_floor) - int(act_floor)} if act > 1 else {}
    rooms = {act: int(act_rooms)} if int(act_rooms) > 0 else {}
    return float(PG.compute(None, act, int(total_floor), ascension, starts, rooms)["progress"])


class EpisodeRewards:
    """Per-slot reward of one objective plus finished-episode bookkeeping for a VecEnv with auto-reset.

    Call ``reset(aux)`` after ``env.reset()`` and ``step(env_reward, done, last_episode, aux)`` after every
    ``env.step`` (``aux`` = the new observation's aux block). ``step`` returns ``(reward float32 [N], finished)``
    where ``finished`` is ``ProgressTracker.finished`` (one dict per ended episode: slot, win, floor, act, steps,
    end_kind and the protocol progress fields) with ``return`` (the episode's summed objective reward) added.
    """

    def __init__(self, objective: str, aux_fields, last_episode_fields, num_envs: int, ascension: int = 0,
                 win_bonus: float = 1.0, shaped_floors: float = 48.0):
        if objective not in OBJECTIVES:
            raise ValueError(f"objective {objective!r}: expected one of {OBJECTIVES}")
        self.objective = objective
        self.win_bonus = float(win_bonus)
        self.shaped_floors = float(shaped_floors)
        self.ascension = ascension
        self.tracker = ProgressTracker(aux_fields, last_episode_fields, num_envs, ascension)
        self._le_floor = self.tracker._l["floor"]
        self.n = num_envs
        self.progress = np.zeros(num_envs, dtype=np.float64)   # progress of each slot's current state
        self._key = np.full((num_envs, 4), -1, dtype=np.int64)  # tracker.prev row `progress` was computed from
        self.ret = np.zeros(num_envs, dtype=np.float64)        # objective return of each slot's current episode

    def _refresh(self) -> None:
        prev = self.tracker.prev           # act_index, total_floor, act_floor, act_rooms of the latest observation
        changed = np.flatnonzero((prev != self._key).any(1))
        for i in changed:                   # progress only moves when the floor / act / map changes: few slots per step
            pa, ptot, pfl, prooms = (int(x) for x in prev[i])
            self.progress[i] = state_progress(pa, ptot, pfl, prooms, self.ascension)
        self._key[changed] = prev[changed]

    def reset(self, aux: np.ndarray) -> None:
        self.tracker.observe(aux)
        self._key[:] = -1
        self._refresh()
        self.ret[:] = 0.0

    def step(self, env_reward: np.ndarray, done: np.ndarray, last_episode: np.ndarray,
             aux: np.ndarray) -> tuple[np.ndarray, list[dict]]:
        done = np.asarray(done, dtype=bool)
        old_p = self.progress.copy()
        old_floor = self.tracker.prev[:, 1].copy()
        finished = self.tracker.finished(done, last_episode)
        self.tracker.observe(aux)
        self._refresh()
        if self.objective == "protocol":
            r = self.progress - old_p                       # live slots: delta of the state progress
            for e in finished:                              # ended slots: final progress (protocol scoring) - last
                i = e["slot"]
                r[i] = float(e["progress"]) - old_p[i] + (self.win_bonus if e["win"] else 0.0)
        else:
            r = np.asarray(env_reward, dtype=np.float64).copy()
            if self.objective == "shaped":
                floor_now = self.tracker.prev[:, 1].copy()
                floor_now[done] = last_episode[done, self._le_floor]
                r += (floor_now - old_floor) / self.shaped_floors
        self.ret += r
        for e in finished:
            e["return"] = float(self.ret[e["slot"]])
        self.ret[done] = 0.0
        return r.astype(np.float32), finished
