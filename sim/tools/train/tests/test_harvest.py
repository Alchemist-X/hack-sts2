"""Self-anchor starts (``--harvest``, ``sts2train.harvest``).

Spec parsing; the detection / capture rule on a fake env (kinds from the current map cell, ongoing combats, finished /
save- / anchor-started / failed / candidate-less slots skipped, probability and per-step / per-ring caps, FIFO release
in one call, ``kind_of``); the real library (determinized clones keep the source's model-read bytes, released ids
are gone, anchor-started episodes begin in combat, the pool stays bounded); the three-group ``split_stats``; and the
CLI (config / metrics / summary records, a resume that keeps harvest on with empty rings, ``--harvest off``, a
checkpoint without the fields).
"""

from __future__ import annotations

import json
from types import SimpleNamespace

import numpy as np
import pytest
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions
from sts2vec.errors import Sts2VecError

from sts2train.features import USED_BUFFERS
from sts2train.harvest import KINDS, Harvester, clone_seed, format_harvest, parse_harvest
from sts2train.startpool import FRESH, SAVE, Layout, StartPool, recent_sums, recent_view, split_stats

MPT = ["Unassigned", "Unknown", "Shop", "Treasure", "RestSite", "Monster", "Elite", "Boss", "Ancient"]
MONSTER, ELITE, BOSS = MPT.index("Monster"), MPT.index("Elite"), MPT.index("Boss")


# ------------------------------------------------------------------------------------------------ spec


def test_parse_harvest():
    assert parse_harvest("boss:1536,elite:512,act2:1024") == {"boss": 1536, "elite": 512, "act2": 1024}
    assert list(parse_harvest(" act2:4 , BOSS:8 ")) == ["boss", "act2"]          # canonical order, case-insensitive
    assert parse_harvest("any:16") == {"any": 16}
    for off in ("", "off", "OFF", "none", None):
        assert parse_harvest(off) == {}
    assert format_harvest(parse_harvest("act2:4,boss:8")) == "boss:8,act2:4" and format_harvest({}) == ""
    for bad, msg in (("mini:3", "unknown kind"), ("boss:0", ">= 1"), ("boss:-2", ">= 1"), ("boss", ">= 1"),
                     ("boss:x", ">= 1"), ("boss:3,boss:4", "twice"), (",", "no ring")):
        with pytest.raises(ValueError, match=msg):
            parse_harvest(bad)


def test_resolve_config_validates_and_old_checkpoints_resume_off():
    from sts2train.ppo import TrainConfig, config_dict, resolve_config

    assert resolve_config({}).harvest == "" and TrainConfig().harvest_prob == 0.25
    cfg = resolve_config({"harvest": "act2:4,boss:8", "harvest_prob": 1.0})
    assert cfg.harvest == "boss:8,act2:4" and cfg.harvest_prob == 1.0 and cfg.harvest_max_per_step == 64
    assert resolve_config({"harvest": "off"}).harvest == ""
    for given in ({"harvest": "boss:0"}, {"harvest": "x:1"}, {"harvest_prob": 0.0}, {"harvest_prob": 1.5},
                  {"harvest_max_per_step": 0}):
        with pytest.raises(ValueError):
            resolve_config(given)
    old = {k: v for k, v in config_dict(cfg).items() if not k.startswith("harvest")}   # a checkpoint before harvest
    assert resolve_config({}, base=old).harvest == ""
    assert resolve_config({}, base=config_dict(cfg)).harvest == "boss:8,act2:4"        # a resume keeps it
    assert resolve_config({"harvest": ""}, base=config_dict(cfg)).harvest == ""         # --harvest off on resume


# ------------------------------------------------------------------------------------------------ fake env

R, C = 4, 3          # map rows x cols of the fake observation


class FakePool:
    def __init__(self, n):
        self.live = list(range(n))
        self.next = n
        self.release_calls: list[list[int]] = []

    def __len__(self):
        return len(self.live)

    def release(self, ids):
        ids = [int(i) for i in ids]
        assert len(set(ids)) == len(ids) and all(i in self.live for i in ids), ids
        self.release_calls.append(ids)
        self.live = [i for i in self.live if i not in ids]
        return len(ids)


class FakeEnv:
    """The parts of VecEnv the harvester uses: schema columns by name, num_envs, pool, clone_determinized."""

    supports_pool_release = True

    def __init__(self, n, base):
        self.num_envs = n
        bufs = {"aux": SimpleNamespace(fields=("total_floor", "act_index", "is_combat")),
                "map_nodes": SimpleNamespace(fields=("point_type", "visited", "current"))}
        self.schema = _Schema(bufs, SimpleNamespace(enums={"MapPointType": SimpleNamespace(index_of=MPT.index)}))
        self.pool = FakePool(base)
        self.lib = SimpleNamespace(require=lambda name: None)
        self.clones: list[tuple[int, int, int]] = []

    def clone_determinized(self, slot, seed):
        eid = self.pool.next
        self.pool.next += 1
        self.pool.live.append(eid)
        self.clones.append((int(slot), int(seed), eid))
        return eid


class _Schema:
    def __init__(self, bufs, vocab):
        self._b, self.vocab = bufs, vocab

    def __getitem__(self, k):
        return self._b[k]


