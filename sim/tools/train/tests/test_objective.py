"""The protocol objective: an episode's summed reward is its protocol v0.2 progress (+ the win bonus on a win).

The expected values are computed the way ``sts2vec eval`` scores a game (``sts2vec.evaluate``): the act's start floor
from the act's first observation (``aux.total_floor - aux.act_floor``), the act's room count from the last observation
that reported one (``aux.act_rooms``), the final (act, floor) from ``last_episode``, then ``sts2vec.protocol.compute``.
"""

from __future__ import annotations

import numpy as np
import pytest
from sts2vec import PoolSpec, VecEnv, random_actions
from sts2vec import protocol as PG

from sts2train.objective import EpisodeRewards, state_progress

AUX = ["total_floor", "act_index", "hp", "act_floor", "act_rooms"]      # any order: looked up by name
LE = ["win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id"]
BONUS = 0.75


class EvalBook:
    """Per-game bookkeeping of sts2vec.evaluate (starts: first observation of the act; rooms: last seen)."""

    def __init__(self):
        self.starts: dict[int, int] = {}
        self.rooms: dict[int, int] = {}

    def observe(self, tf: int, ai: int, af: int, rooms: int) -> None:
        act = ai + 1
        self.starts.setdefault(act, tf - af)
        if rooms > 0:
            self.rooms[act] = rooms

    def score(self, result: str, act0: int, floor: int, ascension: int = 0) -> float:
        return PG.compute(result, act0 + 1, floor, ascension, self.starts, self.rooms)["progress"]


def _aux(tf, ai, af, rooms):
    a = np.zeros(len(AUX), dtype=np.float32)
    a[AUX.index("total_floor")], a[AUX.index("act_index")] = tf, ai
    a[AUX.index("act_floor")], a[AUX.index("act_rooms")] = af, rooms
    a[AUX.index("hp")] = 50
    return a


def _le(win, floor, act0, steps):
    r = np.zeros(len(LE), dtype=np.int32)
    r[LE.index("win")], r[LE.index("floor")], r[LE.index("act")], r[LE.index("steps")] = win, floor, act0, steps
    return r


def _timelines():
    """Per slot: initial observation, then one entry per step: ("obs", tf, ai, af, rooms) for a live observation or
    ("end", (win, floor, act0), result, (tf, ai, af, rooms) of the next episode's first observation)."""
    # slot 0: act 1 with a repeated floor (no reward), act-1 boss, act 2 (start floor 17, L 16), death on floor 22;
    #         then a second (auto-reset) episode that dies on floor 3
    s0 = [("obs", t, 0, t, 17) for t in (1, 2, 2, 3, 5, 8, 12, 16, 17)]
    s0 += [("obs", 17, 1, 0, 16), ("obs", 18, 1, 1, 16), ("obs", 20, 1, 3, 16), ("obs", 22, 1, 5, 16)]
    s0 += [("end", (0, 22, 1), "loss", (0, 0, 0, 17)), ("obs", 1, 0, 1, 17), ("obs", 3, 0, 3, 17),
           ("end", (0, 3, 0), "loss", (0, 0, 0, 17))]
    # slot 1: a win from act 3 (bonus added)
    s1 = [("obs", t, 0, t, 17) for t in (1, 4, 9, 17)] + [("obs", 17, 1, 0, 16), ("obs", 25, 1, 8, 16),
                                                       ("obs", 33, 2, 0, 15), ("obs", 40, 2, 7, 15),
                                                       ("obs", 47, 2, 14, 15)]
    s1 += [("end", (1, 48, 2), "win", (0, 0, 0, 17))] + [("obs", 1, 0, 1, 17)] * 4
    # slot 2: GOLDEN_COMPASS (act 1 map grows to 18 rooms mid-act), then truncated (step cap) on floor 9
    s2 = [("obs", t, 0, t, 17) for t in (1, 2, 3, 4, 5)] + [("obs", t, 0, t, 18) for t in (6, 7, 8, 9)]
    s2 += [("obs", 9, 0, 9, 18)] * 3 + [("end", (0, 9, 0), "capped", (0, 0, 0, 17))] + [("obs", 1, 0, 1, 17)]
    T = max(map(len, (s0, s1, s2)))
    for s in (s0, s1, s2):  # idle (same observation) until the end
        while len(s) < T:
            last = s[-1]
            s.append(("obs", *last[3]) if last[0] == "end" else last)
    return [(0, 0, 0, 17)] * 3, [s0, s1, s2]


def _replay(objective: str):
    init, lines = _timelines()
    n, T = len(lines), len(lines[0])
    rw = EpisodeRewards(objective, AUX, LE, n, ascension=0, win_bonus=BONUS)
    rw.reset(np.stack([_aux(*x) for x in init]))
    books = [EvalBook() for _ in range(n)]
    for i, x in enumerate(init):
        books[i].observe(*x)
    sums = np.zeros(n)
    episodes = []          # (slot, summed reward, finished dict, expected protocol progress, win)
    for t in range(T):
        aux = np.zeros((n, len(AUX)), dtype=np.float32)
        le = np.zeros((n, len(LE)), dtype=np.int32)
        done = np.zeros(n, dtype=bool)
        env_r = np.zeros(n, dtype=np.float32)
        ends = {}
        for i, line in enumerate(lines):
            ev = line[t]
            if ev[0] == "obs":
                aux[i] = _aux(*ev[1:])
            else:
                (win, floor, act0), result, nxt = ev[1], ev[2], ev[3]
                done[i], env_r[i] = True, float(win)
                le[i] = _le(win, floor, act0, t + 1)
                aux[i] = _aux(*nxt)
                ends[i] = (result, act0, floor, win)
        r, fin = rw.step(env_r, done, le, aux)
        assert r.dtype == np.float32
        sums += r
        fin_by_slot = {e["slot"]: e for e in fin}
        assert set(fin_by_slot) == set(ends)
        for i in range(n):
            if i in ends:
                result, act0, floor, win = ends[i]
                episodes.append((i, sums[i], fin_by_slot[i], books[i].score(result, act0, floor), win))
                sums[i] = 0.0
                books[i] = EvalBook()
            ev = lines[i][t]
            books[i].observe(*(ev[1:] if ev[0] == "obs" else ev[3]))
    return episodes


