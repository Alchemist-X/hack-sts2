"""Loop breaker (`sts2train.loopbreak`): the observation digest, no-op and cycle detection on synthetic repeated
observations, the greedy mask and its leave-the-screen escalation, the leave kinds resolved by name (appended or
inserted RunActionKind members; the rewards-screen macros are real actions, never leave rows; the library's own enum),
the sampling mask, the eval wiring on a real 2-game seed set (a policy that ranks an illegal index first never moves
without the breaker; with it every game plays on and replays), the CLI / config flags, and `train()` with
--rollout-loop-breaker."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train.bench import load_spec, random_obs
from sts2train.features import USED_BUFFERS
from sts2train.loopbreak import DEFAULT_WINDOW, LEAVE_KINDS, NEG, LoopBreaker, ObsDigest

DATA = Path(__file__).parent / "data"
TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}
SHOP = ["BuyEntry"] * 5 + ["LeaveShop"]
PREFS = [6.0, 5.0, 4.0, 3.0, 2.0, 1.0]           # row 0 best ... row 5 (LeaveShop) worst
MACRO_KINDS = ("TakeRewardCard", "TakeRewardAlternative")   # the Vec layer's rewards-screen macros (3b73317739c650bd)


@pytest.fixture(scope="module")
def spec():
    return load_spec(str(DATA / "spec-fc2eb34c07bc8513.json"))[0]   # no library needed for the synthetic tests


def _obs(spec, n=2, seed=0, cand_n=6, kinds=SHOP):
    obs = {k: np.ascontiguousarray(v) for k, v in random_obs(spec, n, np.random.default_rng(seed)).items()}
    obs["cand_n"][:] = cand_n
    names, col = spec.enum_names["RunActionKind"], spec.cols["cand"]["kind"]
    for j, k in enumerate(kinds[:cand_n]):
        obs["cand"][:, j, col] = names.index(k)
    return obs


def _moved(obs, slot, bump=1):
    """A copy of `obs` in which `slot` is at another observation (one model-read word changed)."""
    o = {k: v.copy() for k, v in obs.items()}
    o["global_i"][slot, 0] += bump
    return o


def _logits(n, prefs=PREFS):
    return torch.tensor([prefs] * n, dtype=torch.float32)


def _grown(spec, names=MACRO_KINDS, front=False):
    """`spec` with RunActionKind grown by `names`: appended at the end (as the library's rewards-screen macros were,
    21 / 22), or with `front` inserted before every member (every other id shifts)."""
    from sts2train.features import ModelSpec

    d = spec.to_json()
    old = list(d["enum_names"]["RunActionKind"])
    d["enum_names"]["RunActionKind"] = list(names) + old if front else old + list(names)
    d["enums"]["RunActionKind"] = len(old) + len(names)
    return ModelSpec.from_json(d)


def test_digest_equal_and_sensitive(spec):
    obs = random_obs(spec, 6, np.random.default_rng(0))
    d = ObsDigest()
    h = d(obs)
    assert h.dtype == np.uint64 and len(set(h.tolist())) == 6
    assert np.array_equal(ObsDigest()({k: v.copy() for k, v in obs.items()}), h)     # fixed keys
    assert np.array_equal(d(obs, np.array([4, 1])), h[[4, 1]])
    assert len(d(obs, np.array([], dtype=np.int64))) == 0
    for name in USED_BUFFERS:                   # one changed element of any model-read buffer, in one slot only
        o = {k: v.copy() for k, v in obs.items()}
        o[name].reshape(6, -1)[3, -1] += 1
        h2 = d(o)
        assert h2[3] != h[3] and np.array_equal(np.delete(h2, 3), np.delete(h, 3)), name
    assert np.array_equal(d(dict(obs, aux=np.ones((6, 3), dtype=np.int32))), h)   # buffers the model ignores


def test_no_ops_are_masked_then_the_screen_is_left(spec):
    """Shop: rows 0-2 leave the observation unchanged, row 5 (LeaveShop) has the lowest logit. Slot 0 takes 0, 1, 2
    (three no-ops) and then, k = 3, the leave row rather than the better row 3; slot 1, whose observation changes
    every step, keeps its argmax. With k = 4 the fourth decision is the best remaining row."""
    for k, fourth in ((3, 5), (4, 3)):
        obs = _obs(spec, n=2)
        lb = LoopBreaker(spec, k=k)
        taken = []
        for _ in range(4):
            a = lb.greedy(obs, _logits(2))
            taken.append(a.tolist())
            obs = _moved(obs, 1)                # slot 0 stays where it is (its actions are no-ops)
        assert [t[0] for t in taken] == [0, 1, 2, fourth] and [t[1] for t in taken] == [0, 0, 0, 0]
        assert lb.no_op_actions.tolist() == [3, 0] and lb.loop_breaks.tolist() == [3, 0]
        assert lb.cycle_revisits.tolist() == [0, 0]


def test_cycles_are_broken(spec):
    """A -(row 0)-> B -(row 0)-> A: back at A, the row that went round the cycle is masked and the next best taken."""
    a_obs = _obs(spec, n=1, seed=1)
    b_obs, c_obs = _moved(a_obs, 0, 7), _moved(a_obs, 0, 11)
    lb = LoopBreaker(spec)
    state, seq = a_obs, []
    for _ in range(4):
        a = int(lb.greedy(state, _logits(1))[0])
        seq.append(a)
        state = b_obs if (state is a_obs and a == 0) else a_obs if (state is b_obs and a == 0) else c_obs
    assert seq == [0, 0, 1, 0]
    assert lb.cycle_revisits[0] == 1 and lb.no_op_actions[0] == 0 and lb.loop_breaks[0] == 1


def test_window_forgets_old_observations(spec):
    """An observation that left the window (here 2 decisions) is a new one again."""
    a_obs = _obs(spec, n=1, seed=2)
    lb = LoopBreaker(spec, window=2)
    for s in (a_obs, _moved(a_obs, 0, 1), _moved(a_obs, 0, 2)):
        assert int(lb.greedy(s, _logits(1))[0]) == 0
    assert int(lb.greedy(a_obs, _logits(1))[0]) == 0 and lb.cycle_revisits[0] == 0
    assert int(lb.greedy(a_obs, _logits(1))[0]) == 1 and lb.no_op_actions[0] == 1


def test_every_row_a_no_op_leaves_the_argmax(spec):
    obs = _obs(spec, n=1, cand_n=2)
    lb = LoopBreaker(spec)
    seq = [int(lb.greedy(obs, _logits(1, [2.0, 1.0]))[0]) for _ in range(4)]
    assert seq == [0, 1, 0, 0]                  # from the third decision on both rows already led back: no mask
    assert lb.no_op_actions[0] == 3 and lb.loop_breaks[0] == 1


def test_off_counts_but_keeps_the_argmax(spec):
    obs = _obs(spec, n=1)
    lb = LoopBreaker(spec, enabled=False)
    assert [int(lb.greedy(obs, _logits(1))[0]) for _ in range(4)] == [0, 0, 0, 0]
    assert lb.mode == "off" and lb.no_op_actions[0] == 3 and lb.loop_breaks[0] == 0
    t = lb.totals()
    assert t["mode"] == "off" and t["no_op_actions"] == 3 and t["slots_with_no_ops"] == 1


def test_leave_kinds_resolve_by_name(spec):
    """The leave rows are looked up by name in the spec's RunActionKind members: appended members (the macros) shift
    nothing, members inserted in front shift every leave id with them."""
    names = spec.enum_names["RunActionKind"]
    base = LoopBreaker(spec)
    assert base.leave_kinds == LEAVE_KINDS and base.leave_ids == {names.index(x) for x in LEAVE_KINDS}
    assert LoopBreaker(_grown(spec)).leave_ids == base.leave_ids
    assert LoopBreaker(_grown(spec, front=True)).leave_ids == {i + len(MACRO_KINDS) for i in base.leave_ids}


def test_reward_macros_are_real_actions(spec):
    """Rewards screen with the macro rows: TakeRewardCard / TakeRewardAlternative are real actions (like ClaimReward),
    masked once taken at an observation the slot is back at, and never kept as leave rows: from the k-th return the
    breaker takes ProceedRewards over the better-ranked TakeRewardCard row 3; without a leave row, the best remaining
    row (a macro) is taken."""
    wide = _grown(spec)
    names = wide.enum_names["RunActionKind"]
    macro_ids = {names.index(x) for x in MACRO_KINDS}
    assert macro_ids == {21, 22} and not LoopBreaker(wide).leave_ids & macro_ids
    rewards = ["TakeRewardCard", "TakeRewardCard", "TakeRewardAlternative", "TakeRewardCard", "ClaimReward",
               "ProceedRewards"]
    for cand_n, fourth in ((6, 5), (5, 3)):
        obs = _obs(wide, n=1, cand_n=cand_n, kinds=rewards)
        lb = LoopBreaker(wide)
        seq = [int(lb.greedy(obs, _logits(1))[0]) for _ in range(4)]
        assert seq == [0, 1, 2, fourth] and lb.no_op_actions[0] == 3 and lb.loop_breaks[0] == 3


def test_leave_kinds_against_the_library():
    """The library's RunActionKind (the ids in the observations' cand rows; the evaluator and the trainer build the
    breaker from its spec): every leave kind is a member and resolves to its own id, the macros are members and not
    leave rows."""
    from sts2vec import load, schema_of

    from sts2train.features import ModelSpec

    lib = ModelSpec.from_schema(schema_of(load()))
    names = lib.enum_names["RunActionKind"]
    lb = LoopBreaker(lib)
    assert lb.leave_kinds == LEAVE_KINDS and sorted(lb.leave_ids) == sorted(names.index(x) for x in LEAVE_KINDS)
    assert set(MACRO_KINDS) <= set(names) and not lb.leave_ids & {names.index(x) for x in MACRO_KINDS}


def test_live_slots_cand_n_and_reset(spec):
    """Slots that are not live or have no candidates do not decide (not counted, not remembered); reset forgets."""
    obs = _obs(spec, n=4)
    obs["cand_n"][3] = 0
    live = np.array([True, False, True, True])
    lb = LoopBreaker(spec, 4)
    assert lb.greedy(obs, _logits(4), live)[[0, 2]].tolist() == [0, 0]
    assert lb.greedy(obs, _logits(4), live)[[0, 2]].tolist() == [1, 1]
    assert lb.no_op_actions.tolist() == [1, 0, 1, 0] and lb.loop_breaks.tolist() == [1, 0, 1, 0]
    lb.reset(np.array([True, False, False, False]))
    assert lb.greedy(obs, _logits(4), live)[[0, 2]].tolist() == [0, 2]
    lb.reset([2])
    assert lb.greedy(obs, _logits(4), live)[[0, 2]].tolist() == [1, 0]
    assert lb.pop_counts() == (4, 4, 0) and lb.pop_counts() == (0, 0, 0)
    with pytest.raises(ValueError):
        lb.greedy(_obs(spec, n=3), _logits(3))
    with pytest.raises(ValueError):
        LoopBreaker(spec, k=0)


def test_sampling_mask_and_masked_log_probs(spec):
    """Rollout use: at a repeated observation the row taken before has probability 0 and the stored log-prob is the
    masked distribution's; `InferenceRunner.sample` without `adjust` is the plain distribution."""
    from sts2train.ppo import InferenceRunner

    class Stub(torch.nn.Module):
        def forward(self, x):
            return x["logits"], torch.zeros(x["logits"].shape[0])

    obs = _obs(spec, n=3)
    lb = LoopBreaker(spec, 3)
    runner = InferenceRunner(Stub(), amp=False)
    x = {"logits": _logits(3, [3.0, 2.0, 1.0, 0.0, -1.0, -2.0])}
    torch.manual_seed(0)
    a1, lp1, _ = runner.sample(x, adjust=lambda lg: lb.apply(obs, lg))
    lb.commit(a1.numpy())
    torch.manual_seed(0)
    a0, lp0, _ = runner.sample(x)
    assert torch.equal(a1, a0) and torch.equal(lp1, lp0) and not lb.masked.any()
    for seed in range(1, 6):                   # the slots stay at the same observation
        torch.manual_seed(seed)
        a2, lp2, _ = runner.sample(x, adjust=lambda lg: lb.apply(obs, lg))
        lb.commit(a2.numpy())
        assert lb.masked.all()
    taken = lb.ring_a[:, :6]
    for s in range(3):                          # never a row taken earlier at this observation (until all are)
        assert len(set(taken[s].tolist())) == 6
    lb2 = LoopBreaker(spec, 3)
    lb2.apply(obs, x["logits"])
    lb2.commit(np.array([0, 1, 2]))
    masked = lb2.apply(obs, x["logits"])
    want = x["logits"].clone()
    want[[0, 1, 2], [0, 1, 2]] = NEG
    assert torch.equal(masked, want)
    p = torch.softmax(masked, -1)
    assert torch.all(p[[0, 1, 2], [0, 1, 2]] == 0)
    lb2.commit(np.array([3, -1, 4]))           # -1: the slot did not decide
    assert lb2.loop_breaks.tolist() == [1, 0, 1]


