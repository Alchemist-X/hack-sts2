"""Start pools mixing real saves with fresh starts (``--start-saves``, ``sts2train.startpool``).

Synthetic save-started episodes check the bookkeeping (a save start's return telescopes from the progress of its first
observation; fresh / save episodes are reported separately); the pool layout is checked without the library; the
committed fixture save (``run_save_a8_silent_boss1.save``: Silent A8, act-1 boss Rewards, floor 17) exercises the
probe, the build (determinized and exact copies) and random play on the real env.
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path

import numpy as np
import pytest
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions
from sts2vec import protocol as PG

from sts2train.objective import EpisodeRewards, state_progress
from sts2train.startpool import (FRESH, SAVE, Layout, StartPool, build_start_pool, plan_layout, probe_saves,
                                 recent_sums, recent_view, split_stats)

FIXTURE = Path(__file__).resolve().parents[3] / "Sts2Sim.Tests" / "Fixtures" / "run_save_a8_silent_boss1.save"
AUX = ["total_floor", "act_index", "act_floor", "act_rooms"]
LE = ["win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id"]
BONUS = 0.5


# ------------------------------------------------------------------------------------------------ layout


def test_plan_layout_counts_ratio_and_interleaving():
    L = plan_layout(2048, 146, 0.5, seed=0)
    assert (L.fresh, L.saves, L.copies, L.size, L.save_entries) == (2048, 146, 14, 4092, 2044)
    assert L.mix_effective == pytest.approx(0.5, abs=1e-3)
    assert sorted(L.index[L.kinds == FRESH].tolist()) == list(range(2048))      # fresh entries in order, each once
    assert L.index[L.kinds == FRESH].tolist() == list(range(2048))
    saves = list(zip(L.index[L.kinds == SAVE].tolist(), L.copy[L.kinds == SAVE].tolist()))
    assert sorted(saves) == [(i, c) for i in range(146) for c in range(14)]      # every save x every copy, once
    assert all(c == k // 146 for k, (_, c) in enumerate(saves))                  # copy 0 of every save comes first
    # evenly interleaved: any window of w consecutive entries (cyclic, as a slot's start sequence) holds w * S / P
    # save entries, +-1
    s = np.concatenate([L.kinds, L.kinds]).astype(np.int64)
    for w in (2, 7, 64, 1000):
        win = np.convolve(s, np.ones(w, dtype=np.int64), "valid")[:L.size]
        assert np.all(np.abs(win - w * L.save_entries / L.size) <= 1.0 + 1e-9), w
    # a small pool: 10 fresh, 3 saves, mix 0.25 -> round(10 * 1/3 / 3) = 1 copy
    L = plan_layout(10, 3, 0.25, seed=0)
    assert (L.copies, L.save_entries, L.size) == (1, 3, 13)


def test_plan_layout_is_deterministic_and_seeded():
    shas = [f"{i:064x}" for i in range(20)]
    a, b = plan_layout(100, 20, 0.5, seed=3, sha256s=shas), plan_layout(100, 20, 0.5, seed=3, sha256s=shas)
    assert np.array_equal(a.kinds, b.kinds) and np.array_equal(a.index, b.index) and np.array_equal(a.copy, b.copy)
    c = plan_layout(100, 20, 0.5, seed=4, sha256s=shas)
    assert np.array_equal(a.kinds, c.kinds) and not np.array_equal(a.index, c.index)   # same shape, other save order
    # the order of a round depends on the saves' contents, not on their positions in the list
    perm = shas[::-1]
    d = plan_layout(100, 20, 0.5, seed=3, sha256s=perm)
    order_a = [shas[i] for i in a.index[a.kinds == SAVE]]
    order_d = [perm[i] for i in d.index[d.kinds == SAVE]]
    assert order_a == order_d


def test_plan_layout_edges():
    L = plan_layout(64, 5, 0.0)
    assert L.save_entries == 0 and L.size == 64 and L.copies == 0
    L = plan_layout(64, 0, 0.5)
    assert L.save_entries == 0 and L.size == 64
    L = plan_layout(64, 5, 1.0)                     # mix 1: no fresh entries, `fresh` sets the save entries
    assert L.fresh == 0 and L.copies == round(64 / 5) and L.size == L.save_entries == 5 * L.copies
    L = plan_layout(10, 50, 0.5)                    # fewer target entries than saves: every save once
    assert L.copies == 1 and L.save_entries == 50 and L.mix_effective == pytest.approx(50 / 60)
    for bad in (-0.1, 1.5, float("nan")):
        with pytest.raises(ValueError, match="save_mix"):
            plan_layout(10, 2, bad)
    with pytest.raises(ValueError, match="empty"):
        plan_layout(0, 0, 0.5)


# ------------------------------------------------------------------------------------------------ bookkeeping


def _aux(tf, ai, af, rooms):
    return np.array([tf, ai, af, rooms], dtype=np.float32)


def _le(win, floor, act0, steps=1):
    r = np.zeros(len(LE), dtype=np.int32)
    r[LE.index("win")], r[LE.index("floor")], r[LE.index("act")], r[LE.index("steps")] = win, floor, act0, steps
    return r


def _pool(kinds) -> StartPool:
    k = np.asarray(kinds, dtype=np.int8)
    L = Layout(k, np.zeros(len(k), np.int32), np.zeros(len(k), np.int32), int((k == FRESH).sum()), 1, 1, 0.5)
    return StartPool("synthetic", L, [], [], 0, "exact")


def test_synthetic_save_started_episodes_measure_progress_from_their_start():
    """Entries 0, 3 fresh; 1 = a save in act 2 (floor 20, act floor 3, L 16); 2 = a save in act 3 (floor 45, act floor
    12, L 15). Returns telescope from the first observation: a save start's return is progress(end) - progress(start)
    (+ bonus on a win), and the first observation's progress never appears as a reward."""
    pool = _pool([FRESH, SAVE, SAVE, FRESH])
    s_act2, s_act3 = (20, 1, 3, 16), (45, 2, 12, 15)
    # per slot: (initial start entry, initial obs), then steps: ("obs", aux) or ("end", (win, floor, act0), next entry,
    # next first obs)
    lines = [
        (0, (0, 0, 0, 17), [("obs", (t, 0, t, 17)) for t in (1, 2, 3, 5)]
         + [("end", (0, 5, 0), 1, s_act2), ("obs", (21, 1, 4, 16)), ("obs", (22, 1, 5, 16)),
            ("end", (0, 22, 1), 3, (0, 0, 0, 17))]),
        (2, s_act3, [("obs", (46, 2, 13, 15)), ("obs", (47, 2, 14, 15)), ("obs", (48, 2, 15, 15)),
                     ("end", (1, 48, 2), 0, (0, 0, 0, 17))] + [("obs", (1, 0, 1, 17))] * 3),
        (1, s_act2, [("obs", s_act2)] * 6 + [("end", (0, 20, 1), 2, s_act3)]),
    ]
    n, T = len(lines), max(len(x[2]) for x in lines)
    rw = EpisodeRewards("protocol", AUX, LE, n, ascension=9, win_bonus=BONUS)
    start = np.array([x[0] for x in lines])
    rw.reset(np.stack([_aux(*x[1]) for x in lines]), start_entry=start)
    assert rw.start_progress[1] == pytest.approx((2 + 11 / 15) / 3, abs=1e-6)
    assert rw.start_progress[2] == pytest.approx((1 + 2 / 16) / 3, abs=1e-6)
    sums, eps = np.zeros(n), []
    for t in range(T):
        aux = np.stack([_aux(*rw.tracker.prev[i][[1, 0, 2, 3]]) for i in range(n)])   # idle = same observation
        le, done, env_r = np.zeros((n, len(LE)), np.int32), np.zeros(n, bool), np.zeros(n, np.float32)
        for i, (_, _, steps) in enumerate(lines):
            if t >= len(steps):
                continue
            ev = steps[t]
            if ev[0] == "obs":
                aux[i] = _aux(*ev[1])
            else:
                (win, floor, act0), nxt, first = ev[1], ev[2], ev[3]
                done[i], env_r[i], le[i], aux[i], start[i] = True, win, _le(win, floor, act0), _aux(*first), nxt
        r, fin = rw.step(env_r, done, le, aux, start_entry=start)
        sums += r
        for e in fin:
            e["sum"] = sums[e["slot"]]
            sums[e["slot"]] = 0.0
            eps.append(e)
    by = [(e["slot"], e["start_entry"]) for e in eps]
    assert by == [(1, 2), (0, 0), (2, 1), (0, 1)], by       # in the order they end
    save_win, fresh, save_capped, save_loss = eps
    assert fresh["start_progress"] == 0.0 and fresh["start_floor"] == 0 and fresh["start_act"] == 1
    assert fresh["progress"] == pytest.approx((4 / 17) / 3, abs=1e-6)
    assert fresh["progress_gain"] == pytest.approx(fresh["progress"]) and fresh["sum"] == pytest.approx(fresh["progress"], abs=1e-6)
    # a save start that never moves: progress (2 rooms cleared of act 2) but nothing gained, return 0
    assert save_capped["progress"] == pytest.approx((1 + 2 / 16) / 3, abs=1e-6)
    assert save_capped["progress_gain"] == pytest.approx(0.0, abs=1e-9) and save_capped["sum"] == pytest.approx(0.0, abs=1e-6)
    assert (save_capped["start_floor"], save_capped["start_act"]) == (20, 2)
    # a win from act 3: return = 1 - start progress + bonus
    p3 = (2 + 11 / 15) / 3
    assert save_win["progress"] == 1.0 and save_win["start_progress"] == pytest.approx(p3, abs=1e-6)
    assert save_win["sum"] == pytest.approx(1.0 - p3 + BONUS, abs=1e-5) and save_win["return"] == pytest.approx(save_win["sum"])
    # act-2 save started on floor 20 (2 rooms cleared), died on floor 22 (4 rooms cleared): gained 2/16 of an act
    assert save_loss["progress"] == pytest.approx((1 + 4 / 16) / 3, abs=1e-6)
    assert save_loss["progress_gain"] == pytest.approx((2 / 16) / 3, abs=1e-6)
    assert save_loss["sum"] == pytest.approx(save_loss["progress_gain"], abs=1e-6)
    for e in eps:                     # the evaluator's score of the same game, from the save's first observation on
        assert e["progress"] == pytest.approx(
            PG.compute("win" if e["win"] else "loss", e["act"], e["floor"], 9,
                       {e["act"]: {1: 0, 2: 17, 3: 33}[e["act"]]}, {e["act"]: {1: 17, 2: 16, 3: 15}[e["act"]]})["progress"])

    st = split_stats(eps, pool)
    assert (st["fresh_episodes"], st["save_episodes"], st["fresh_wins"], st["save_wins"]) == (1, 3, 0, 1)
    assert st["save_win_rate"] == pytest.approx(1 / 3) and st["fresh_win_rate"] == 0.0
    assert st["fresh_progress_mean"] == pytest.approx(fresh["progress"])
    saves = [save_win, save_capped, save_loss]
    assert st["save_progress_gain_mean"] == pytest.approx(np.mean([e["progress_gain"] for e in saves]))
    assert st["save_progress_mean"] == pytest.approx(np.mean([e["progress"] for e in saves]))
    assert st["save_start_floor_mean"] == pytest.approx((20 + 45 + 20) / 3)
    assert st["save_floors_gained_mean"] == pytest.approx((0 + 3 + 2) / 3)
    assert st["save_episodes_by_start_act"] == {2: 2, 3: 1} and st["save_act_reached"] == {2: 2, 3: 1}
    assert st["save_gain_by_start_act"][3] == pytest.approx(1.0 - p3, abs=1e-6)   # progress is rounded to 1e-6
    assert split_stats(eps, None) == {} and split_stats([], pool)["save_episodes"] == 0
    view = recent_view([recent_sums(eps, pool), recent_sums([fresh], pool), {"episodes": 0}])
    assert view["fresh_episodes"] == 2 and view["save_episodes"] == 3
    assert view["save_progress_gain_mean"] == pytest.approx(st["save_progress_gain_mean"])