def test_synthetic_episodes_return_equals_protocol_progress():
    episodes = _replay("protocol")
    assert [(i, w) for i, _, _, _, w in episodes] == [(1, 1), (2, 0), (0, 0), (0, 0)]
    for i, total, fin, expected, win in episodes:
        assert total == pytest.approx(expected + (BONUS if win else 0.0), abs=1e-5), (i, fin)
        assert fin["return"] == pytest.approx(total, abs=1e-6)
        assert fin["progress"] == pytest.approx(expected, abs=1e-9)
    by_slot = {}
    for i, total, *_ in episodes:
        by_slot.setdefault(i, []).append(total)
    # hand-checked values: act-2 death on floor 22 (start 17, L 16): (1 + 4/16)/3; golden-compass act 1 (L 18) cut
    # on floor 9: (8/18)/3; win: 1 + bonus; act-1 death on floor 3: (2/17)/3
    assert by_slot[0][0] == pytest.approx((1 + 4 / 16) / 3, abs=1e-5)
    assert by_slot[0][1] == pytest.approx((2 / 17) / 3, abs=1e-5)
    assert by_slot[1][0] == pytest.approx(1.0 + BONUS, abs=1e-5)
    assert by_slot[2][0] == pytest.approx((8 / 18) / 3, abs=1e-5)


def test_synthetic_win_and_legacy_objectives():
    win = {i: (tot, w) for i, tot, _, _, w in _replay("win")}
    assert win[1] == (1.0, 1) and win[2] == (0.0, 0)
    shaped = [(i, tot, fin) for i, tot, fin, _, _ in _replay("shaped")]
    for i, tot, fin in shaped:      # legacy: win + (final floor - first floor) / 48, first floor 0 here
        assert tot == pytest.approx(fin["win"] + fin["floor"] / 48.0, abs=1e-5)


def test_state_progress_is_the_protocol_formula():
    # act 1 start: 0; act-1 boss room (floor 17): 16/17/3; act 2 first room: 1/3; act 3 boss of A10 (L 16): (2+15/16)/3
    assert state_progress(0, 0, 0, 17) == 0.0
    assert state_progress(0, 1, 1, 17) == 0.0
    assert state_progress(0, 17, 17, 17) == pytest.approx(16 / 17 / 3, abs=1e-6)
    assert state_progress(1, 17, 0, 16) == pytest.approx(1 / 3, abs=1e-6)
    assert state_progress(2, 49, 16, 16, ascension=10) == pytest.approx((2 + 15 / 16) / 3, abs=1e-6)
    assert state_progress(1, 20, 3, 0) == PG.compute("loss", 2, 20, 0, {2: 17}, {})["progress"]   # default L


@pytest.fixture(scope="module")
def env():
    e = VecEnv(48, threads=4, step_cap=90, pool=PoolSpec(count=24, seed_prefix="TOBJ"), seed_text="TOBJ")
    e.pool.warm()
    yield e
    e.close()


def test_real_env_returns_equal_protocol_progress(env):
    """Random play on the real env (auto-reset, step-cap truncations): every finished episode's summed reward equals
    the evaluator-style protocol progress of that episode (+ bonus on a win), and fresh starts have progress 0."""
    n = env.num_envs
    ax = {f: i for i, f in enumerate(env.schema["aux"].fields)}
    rw = EpisodeRewards("protocol", env.schema["aux"].fields, env.schema["last_episode"].fields, n, win_bonus=BONUS)
    obs = env.reset()
    rw.reset(obs["aux"])
    assert np.all(rw.progress == 0.0)

    def observe(books, aux, slots):
        for i in slots:
            a = aux[i]
            books[i].observe(int(a[ax["total_floor"]]), int(a[ax["act_index"]]), int(a[ax["act_floor"]]),
                             int(a[ax["act_rooms"]]))

    books = [EvalBook() for _ in range(n)]
    observe(books, obs["aux"], range(n))
    sums = np.zeros(n)
    rng = np.random.default_rng(3)
    checked = acts2 = truncs = 0
    for _ in range(900):
        obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
        done = (term | trunc).astype(bool)
        r, fin = rw.step(rew, done, info["last_episode"], obs["aux"])
        sums += r
        for e in fin:
            i = e["slot"]
            result = "win" if e["win"] else ("loss" if term[i] else "capped")
            expected = books[i].score(result, e["act"] - 1, e["floor"])
            assert sums[i] == pytest.approx(expected + (BONUS if e["win"] else 0.0), abs=1e-5), (i, e, books[i].starts)
            assert e["progress"] == pytest.approx(expected, abs=1e-9)
            checked += 1
            acts2 += e["act"] >= 2
            truncs += bool(trunc[i])
            sums[i] = 0.0
            books[i] = EvalBook()
        observe(books, obs["aux"], range(n))
        assert np.all(rw.progress[done] == 0.0)      # the auto-reset start of the next episode
    assert checked > 100, checked
    assert 0 < truncs < checked, (truncs, checked)     # both terminal losses and step-cap truncations
