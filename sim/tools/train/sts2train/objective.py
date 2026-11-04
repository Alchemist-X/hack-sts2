"""Training objectives on sts2vec, computed on the Python side from fields the env already exposes.

``protocol`` (default): per-step reward = the change of the STS2-Bench v0.2 progress metric (protocol §4.2) plus
``win_bonus`` at a Victory terminal. The progress of a state is ``sts2vec.protocol.compute`` -- the very function
``sts2vec eval`` (``sts2vec.evaluate``) scores games with -- on (act, floor) with the act's start floor
(``aux.total_floor - aux.act_floor``) and room count (``aux.act_rooms``); a finished episode is scored from the slot's
previous ``aux`` plus ``last_episode`` (win / act / floor) exactly like ``ProgressTracker``. The rewards of an episode
telescope: they sum to ``progress(final) - progress(first observation) (+ win_bonus on a win)``, and a fresh run
starts at progress 0 (act 1, no room cleared), so the return of a fresh episode is its protocol progress (+ bonus).
An episode started from a save (``--start-saves``, ``sts2train.startpool``) starts at the progress of the save's
state, so its return is the progress gained from there (``progress_gain`` of the finished-episode record).
Truncated episodes (step cap, floor step cap) end with the progress of their last state, as the protocol scores
``capped`` / ``stuck`` games.

``win``: the env's own reward (1 at a Victory terminal, else 0; CONTRACTS.md D5).
``AcqShaping`` (``--acq-shaping``, any objective): potential-based shaping for deck size, relics and upgrades on top
of the objective's reward; the trainer logs it apart, and episode returns / progress stay the objective's.
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

    Call ``reset(aux[, start_entry])`` after ``env.reset()`` and ``step(env_reward, done, last_episode, aux[,
    start_entry])`` after every ``env.step`` (``aux`` = the new observation's aux block, ``start_entry`` =
    ``info["start_entry"]``). ``step`` returns ``(reward float32 [N], finished)`` where ``finished`` is
    ``ProgressTracker.finished`` (one dict per ended episode: slot, win, floor, act, steps, end_kind and the protocol
    progress fields) with ``return`` (the episode's summed objective reward) added, plus the episode's start:
    ``start_entry`` (the pool entry it started from; -1 when no ``start_entry`` was passed), ``start_floor``,
    ``start_act`` (1-based), ``start_progress`` (protocol progress of its first observation: 0 for a fresh run, the
    save's progress for a save start) and ``progress_gain`` = ``progress - start_progress`` (for the protocol
    objective: the return without the win bonus).
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
        # first observation of each slot's current episode (a save start begins mid-run)
        self.start_entry = np.full(num_envs, -1, dtype=np.int64)
        self.start_progress = np.zeros(num_envs, dtype=np.float64)
        self.start_floor = np.zeros(num_envs, dtype=np.int64)
        self.start_act = np.ones(num_envs, dtype=np.int64)

    def _refresh(self) -> None:
        prev = self.tracker.prev           # act_index, total_floor, act_floor, act_rooms of the latest observation
        changed = np.flatnonzero((prev != self._key).any(1))
        for i in changed:                   # progress only moves when the floor / act / map changes: few slots per step
            pa, ptot, pfl, prooms = (int(x) for x in prev[i])
            self.progress[i] = state_progress(pa, ptot, pfl, prooms, self.ascension)
        self._key[changed] = prev[changed]

    def _mark_starts(self, slots: np.ndarray, start_entry: np.ndarray | None) -> None:
        """Record the current observation of `slots` (bool mask) as their episodes' first observation."""
        prev = self.tracker.prev
        self.start_progress[slots] = self.progress[slots]
        self.start_floor[slots] = prev[slots, 1]
        self.start_act[slots] = prev[slots, 0] + 1
        if start_entry is not None:
            self.start_entry[slots] = np.asarray(start_entry, dtype=np.int64)[slots]

    def reset(self, aux: np.ndarray, start_entry: np.ndarray | None = None) -> None:
        self.tracker.observe(aux)
        self._key[:] = -1
        self._refresh()
        self.ret[:] = 0.0
        self.start_entry[:] = -1
        self._mark_starts(np.ones(self.n, dtype=bool), start_entry)

    def step(self, env_reward: np.ndarray, done: np.ndarray, last_episode: np.ndarray,
             aux: np.ndarray, start_entry: np.ndarray | None = None) -> tuple[np.ndarray, list[dict]]:
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
            i = e["slot"]
            e["return"] = float(self.ret[i])
            e.update(start_entry=int(self.start_entry[i]), start_floor=int(self.start_floor[i]),
                     start_act=int(self.start_act[i]), start_progress=float(self.start_progress[i]))
            e["progress_gain"] = None if e.get("progress") is None else float(e["progress"]) - e["start_progress"]
        self.ret[done] = 0.0
        self._mark_starts(done, start_entry)        # auto-reset: the next episodes' first observations
        return r.astype(np.float32), finished