def obs_of(n, combat=(), cells=None, acts=None, cand_n=None):
    """Fake observation: slots in `combat` have is_combat 1; cells[i] = (point type, row, col) of slot i's current map
    cell (default: a Monster cell at (1, 1)); acts[i] = act_index."""
    aux = np.zeros((n, 3), dtype=np.float32)
    aux[list(combat), 2] = 1
    for i, a in (acts or {}).items():
        aux[i, 1] = a
    mn = np.zeros((n, R, C, 3), dtype=np.int16)
    for i in range(n):
        pt, r, c = (cells or {}).get(i, (MONSTER, 1, 1))
        mn[i, r, c, 0] = pt
        mn[i, r, c, 2] = 1
    cn = np.full(n, 5, dtype=np.int32) if cand_n is None else np.asarray(cand_n, dtype=np.int32)
    return {"aux": aux, "map_nodes": mn, "cand_n": cn}


def _start_pool(kinds) -> StartPool:
    k = np.asarray(kinds, dtype=np.int8)
    L = Layout(k, np.zeros(len(k), np.int32), np.zeros(len(k), np.int32), int((k == FRESH).sum()), 1, 1, 0.5)
    return StartPool("synthetic", L, [], [], 0, "exact")


def test_detection_kinds_and_exclusions():
    """Slots 0-3 enter a boss / elite / act-2 hallway / act-1 hallway combat; 4 is already in combat; 5 just finished
    (its next episode starts in combat); 6 runs a save-started episode; 7 an anchor-started one; 8 has SIM_ERROR; 9 has
    no legal action; 10 stays out of combat. Only 0-2 are captured, with their kinds."""
    n, base = 11, 20
    env = FakeEnv(n, base)
    sp = _start_pool([FRESH] * 10 + [SAVE] * 10)          # entries 10..19 are saves
    h = Harvester(env, {"boss": 4, "elite": 4, "act2": 4}, prob=1.0, seed=3, starts=sp)
    assert h.base == base and h.kinds == ("boss", "elite", "act2")
    start = np.array([0, 1, 2, 3, 4, 5, 15, base + 100, 6, 7, 8])    # slot 6: a save, slot 7: an anchor (id >= base)
    h.reset(obs_of(n, combat=[4]), start)
    o = obs_of(n, combat=range(10), cells={0: (BOSS, 3, 1), 1: (ELITE, 2, 0)}, acts={2: 1, 5: 1})
    start2 = start.copy()
    start2[5] = 9                                          # slot 5 finished and restarted from entry 9
    done = np.zeros(n, dtype=bool)
    done[5] = True
    status = np.zeros(n, dtype=np.int32)
    status[8] = STATUS["SIM_ERROR"]
    o["cand_n"][9] = 0
    got = h.step(o, done, start2, status, upd=7, t=2)
    assert got == 3
    assert [c[0] for c in env.clones] == [0, 1, 2]
    assert [c[1] for c in env.clones] == [clone_seed(3, 7, 2, s) for s in (0, 1, 2)]
    ids = [c[2] for c in env.clones]
    assert ids == [base, base + 1, base + 2]
    assert list(h.kind_of(ids)) == [KINDS.index("boss"), KINDS.index("elite"), KINDS.index("act2")]
    assert list(h.kind_of([0, base - 1, base + 50])) == [-1, -1, -1]
    assert h.is_anchor([base - 1, base]).tolist() == [False, True]
    st = h.pop_stats()
    assert (st["harvest_captured"], st["harvest_released"], st["harvest_candidates"]) == (3, 0, 3)
    live = (st["harvest_live_boss"], st["harvest_live_elite"], st["harvest_live_act2"], st["harvest_live"])
    assert live == (1, 1, 1, 3)
    assert (st["harvest_captured_boss"], st["harvest_captured_elite"], st["harvest_captured_act2"]) == (1, 1, 1)
    assert st["harvest_starts"] == 1 and st["harvest_starts_in_combat"] == 0      # slot 7 at the reset
    assert st["harvest_anchor_decisions"] == 1 and st["harvest_step_share"] == pytest.approx(1 / 11)
    # the same observation again: every combat is now ongoing, nothing new
    assert h.step(o, np.zeros(n, bool), start2, status, upd=7, t=3) == 0
    st = h.pop_stats()
    assert st["harvest_captured"] == 0 and st["harvest_anchor_decisions"] == 1      # slot 7's decision
    assert st["harvest_step_share"] == pytest.approx(1 / 10)                         # slot 9 had no legal action
    # an auto-reset slot that starts from an anchor in combat counts as an anchor start in combat
    start3 = start2.copy()
    start3[10] = base
    done = np.zeros(n, dtype=bool)
    done[10] = True
    o3 = obs_of(n, combat=range(11))
    assert h.step(o3, done, start3, None, upd=7, t=4) == 0                       # slot 10 just started: no capture
    st = h.pop_stats()
    assert st["harvest_starts"] == 1 and st["harvest_starts_in_combat"] == 1