class _IllegalFirst(torch.nn.Module):
    """Ranks an extra row past every slot's candidates first: the env receives an illegal index, which leaves the
    observation unchanged (a no-op), so the greedy argmax alone never moves."""

    def __init__(self, inner):
        super().__init__()
        self.inner = inner

    def forward(self, x, aux=False):
        logits, v = self.inner(x)
        return torch.cat([logits, torch.full_like(logits[:, :1], 1e6)], 1), v


def test_eval_breaks_no_op_loops_and_replays(tmp_path):
    from sts2vec import load, schema_of

    from sts2train.evaluate import evaluate_model
    from sts2train.features import ModelSpec, bind_spec
    from sts2train.model import Policy

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(1)
    model = _IllegalFirst(Policy(spec))
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    reps = {m: evaluate_model(model, spec, str(ss), str(tmp_path / m), weights_hash="test", threads=2,
                              loop_breaker=m == "on", log=lambda _: None) for m in ("off", "on")}
    for m, rep in reps.items():
        h = rep["headline"]
        assert h["games"] == 2 and h["replayable"] == 2, m
        conf = json.loads((tmp_path / m / "config.json").read_text())
        assert conf["loop_breaker"] == {"mode": m, "k": 3, "window": DEFAULT_WINDOW,
                                        "leave_kinds": list(LEAVE_KINDS)}
        games = [json.loads((tmp_path / m / "games" / f"{g['character']}-{g['seed']}.summary.json").read_text())
                 for g in TWO_GAMES["games"]]
        lb = rep["loop_breaker"]
        assert json.loads((tmp_path / m / "report.json").read_text())["loop_breaker"] == lb
        for key in ("loop_breaks", "no_op_actions", "cycle_revisits"):
            assert lb[key] == sum(g[key] for g in games), key
        assert all(g["loop_breaker"] == m and g["no_op_actions"] > 0 and g["illegal"] > 0 for g in games)
    off, on = reps["off"]["headline"], reps["on"]["headline"]
    assert off["results"]["stuck"] == 2 and reps["off"]["loop_breaker"]["loop_breaks"] == 0
    assert on["results"]["stuck"] == 0 and reps["on"]["loop_breaker"]["loop_breaks"] > 0
    assert on["floor_mean"] > off["floor_mean"] and on["progress_mean"] > off["progress_mean"]


