"""Boss bonus (``--boss-bonus B``, wave 17, ``objective.EpisodeRewards``): paid once at every act-2+ boss kill (the act
k -> k + 1 transition with k >= 1, and a Victory terminal on top of the win bonus), never at the act 1 -> 2 transition
or across an episode boundary; part of the reward / return, never of progress; config, CLI and resume defaults."""

from __future__ import annotations

import math

import numpy as np
import pytest

from sts2train.objective import EpisodeRewards

AUX = ["total_floor", "act_index", "act_floor", "act_rooms"]
LE = ["win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id"]
WIN_BONUS, B = 0.5, 0.2


def _aux(tf, ai, af, rooms):
    return np.array([tf, ai, af, rooms], dtype=np.float32)


def _le(win, floor, act0, steps=1):
    r = np.zeros(len(LE), dtype=np.int32)
    r[LE.index("win")], r[LE.index("floor")], r[LE.index("act")], r[LE.index("steps")] = win, floor, act0, steps
    r[LE.index("end_kind")] = 1
    return r


# per slot: first observation, then per step ("obs", aux) or ("end", (win, floor, act0), next first observation)
LINES = [
    # act 1 -> 2 (k = 0: nothing), dies in act 2; the next episode is a save start in act 3 (a done slot: nothing)
    ((16, 0, 16, 17), [("obs", (17, 1, 0, 16)), ("obs", (18, 1, 1, 16)), ("end", (0, 18, 1), (45, 2, 12, 15))]),
    # a save start in act 2 kills the act-2 boss (act 2 -> 3, k = 1: B once), dies in act 3
    ((32, 1, 15, 16), [("obs", (33, 2, 0, 15)), ("obs", (34, 2, 1, 15)), ("end", (0, 34, 2), (0, 0, 0, 17))]),
    # act 3 -> Victory (the final boss: B + the win bonus), then a fresh episode
    ((47, 2, 14, 15), [("end", (1, 48, 2), (0, 0, 0, 17)), ("obs", (1, 0, 1, 17)), ("obs", (2, 0, 2, 17))]),
]


def _play(boss_bonus):
    n = len(LINES)
    kw = {} if boss_bonus is None else {"boss_bonus": boss_bonus}
    rw = EpisodeRewards("protocol", AUX, LE, n, ascension=10, win_bonus=WIN_BONUS, **kw)
    cur = np.stack([_aux(*x[0]) for x in LINES])
    rw.reset(cur.copy(), start_entry=np.arange(n))
    rs, eps = [], []
    for t in range(3):
        aux, le, done = cur.copy(), np.zeros((n, len(LE)), np.int32), np.zeros(n, bool)
        for i, (_, steps) in enumerate(LINES):
            ev = steps[t]
            if ev[0] == "obs":
                aux[i] = _aux(*ev[1])
            else:
                done[i], le[i], aux[i] = True, _le(*ev[1]), _aux(*ev[2])
        r, fin = rw.step(le[:, 0].astype(np.float32), done, le, aux, start_entry=np.arange(n))
        cur = aux
        rs.append(r.astype(np.float64))
        eps += fin
    return np.stack(rs), {e["slot"]: e for e in eps}, rw.pop_boss_counts()


def test_bonus_paid_once_per_act2plus_boss_kill_and_never_in_progress():
    r0, e0, c0 = _play(0.0)
    rd, ed, _ = _play(None)                    # default = 0: identical rewards and records
    assert np.array_equal(r0, rd) and e0.keys() == ed.keys()
    assert all(e0[i] == ed[i] for i in e0) and "boss_bonus_total" not in e0[0]
    rb, eb, cb = _play(B)
    diff = rb - r0
    expect = np.zeros_like(diff)
    expect[0, 1] = B                           # slot 1: act 2 -> 3 at step 0
    expect[0, 2] = B                           # slot 2: the win at step 0 (on top of the win bonus)
    np.testing.assert_allclose(diff, expect, atol=1e-6)
    assert diff[:, 0].tolist() == [0.0, 0.0, 0.0]          # act 1 -> 2 and the done step into act 3: nothing
    for i in range(3):                         # protocol progress / start progress / gain are the objective's
        for k in ("progress", "start_progress", "progress_gain", "win", "act", "floor"):
            assert eb[i][k] == e0[i][k], (i, k)
    assert eb[0]["boss_bonus_total"] == 0.0 and eb[0]["boss2_kills"] == 0
    assert eb[0]["return"] == pytest.approx(e0[0]["return"])
    assert eb[1]["boss_bonus_total"] == pytest.approx(B) and eb[1]["boss2_kills"] == 1
    assert eb[1]["return"] == pytest.approx(eb[1]["progress_gain"] + B, abs=1e-6)
    assert eb[2]["boss_bonus_total"] == pytest.approx(B) and eb[2]["boss2_kills"] == 1 and eb[2]["win"]
    assert eb[2]["return"] == pytest.approx(eb[2]["progress_gain"] + WIN_BONUS + B, abs=1e-6)
    assert cb == {"boss2_kills": 2, "boss2_kills_transition": 1, "boss2_kills_win": 1, "boss2_transitions_unearned": 0,
                  "boss_bonus_sum": pytest.approx(2 * B)}
    assert c0 == {"boss2_kills": 2, "boss2_kills_transition": 1, "boss2_kills_win": 1, "boss2_transitions_unearned": 0,
                  "boss_bonus_sum": 0.0}