def test_fallback_rings_any_and_act2():
    n, base = 4, 10
    o0 = obs_of(n)
    o1 = obs_of(n, combat=range(4), cells={0: (BOSS, 3, 0), 2: (ELITE, 1, 2)}, acts={0: 1, 3: 1})
    start = np.arange(n)
    env = FakeEnv(n, base)                                   # any: every combat, the act-1 hallway included
    h = Harvester(env, {"any": 8}, prob=1.0)
    h.reset(o0, start)
    assert h.step(o1, np.zeros(n, bool), start) == 4
    assert set(h.kind_of(range(base, base + 4)).tolist()) == {KINDS.index("any")}
    env = FakeEnv(n, base)                    # act2 only: the act-2 boss and hallway; act-1 elite / hallway skipped
    h = Harvester(env, {"act2": 8}, prob=1.0)
    h.reset(o0, start)
    assert h.step(o1, np.zeros(n, bool), start) == 2 and [c[0] for c in env.clones] == [0, 3]
    env = FakeEnv(n, base)                                   # boss + any: the boss to its ring, the rest to any
    h = Harvester(env, {"boss": 8, "any": 8}, prob=1.0)
    h.reset(o0, start)
    assert h.step(o1, np.zeros(n, bool), start) == 4
    assert h.kind_of(range(base, base + 4)).tolist() == [KINDS.index(k) for k in ("boss", "any", "any", "any")]
    # a cell that is not uniquely current: no boss / elite kind (act 2 -> act2, act 1 -> only any)
    o2 = obs_of(n, combat=range(4), cells={0: (BOSS, 3, 0)}, acts={0: 1})
    o2["map_nodes"][0, 2, 2, 2] = 1
    env = FakeEnv(n, base)
    h = Harvester(env, {"boss": 8, "act2": 8}, prob=1.0)
    h.reset(o0, start)
    assert h.step(o2, np.zeros(n, bool), start) == 1 and h.kind_of([base]).tolist() == [KINDS.index("act2")]


def test_probability_caps_and_fifo_release():
    n, base = 64, 64
    o0, o1 = obs_of(n), obs_of(n, combat=range(n), cells={i: (BOSS, 3, 1) for i in range(n)})
    start = np.arange(n)
    # probability: about half of 64 candidates per step, reproducible from (seed, update, step)
    counts = []
    for rep in range(2):
        env = FakeEnv(n, base)
        h = Harvester(env, {"boss": 1000}, prob=0.5, max_per_step=64, seed=11)
        got = []
        for t in range(20):
            h.reset(o0, start)
            got.append(h.step(o1, np.zeros(n, bool), start, upd=1, t=t))
        counts.append((got, [c[:2] for c in env.clones]))
    assert counts[0] == counts[1]
    assert 0.4 < np.mean(counts[0][0]) / n < 0.6
    # the per-step cap
    env = FakeEnv(n, base)
    h = Harvester(env, {"boss": 1000}, prob=1.0, max_per_step=5)
    h.reset(o0, start)
    assert h.step(o1, np.zeros(n, bool), start) == 5 and len(env.clones) == 5
    # FIFO: ring of 3; a step never clones more than the ring holds; overflow released oldest-first, one call per step
    env = FakeEnv(n, base)
    h = Harvester(env, {"boss": 3, "elite": 2}, prob=1.0, max_per_step=64)
    h.reset(o0, start)
    assert h.step(o1, np.zeros(n, bool), start) == 3                        # 64 boss candidates, ring of 3
    first = [c[2] for c in env.clones]
    assert env.pool.release_calls == []
    two = obs_of(n, combat=[0, 1, 2, 3], cells={0: (BOSS, 3, 1), 1: (BOSS, 3, 1), 2: (ELITE, 2, 1), 3: (ELITE, 2, 1)})
    h.reset(o0, start)
    assert h.step(two, np.zeros(n, bool), start) == 4
    assert env.pool.release_calls == [first[:2]]                            # the two oldest boss anchors, one call
    st = h.pop_stats()
    assert (st["harvest_live_boss"], st["harvest_live_elite"], st["harvest_released"]) == (3, 2, 2)
    assert (st["harvest_captured_boss"], st["harvest_released_boss"], st["harvest_captured_elite"]) == (5, 2, 2)
    assert len(env.pool) == base + 5 and list(h.rings["boss"]) == [first[2]] + [c[2] for c in env.clones[3:5]]
    assert h.kind_of(first).tolist() == [KINDS.index("boss")] * 3           # released ids keep their kind
    tot = h.totals()
    assert tot["captured"] == 7 and tot["released"] == 2 and tot["live"] == {"boss": 3, "elite": 2}
    assert tot["base_pool_entries"] == base and tot["spec"] == "boss:3,elite:2" and tot["ids_issued"] == 7


def test_harvester_refuses_without_release():
    env = FakeEnv(2, 4)
    env.supports_pool_release = False
    with pytest.raises(Sts2VecError, match="ABI 2"):
        Harvester(env, {"boss": 2})
    with pytest.raises(ValueError):
        Harvester(FakeEnv(2, 4), {"boss": 2}, prob=0.0)


# ------------------------------------------------------------------------------------------------ split_stats