# ------------------------------------------------------------------------------------------------ real library


@pytest.fixture
def saves_dir(tmp_path):
    """The fixture save, a byte-identical duplicate, an unsupported variant (game mode), a non-JSON file."""
    d = tmp_path / "saves"
    (d / "sub").mkdir(parents=True)
    shutil.copyfile(FIXTURE, d / "a_boss1.save")
    shutil.copyfile(FIXTURE, d / "sub" / "b_dup.save")
    data = json.loads(FIXTURE.read_text(encoding="utf-8-sig"))
    data["game_mode"] = "custom"
    (d / "c_custom.save").write_text(json.dumps(data), encoding="utf-8")
    (d / "d_garbage.save").write_text("not a save", encoding="utf-8")
    (d / "notes.txt").write_text("ignored", encoding="utf-8")
    return d


def test_probe_skips_and_reports(saves_dir):
    ok, skipped, files = probe_saves(saves_dir)
    assert files == 4 and [s.file for s in ok] == ["a_boss1.save"]
    s = ok[0]
    assert (s.character, s.ascension, s.act, s.floor, s.act_floor, s.act_rooms, s.phase) == \
        ("SILENT", 8, 1, 17, 17, 17, "Rewards")
    assert s.start_progress == pytest.approx(state_progress(0, 17, 17, 17, 8)) and not s.pending_prompt
    reasons = {r["file"]: r["reason"] for r in skipped}
    assert reasons["sub/b_dup.save"] == "duplicate of a_boss1.save"
    assert "game_mode custom is not standard" in reasons["c_custom.save"]
    assert "d_garbage.save" in reasons
    ok, skipped, _ = probe_saves(saves_dir, characters=("IRONCLAD",))
    assert ok == [] and any("character SILENT not in --characters" in r["reason"] for r in skipped)
    with VecEnv(2, threads=1, pool=PoolSpec(count=0)) as env, pytest.raises(ValueError, match="no usable save"):
        build_start_pool(env, saves_dir, characters=("IRONCLAD",))