def test_synthetic_k0_pays_nothing_k1_pays_once():
    """One slot: act 1 -> 2 (k = 0), idle steps, act 2 -> 3 (k = 1), idle steps: B exactly once, at the k = 1 step."""
    rw = EpisodeRewards("protocol", AUX, LE, 1, ascension=10, boss_bonus=B)
    rw.reset(_aux(16, 0, 16, 17)[None], start_entry=np.zeros(1, np.int64))
    base = EpisodeRewards("protocol", AUX, LE, 1, ascension=10)
    base.reset(_aux(16, 0, 16, 17)[None], start_entry=np.zeros(1, np.int64))
    seq = [(17, 1, 0, 16), (17, 1, 0, 16), (33, 2, 0, 15), (33, 2, 0, 15), (34, 2, 1, 15)]
    no = np.zeros(1, bool)
    le = np.zeros((1, len(LE)), np.int32)
    paid = []
    for a in seq:
        r, _ = rw.step(np.zeros(1, np.float32), no, le, _aux(*a)[None])
        r0, _ = base.step(np.zeros(1, np.float32), no, le, _aux(*a)[None])
        paid.append(float(r[0]) - float(r0[0]))
    np.testing.assert_allclose(paid, [0.0, 0.0, B, 0.0, 0.0], atol=1e-6)
    assert rw.pop_boss_counts()["boss2_kills_transition"] == 1 and rw.pop_boss_counts()["boss2_kills"] == 0


def test_only_earned_kills_pay_with_is_combat():
    """With aux.is_combat (the trainer's aux block): slot 0 starts (a save) on the Rewards screen of an act-2 boss the
    human killed and walks into act 3: not paid (unearned); slot 1 starts out of combat in act 2, fights (the boss), then
    enters act 3: paid once; slot 2 fought in act 2 in an earlier episode only (the next episode starts out of combat
    in act 2 and transitions): not paid."""
    aux_f = AUX + ["is_combat"]

    def a(tf, ai, af, rooms, c=0):
        return np.array([tf, ai, af, rooms, c], dtype=np.float32)

    rw = EpisodeRewards("protocol", aux_f, LE, 3, ascension=10, boss_bonus=B)
    rw.reset(np.stack([a(33, 1, 16, 16), a(32, 1, 15, 16), a(32, 1, 15, 16, 1)]), start_entry=np.arange(3))
    no = np.zeros(3, bool)
    le = np.zeros((3, len(LE)), np.int32)
    steps = [
        (np.stack([a(34, 2, 0, 15), a(33, 1, 16, 16, 1), a(32, 1, 15, 16)]), no, None),
        (np.stack([a(34, 2, 0, 15), a(33, 1, 16, 16), a(33, 1, 16, 16)]), np.array([False, False, True]),
         np.stack([le[0], le[0], _le(0, 32, 1)])),                   # slot 2 dies; next episode at (33, act 2) no combat
        (np.stack([a(34, 2, 0, 15), a(34, 2, 0, 15), a(34, 2, 0, 15)]), no, None),
    ]
    paid = np.zeros(3)
    base = EpisodeRewards("protocol", aux_f, LE, 3, ascension=10)
    base.reset(np.stack([a(33, 1, 16, 16), a(32, 1, 15, 16), a(32, 1, 15, 16, 1)]), start_entry=np.arange(3))
    for aux, done, l in steps:
        l = le if l is None else l
        r, _ = rw.step(np.zeros(3, np.float32), done, l, aux)
        r0, _ = base.step(np.zeros(3, np.float32), done, l, aux)
        paid += r - r0
    np.testing.assert_allclose(paid, [0.0, B, 0.0], atol=1e-6)
    c = rw.pop_boss_counts()
    assert c["boss2_kills"] == 1 and c["boss2_transitions_unearned"] == 2


def test_config_cli_and_resume_defaults():
    from sts2train.ppo import TrainConfig, config_dict, resolve_config

    assert TrainConfig().boss_bonus == 0.0 and resolve_config({}).boss_bonus == 0.0
    cfg = resolve_config({"boss_bonus": 0.2})
    assert cfg.boss_bonus == 0.2 and config_dict(cfg)["boss_bonus"] == 0.2      # checkpoints record it
    old = {k: v for k, v in config_dict(cfg).items() if k != "boss_bonus"}       # a checkpoint before wave 17
    assert resolve_config({}, base=old).boss_bonus == 0.0
    assert resolve_config({}, base=config_dict(cfg)).boss_bonus == 0.2         # a resume keeps it
    assert resolve_config({"boss_bonus": 0.0}, base=config_dict(cfg)).boss_bonus == 0.0
    with pytest.raises(ValueError, match="boss_bonus"):
        resolve_config({"boss_bonus": math.nan})
    import argparse

    from sts2train.cli import _train_parser

    p = argparse.ArgumentParser()
    _train_parser(p.add_subparsers(dest="cmd"))
    a = p.parse_args(["train", "--boss-bonus", "0.2", "--copies-mode", "determinized"])
    assert a.boss_bonus == 0.2 and a.save_copies == "determinized"