def _ep(start_entry, *, win=False, floor=5, start_floor=0, progress=0.1, start_progress=0.0, act=1):
    return {"start_entry": start_entry, "win": win, "floor": floor, "start_floor": start_floor, "act": act,
            "start_act": 1, "progress": progress, "start_progress": start_progress,
            "progress_gain": progress - start_progress, "return": progress - start_progress, "steps": 10}


def test_split_stats_three_groups():
    base = 6
    pool = _start_pool([FRESH, SAVE, FRESH, SAVE, FRESH, FRESH])
    stub = SimpleNamespace(base=base, kinds=("boss", "elite"), is_anchor=lambda s: np.asarray(s) >= base,
                           kind_of=lambda s: np.where(np.asarray(s) >= base,
                                                      np.where(np.asarray(s) % 2 == 0, 0, 1), -1).astype(np.int8))
    fresh = [_ep(0, progress=0.2), _ep(2, progress=0.3, floor=9)]
    saves = [_ep(1, progress=0.5, start_progress=0.4, floor=20, start_floor=18, act=2)]
    anchors = [_ep(6, floor=18, start_floor=17, progress=0.34, start_progress=0.31, act=2),     # boss, cleared
               _ep(8, floor=17, start_floor=17, progress=0.31, start_progress=0.31),             # boss, died there
               _ep(7, floor=8, start_floor=7, progress=0.15, start_progress=0.12)]               # elite, cleared
    eps = fresh + saves + anchors
    plain = split_stats(fresh + saves, pool)
    st = split_stats(eps, pool, stub)
    for k, v in plain.items():                                   # fresh_* / save_* do not see the anchors
        assert st[k] == v, k
    assert (st["fresh_episodes"], st["save_episodes"], st["harvest_episodes"]) == (2, 1, 3)
    assert st["harvest_start_share"] == pytest.approx(3 / 6)
    assert st["harvest_progress_gain_mean"] == pytest.approx(np.mean([0.03, 0.0, 0.03]))
    assert st["harvest_start_floor_mean"] == pytest.approx((17 + 17 + 7) / 3)
    assert (st["harvest_episodes_boss"], st["harvest_episodes_elite"]) == (2, 1)
    assert st["harvest_clear_rate_boss"] == 0.5 and st["harvest_clear_rate_elite"] == 1.0
    assert st["harvest_act_reached"] == {1: 2, 2: 1}
    # without saves: everything below base is fresh (a fresh-only pool), the save group is empty
    st0 = split_stats(eps, None, stub)
    assert (st0["fresh_episodes"], st0["save_episodes"], st0["harvest_episodes"]) == (3, 0, 3)
    assert split_stats(eps, None, None) == {} and split_stats([], pool, stub)["harvest_start_share"] is None
    assert "harvest_episodes" not in split_stats(eps, pool)               # harvest off: two groups, as before
    view = recent_view([recent_sums(eps, pool, stub), recent_sums(anchors, pool, stub), {"episodes": 0}])
    assert (view["fresh_episodes"], view["save_episodes"], view["harvest_episodes"]) == (2, 1, 6)
    assert view["harvest_start_share"] == pytest.approx(6 / 9)
    assert view["harvest_progress_gain_mean"] == pytest.approx(st["harvest_progress_gain_mean"])
    assert "harvest_episodes" not in recent_view([recent_sums(eps, pool)])


# ------------------------------------------------------------------------------------------------ real library


