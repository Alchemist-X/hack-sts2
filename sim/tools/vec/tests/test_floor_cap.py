"""floor_step_cap: the env truncates an episode after ``floor_step_cap`` non-combat decisions on one floor without the
floor advancing (status FLOOR_STALL, ``last_episode.end_kind`` FloorStall) — the protocol evaluator's ``stuck`` rule,
now enforced by the env for training and evaluation alike."""

from __future__ import annotations

import json

import numpy as np
import pytest

from conftest import SMALL_POOL
from sts2vec import FLOOR_STEP_CAP, STATUS, PoolSpec, Sts2VecError, VecEnv, random_actions
from sts2vec.evaluate import evaluate
from sts2vec.pool import make_config

FLOOR_STALL = STATUS["FLOOR_STALL"]


def test_floor_step_cap_config_and_defaults(lib, schema):
    assert FLOOR_STEP_CAP == 80 and FLOOR_STALL == 256
    assert schema.supports_floor_step_cap
    assert schema.vocab.enums["EndKind"][-1] == "FloorStall"
    with VecEnv(2, threads=1, pool=SMALL_POOL) as env:
        assert env.floor_step_cap == 80 and env.config["floor_step_cap"] == 80
    with VecEnv(2, threads=1, pool=SMALL_POOL, floor_step_cap=0) as env:
        assert env.config["floor_step_cap"] == 0
    with pytest.raises(ValueError):
        VecEnv(2, pool=SMALL_POOL, floor_step_cap=-1)
    assert "floor_step_cap" not in make_config(2)          # None = the library's default
    bad = json.dumps(dict(make_config(1), floor_step_cap=-3)).encode()
    assert lib.sts2vec_create(bad, len(bad)) == -1 and "floor_step_cap" in lib.last_error(0)


def _stall_until_cut(env, steps: int, action_fn) -> int | None:
    """Steps slot 0 with ``action_fn(obs)`` up to ``steps`` times; returns the 1-based step that set FLOOR_STALL."""
    obs = env.obs
    for t in range(1, steps + 1):
        obs, _, term, trunc, status, _ = env.step(np.array([action_fn(obs)], dtype=np.int32))
        if status[0] & FLOOR_STALL:
            assert trunc[0] == 1 and term[0] == 0
            return t
        assert trunc[0] == 0 and term[0] == 0, f"step {t}: status {status[0]}"
    return None


def test_default_cap_through_the_abi_matches_python(lib, schema, full):
    """A config without the key gets the C# default; an always-illegal policy at the opening (non-combat) screen is cut
    at exactly decision FLOOR_STEP_CAP. Same through VecEnv with an explicit cap."""
    cfg = json.dumps(make_config(1, threads=1, auto_reset=False, pool=PoolSpec(("IRONCLAD",), count=1))).encode()
    h = lib.sts2vec_create(cfg, len(cfg))
    assert h > 0
    try:
        bufs = schema.allocate(1)
        assert lib.sts2vec_reset(h, None, None, bufs.ptr) == 0
        gi = schema.cols("global_i")
        assert bufs["global_i"][0, gi.is_combat] == 0 and bufs["cand_n"][0] > 0
        act = np.array([10_000], dtype=np.int32)
        for t in range(1, FLOOR_STEP_CAP + 1):
            assert lib.sts2vec_step(h, act.ctypes.data, bufs.ptr) == 0
            st = int(bufs["status"][0])
            assert st & STATUS["ILLEGAL_ACTION"]
            assert bool(st & FLOOR_STALL) == (t == FLOOR_STEP_CAP), f"decision {t}: status {st}"
        assert bufs["truncated"][0] == 1
        le = schema.cols("last_episode")
        assert schema.vocab.enums["EndKind"].name(bufs["last_episode"][0, le.end_kind]) == "FloorStall"
    finally:
        lib.sts2vec_destroy(h)
    with VecEnv(1, threads=1, auto_reset=False, pool=PoolSpec(("SILENT",), count=1), floor_step_cap=9) as env:
        env.reset()
        assert _stall_until_cut(env, 50, lambda obs: 10_000) == 9
        assert env.last_episode(0)["end_kind"] == "FloorStall" and env.last_episode(0)["steps"] == 0
    with VecEnv(1, threads=1, auto_reset=False, pool=PoolSpec(("SILENT",), count=1), floor_step_cap=0) as env:
        env.reset()
        assert _stall_until_cut(env, 200, lambda obs: 10_000) is None


class _Reference:
    """The protocol evaluator's ``stuck`` rule (the pre-env implementation in ``sts2vec eval``), from observations only:
    per floor, count the decisions taken at non-combat observations; a game is stuck when an observation on the same
    floor is non-combat and the count has reached the cap."""

    def __init__(self, n: int, cap: int):
        self.cap = cap
        self.floor = np.full(n, -1, dtype=np.int64)
        self.count = np.zeros(n, dtype=np.int64)

    def observe(self, i: int, floor: int) -> None:
        if floor != self.floor[i]:
            self.floor[i], self.count[i] = floor, 0

    def decide(self, i: int, combat: bool) -> None:
        if not combat:
            self.count[i] += 1

    def stuck(self, i: int, floor: int, combat: bool) -> bool:
        self.observe(i, floor)
        return not combat and self.count[i] >= self.cap