@pytest.mark.parametrize("mode", ["determinized", "exact"])
def test_build_start_pool_copies(saves_dir, mode):
    with VecEnv(4, threads=2, pool=PoolSpec(count=0), seed_text="TSP") as env:
        sp = build_start_pool(env, saves_dir, mix=0.5, copies_mode=mode, fresh=6, characters=("SILENT", "IRONCLAD"),
                              ascension=0, seed_prefix="TSP", seed=5)
        assert len(env.pool) == sp.layout.size == 12 and sp.layout.copies == 6 and sp.layout.save_entries == 6
        assert sp.summary()["skipped"] == 3 and sp.summary(brief=True)["unique_saves"] == 1
        saves = np.flatnonzero(sp.kinds == SAVE)
        fresh = np.flatnonzero(sp.kinds == FRESH)
        assert np.array_equal(sp.kinds, np.tile([FRESH, SAVE], 6))
        views, hashes = [], []
        for e in saves:
            env.restore(0, int(e))
            views.append({k: v[0].copy() for k, v in env.obs.items()})
            hashes.append(int(env.state_hash([0])[0]))
        for v in views[1:]:                         # every copy shows the save's player view byte for byte
            for k in v:
                np.testing.assert_array_equal(v[k], views[0][k], err_msg=k)
        if mode == "exact":
            assert len(set(hashes)) == 1
        else:                                       # hidden state resampled per copy
            assert len(set(hashes)) == len(hashes)
        env.restore(1, int(fresh[1]))               # fresh entry 1 = the config.pool.fresh entry 1 (IRONCLAD)
        ch = env.schema.vocab.lists["characters"].name(int(env.obs["global_i"][1, env.schema.col("global_i", "character")]))
        assert ch == "IRONCLAD"
    with VecEnv(4, threads=2, pool=PoolSpec(count=0), seed_text="TSP") as env2:   # deterministic given the seed
        build_start_pool(env2, saves_dir, mix=0.5, copies_mode=mode, fresh=6, characters=("SILENT", "IRONCLAD"),
                         seed_prefix="TSP", seed=5)
        h2 = []
        for e in saves:
            env2.restore(0, int(e))
            h2.append(int(env2.state_hash([0])[0]))
        assert h2 == hashes