def test_real_env_anchors():
    """17 slots (slot 16 a spare that only no-ops), a 64-entry Silent A10 fresh pool, random play for 1,500 steps with
    one `any` ring of 32 at probability 1: anchors are captured and released (the pool never exceeds base + 32), a
    released id cannot be restored, every live anchor restored into the spare slot shows its source's bytes on the
    model's 27 input buffers, and episodes that start from anchors begin in combat."""
    n, spare = 17, 16
    with VecEnv(n, threads=4, step_cap=3000, seed_text="THARV",
                pool=PoolSpec(characters=("SILENT",), ascension=10, count=64, seed_prefix="THARV")) as env:
        env.pool.warm()
        base = len(env.pool)
        h = Harvester(env, {"any": 32}, prob=1.0, max_per_step=64, seed=5)
        src: dict[int, dict] = {}
        real_clone = env.clone_determinized

        def clone(slot, seed):                               # record the source's model-read bytes at the capture
            eid = real_clone(slot, seed)
            src[eid] = {b: env.buffers[b][slot].copy() for b in USED_BUFFERS}
            return eid

        env.clone_determinized = clone
        c_comb = env.schema.col("aux", "is_combat")
        obs = env.reset()
        h.reset(obs, env.info()["start_entry"])
        rng = np.random.default_rng(3)
        anchor_starts = anchor_starts_combat = errs = 0
        for t in range(1500):
            a = random_actions(rng, obs["cand_n"])
            a[spare] = -1
            obs, rew, term, trunc, status, info = env.step(a)
            errs += int(np.count_nonzero(status & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"])))
            done = (term | trunc).astype(bool)
            h.step(obs, done, info["start_entry"], status, upd=1, t=t)
            new = done & (info["start_entry"] >= base)
            anchor_starts += int(new.sum())
            anchor_starts_combat += int((new & (obs["aux"][:, c_comb] != 0)).sum())
            assert len(env.pool) == base + sum(h.live().values()) <= base + 32
        st = h.pop_stats()
        tot = h.totals()
        assert errs == 0
        assert tot["captured"] > 32 and tot["released"] == tot["captured"] - 32 and h.live() == {"any": 32}
        assert anchor_starts > 0 and anchor_starts_combat == anchor_starts, (anchor_starts, anchor_starts_combat)
        assert tot["anchor_starts"] == anchor_starts and tot["anchor_starts_in_combat"] == anchor_starts
        assert st["harvest_step_share"] is not None and 0.0 < st["harvest_step_share"] < 1.0
        released = sorted(set(src) - set(h.rings["any"]))
        with pytest.raises(Sts2VecError):
            env.restore(spare, released[0])
        for eid in list(h.rings["any"]):
            o = env.restore(spare, eid)
            assert o["aux"][spare, c_comb] != 0
            for b in USED_BUFFERS:
                np.testing.assert_array_equal(o[b][spare], src[eid][b], err_msg=f"entry {eid} buffer {b}")


# ------------------------------------------------------------------------------------------------ CLI


def test_train_cli_harvest(tmp_path):
    """`sts2train train --harvest any:16 --harvest-prob 1`: anchors captured, the harvest record in config.json and
    summary.json, harvest_* keys in the metrics rows; `--resume auto` keeps harvest on with empty rings; `--harvest
    off` turns it off."""
    from sts2train.cli import main
    from sts2train.runlog import read_jsonl

    run = tmp_path / "run"
    common = ["--preset", "smoke", "--device", "cpu", "--envs", "16", "--threads", "2", "--pool", "16", "--rollout",
              "8", "--minibatch", "64", "--torch-threads", "2", "--ckpt-every", "1", "--log-every", "100",
              "--max-hours", "0.5", "--out", str(run)]
    assert main(["train", *common, "--max-updates", "3", "--harvest", "any:16", "--harvest-prob", "1"]) == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["harvest"] == "any:16" and conf["config"]["harvest_prob"] == 1.0
    assert conf["harvest"]["rings"] == {"any": 16} and conf["harvest"]["base_pool_entries"] == 16
    rows = read_jsonl(run / "metrics.jsonl")
    assert [r["update"] for r in rows] == [1, 2, 3]
    assert sum(r["harvest_captured"] for r in rows) > 0
    for k in ("harvest_live_any", "harvest_released", "harvest_clone_s", "harvest_step_share", "harvest_start_share",
              "harvest_episodes", "harvest_win_rate", "harvest_progress_gain_mean", "harvest_clear_rate_any",
              "fresh_episodes", "fresh_progress_mean", "save_episodes"):
        assert k in rows[-1], k
    assert all(r["harvest_live_any"] <= 16 and r["sim_error"] == 0 and r["content_gap"] == 0 for r in rows)
    summ = json.loads((run / "summary.json").read_text())
    s1 = summ["harvest"]
    assert s1["captured"] == sum(r["harvest_captured"] for r in rows)
    assert s1["live"]["any"] == rows[-1]["harvest_live_any"]
    assert "harvest_live" in json.loads((run / "status.json").read_text())
    # resume: harvest stays on (the checkpoint's config), the rings start empty
    assert main(["train", *common, "--max-updates", "4", "--resume", "auto"]) == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["harvest"] == "any:16" and conf["harvest"]["spec"] == "any:16"
    row = read_jsonl(run / "metrics.jsonl")[-1]
    summ = json.loads((run / "summary.json").read_text())["harvest"]
    assert row["update"] == 4 and summ["captured"] == row["harvest_captured"] == summ["ids_issued"]
    assert row["harvest_live_any"] == min(16, row["harvest_captured"])
    # --harvest off on resume
    assert main(["train", *common, "--max-updates", "5", "--resume", "auto", "--harvest", "off"]) == 0
    conf = json.loads((run / "config.json").read_text())
    assert conf["config"]["harvest"] == "" and conf["harvest"] is None
    row = read_jsonl(run / "metrics.jsonl")[-1]
    assert row["update"] == 5 and not any(k.startswith("harvest") for k in row)
    assert "harvest" not in json.loads((run / "summary.json").read_text())
    assert "harvest_live" not in json.loads((run / "status.json").read_text())


# ------------------------------------------------------------------------------------------------ stream mode


class StreamFakeEnv(FakeEnv):
    """FakeEnv + the VecEnv parts StreamEnv needs (auto_reset off, empty pool, restore_many, status, cand_n)."""

    config, supports_restore_many = {"auto_reset": False}, True

    def __init__(self, n):
        super().__init__(n, 0)
        self.status = np.zeros(n, dtype=np.int32)
        self.buffers = {"cand_n": np.full(n, 5, dtype=np.int32)}
        self.restores: list[tuple[int, int]] = []
        self.pool.add_fresh = self._add_fresh
        self.pool.warm = lambda: 0
        self.fresh: set[int] = set()
        self.obs = {}

    def _add_fresh(self, ch, *, seed, ascension=0, act1="random"):
        eid = self.pool.next
        self.pool.next += 1
        self.pool.live.append(eid)
        self.fresh.add(eid)
        return eid

    def restore_many(self, slots, entries):
        for i, e in zip(slots, entries):
            assert int(e) in self.pool.live, e              # only live entries can be restored
            self.restores.append((int(i), int(e)))
            self.status[i] = STATUS["RESET"]

    def last_error(self, i):
        return ""

    def info(self, copy=False):
        return {"step_index": np.zeros(self.num_envs, dtype=np.int32)}


def _stream(n, share, seed=0, rings=None, prob=1.0):
    from sts2train.seedstream import SeedStream, StreamEnv

    env = StreamFakeEnv(n)
    s = StreamEnv(env, SeedStream("SPH", ("SILENT",), 10), lookahead=4, prefetch=False)
    h = Harvester(s, rings or {"boss": 4, "any": 4}, prob=prob, seed=seed, stream=True)
    s.attach_anchors(h, share, seed=seed)
    return env, s, h


def test_stream_capture_rule_uses_issued_ids():
    """Stream mode: stream entries and anchors interleave in id order; an anchor is an id the harvester issued (not
    id >= base); captures only from stream-started episodes; the source seed text / index is kept per live anchor and
    dropped at release."""
    n = 4
    env, s, h = _stream(n, 0.0, rings={"boss": 1, "any": 8})
    s._start(np.arange(n))                                  # stream entries 0..3 (+ lookahead 4..7)
    start = np.asarray(s.slot_entry).copy()
    h.reset(obs_of(n), start)
    o1 = obs_of(n, combat=[0, 1], cells={0: (BOSS, 3, 1)})
    assert h.step(o1, np.zeros(n, bool), start, upd=1, t=0) == 2
    a_boss, a_any = env.clones[0][2], env.clones[1][2]
    assert a_boss > max(start) and h.is_anchor([a_boss, a_any, *start]).tolist() == [True, True] + [False] * n
    assert h.anchor_source(a_boss) == ("SPH-SILENT-0", 0, "boss") and h.anchor_source(a_any)[2] == "any"
    # slot 2 restarts from the anchor a_any; slot 3 takes a lookahead entry issued before the anchors (lower id), and
    # once the lookahead is used up the stream's entries get ids above the anchors: neither is an anchor
    s._start(np.array([3]))
    assert s.slot_entry[3] < a_boss and not h.is_anchor([s.slot_entry[3]])[0]
    for _ in range(5):
        s._start(np.array([1]))
    assert s.slot_entry[1] > a_any and not h.is_anchor([s.slot_entry[1]])[0]
    s._start(np.array([1]))
    start2 = np.asarray(s.slot_entry).copy()
    start2[2] = a_any
    h.reset(obs_of(n), start2)
    assert h.step(obs_of(n, combat=[2, 3]), np.zeros(n, bool), start2, upd=1, t=1) == 1    # slot 3 only
    assert env.clones[-1][0] == 3
    # a second boss: the ring of 1 releases the first, its source is forgotten, its kind is kept
    h.reset(obs_of(n), start2)
    assert h.step(obs_of(n, combat=[0], cells={0: (BOSS, 3, 1)}), np.zeros(n, bool), start2, upd=1, t=2) == 1
    assert env.pool.release_calls[-1] == [a_boss] and h.anchor_source(a_boss) == (None, None, "boss")
    assert h.kind_of([a_boss]).tolist() == [KINDS.index("boss")] and h.record()["start_mode"] == "stream"


def test_stream_anchor_starts_share_and_counters():
    """Finished slots start from a live anchor at the configured share; anchor starts consume no stream index, keep
    the anchor live, carry start_kind anchor:<kind> and the source text; the episode-id identity holds on
    slot_attempts; without live anchors every start is a stream start."""
    n = 16
    env, s, h = _stream(n, 0.5, seed=4, rings={"boss": 4, "act2": 4})
    s.reset()
    start = np.asarray(s.slot_entry).copy()
    h.reset(obs_of(n), start)
    # no anchors yet: a step's ended slots are all stream starts
    s.ended = {}
    left = s._start_anchors(np.arange(n))
    assert left.tolist() == list(range(n)) and s.update["anchor_starts"] == 0
    o = obs_of(n, combat=range(8), cells={i: (BOSS, 3, 1) for i in range(4)}, acts={i: 1 for i in range(4, 8)})
    assert h.step(o, np.zeros(n, bool), start, upd=1, t=0) == 8
    live = set(h.live_ids().tolist())
    assert len(live) == 8
    st0 = (s.stream.state.next, s.stream.state.started)
    stream_before = s.update["starts"]
    total, anchors = 0, 0
    for r in range(200):                                    # 200 rounds of 16 ended slots
        slots = np.arange(n)
        left = s._start_anchors(slots)
        if len(left):
            s._start(left)
        a = set(range(n)) - set(left.tolist())
        anchors += len(a)
        total += n
        for i in a:
            assert s.slot_entry[i] in live and s.slot_n[i] == -1 and s.slot_kind[i] in ("anchor:boss", "anchor:act2")
            assert s.slot_text[i] is not None and s.slot_text[i].startswith("SPH-SILENT-")
        for i in left:
            assert s.slot_kind[i] == "stream" and s.slot_n[i] >= 0
    assert 0.45 < anchors / total < 0.55, anchors / total
    assert s.update["anchor_starts"] == anchors and s.update["starts"] - stream_before == total - anchors
    st = s.stream.state
    assert st.started - st0[1] == total - anchors and st.next - st0[0] == total - anchors    # counter: streams only
    assert set(h.live_ids().tolist()) == live and all(e in env.pool.live for e in live)     # anchors stay live
    assert (s.slot_attempts == s.slot_starts + s.slot_anchor_starts).all()
    assert sum(s.slot_starts) == st.started
    u = s.pop_update()
    assert u["anchor_starts"] == anchors and u["anchor_start_failed"] == 0
    assert s.record()["anchor_share"] == 0.5 and s.record()["anchor_starts"] == anchors
    # annotate: start_kind of the ended episodes, n None for anchor episodes
    s.ended = {i: (int(s.slot_n[i]), s.slot_text[i], int(s.slot_entry[i])) for i in range(n)}
    s.ended_kind = {i: s.slot_kind[i] for i in range(n)}
    fin = [{"slot": i} for i in range(n)]
    s.annotate(fin)
    for e in fin:
        assert e["start_kind"] == s.slot_kind[e["slot"]]
        assert (e["seed_n"] is None) == e["start_kind"].startswith("anchor:")
    inf = s.inflight()
    assert inf["slot"] == list(range(n)) and len(inf["start_kind"]) == n
    assert all((nn is None) == k.startswith("anchor:") for nn, k in zip(inf["n"], inf["start_kind"]))


def test_stream_share_zero_and_one():
    n = 8
    for share, want in ((0.0, 0), (1.0, n)):
        env, s, h = _stream(n, share, rings={"any": 2})
        s.reset()
        start = np.asarray(s.slot_entry).copy()
        h.reset(obs_of(n), start)
        assert h.step(obs_of(n, combat=[0]), np.zeros(n, bool), start, upd=1, t=0) == 1
        left = s._start_anchors(np.arange(n))
        assert n - len(left) == want


def test_stream_failed_anchor_start_falls_back_to_the_stream():
    n = 4
    env, s, h = _stream(n, 1.0, rings={"any": 2})
    s.reset()
    start = np.asarray(s.slot_entry).copy()
    h.reset(obs_of(n), start)
    h.step(obs_of(n, combat=[0]), np.zeros(n, bool), start, upd=1, t=0)
    anchor = h.live_ids()[0]
    real = env.restore_many

    def restore_many(slots, entries):
        real(slots, entries)
        for i, e in zip(slots, entries):
            env.buffers["cand_n"][i] = 0 if int(e) == anchor else 5

    env.restore_many = restore_many
    left = s._start_anchors(np.array([1, 3]))
    assert left.tolist() == [1, 3] and s.update["anchor_start_failed"] == 2 and s.update["anchor_starts"] == 0


def test_resolve_config_start_share():
    from sts2train.ppo import resolve_config

    assert resolve_config({"start_mode": "stream", "harvest": "boss:4"}).harvest_start_share == 0.33
    assert resolve_config({"start_mode": "stream", "harvest": "boss:4",
                           "harvest_start_share": 0.5}).harvest_start_share == 0.5
    assert resolve_config({"start_mode": "stream"}).harvest_start_share is None       # harvest off
    assert resolve_config({"harvest": "boss:4"}).harvest_start_share is None          # pool mode
    for given in ({"start_mode": "stream", "harvest": "boss:4", "harvest_start_share": 1.5},
                  {"harvest": "boss:4", "harvest_start_share": 0.3}):
        with pytest.raises(ValueError):
            resolve_config(given)


def test_real_stream_env_anchors():
    """The real library in stream mode: 12 slots, random play, rings any:8 at probability 1, share 0.5: anchors are
    captured only on stream-started episodes, anchor-started episodes begin in combat, the anchor share of the starts
    after the first capture is about 0.5, the stream counter counts stream starts only, and the live pool is the
    lookahead plus the live anchors."""
    from sts2train.seedstream import SeedStream, StreamEnv

    n, LA = 12, 8
    env = VecEnv(n, threads=4, auto_reset=False, pool=None, seed_text="THS", step_cap=3000)
    s = StreamEnv(env, SeedStream("SPHS", ("SILENT",), 10), lookahead=LA, prefetch=False)
    try:
        h = Harvester(s, {"any": 8}, prob=1.0, seed=2, stream=True)
        s.attach_anchors(h, 0.5, seed=2)
        sources = []
        real_clone = env.clone_determinized

        def clone(slot, seed):
            sources.append(s.slot_kind[slot])
            return real_clone(slot, seed)

        env.clone_determinized = clone
        s.prime()
        env.pool.warm()
        obs = s.reset()
        h.reset(obs, s.info()["start_entry"])
        c_comb = env.schema.col("aux", "is_combat")
        rng = np.random.default_rng(5)
        a_starts = a_comb = st_starts = 0
        for t in range(1500):
            had = len(h.live_ids()) > 0                     # live anchors when this step's ended slots restart
            obs, rew, term, trunc, status, info = s.step(random_actions(rng, obs["cand_n"]))
            done = (term | trunc).astype(bool)
            h.step(obs, done, info["start_entry"], status, upd=1, t=t)
            if done.any():
                anc = done & h.is_anchor(info["start_entry"])
                a_starts += int(anc.sum())
                a_comb += int((anc & (obs["aux"][:, c_comb] != 0)).sum())
                st_starts += int((done & ~anc).sum()) if had else 0
            assert len(env.pool) == len(s.fifo) + len(h.live_ids())
            if t % 100 == 0:
                s.prefetch()
        h.pop_stats()
        tot = h.totals()
        assert tot["captured"] > 8 and set(sources) == {"stream"}
        assert a_starts > 5 and a_comb == a_starts
        assert 0.3 < a_starts / (a_starts + st_starts) < 0.7, (a_starts, st_starts)
        assert s.stream.state.started == int(s.slot_starts.sum()) and int(s.slot_anchor_starts.sum()) == a_starts
        ids = s.info()["episode_id"]
        assert (ids == (s.slot_attempts - 1) * n + np.arange(n) + 1).all()
    finally:
        s.close()


def test_stream_train_harvest_resume(tmp_path):
    """The trainer in stream mode with --harvest any:16 --harvest-prob 1 --harvest-start-share 0.5 and a short step
    cap (so slots restart): anchor starts in the metrics and episodes.jsonl (start_kind, n null), the stream identity
    seeds_used_total == starts_total == seed_stream_next still holds, stream indices are never reused; a resume starts
    with empty rings and continues the run counters."""
    from sts2train.ppo import plan_run, train
    from sts2train.runlog import read_jsonl

    import torch

    run = tmp_path / "run"
    base = {"envs": 8, "threads": 2, "rollout": 32, "minibatch": 64, "epochs": 1, "eval_every": 0, "ckpt_every": 1,
            "ckpt_every_min": 0, "keep_ckpts": 10, "log_every": 100, "max_hours": 0.5, "torch_threads": 1,
            "characters": ("SILENT",), "ascension": 10, "d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2,
            "start_mode": "stream", "step_cap": 40, "harvest": "any:16", "harvest_prob": 1.0,
            "harvest_start_share": 0.5}

    def go(updates, resume=None):
        given = {**base, "out": str(run), "max_updates": updates} if resume is None else \
            {"out": str(run), "max_updates": updates}
        cfg, ck, ckp = plan_run(given, resume, log=lambda m: None)
        return train(cfg, log=lambda m: None, resume=ck, resume_path=ckp, given=given)

    s1 = go(4)
    m1 = read_jsonl(run / "metrics.jsonl")
    assert sum(r["harvest_captured"] for r in m1) > 0 and sum(r["anchor_starts_update"] for r in m1) > 0
    assert all(r["seeds_used_total"] == r["starts_total"] == r["seed_stream_next"] for r in m1)
    for r in m1:
        assert r["harvest_starts"] == r["anchor_starts_update"]
        assert r["harvest_episodes"] + r["fresh_episodes"] == r["episodes"] and r["save_episodes"] == 0
    rows = read_jsonl(run / "episodes.jsonl")
    kinds = {r["start_kind"] for r in rows}
    assert kinds == {"stream", "anchor:any"}, kinds
    assert all((r["n"] is None) == (r["start_kind"] != "stream") for r in rows)
    assert sum(r["start_kind"] != "stream" for r in rows) == sum(r["harvest_episodes"] for r in m1)
    ck4 = torch.load(run / "checkpoints" / "ckpt-0000004.pt", weights_only=False)
    hk = ck4["harvest"]
    assert hk["captured"] == sum(r["harvest_captured"] for r in m1) == m1[-1]["harvest_captured_run"]
    assert hk["anchor_starts"] == m1[-1]["harvest_starts_run"] and hk["sessions"] == 1
    assert len(ck4["inflight"]["start_kind"]) == 8
    conf = json.loads((run / "config.json").read_text())
    assert conf["harvest"]["start_mode"] == "stream" and conf["seed_stream"]["anchor_share"] == 0.5
    assert s1["harvest"]["run_totals"]["captured"] == hk["captured"]

    go(6, resume="auto")
    m = read_jsonl(run / "metrics.jsonl")
    m2 = [r for r in m if r["session"] == 2]
    assert [r["update"] for r in m2] == [5, 6]
    assert m2[0]["harvest_live"] <= m2[0]["harvest_captured"]                   # the rings started empty
    assert m2[-1]["harvest_captured_run"] == hk["captured"] + sum(r["harvest_captured"] for r in m2)
    assert m2[-1]["harvest_starts_run"] == hk["anchor_starts"] + sum(r["harvest_starts"] for r in m2)
    assert all(r["seeds_used_total"] == r["starts_total"] == r["seed_stream_next"] for r in m)
    rows = read_jsonl(run / "episodes.jsonl")
    dropped = [r for r in rows if r["result"] == "dropped"]
    assert len(dropped) == 8 and all("start_kind" in r for r in dropped)
    ns = [r["n"] for r in rows if r["n"] is not None]
    assert len(ns) == len(set(ns))                                              # no stream index twice
    ck6 = torch.load(run / "checkpoints" / "ckpt-0000006.pt", weights_only=False)
    assert ck6["harvest"]["sessions"] == 2 and ck6["harvest"]["captured"] == m2[-1]["harvest_captured_run"]