def test_cli_and_config_flags(tmp_path, monkeypatch, capsys):
    import sts2train.evaluate as E
    import sts2train.ppo as P
    from sts2train.cli import main

    seen = {}

    def fake_evaluate(*a, **k):
        seen.update(k)
        return {"headline": {"progress_mean": 0.2, "progress_ci95": [0.1, 0.3], "wins": 0, "games": 2,
                             "results": {"loss": 2}, "floor_mean": 7.0, "act_reached": {"1": 2}, "replayable": 2},
                "loop_breaker": {"mode": "off", "loop_breaks": 0, "no_op_actions": 7, "cycle_revisits": 2}}

    monkeypatch.setattr(E, "evaluate", fake_evaluate)
    assert main(["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o"),
                 "--loop-breaker", "off", "--loop-breaker-k", "5"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert seen["loop_breaker"] is False and seen["loop_breaker_k"] == 5
    assert (out["loop_breaker"], out["loop_breaks"], out["no_op_actions"], out["cycle_revisits"]) == ("off", 0, 7, 2)
    assert main(["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o")]) == 0
    assert seen["loop_breaker"] is True and seen["loop_breaker_k"] == 3

    got = {}

    def fake_train(cfg, **kw):
        got["cfg"] = cfg
        return {}

    monkeypatch.setattr(P, "train", fake_train)
    assert main(["train", "--out", str(tmp_path / "run"), "--loop-breaker", "off", "--rollout-loop-breaker",
                 "--loop-breaker-k", "4"]) == 0
    cfg = got["cfg"]
    assert cfg.loop_breaker is False and cfg.rollout_loop_breaker is True and cfg.loop_breaker_k == 4
    d = P.TrainConfig()
    assert d.loop_breaker is True and d.rollout_loop_breaker is False and d.loop_breaker_k == 3
    assert P.resolve_config({"loop_breaker": "on"}).loop_breaker is True
    for bad in ({"loop_breaker_k": 0}, {"loop_breaker": "maybe"}):
        with pytest.raises(ValueError):
            P.resolve_config(bad)