class EvalBook:
    """sts2vec.evaluate's per-game bookkeeping: act start floor from the act's first observation, rooms last seen."""

    def __init__(self):
        self.starts, self.rooms = {}, {}

    def observe(self, tf, ai, af, rooms):
        self.starts.setdefault(ai + 1, tf - af)
        if rooms > 0:
            self.rooms[ai + 1] = rooms

    def score(self, result, act0, floor, asc):
        return PG.compute(result, act0 + 1, floor, asc, self.starts, self.rooms)["progress"]


def test_real_env_save_starts_telescope_from_the_save(saves_dir):
    """Random play on a mixed pool (auto-reset): every finished episode's summed reward equals the evaluator's
    progress of its end minus the progress of its first observation (+ bonus), save starts begin at the fixture's
    floor 17 progress, fresh starts at 0; no SIM_ERROR / CONTENT_GAP."""
    n = 48
    with VecEnv(n, threads=4, step_cap=150, pool=PoolSpec(count=0), seed_text="TSPRL") as env:
        sp = build_start_pool(env, saves_dir, mix=0.5, fresh=8, characters=("SILENT",), seed=1)
        ax = {f: i for i, f in enumerate(env.schema["aux"].fields)}
        rw = EpisodeRewards("protocol", env.schema["aux"].fields, env.schema["last_episode"].fields, n,
                            ascension=0, win_bonus=BONUS)
        obs = env.reset()
        rw.reset(obs["aux"], start_entry=env.info()["start_entry"])
        is_save = sp.is_save(rw.start_entry)
        p0 = sp.saves[0].start_progress
        assert is_save.any() and (~is_save).any()
        assert np.allclose(rw.start_progress[is_save], p0) and np.all(rw.start_progress[~is_save] == 0.0)

        def observe(books, aux, slots):
            for i in slots:
                a = aux[i]
                books[i].observe(int(a[ax["total_floor"]]), int(a[ax["act_index"]]), int(a[ax["act_floor"]]),
                                 int(a[ax["act_rooms"]]))

        books = [EvalBook() for _ in range(n)]
        observe(books, obs["aux"], range(n))
        first = rw.start_progress.copy()
        sums, eps = np.zeros(n), []
        rng = np.random.default_rng(11)
        bad = 0
        for _ in range(700):
            obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
            bad += int(np.count_nonzero(status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])))
            done = (term | trunc).astype(bool)
            r, fin = rw.step(rew, done, info["last_episode"], obs["aux"], start_entry=info["start_entry"])
            sums += r
            for e in fin:
                i = e["slot"]
                asc = 8 if sp.is_save(e["start_entry"]) else 0
                result = "win" if e["win"] else ("loss" if term[i] else "capped")
                expected = books[i].score(result, e["act"] - 1, e["floor"], asc)
                assert e["progress"] == pytest.approx(expected, abs=1e-9)
                assert e["start_progress"] == pytest.approx(first[i], abs=1e-9)
                assert sums[i] == pytest.approx(expected - first[i] + (BONUS if e["win"] else 0.0), abs=1e-5)
                assert e["progress_gain"] == pytest.approx(expected - first[i], abs=1e-9)
                sums[i] = 0.0
                books[i] = EvalBook()
                eps.append(e)
            observe(books, obs["aux"], range(n))
            first[done] = rw.start_progress[done]
        assert bad == 0
        st = split_stats(eps, sp)
        assert st["save_episodes"] > 20 and st["fresh_episodes"] > 20, st
        save_eps = [e for e in eps if sp.is_save(e["start_entry"])]
        assert all(e["start_floor"] == 17 and e["start_act"] == 1 for e in save_eps)
        assert any(e["act"] >= 2 for e in save_eps)          # save starts reach act 2 (fresh random play rarely does)
        assert st["save_start_progress_mean"] == pytest.approx(p0)