ACQ_DEFAULTS = {"deck": 0.003, "relic": 0.01, "upgrade": 0.004}


class AcqShaping:
    """Potential-based acquisition shaping (``--acq-shaping``), added to the objective's reward.

    Potential of an observation: ``phi = c_deck * min(deck_n - 10, 12) + c_relic * relic_n + c_upgrade * upgrades``
    (``aux.deck_n``, ``aux.relic_n``; upgrades = the summed upgrade levels of the deck's cards), and 0 after a terminal
    or a truncation. Per step the shaping reward is ``F = gamma * phi(s') - phi(s)`` (``phi(s') = 0`` when the step ended
    the episode), so it telescopes: with gamma = 1 an episode's shaping sums to ``phi(last) - phi(first) = -phi(first)``
    (a constant of its start state), and the optimal policy is unchanged (Ng et al. 1999); taking a card / relic /
    upgrade is paid immediately and the debt falls due at the end.

    ``reset(aux, deck)`` after ``env.reset()``; ``step(done, aux, deck)`` after every ``env.step`` with the new
    observation (the next episode's first one for a finished slot) -> ``(F float32 [N], shaping sums of the episodes
    that ended {slot: sum})``."""

    def __init__(self, aux_fields, deck_cols: dict, num_envs: int, gamma: float = 1.0, *,
                 deck: float = ACQ_DEFAULTS["deck"], relic: float = ACQ_DEFAULTS["relic"],
                 upgrade: float = ACQ_DEFAULTS["upgrade"]):
        a = {f: i for i, f in enumerate(aux_fields)}
        missing = [f for f in ("deck_n", "relic_n") if f not in a]
        if missing:
            raise ValueError(f"the library's aux block lacks {missing} (needed by --acq-shaping)")
        self._deck_n, self._relic_n = a["deck_n"], a["relic_n"]
        self._cid, self._up = deck_cols["card_id"], deck_cols["upgrade"]
        self.gamma = float(gamma)
        self.coefs = (float(deck), float(relic), float(upgrade))
        self.phi = np.zeros(num_envs, dtype=np.float64)       # potential of each slot's current observation
        self.sum = np.zeros(num_envs, dtype=np.float64)       # shaping accumulated in each slot's current episode

    def potential(self, aux: np.ndarray, deck: np.ndarray) -> np.ndarray:
        c_deck, c_relic, c_up = self.coefs
        upgrades = (deck[..., self._up].astype(np.float64) * (deck[..., self._cid] != 0)).sum(1)
        return (c_deck * np.minimum(aux[:, self._deck_n].astype(np.float64) - 10.0, 12.0)
                + c_relic * aux[:, self._relic_n].astype(np.float64) + c_up * upgrades)

    def reset(self, aux: np.ndarray, deck: np.ndarray) -> None:
        self.phi = self.potential(aux, deck)
        self.sum[:] = 0.0

    def step(self, done: np.ndarray, aux: np.ndarray, deck: np.ndarray, *,
             final_phi: tuple[np.ndarray, np.ndarray] | None = None) -> tuple[np.ndarray, dict[int, float]]:
        """Apply next-state potential from sparse final slots at externally truncated steps."""
        done = np.asarray(done, dtype=bool)
        nxt = self.potential(aux, deck)
        continuation_phi = np.where(done, 0.0, nxt)
        if final_phi is not None:
            slots, potential = final_phi
            continuation_phi[slots] = potential
        f = self.gamma * continuation_phi - self.phi
        self.sum += f
        ended = {int(i): float(self.sum[i]) for i in np.flatnonzero(done)}
        self.sum[done] = 0.0
        self.phi = nxt
        return f.astype(np.float32), ended
