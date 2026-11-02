"""Protocol v0.2 progress of finished sts2vec episodes, from the `aux` block and `last_episode`.

The metric itself is `sts2eval.progress.compute` (the protocol's single implementation). Its per-act inputs come from
the aux columns added for this purpose (sim/VEC.md, aux): the act's start floor is `total_floor - act_floor` (act 1:
0) and the act's room count L is `act_rooms` (the current act map's last row + 1 = the play server's `map.nodes` max
row + 1). With auto-reset a finished slot's aux already shows the next episode, so the tracker keeps each slot's aux
from the previous call and scores the finished episode from it plus `last_episode` (win, floor, act).
"""

from __future__ import annotations

import numpy as np
from sts2eval import progress as PG


class ProgressTracker:
    def __init__(self, aux_fields, last_episode_fields, num_envs: int, ascension: int = 0):
        a = {f: i for i, f in enumerate(aux_fields)}
        missing = [f for f in ("total_floor", "act_index", "act_floor", "act_rooms") if f not in a]
        if missing:
            raise ValueError(f"the library's aux block lacks {missing}: rebuild Sts2Sim.Vec (needs the E3 aux columns)")
        self._a = a
        self._l = {f: i for i, f in enumerate(last_episode_fields)}
        self.ascension = ascension
        self.prev = np.zeros((num_envs, 4), dtype=np.int64)  # act_index, total_floor, act_floor, act_rooms

    def observe(self, aux: np.ndarray, mask: np.ndarray | None = None) -> None:
        a = self._a
        cur = np.stack([aux[:, a["act_index"]], aux[:, a["total_floor"]], aux[:, a["act_floor"]],
                        aux[:, a["act_rooms"]]], -1).astype(np.int64)
        if mask is None:
            self.prev[:] = cur
        else:
            self.prev[mask] = cur[mask]

    def score(self, slot: int, result: str, act0: int, floor: int) -> dict:
        """Progress fields of the episode that just ended on `slot` (act0 = 0-based act from last_episode)."""
        pa, ptot, pfl, prooms = (int(x) for x in self.prev[slot])
        act = act0 + 1
        starts, rooms = {}, {}
        if pa == act0:
            starts[act] = 0 if act == 1 else ptot - pfl
            if prooms > 0:
                rooms[act] = prooms
        out = PG.compute(result, act, floor, self.ascension, starts, rooms)
        out["act_start_floor"] = {str(k): v for k, v in starts.items()}
        out["act_rooms"] = {str(k): v for k, v in rooms.items()}
        return out

    def finished(self, done: np.ndarray, last_episode: np.ndarray, *, wins: np.ndarray | None = None) -> list[dict]:
        """One dict per slot with done[i]: slot, win, floor, act (1-based), steps, end_kind, progress fields."""
        l = self._l
        out = []
        for i in np.flatnonzero(done):
            le = last_episode[i]
            win = bool(le[l["win"]]) if wins is None else bool(wins[i])
            d = self.score(int(i), "win" if win else "loss", int(le[l["act"]]), int(le[l["floor"]]))
            d.update(slot=int(i), win=win, floor=int(le[l["floor"]]), act=int(le[l["act"]]) + 1,
                     steps=int(le[l["steps"]]), end_kind=int(le[l["end_kind"]]))
            out.append(d)
        return out