def test_train_cli_with_start_saves(saves_dir, tmp_path):
    """`sts2train train --start-saves`: the pool summary lands in config.json / summary.json, metrics rows carry the
    fresh_* / save_* split, status.json's recent block too; a plain resume keeps the saves (next to the resume-compat
    and floor-cap records of config.json), a resume with `--start-saves none` turns it off."""
    from sts2train.cli import main
    from sts2train.runlog import read_jsonl

    run = tmp_path / "run"
    common = ["--envs", "24", "--threads", "2", "--pool", "8", "--rollout", "16", "--minibatch", "128", "--d-model",
              "32", "--state-dim", "64", "--torch-threads", "2", "--step-cap", "120", "--ckpt-every", "1",
              "--log-every", "100", "--max-hours", "0.5", "--out", str(run)]
    rc = main(["train", *common, "--max-updates", "2", "--start-saves", str(saves_dir), "--save-mix", "0.5",
               "--characters", "SILENT"])
    assert rc == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["start_saves"] == str(saves_dir) and conf["config"]["save_copies"] == "determinized"
    sp = conf["start_pool"]
    assert (sp["unique_saves"], sp["fresh_entries"], sp["save_entries"], sp["skipped"]) == (1, 8, 8, 3)
    assert sp["histograms_unique_saves"]["start_floor"] == {"17": 1} and len(sp["saves"]) == 1
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2]
    for k in ("fresh_episodes", "fresh_progress_mean", "fresh_win_rate", "save_episodes", "save_progress_mean",
              "save_progress_gain_mean", "save_win_rate", "save_start_floor_mean"):
        assert k in rows[-1], k
    assert sum(r["save_episodes"] for r in rows) > 0
    st = json.loads((run / "status.json").read_text())
    assert "save_episodes" in st["recent"] and "fresh_progress_mean" in st["recent"]
    summ = json.loads((run / "summary.json").read_text())
    assert summ["start_pool"]["save_entries"] == 8 and summ["sim_error"] == 0
    rc = main(["train", *common, "--max-updates", "3", "--resume", "auto"])
    assert rc == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["start_saves"] == str(saves_dir) and conf["start_pool"]["save_entries"] == 8
    assert conf["resume_compat"] is not None and conf["checkpoint_encoder_version"] == conf["encoder_version"]
    assert "floor_step_cap" in conf["env"] and [x["update_start"] for x in conf["sessions"]] == [0, 2]
    assert "save_episodes" in read_jsonl(run / "metrics.jsonl")[-1]
    rc = main(["train", *common, "--max-updates", "4", "--resume", "auto", "--start-saves", "none"])
    assert rc == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["start_saves"] is None and conf["start_pool"] is None
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2, 3, 4] and "save_episodes" not in rows[-1]