def test_floor_cap_matches_the_evaluator_rule_with_a_shop_stall_policy(full):
    """64 games under a random policy that turns into a staller in any shop where the card removal is affordable
    (BuyEntry removal -> PromptCancel, the loop the train smoke learned). With auto_reset off, every step's FLOOR_STALL
    must coincide exactly with the evaluator rule computed from the observations (combat decisions excluded, terminal
    and step_cap first), and every shop stall is cut after exactly ``cap`` non-combat decisions on its floor."""
    n, cap, steps = 64, 12, 900
    with VecEnv(n, threads=4, auto_reset=False, seed_text="FLOORCAP", step_cap=3000, floor_step_cap=cap,
                pool=PoolSpec(count=64, seed_prefix="FLOORCAP")) as env:
        S = env.schema
        G, C, SH = S.cols("global_i"), S.cols("cand"), S.cols("shop")
        kinds, phases = S.vocab.enums["RunActionKind"], S.vocab.enums["RunPhase"]
        buy, cancel = kinds.index_of("BuyEntry"), kinds.index_of("PromptCancel")
        shop_phase, prompt_phase = phases.index_of("Shop"), phases.index_of("Prompt")
        removal = S.vocab.enums["ShopCategory"].index_of("card_removal")
        rng = np.random.default_rng(7)
        ref = _Reference(n, cap)
        obs = env.reset()
        for i in range(n):
            ref.observe(i, int(obs["global_i"][i, G.total_floor]))
        stalling = np.zeros(n, dtype=bool)
        shop_stalls = other_stalls = 0
        for t in range(steps):
            gi = obs["global_i"].copy()
            acts = random_actions(rng, obs["cand_n"])
            for i in range(n):
                dk = int(gi[i, G.decision_kind])
                rows = obs["cand"][i, :obs["cand_n"][i]]
                if dk == shop_phase:
                    srow = obs["shop"][i]
                    ok = np.flatnonzero((srow[:, SH.category] == removal) & (srow[:, SH.affordable] == 1))
                    hit = np.flatnonzero((rows[:, C.kind] == buy) & np.isin(rows[:, C.option_index], ok))
                    if len(hit):
                        stalling[i] = True
                        acts[i] = hit[0]
                elif stalling[i] and dk == prompt_phase:
                    hit = np.flatnonzero(rows[:, C.kind] == cancel)
                    if len(hit):
                        acts[i] = hit[0]
            for i in range(n):
                if acts[i] >= 0:
                    ref.decide(i, bool(gi[i, G.is_combat]))
            obs, rew, term, trunc, status, info = env.step(acts)
            gi2 = obs["global_i"]
            for i in range(n):
                cut = bool(status[i] & FLOOR_STALL)
                expect = (not term[i] and not (status[i] & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]))
                          and info["step_index"][i] < 3000
                          and ref.stuck(i, int(gi2[i, G.total_floor]), bool(gi2[i, G.is_combat])))
                assert cut == expect, f"step {t} slot {i}: env {cut}, evaluator rule {expect} (count {ref.count[i]})"
                if cut:
                    assert trunc[i] == 1 and env.last_episode(i)["end_kind"] == "FloorStall"
                    assert ref.count[i] == cap
                    if stalling[i]:
                        shop_stalls += 1
                    else:
                        other_stalls += 1
            done = (term | trunc).astype(bool)
            if done.any():
                stalling[done] = False
                obs = env.reset(mask=done)
                for i in np.flatnonzero(done):
                    ref.floor[i], ref.count[i] = -1, 0
                    ref.observe(int(i), int(obs["global_i"][i, G.total_floor]))
        assert shop_stalls >= 3, f"only {shop_stalls} shop stalls reached (other stalls {other_stalls})"


def test_eval_noop_policy_ends_stuck(tmp_path, full):
    """sts2vec eval sends a live game's no-op as an illegal decision, so it counts toward the env's floor cap."""
    ss = {"name": "t1", "ascension": 0, "content": "ported_only", "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}]}
    rep = evaluate(ss, lambda obs, ctx: np.full(ctx.num_envs, -1, dtype=np.int32), tmp_path / "noop", threads=1,
                   step_cap=50, floor_step_cap=4, log=lambda _: None)
    g = rep["games"][0]
    assert g["result"] == "stuck" and g["illegal"] == 4 and g["steps"] == 4 and g["replayable"]
    s = json.loads((tmp_path / "noop" / "games" / "IRONCLAD-E1A0IC01.summary.json").read_text())
    assert s["stuck"]["floor"] == s["floor"] and s["truncated"] is True


def test_old_library_without_floor_cap_is_refused(monkeypatch, schema):
    """A library whose schema predates floor_step_cap: a non-zero cap is refused with a rebuild hint; 0 omits the key."""
    monkeypatch.setattr(type(schema), "supports_floor_step_cap", property(lambda self: False))
    with pytest.raises(Sts2VecError, match="rebuild"):
        VecEnv(1, pool=SMALL_POOL, floor_step_cap=5)
    with VecEnv(1, threads=1, pool=SMALL_POOL, floor_step_cap=0) as env:
        assert "floor_step_cap" not in env.config
