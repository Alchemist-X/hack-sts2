"""Gate tests of the decision-time combat lookahead (``sts2train.lookahead``), CPU, random-init models.

The ABI semantics the search relies on (cross-slot exact restore, determinized clones, restore counters), the shadow
mirror through the unmodified evaluator (``sts2vec.evaluate``), K=1 = greedy, identical re-runs, honest mode (never an
exact snapshot), defeat latching with slot reuse, rebuild equivalence, the paired statistics and the Silent seed sets.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
import pytest
import torch
from sts2vec import STATUS, FreshEntry, PoolSpec, VecEnv, load, schema_of
from sts2vec import protocol as PG
from sts2vec.policies import HeuristicPolicy, PolicyContext

from sts2train import lookahead as L
from sts2train.features import ModelSpec, bind_spec
from sts2train.model import Policy
from sts2train.ppo import TrainConfig, save_checkpoint

TINY = {"d": 64, "s": 128, "depth": 1, "heads": 4, "ff_mult": 2}
SS4 = {"name": "lookahead-test4", "ascension": 10, "content": "ported_only",
       "games": [{"character": "SILENT", "seed": f"LKT{i:02d}"} for i in range(1, 5)]}
TURN = L.LookaheadConfig(K=8, R=1, H=12, horizon="turn", threads=2)


def quiet(_m: str) -> None:
    pass


def histories(run: Path) -> dict[str, list[int]]:
    return {p.name: json.loads(p.read_text())["history_actions"] for p in sorted((run / "games").glob("*.summary.json"))}


def stats(run: Path) -> dict:
    return json.loads((run / "stats.json").read_text())


@pytest.fixture(scope="module")
def spec():
    s = ModelSpec.from_schema(schema_of(load()))
    bind_spec(s)
    return s


@pytest.fixture(scope="module")
def ckpt(tmp_path_factory, spec):
    torch.manual_seed(0)
    p = tmp_path_factory.mktemp("la") / "tiny.pt"
    save_checkpoint(p, Policy(spec, **TINY), spec, TrainConfig(), {"updates": 0})
    return p


@pytest.fixture(scope="module")
def runs(tmp_path_factory, ckpt):
    """The greedy baseline and the default turn lookahead on the 4-game set (shared by several tests)."""
    root = tmp_path_factory.mktemp("runs")
    L.evaluate_greedy(SS4, ckpt, root / "greedy", threads=2, log=quiet)
    L.evaluate_lookahead(SS4, ckpt, root / "la", TURN, log=quiet)
    return root


# ------------------------------------------------------------------------------------------------ ABI semantics


@pytest.fixture(scope="module")
def combat_env():
    """8 slots, auto_reset off; slot 0 played by the heuristic to a combat decision (turn >= 2, >= 3 candidates) and
    then left alone (the tests only step the slots they restore); ``root`` = an exact snapshot of it."""
    env = VecEnv(8, threads=2, auto_reset=False, seed_text="LKABI",
                 pool=PoolSpec.of(FreshEntry("SILENT", "LKPROBE1", ascension=10)))
    S = env.schema
    G = S.cols("global_i")
    mask = np.zeros(8, np.uint8)
    mask[0] = 1
    env.reset(mask=mask, start_ids=np.array([0] + [-1] * 7, np.int32))
    pol = HeuristicPolicy()
    ctx = PolicyContext(schema=S, num_envs=8, live=np.ones(8, bool))
    for _ in range(400):
        gi = env.obs["global_i"][0]
        if gi[G.is_combat] == 1 and env.obs["cand_n"][0] >= 3 and gi[G.turn] >= 2:
            break
        a = np.full(8, -1, np.int32)
        a[0] = pol(env.obs, ctx)[0]
        env.step(a)
    assert env.obs["global_i"][0, G.is_combat] == 1
    yield env, env.snapshot(0), pol, ctx
    env.close()


def _same_view(env, i, j) -> list[str]:
    return [k for k, v in env.obs.items() if not np.array_equal(v[i], v[j])]


def test_cross_slot_restore_is_exact(combat_env):
    env, root, pol, ctx = combat_env
    env.restore(5, root)
    assert _same_view(env, 0, 5) == []
    assert env.state_hash([0])[0] == env.state_hash([5])[0] != 0
    env.restore(4, root)
    for _ in range(6):                          # two restores of one entry replay identically
        if env.obs["cand_n"][4] <= 0:
            break
        a = np.full(8, -1, np.int32)
        a[4] = a[5] = pol(env.obs, ctx)[4]
        env.step(a)
        h = env.state_hash([4, 5])
        assert h[0] == h[1] and _same_view(env, 4, 5) == []


def test_determinized_clone_same_view_resampled_hidden_state(combat_env):
    env, _, _, _ = combat_env
    d1, d2, d3 = env.clone_determinized(0, 12345), env.clone_determinized(0, 12345), env.clone_determinized(0, 999)
    for slot, entry in ((1, d1), (2, d2), (3, d3)):
        env.restore(slot, entry)
        assert _same_view(env, 0, slot) == []
    h0, h1, h2, h3 = env.state_hash([0, 1, 2, 3])
    assert h1 != h0                             # hidden state resampled
    assert h1 == h2                             # same state + same seed -> same clone
    assert h3 != h1                             # another seed -> another clone


def test_restore_counters_and_slot_reuse_after_defeat(combat_env):
    """A slot that finished an episode (defeat by always ending the turn) is restored: step index 0, status RESET,
    last_episode not rewritten, the exact root state again."""
    env, root, _, _ = combat_env
    S = env.schema
    end = S.vocab.enums["RunActionKind"].index_of("EndTurn")
    kind = S.cols("cand").kind
    env.restore(6, root)
    for _ in range(200):
        a = np.full(8, -1, np.int32)
        rows = env.obs["cand"][6, :env.obs["cand_n"][6], kind]
        a[6] = int(np.flatnonzero(rows == end)[0]) if (rows == end).any() else 0
        _, rew, term, trunc, _, _ = env.step(a)
        if term[6] or trunc[6]:
            break
    assert term[6] == 1 and rew[6] == 0         # a defeat
    le = env.info(copy=True)["last_episode"][6]
    assert le[S.cols("last_episode").win] == 0 and le[S.cols("last_episode").steps] > 0
    env.restore(6, root)
    inf = env.info()
    assert inf["step_index"][6] == 0
    assert env.status[6] & STATUS["RESET"]
    assert np.array_equal(inf["last_episode"][6], le)
    assert _same_view(env, 0, 6) == []
    assert env.state_hash([6])[0] == env.state_hash([0])[0]


# ------------------------------------------------------------------------------------------------ the policy


def test_shadow_sync_through_the_evaluator(runs):
    run = runs / "la"
    rep = json.loads((run / "report.json").read_text())
    st = stats(run)
    assert rep["headline"]["games"] == rep["headline"]["replayable"] == 4
    assert st["desyncs"] == 0 and st["sync_checks"] == st["live_decisions"] > 0
    assert st["searched"] > 0 and st["restores"] >= 2 * st["searched"] and st["rollout_errors"] == 0
    assert st["outcomes"]["turn"] > 0 and st["outcomes"]["horizon"] <= st["restores"]
    cfg = json.loads((run / "config.json").read_text())
    assert cfg["policy"] == "nn:tiny.pt+la(8,1,turn,honest)"
    assert cfg["prompt_hash"] == f"weights:{cfg['weights_hash']}+la:{TURN.sha8}"
    assert cfg["information"] == "determinized, combat-only" and cfg["mirror_env"] is True
    assert cfg["search"]["K"] == 8 and cfg["search"]["horizon"] == "turn"


def test_k1_is_greedy(runs, ckpt, tmp_path):
    L.evaluate_lookahead(SS4, ckpt, tmp_path / "k1", L.LookaheadConfig(K=1, threads=2), log=quiet)
    assert histories(tmp_path / "k1") == histories(runs / "greedy")
    assert stats(tmp_path / "k1")["searched"] == 0


def test_rerun_is_identical(runs, ckpt, tmp_path):
    L.evaluate_lookahead(SS4, ckpt, tmp_path / "again", TURN, log=quiet)
    assert histories(tmp_path / "again") == histories(runs / "la")
    a, b = stats(runs / "la"), stats(tmp_path / "again")
    for k in ("searched", "changed", "restores", "forwards", "env_steps", "outcomes", "changed_to"):
        assert a[k] == b[k], k


def test_honest_mode_never_snapshots(ckpt, tmp_path, monkeypatch):
    clones = []
    real = VecEnv.clone_determinized

    def no_snapshot(self, slot):
        raise AssertionError("the lookahead called snapshot (exact clone): not honest")

    def counting(self, slot, seed):
        clones.append(seed)
        return real(self, slot, seed)

    monkeypatch.setattr(VecEnv, "snapshot", no_snapshot)
    monkeypatch.setattr(VecEnv, "clone_determinized", counting)
    cfg = L.LookaheadConfig(K=4, R=2, H=40, horizon="combat", threads=2)
    rep = L.evaluate_lookahead(SS4, ckpt, tmp_path / "honest", cfg, log=quiet)
    assert rep["headline"]["replayable"] == 4
    assert len(clones) == 2 * stats(tmp_path / "honest")["searched"] > 0
    assert len(set(clones)) == len(clones)      # one fresh determinization seed per (game, decision, r)


class EndTurnModel(torch.nn.Module):
    """Always ends the turn (else the first candidate); value 0."""

    def __init__(self, spec):
        super().__init__()
        self.kind = spec.cols["cand"]["kind"]
        self.end = spec.enum_names["RunActionKind"].index("EndTurn")

    def forward(self, x):
        cand = x["cand"]
        valid = torch.arange(cand.shape[1]).view(1, -1) < x["cand_n"].view(-1, 1)
        logits = (cand[..., self.kind] == self.end).float().masked_fill(~valid, -1e9)
        return logits, torch.zeros(cand.shape[0])


def test_defeat_is_latched_and_slots_are_reused(spec):
    """Rollouts that only end turns die: every rollout latches defeat (Q 0, ties -> greedy), a defeated slot never
    acts again in its round, and the next round restores into the same slots."""
    pol = L.LookaheadPolicy(EndTurnModel(spec), spec, L.LookaheadConfig(K=2, R=1, H=40, horizon="combat", threads=2))
    S = schema_of(load())
    ctx = PolicyContext(schema=S, num_envs=1, live=np.ones(1, bool), keys=["SILENT-LKDEF1"], ascension=10)
    pol._init(ctx)
    env = pol.env
    log, real_step, real_reset = [], env.step, env.reset

    def step(actions, **kw):
        out = real_step(actions, **kw)
        log.append(("step", np.array(actions, copy=True), out[2].copy()))
        return out

    def reset(mask=None, start_ids=None, **kw):
        log.append(("reset", None if mask is None else np.array(mask, copy=True), None))
        return real_reset(mask=mask, start_ids=start_ids, **kw)

    env.step, env.reset = step, reset
    rounds = 0
    for _ in range(400):
        obs = {k: v[:1].copy() for k, v in env.obs.items()}   # the shadow's rows stand in for the evaluator's
        if obs["cand_n"][0] <= 0 or env.buffers["terminated"][0]:
            break
        before = pol.stats["searched"]
        pol(obs, ctx)
        rounds += pol.stats["searched"] - before
    st = pol.stats
    pol.close()
    assert env.buffers["terminated"][0] == 1 and env.buffers["reward"][0] == 0   # the game itself was lost
    assert rounds >= 2 and st["restores"] == 2 * rounds
    assert st["outcomes"]["defeat"] == st["restores"] and st["rollout_errors"] == 0
    assert st["changed"] == 0                                  # all Q = 0: ties go to the greedy choice
    dead: set[int] = set()
    for kind, a, term in log:
        if kind == "reset":
            dead = set()                                       # a new round restores the rollout slots
            continue
        for s in (1, 2):
            assert not (s in dead and a[s] >= 0), "a defeated rollout slot acted again"
            if term[s]:
                dead.add(s)


def test_forced_rebuild_equals_no_rebuild(runs, ckpt, tmp_path):
    cfg = L.LookaheadConfig(K=8, R=1, H=12, horizon="turn", threads=2, pool_budget=4 + 25)
    L.evaluate_lookahead(SS4, ckpt, tmp_path / "rebuild", cfg, log=quiet)
    st = stats(tmp_path / "rebuild")
    assert st["rebuilds"] >= 3 and st["desyncs"] == 0
    assert histories(tmp_path / "rebuild") == histories(runs / "la")
    assert st["changed"] == stats(runs / "la")["changed"]


# ------------------------------------------------------------------------------------------------ analysis, data


def test_paired_statistics(runs):
    r = L.paired(runs / "greedy", runs / "la")
    pa = {k: v["progress"] for k, v in L._summaries(runs / "greedy").items()}
    pb = {k: v["progress"] for k, v in L._summaries(runs / "la").items()}
    assert r["games"] == 4 and r["better"] + r["worse"] + r["same"] == 4
    assert r["per_game"] == {k: round(pb[k] - pa[k], 6) for k in pa}
    assert math.isclose(r["delta_mean"], np.mean([pb[k] - pa[k] for k in pa]), abs_tol=1e-6)
    assert r["t_ci95"][0] <= r["delta_mean"] <= r["t_ci95"][1]
    assert r["bootstrap_ci95"][0] <= r["delta_mean"] + 1e-9 and r["delta_mean"] - 1e-9 <= r["bootstrap_ci95"][1]
    assert r == L.paired(runs / "greedy", runs / "la")          # deterministic bootstrap
    assert math.isclose(L.t_ppf(0.975, 19), 2.093024, abs_tol=1e-5)
    assert math.isclose(L.t_ppf(0.975, 127), 1.978820, abs_tol=1e-5)


SILENT_SETS = {"silent-a10-dev-v1": ("silent-a10-dev-s128", "D1A10SI", 128, 10, "dev"),
               "silent-a10-test-v1": ("silent-a10-test-s100", "H1A10SI", 100, 10, "test"),
               "silent-a0-dev-v1": ("silent-a0-dev-s100", "D1A0SI", 100, 0, "dev")}


@pytest.mark.parametrize("sid", sorted(SILENT_SETS))
def test_silent_dev_and_test_seedsets(sid):
    name, prefix, n, asc, role = SILENT_SETS[sid]
    d = PG.load_seedset(sid)                    # validates the sha256 against the content
    assert d == PG.load_seedset(name) and d["id"] == sid and d["version"] == 1 and d["role"] == role
    assert (d["ascension"], d["content"], d["protocol"]) == (asc, "ported_only", "0.2")
    assert d["games"] == [{"seed": f"{prefix}{i:03d}", "character": "SILENT"} for i in range(1, n + 1)]
    assert PG.seedset_identity(d) == {"seedset_id": sid, "seedset_version": 1, "seedset_sha256": d["sha256"],
                                      "seedset_frozen": True}
    others = {(g["character"], g["seed"]) for p in PG.seedset_dirs()[-1].glob("*.json") if p.stem != name
              for g in json.loads(p.read_text())["games"]}
    assert not others & {(g["character"], g["seed"]) for g in d["games"]}


def test_trim_padding_keeps_the_model_outputs(spec):
    """Rollout forwards cut unoccupied trailing hand / enemy / potion rows: same argmax, values within rounding."""
    from sts2train.features import obs_to_torch

    torch.manual_seed(3)
    model = Policy(spec, **TINY).eval()
    env = VecEnv(64, threads=2, pool=PoolSpec(count=16, seed_prefix="LKTRIM", ascension=10), seed_text="LKTRIM")
    pol = HeuristicPolicy()
    ctx = PolicyContext(schema=env.schema, num_envs=64, live=np.ones(64, bool))
    obs = env.reset()
    seen = 0
    with torch.no_grad():
        for t in range(60):
            obs, *_ = env.step(pol(obs, ctx))
            if t % 6:
                continue
            x = obs_to_torch(obs, "cpu")
            y = L.trim_padding(x, spec)
            assert y["potions"].shape[1] < x["potions"].shape[1]          # Silent never fills 10 potion slots
            l1, v1 = model(x)
            l2, v2 = model(y)
            assert torch.equal(l1.argmax(-1), l2.argmax(-1))
            assert torch.allclose(l1, l2, atol=1e-4) and torch.allclose(v1, v2, atol=1e-4)
            seen += 1
    env.close()
    assert seen >= 9