def test_train_rollout_loop_breaker(tmp_path):
    """--rollout-loop-breaker: a short real run logs loop_breaks / no_op_actions / cycle_revisits per update and the
    totals in summary.json; without it the keys are absent (the default path is pinned by test_perf.py)."""
    from sts2train.ppo import resolve_config, train
    from sts2train.runlog import read_jsonl

    keys = ("loop_breaks", "no_op_actions", "cycle_revisits")
    for on in (True, False):
        run = tmp_path / f"run-{on}"
        s = train(resolve_config({"out": str(run), "envs": 8, "threads": 2, "pool": 4, "rollout": 8, "minibatch": 32,
                                  "max_updates": 2, "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0,
                                  "max_hours": 0.5, "torch_threads": 2, "rollout_loop_breaker": on}),
                  log=lambda m: None)
        rows = read_jsonl(run / "metrics.jsonl")
        assert len(rows) == 2
        if on:
            assert all(isinstance(r[k], int) and r[k] >= 0 for r in rows for k in keys)
            assert s["rollout_loop_breaker"]["window"] == DEFAULT_WINDOW
            assert s["rollout_loop_breaker"]["loop_breaks"] == sum(r["loop_breaks"] for r in rows)
        else:
            assert not any(k in r for r in rows for k in keys) and "rollout_loop_breaker" not in s
        assert json.loads((run / "config.json").read_text())["config"]["rollout_loop_breaker"] is on


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
