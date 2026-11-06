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

``boss_bonus`` (``--boss-bonus B``, wave 17, any objective; default 0 = off, rewards bit-identical): a one-time reward
B at every boss kill from act 2 on: the step whose observation increments ``aux.act_index`` from k to k + 1 with
k >= 1 (the act-2 boss is behind the act 2 -> 3 transition; it is paid at that transition, after the boss's rewards
screen), and the Victory terminal (the act-3 / final boss kill: B on top of ``win_bonus``). The act 1 -> 2 transition
(k = 0) pays nothing. Earned kills only: with ``aux.is_combat`` in the aux block (the trainer's), a transition pays only
when the episode observed a combat in act k (so a save / anchor start on the Rewards screen of a boss the human killed,
or past it, is not paid for that kill; counted as ``boss2_transitions_unearned``). B is part of the reward and so of an episode's ``return``, never of ``progress`` /
``start_progress`` / ``progress_gain`` (the protocol's); each finished episode carries ``boss_bonus_total`` (B paid in
it) and ``boss2_kills`` (act-2+ boss kills: transitions k >= 1 plus a win), and ``pop_boss_counts`` gives the trainer's
per-update counts. A save start that begins in act 2 is paid on its act-2 boss kill like any other episode; a slot's
first observation (reset / auto-reset / save / anchor start) never pays (no transition is observed there).
``win``: the env's own reward (1 at a Victory terminal, else 0; CONTRACTS.md D5).
``AcqShaping`` (``--acq-shaping``, any objective): potential-based shaping for deck size, relics and upgrades on top
of the objective's reward; the trainer logs it apart, and episode returns / progress stay the objective's.
``HpShaping`` (``--hp-shaping-coef KAPPA``, any objective): potential-based HP shaping, phi = KAPPA * hp / max_hp
(0 at an episode end); composes with ``AcqShaping`` (``ShapingSet``), logged apart the same way.
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


BOSS_COUNTS = {"boss2_kills": 0, "boss2_kills_transition": 0, "boss2_kills_win": 0, "boss2_transitions_unearned": 0,
               "boss_bonus_sum": 0.0}


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
    objective: the return without the win bonus and the boss bonus). With ``boss_bonus`` != 0 also ``boss_bonus_total``
    and ``boss2_kills`` (module docstring).
    """

    def __init__(self, objective: str, aux_fields, last_episode_fields, num_envs: int, ascension: int = 0,
                 win_bonus: float = 1.0, shaped_floors: float = 48.0, boss_bonus: float = 0.0):
        if objective not in OBJECTIVES:
            raise ValueError(f"objective {objective!r}: expected one of {OBJECTIVES}")
        self.objective = objective
        self.win_bonus = float(win_bonus)
        self.boss_bonus = float(boss_bonus)
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
        # --boss-bonus bookkeeping: per slot (current episode) and per window (pop_boss_counts)
        self.boss_paid = np.zeros(num_envs, dtype=np.float64)
        self.boss_kills = np.zeros(num_envs, dtype=np.int64)
        self._win_counts = dict(BOSS_COUNTS)
        fields = list(aux_fields)
        self._comb = fields.index("is_combat") if "is_combat" in fields else None
        self.seen_combat = np.ones(num_envs, dtype=bool)      # a combat observed in the current act of the episode

    def pop_boss_counts(self) -> dict:
        """Act-2+ boss kills since the last call (``boss2_kills`` = ``boss2_kills_transition`` (act k -> k + 1 with
        k >= 1) + ``boss2_kills_win`` (Victory terminals)) and the boss bonus paid (``boss_bonus_sum``); counted with
        ``boss_bonus`` 0 too (diagnostic; nothing is paid then). Clears the counters."""
        out = dict(self._win_counts)
        out["boss_bonus_sum"] = round(out["boss_bonus_sum"], 6)
        self._win_counts = dict(BOSS_COUNTS)
        return out

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
        self.boss_paid[:] = 0.0
        self.boss_kills[:] = 0
        if self._comb is not None:
            self.seen_combat = np.asarray(aux)[:, self._comb] != 0
        self.start_entry[:] = -1
        self._mark_starts(np.ones(self.n, dtype=bool), start_entry)

    def step(self, env_reward: np.ndarray, done: np.ndarray, last_episode: np.ndarray,
             aux: np.ndarray, start_entry: np.ndarray | None = None) -> tuple[np.ndarray, list[dict]]:
        done = np.asarray(done, dtype=bool)
        old_p = self.progress.copy()
        old_floor = self.tracker.prev[:, 1].copy()
        old_act = self.tracker.prev[:, 0].copy()
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
        # act-2+ boss kills: a live slot's act k -> k + 1 with k >= 1 after a combat of act k in this episode, or a
        # Victory terminal (the final boss)
        new_act = self.tracker.prev[:, 0]
        trans = ~done & (old_act >= 1) & (new_act == old_act + 1)
        kill = trans & self.seen_combat
        if self._comb is not None:
            comb = np.asarray(aux)[:, self._comb] != 0
            self.seen_combat = np.where(done | (new_act != old_act), comb, self.seen_combat | comb)
        n_un = int(np.count_nonzero(trans & ~kill))
        if n_un:
            self._win_counts["boss2_transitions_unearned"] += n_un
        n_tr = int(np.count_nonzero(kill))
        wins = [e["slot"] for e in finished if e["win"]]
        if n_tr or wins:
            kill = kill.copy()
            kill[wins] = True
            self.boss_kills += kill
            W = self._win_counts
            W["boss2_kills_transition"] += n_tr
            W["boss2_kills_win"] += len(wins)
            W["boss2_kills"] += n_tr + len(wins)
            if self.boss_bonus != 0.0:
                r[kill] += self.boss_bonus
                self.boss_paid[kill] += self.boss_bonus
                W["boss_bonus_sum"] += self.boss_bonus * (n_tr + len(wins))
        self.ret += r
        for e in finished:
            i = e["slot"]
            e["return"] = float(self.ret[i])
            e.update(start_entry=int(self.start_entry[i]), start_floor=int(self.start_floor[i]),
                     start_act=int(self.start_act[i]), start_progress=float(self.start_progress[i]))
            e["progress_gain"] = None if e.get("progress") is None else float(e["progress"]) - e["start_progress"]
            if self.boss_bonus != 0.0:
                e["boss_bonus_total"] = float(self.boss_paid[i])
                e["boss2_kills"] = int(self.boss_kills[i])
        self.ret[done] = 0.0
        self.boss_paid[done] = 0.0
        self.boss_kills[done] = 0
        self._mark_starts(done, start_entry)        # auto-reset: the next episodes' first observations
        return r.astype(np.float32), finished


ACQ_DEFAULTS = {"deck": 0.003, "relic": 0.01, "upgrade": 0.004}


class _PotentialShaping:
    """Potential-based shaping over a potential ``phi`` of an observation: per step ``F = gamma * phi(s') - phi(s)``
    with ``phi(s') = 0`` when the step ended the episode (terminal or truncation; ``s'`` is then the next episode's
    first observation, whose potential becomes the slot's new ``phi``). With gamma = 1 an episode's shaping telescopes
    to ``phi(last) - phi(first) = -phi(first)`` (Ng et al. 1999: the optimal policy is unchanged).

    ``reset(aux, deck)`` after ``env.reset()`` (and at the start of every training session, a resume included: the
    potential is read from the session's first observations, so no shaping step crosses a session boundary);
    ``step(done, aux, deck)`` after every ``env.step`` with the new observation -> ``(F float32 [N], shaping sums of
    the episodes that ended {slot: sum})``. Subclasses define ``potential(aux, deck) -> float64 [N]``."""

    def __init__(self, num_envs: int, gamma: float = 1.0):
        self.gamma = float(gamma)
        self.phi = np.zeros(num_envs, dtype=np.float64)       # potential of each slot's current observation
        self.sum = np.zeros(num_envs, dtype=np.float64)       # shaping accumulated in each slot's current episode

    def potential(self, aux: np.ndarray, deck: np.ndarray) -> np.ndarray:   # pragma: no cover - abstract
        raise NotImplementedError

    def reset(self, aux: np.ndarray, deck: np.ndarray) -> None:
        self.phi = self.potential(aux, deck)
        self.sum[:] = 0.0

    def step(self, done: np.ndarray, aux: np.ndarray, deck: np.ndarray) -> tuple[np.ndarray, dict[int, float]]:
        done = np.asarray(done, dtype=bool)
        nxt = self.potential(aux, deck)
        f = np.where(done, 0.0, self.gamma * nxt) - self.phi
        self.sum += f
        ended = {int(i): float(self.sum[i]) for i in np.flatnonzero(done)}
        self.sum[done] = 0.0
        self.phi = nxt
        return f.astype(np.float32), ended


class AcqShaping(_PotentialShaping):
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
        super().__init__(num_envs, gamma)
        self._deck_n, self._relic_n = a["deck_n"], a["relic_n"]
        self._cid, self._up = deck_cols["card_id"], deck_cols["upgrade"]
        self.coefs = (float(deck), float(relic), float(upgrade))

    def potential(self, aux: np.ndarray, deck: np.ndarray) -> np.ndarray:
        c_deck, c_relic, c_up = self.coefs
        upgrades = (deck[..., self._up].astype(np.float64) * (deck[..., self._cid] != 0)).sum(1)
        return (c_deck * np.minimum(aux[:, self._deck_n].astype(np.float64) - 10.0, 12.0)
                + c_relic * aux[:, self._relic_n].astype(np.float64) + c_up * upgrades)


class HpShaping(_PotentialShaping):
    """Potential-based HP shaping (``--hp-shaping-coef KAPPA``), added to the objective's reward.

    Potential of a live observation: ``phi = KAPPA * aux.hp / aux.max_hp`` (0 when ``max_hp <= 0``), 0 after a terminal
    or a truncation (death, win, step cap, floor stall). Per step ``F = gamma * phi(s') - phi(s)``: with gamma = 1 a
    step that loses 20% of max HP pays ``-0.2 KAPPA``, a heal / rest pays it back, and a max-HP change moves the ratio
    only. An episode's HP shaping telescopes to ``-KAPPA * hp_0 / max_hp_0`` (its first observation; ``-KAPPA`` for a
    fresh start at full HP), whatever happens in between: at a death the final step pays ``-phi(last)``, at a win it
    pays back ``-KAPPA * hp_T / max_hp_T`` (terminal potential 0, the conventional form; the optimal policy is
    unchanged). A harvest-anchor / save start begins from the restored state's ratio (the env's next observation of
    the slot), never from the previous episode's value."""

    def __init__(self, aux_fields, num_envs: int, gamma: float = 1.0, *, coef: float):
        a = {f: i for i, f in enumerate(aux_fields)}
        missing = [f for f in ("hp", "max_hp") if f not in a]
        if missing:
            raise ValueError(f"the library's aux block lacks {missing} (needed by --hp-shaping-coef)")
        super().__init__(num_envs, gamma)
        self._hp, self._max = a["hp"], a["max_hp"]
        self.coef = float(coef)

    def potential(self, aux: np.ndarray, deck: np.ndarray | None = None) -> np.ndarray:
        hp = aux[:, self._hp].astype(np.float64)
        mx = aux[:, self._max].astype(np.float64)
        ratio = np.divide(hp, mx, out=np.zeros_like(hp), where=mx > 0)
        return self.coef * ratio


class ShapingSet:
    """The trainer's shaping: the potential-based parts that are on (``acq`` = ``AcqShaping``, ``hp`` = ``HpShaping``),
    summed. Same interface as one part (``reset`` / ``step`` / ``phi``); with one part ``step`` returns that part's
    ``F`` unchanged (so ``--acq-shaping`` alone trains on exactly the rewards it did before ``HpShaping`` existed).
    Potential-based terms compose: the sum is the shaping of the summed potential. Per part it keeps the episode sums
    of the last step (``ended_parts[name]`` {slot: sum}) and the window totals since ``pop_window`` (sum, slot-steps)."""

    def __init__(self, parts: dict):
        self.parts = {k: v for k, v in parts.items() if v is not None}
        if not self.parts:
            raise ValueError("ShapingSet needs at least one part")
        self.ended_parts: dict[str, dict[int, float]] = {k: {} for k in self.parts}
        self._win = {k: [0.0, 0] for k in self.parts}

    @property
    def phi(self) -> np.ndarray:
        ps = list(self.parts.values())
        return ps[0].phi if len(ps) == 1 else sum(p.phi for p in ps)

    def part_phi(self, name: str) -> np.ndarray:
        return self.parts[name].phi

    def reset(self, aux: np.ndarray, deck: np.ndarray) -> None:
        for p in self.parts.values():
            p.reset(aux, deck)
        self.ended_parts = {k: {} for k in self.parts}

    def step(self, done: np.ndarray, aux: np.ndarray, deck: np.ndarray) -> tuple[np.ndarray, dict[int, float]]:
        fs, ended = [], {}
        for k, p in self.parts.items():
            f, e = p.step(done, aux, deck)
            fs.append(f)
            self.ended_parts[k] = e
            w = self._win[k]
            w[0] += float(f.sum(dtype=np.float64))
            w[1] += int(f.size)
            for i, v in e.items():
                ended[i] = ended.get(i, 0.0) + v
        return (fs[0] if len(fs) == 1 else np.sum(fs, axis=0, dtype=np.float32)), ended

    def pop_window(self) -> dict[str, tuple[float, int]]:
        out = {k: (w[0], w[1]) for k, w in self._win.items()}
        self._win = {k: [0.0, 0] for k in self.parts}
        return out


def annotate_shaping(fin: list[dict], ended: dict[int, float], shaper, lo: int = 0) -> None:
    """Shaping fields of the finished episodes `fin` (their ``slot`` = local slot + `lo`): ``shaping`` (all parts),
    ``shaped_return`` = ``return`` + ``shaping`` and, with ``HpShaping`` on, ``hp_shaping``."""
    hp = shaper.ended_parts.get("hp") if isinstance(shaper, ShapingSet) else None
    for e in fin:
        e["shaping"] = ended.get(e["slot"] - lo, 0.0)
        e["shaped_return"] = e["return"] + e["shaping"]
        if hp is not None:
            e["hp_shaping"] = hp.get(e["slot"] - lo, 0.0)


def make_shaper(aux_fields, deck_cols: dict, num_envs: int, gamma: float, *, acq: bool, acq_coefs: dict,
                hp_coef: float) -> ShapingSet | None:
    """The trainer's shaping for `num_envs` slots, or None when no part is on (``acq`` off and ``hp_coef`` == 0: the
    trainer's rewards are then exactly the objective's)."""
    parts = {"acq": AcqShaping(aux_fields, deck_cols, num_envs, gamma, **acq_coefs) if acq else None,
             "hp": HpShaping(aux_fields, num_envs, gamma, coef=hp_coef) if float(hp_coef) != 0.0 else None}
    return ShapingSet(parts) if any(v is not None for v in parts.values()) else None
