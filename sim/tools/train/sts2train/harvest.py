"""Self-anchor start pool (``sts2train train --harvest boss:N,elite:N,act2:N``): training episodes that start from
determinized clones of the policy's own combat entries.

At a combat's first decision inside a running, fresh-started training episode the trainer calls
``env.clone_determinized(slot, seed)``: a new pool entry with the same player view (identical observation bytes) and
the hidden state resampled (draw order, every RNG stream, monster AI, relic bag, event order; sim/VEC.md
"Determinized clones"). Entries are kept in per-kind FIFO rings of live pool entries; when a ring is full its oldest
entries are released (one ``env.pool.release(ids)`` call per step, ABI 2). Nothing schedules them: the env's own
auto-reset start sequence (a slot's k-th episode starts from the live entry ``(slot_seed + k) mod len(pool)``, in id
order) runs over the anchors like over every other entry, so they take a share of the *slots* that grows with their
count (the anchors are one contiguous block at the end of the id order).

Capture rule (``RULE``): ``aux.is_combat`` goes 0 -> 1 between two observations of one running episode (not at an
episode's first observation), the episode started from a fresh pool entry (``start_entry < base`` and not a save:
anchors are a re-weighting of the fresh on-policy state distribution, never of human saves or of other anchors), the
slot has no SIM_ERROR / CONTENT_GAP bit and a legal action. Kind from the map cell flagged ``map_nodes.current``:
``boss`` (MapPointType Boss), ``boss2`` (wave 17: a Boss cell with ``aux.act_index >= 1``, i.e. the act-2 and act-3
bosses; when a ``boss2`` ring exists these go there and ``boss`` keeps the act-1 boss, without one ``boss`` takes every
boss as before), ``elite`` (Elite), ``act2`` (any other combat with ``aux.act_index >= 1``: act 2-3
hallway and unknown-room fights; also bosses / elites in act 2+ when they have no ring of their own), ``any`` (every
combat entry, the act-1 hallway included, that no ring above takes; tests / smoke). Each candidate is kept with
probability ``prob`` (a generator seeded from (seed, update, step)), at most ``max_per_step`` per env step and at most
a ring's size per ring and step; the clone seed is sha256("harvest:<seed>:<update>:<step>:<slot>").

Stream mode (``--start-mode stream``, ``Harvester(..., stream=True)``): the pool holds only the stream's lookahead
entries (released after their one start) and the anchors, so ids are not split at a ``base``: an anchor is an id this
harvester issued (``is_anchor`` / ``kind_of`` look the id up). Nothing in the env's own start sequence runs (no
auto-reset): ``seedstream.StreamEnv.attach_anchors`` makes every slot whose episode ended start from a uniformly drawn
live anchor with probability ``harvest_start_share`` (``--harvest-start-share``), else from the next stream seed; an
anchor start consumes no stream index (the seed counter and the isolation guard only see stream seeds; an anchor
derives from a stream seed the guard already passed), and an anchor stays live after the start (only the ring's FIFO
releases it). Captures come from stream-started episodes only. ``anchor_source(eid)`` gives the source episode's seed
text and stream index (episodes.jsonl ``seed_text`` of an anchor-started episode, ``start_kind`` ``anchor:<kind>``).

Information parity: anchors are training-only start points; the clone is determinized (the policy cannot know the
hidden state it got), the observation the model reads is the source's byte for byte, and evaluation (fresh seeds,
single pass, greedy + loop breaker) is unchanged. Pool entries live in the training process: a resumed session starts
with empty rings.
"""

from __future__ import annotations

import hashlib
import time
from collections import deque

import numpy as np
from sts2vec import STATUS
from sts2vec.errors import Sts2VecError

KINDS = ("boss", "elite", "act2", "any", "boss2")   # "any" = every combat entry no other ring takes (tests / smoke);
#                                                 "boss2" (wave 17) = act 2+ bosses; appended so the codes of the
#                                                 older kinds and the canonical order of their specs are unchanged
BOSS, ELITE, ACT2, ANY, BOSS2 = range(len(KINDS))
ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
RULE = ("capture at a combat's first decision (aux.is_combat 0 -> 1 inside a running episode) of an episode started "
        "from a fresh pool entry (start_entry < base, not a save, not an anchor), slot without SIM_ERROR / "
        "CONTENT_GAP and with a legal action; kind by the map_nodes cell with current = 1: Boss -> boss, Elite -> "
        "elite, else act_index >= 1 -> act2; a Boss cell with act_index >= 1 goes to boss2 when that ring exists, "
        "else to boss; a kind without a ring falls back to act2 (act 2+), then to any; kept "
        "with probability prob, at most max_per_step per step and a ring's size per ring and step; clone = "
        "env.clone_determinized(slot, sha256('harvest:<seed>:<update>:<step>:<slot>')); full rings release their "
        "oldest entries (FIFO, one env.pool.release call per step); starts come from the env's auto-reset sequence")
RULE_STREAM = ("capture at a combat's first decision (aux.is_combat 0 -> 1 inside a running episode) of an episode "
               "started from a stream seed (not an anchor), slot without SIM_ERROR / CONTENT_GAP and with a legal "
               "action; kind by the map_nodes cell with current = 1: Boss -> boss, Elite -> elite, else act_index >= 1 "
               "-> act2; a Boss cell with act_index >= 1 goes to boss2 when that ring exists, else to boss; a kind "
               "without a ring falls back to act2 (act 2+), then to any; kept with probability prob, "
               "at most max_per_step per step and a ring's size per ring and step; clone = "
               "env.clone_determinized(slot, sha256('harvest:<seed>:<update>:<step>:<slot>')); full rings release "
               "their oldest entries (FIFO, one env.pool.release call per step); a finished slot starts from a "
               "uniformly drawn live anchor with probability harvest_start_share, else from the next stream seed")


def parse_harvest(spec) -> dict[str, int]:
    """``"boss:1536,elite:512,act2:1024"`` -> ring sizes by kind (in KINDS order); ``""`` / ``"off"`` / ``"none"`` /
    None -> {} (off). Raises ValueError for an unknown kind, a repeated kind or a size that is not a positive int."""
    if spec is None:
        return {}
    text = str(spec).strip()
    if text.lower() in ("", "off", "none"):
        return {}
    rings: dict[str, int] = {}
    for part in text.split(","):
        part = part.strip()
        if not part:
            continue
        kind, sep, size = part.partition(":")
        kind = kind.strip().lower()
        if kind not in KINDS:
            raise ValueError(f"--harvest {spec!r}: unknown kind {kind!r} (expected kind:size with kind in "
                             f"{', '.join(KINDS)})")
        if kind in rings:
            raise ValueError(f"--harvest {spec!r}: kind {kind!r} given twice")
        try:
            n = int(size.strip()) if sep else 0
        except ValueError:
            n = 0
        if n < 1:
            raise ValueError(f"--harvest {spec!r}: {part!r} needs a ring size >= 1 (kind:size)")
        rings[kind] = n
    if not rings:
        raise ValueError(f"--harvest {spec!r}: no ring (expected e.g. boss:1536,elite:512,act2:1024)")
    return {k: rings[k] for k in KINDS if k in rings}


def is_boss2(code: int, act: int) -> bool:
    """The ``boss2`` predicate: a Boss cell (detected kind BOSS) with ``aux.act_index >= 1`` (act-2 / act-3 bosses)."""
    return int(code) == BOSS and int(act) >= 1


def format_harvest(rings: dict[str, int]) -> str:
    """The canonical spec of `rings` (KINDS order); "" for none."""
    return ",".join(f"{k}:{int(rings[k])}" for k in KINDS if k in rings)


def _u64(text: str) -> int:
    return int.from_bytes(hashlib.sha256(text.encode("utf-8")).digest()[:8], "little")


def clone_seed(seed: int, upd: int, t: int, slot: int) -> int:
    """uint64 determinization seed of the anchor captured at (update, step, slot)."""
    return _u64(f"harvest:{seed}:{upd}:{t}:{slot}")


class Harvester:
    """Self-anchors of one training env (module docstring). Call ``reset(obs, start_entry)`` after ``env.reset()`` and
    ``step(obs, done, start_entry, status, upd, t)`` after every ``env.step`` (``obs`` / ``start_entry`` /
    ``status`` = the step's new observation, ``info["start_entry"]`` and status bits). ``pop_stats()`` returns and
    clears the per-update counters (``harvest_*`` keys of a metrics row); ``kind_of`` / ``is_anchor`` classify
    episode start entries for ``startpool.split_stats``.

    ``starts`` = the trainer's ``StartPool`` (its saves are never harvested) or None (fresh entries only). The pool's
    size at construction is ``base``: ids ``>= base`` are anchors (only the harvester adds entries during training;
    ids are never reused)."""

    def __init__(self, env, rings: dict[str, int], prob: float = 0.25, max_per_step: int = 64, seed: int = 0,
                 starts=None, log=print, stream: bool = False, state: dict | None = None):
        rings = dict(rings)
        bad = sorted(set(rings) - set(KINDS))
        if not rings or bad:
            raise ValueError(f"harvest rings {rings}: expected kinds among {KINDS}")
        if any(int(v) < 1 for v in rings.values()):
            raise ValueError(f"harvest rings {rings}: sizes must be >= 1")
        if not 0.0 < float(prob) <= 1.0:
            raise ValueError(f"harvest_prob {prob} must be in (0, 1]")
        if int(max_per_step) < 1:
            raise ValueError(f"harvest_max_per_step {max_per_step} must be >= 1")
        if not getattr(env, "supports_pool_release", False):
            raise Sts2VecError("--harvest needs env.pool.release (sts2vec ABI 2): rebuild Sts2Sim.Vec")
        env.lib.require("sts2vec_clone_determinized")
        self.env = env
        self.ring_size = {k: int(rings[k]) for k in KINDS if k in rings}
        self.kinds = tuple(self.ring_size)
        self.rings = {k: deque() for k in self.kinds}
        self.prob = float(prob)
        self.max_per_step = int(max_per_step)
        self.seed = int(seed)
        self.starts = starts
        self.log = log
        self.stream = bool(stream)
        self.base = len(env.pool)                     # pool mode: ids >= base are anchors (stream: not used)
        S = env.schema
        aux, mn = list(S["aux"].fields), list(S["map_nodes"].fields)
        missing = [f for f in ("is_combat", "act_index") if f not in aux] + \
                  [f"map_nodes.{f}" for f in ("point_type", "current") if f not in mn]
        if missing:
            raise ValueError(f"--harvest: the library's observation lacks {missing}")
        self._c_comb, self._c_act = aux.index("is_combat"), aux.index("act_index")
        self._c_pt, self._c_cur = mn.index("point_type"), mn.index("current")
        mt = S.vocab.enums["MapPointType"]
        self._pt_boss, self._pt_elite = int(mt.index_of("Boss")), int(mt.index_of("Elite"))
        n = int(env.num_envs)
        self._prev = np.zeros(n, dtype=bool)          # in combat at the previous observation
        self._start = np.full(n, -1, dtype=np.int64)  # start entry of each slot's current episode
        self._anchor = np.zeros(n, dtype=bool)        # ... and whether it is an anchor
        self._valid = np.zeros(n, dtype=bool)         # the current observation has a legal action
        self._codes = np.full(1024, -1, dtype=np.int8)   # ring kind of anchor id base + i (-1: none)
        self._issued = 0                                 # highest anchor offset + 1 (pool) / anchors issued (stream)
        self._ids = np.zeros(1024, dtype=np.int64)       # stream: issued anchor ids (ascending) and their kinds
        self._id_codes = np.zeros(1024, dtype=np.int8)
        self._source: dict[int, tuple] = {}              # stream: live anchor id -> (source seed text, stream index)
        self._reset_counts()
        self.total = {"candidates": 0, "captured": 0, "released": 0, "clone_s": 0.0, "decisions": 0,
                      "anchor_decisions": 0, "anchor_starts": 0, "anchor_starts_in_combat": 0,
                      "captured_by_kind": {k: 0 for k in self.kinds}, "released_by_kind": {k: 0 for k in self.kinds}}
        # the same counters over every session of the run (checkpoint ``harvest``; a resume starts with empty rings)
        self.run_total = _run_totals(state, self.kinds)

    # ------------------------------------------------------------------------------------------------ bookkeeping

    def _reset_counts(self) -> None:
        self.n = {"candidates": 0, "captured": 0, "released": 0, "clone_s": 0.0, "decisions": 0,
                  "anchor_decisions": 0, "anchor_starts": 0, "anchor_starts_in_combat": 0}
        self.n_kind = {k: [0, 0] for k in self.kinds}     # captured, released this update

    def _set_code(self, eid: int, code: int) -> None:
        if self.stream:
            k = self._issued
            if k and int(eid) <= int(self._ids[k - 1]):
                raise RuntimeError(f"harvest: clone_determinized returned id {eid} after {self._ids[k - 1]}")
            if k >= len(self._ids):
                self._ids = np.concatenate([self._ids, np.zeros(len(self._ids), dtype=np.int64)])
                self._id_codes = np.concatenate([self._id_codes, np.zeros(len(self._id_codes), dtype=np.int8)])
            self._ids[k], self._id_codes[k] = int(eid), code
            self._issued = k + 1
            return
        off = int(eid) - self.base
        if off < 0:
            raise RuntimeError(f"harvest: clone_determinized returned id {eid} below the base pool size {self.base}")
        if off >= len(self._codes):
            grown = np.full(max(2 * len(self._codes), off + 1), -1, dtype=np.int8)
            grown[: len(self._codes)] = self._codes
            self._codes = grown
        self._codes[off] = code
        self._issued = max(self._issued, off + 1)

    def is_anchor(self, start_entry) -> np.ndarray:
        """Per start entry: an anchor (pool mode: id >= base; stream mode: an id this harvester issued)."""
        if self.stream:
            return self.kind_of(start_entry) >= 0
        return np.asarray(start_entry, dtype=np.int64) >= self.base

    def kind_of(self, start_entry) -> np.ndarray:
        """Ring kind code (index into KINDS) of each start entry; -1 = not an anchor."""
        if self.stream:
            e = np.asarray(start_entry, dtype=np.int64)
            ids = self._ids[: self._issued]
            j = np.clip(np.searchsorted(ids, e), 0, max(len(ids) - 1, 0))
            out = np.full(e.shape, -1, dtype=np.int8)
            if len(ids):
                hit = ids[j] == e
                out[hit] = self._id_codes[: self._issued][j[hit]]
            return out
        e = np.asarray(start_entry, dtype=np.int64) - self.base
        out = np.full(e.shape, -1, dtype=np.int8)
        ok = (e >= 0) & (e < self._issued)
        out[ok] = self._codes[e[ok]]
        return out

    def live(self) -> dict[str, int]:
        return {k: len(r) for k, r in self.rings.items()}

    def live_ids(self) -> np.ndarray:
        """Every live anchor id (rings in KINDS order, oldest first): the candidates of a stream-mode anchor start."""
        return np.fromiter((e for k in self.kinds for e in self.rings[k]), dtype=np.int64)

    def anchor_source(self, eid: int) -> tuple:
        """(source seed text, source stream index, kind) of a live stream-mode anchor."""
        text, n = self._source.get(int(eid), (None, None))
        c = int(self.kind_of([int(eid)])[0])
        return text, n, (KINDS[c] if c >= 0 else None)

    # ------------------------------------------------------------------------------------------------ the hook

    def reset(self, obs: dict, start_entry) -> None:
        """After ``env.reset()``: every slot starts an episode at this observation (nothing is captured there)."""
        self._prev = obs["aux"][:, self._c_comb] != 0
        self._start = np.asarray(start_entry, dtype=np.int64).copy()
        self._valid = np.asarray(obs["cand_n"]) > 0
        anchor = self._anchor = self.is_anchor(self._start)
        self.n["anchor_starts"] += int(anchor.sum())
        self.n["anchor_starts_in_combat"] += int((anchor & self._prev).sum())

    def _ring_for(self, code: int, act: int) -> str | None:
        """The ring a candidate of detected kind `code` (BOSS / ELITE / ACT2, -1 = act-1 hallway) goes to."""
        prefs = []
        if code == BOSS and is_boss2(code, act):
            prefs.append("boss2")
        if code >= 0:
            prefs.append(KINDS[code])
        if act >= 1:
            prefs.append("act2")
        prefs.append("any")
        for k in prefs:
            if k in self.ring_size:
                return k
        return None

    def step(self, obs: dict, done, start_entry, status=None, upd: int = 0, t: int = 0) -> int:
        """After ``env.step``: count the step's decisions (anchor share), the episodes that just started from anchors,
        and capture the candidates of this observation. Returns the number of anchors captured."""
        n = self.n
        start = np.asarray(start_entry, dtype=np.int64)
        done = np.asarray(done, dtype=bool)
        inc = obs["aux"][:, self._c_comb] != 0
        n["decisions"] += int(self._valid.sum())
        n["anchor_decisions"] += int((self._valid & self._anchor).sum())
        anchor_now = self.is_anchor(start)
        anchor_new = done & anchor_now                     # auto-reset / stream slots that started from an anchor
        n["anchor_starts"] += int(anchor_new.sum())
        n["anchor_starts_in_combat"] += int((anchor_new & inc).sum())
        if self.stream:                                    # stream: every non-anchor start is a stream seed
            cand = inc & ~self._prev & ~done & (start >= 0) & ~anchor_now
        else:
            cand = inc & ~self._prev & ~done & (start >= 0) & (start < self.base)
        if self.starts is not None and cand.any():
            cand &= ~self.starts.is_save(start)
        if status is not None:
            cand &= (np.asarray(status) & ERR_BITS) == 0
        cand &= np.asarray(obs["cand_n"]) > 0
        self._prev = inc.copy()
        self._start = start.copy()
        self._anchor = anchor_now
        self._valid = np.asarray(obs["cand_n"]) > 0
        idx = np.flatnonzero(cand)
        if len(idx) == 0:
            return 0
        return self._capture(obs, idx, upd, t)

    def _capture(self, obs: dict, idx: np.ndarray, upd: int, t: int) -> int:
        mn = obs["map_nodes"][idx]
        cur = mn[..., self._c_cur] != 0
        ncur = cur.reshape(len(idx), -1).sum(1)
        pt = np.where(cur, mn[..., self._c_pt], 0).reshape(len(idx), -1).max(1).astype(np.int64)
        pt[ncur != 1] = -1                                   # no single current cell: kind unknown
        act = obs["aux"][idx, self._c_act].astype(np.int64)
        code = np.full(len(idx), -1, dtype=np.int64)
        code[pt == self._pt_boss] = BOSS
        code[pt == self._pt_elite] = ELITE
        code[(code < 0) & (act >= 1)] = ACT2
        ring = [self._ring_for(int(c), int(a)) for c, a in zip(code, act)]
        self.n["candidates"] += sum(r is not None for r in ring)
        if all(r is None for r in ring):
            return 0
        rng = np.random.default_rng(_u64(f"harvest-pick:{self.seed}:{upd}:{t}"))
        u = rng.random(len(idx))
        order = rng.permutation(len(idx))
        per_ring = {k: 0 for k in self.kinds}
        chosen = []
        for j in order:
            r = ring[j]
            if r is None or u[j] >= self.prob or per_ring[r] >= self.ring_size[r]:
                continue
            if len(chosen) >= self.max_per_step:
                break
            per_ring[r] += 1
            chosen.append(int(j))
        if not chosen:
            return 0
        t0 = time.perf_counter()
        release: list[int] = []
        for j in sorted(chosen, key=lambda j: int(idx[j])):
            slot, r = int(idx[j]), ring[j]
            eid = int(self.env.clone_determinized(slot, clone_seed(self.seed, upd, t, slot)))
            self._set_code(eid, KINDS.index(r))
            if self.stream:
                self._source[eid] = self._slot_source(slot)
            q = self.rings[r]
            q.append(eid)
            self.n_kind[r][0] += 1
            while len(q) > self.ring_size[r]:
                release.append(q.popleft())
                self.n_kind[r][1] += 1
        if release:
            self.env.pool.release(release)               # all or nothing, one call per step
            for e in release:
                self._source.pop(int(e), None)
        self.n["captured"] += len(chosen)
        self.n["released"] += len(release)
        self.n["clone_s"] += time.perf_counter() - t0
        return len(chosen)

    def _slot_source(self, slot: int) -> tuple:
        """(seed text, stream index) of the episode running on `slot` (a StreamEnv's slot bookkeeping)."""
        texts, ns = getattr(self.env, "slot_text", None), getattr(self.env, "slot_n", None)
        text = texts[slot] if texts is not None else None
        n = int(ns[slot]) if ns is not None and int(ns[slot]) >= 0 else None
        return text, n

    # ------------------------------------------------------------------------------------------------ reporting

    def pop_stats(self) -> dict:
        """The ``harvest_*`` keys of this update's metrics row (counters since the last call), then clears them:
        candidates (detected entries that had a ring), captured / released (total and per ring kind), live ring
        sizes, clone_s (seconds in clone_determinized + release), step_share (decisions in anchor-started episodes /
        all decisions), starts / starts_in_combat (episodes that started from an anchor / with a first observation in
        combat)."""
        n = self.n
        out = {"harvest_candidates": n["candidates"], "harvest_captured": n["captured"],
               "harvest_released": n["released"]}
        for k in self.kinds:
            out[f"harvest_captured_{k}"] = self.n_kind[k][0]
            out[f"harvest_released_{k}"] = self.n_kind[k][1]
        live = self.live()
        out["harvest_live"] = sum(live.values())
        out.update({f"harvest_live_{k}": v for k, v in live.items()})
        out.update(harvest_clone_s=round(n["clone_s"], 4),
                   harvest_step_share=(n["anchor_decisions"] / n["decisions"]) if n["decisions"] else None,
                   harvest_anchor_decisions=n["anchor_decisions"], harvest_starts=n["anchor_starts"],
                   harvest_starts_in_combat=n["anchor_starts_in_combat"])
        for T in (self.total, self.run_total):
            for key in ("candidates", "captured", "released", "clone_s", "decisions", "anchor_decisions",
                        "anchor_starts", "anchor_starts_in_combat"):
                T[key] += n[key]
            for k in self.kinds:
                T["captured_by_kind"][k] = T["captured_by_kind"].get(k, 0) + self.n_kind[k][0]
                T["released_by_kind"][k] = T["released_by_kind"].get(k, 0) + self.n_kind[k][1]
        R = self.run_total
        out.update(harvest_captured_run=R["captured"], harvest_starts_run=R["anchor_starts"])
        self._reset_counts()
        return out

    def record(self) -> dict:
        """config.json ``harvest``: rings, prob, per-step cap, base pool size, the rule."""
        return {"spec": format_harvest(self.ring_size), "rings": dict(self.ring_size),
                "live_max": sum(self.ring_size.values()), "prob": self.prob, "max_per_step": self.max_per_step,
                "seed": self.seed, "base_pool_entries": self.base, "fresh_only": True, "clone": "determinized",
                "rule": RULE_STREAM if self.stream else RULE, "start_mode": "stream" if self.stream else "pool",
                "starts": "stream: harvest_start_share of the finished slots from a uniform live anchor" if self.stream
                else "pool: the env's auto-reset start sequence"}

    def totals(self) -> dict:
        """summary.json ``harvest``: session totals (call after the last ``pop_stats``)."""
        T = self.total
        return {**self.record(), "captured": T["captured"], "captured_by_kind": dict(T["captured_by_kind"]),
                "released": T["released"], "released_by_kind": dict(T["released_by_kind"]), "live": self.live(),
                "candidates": T["candidates"], "clone_s": round(T["clone_s"], 3),
                "step_share": (T["anchor_decisions"] / T["decisions"]) if T["decisions"] else None,
                "anchor_starts": T["anchor_starts"], "anchor_starts_in_combat": T["anchor_starts_in_combat"],
                "ids_issued": self._issued, "run_totals": self.state_dict()}

    def state_dict(self) -> dict:
        """Checkpoint ``harvest``: the run's counters over every session (the rings themselves are not kept: pool
        entries live in the training process, a resume starts with empty rings)."""
        R = self.run_total
        return {"spec": format_harvest(self.ring_size), **{k: (round(v, 3) if k == "clone_s" else v)
                                                           for k, v in R.items() if not isinstance(v, dict)},
                "captured_by_kind": dict(R["captured_by_kind"]), "released_by_kind": dict(R["released_by_kind"]),
                "sessions": R["sessions"]}

    def describe(self) -> str:
        rings = ", ".join(f"{k} {v}" for k, v in self.ring_size.items())
        where = (f"stream mode: a finished slot starts from a uniform live anchor at the --harvest-start-share, else "
                 f"from the next stream seed" if self.stream else
                 f"on a base pool of {self.base} entries; the env's auto-reset sequence starts episodes from them")
        src = "stream-started" if self.stream else "fresh-started"
        return (f"self-anchor starts: rings {rings} (at most {sum(self.ring_size.values())} live entries); "
                f"determinized clones of {src} episodes' combat entries (boss / elite cells, act 2+), kept with "
                f"probability {self.prob:g}, at most {self.max_per_step} per step; {where}"
                + (f"; run totals so far: {self.run_total['captured']} captured, {self.run_total['anchor_starts']} "
                   f"anchor starts over {self.run_total['sessions'] - 1} earlier sessions"
                   if self.run_total["sessions"] > 1 else ""))


def _run_totals(state: dict | None, kinds) -> dict:
    """The run counters of a checkpoint's ``harvest`` block (+ this session); zeros without one."""
    st = state or {}
    out = {k: (float if k == "clone_s" else int)(st.get(k, 0) or 0)
           for k in ("candidates", "captured", "released", "clone_s", "decisions", "anchor_decisions",
                     "anchor_starts", "anchor_starts_in_combat")}
    out["captured_by_kind"] = {k: int((st.get("captured_by_kind") or {}).get(k, 0)) for k in kinds}
    out["released_by_kind"] = {k: int((st.get("released_by_kind") or {}).get(k, 0)) for k in kinds}
    out["sessions"] = int(st.get("sessions", 0) or 0) + 1
    return out
