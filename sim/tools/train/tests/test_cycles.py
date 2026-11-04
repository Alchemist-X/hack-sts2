"""UI revisit cycles (`sts2train.cycles`) and the evaluation modes / fixed-endpoint evals built on them.

Scripted observations (the library's schema, buffers built by hand): the rest-site SMITH -> select -> cancel loop and
the shop removal -> select -> cancel loop are counted once each, at the revisit, with their path; going round again is
another cycle; the trainer hook (`after_step`) returns the closing mask and the finished episodes' counts. Not counted:
a confirmed upgrade, leaving the room, a fight in between, an event prompt cancelled back to the event, a prompt that
shows a card the deck does not hold (new information), a no-op, a floor change. `PartDigest` sums to `ObsDigest`.
Then: the sample policy's per-play RNG (batch independent, deterministic), the eval modes' summaries / report, the
compare tool (slices merged, pairs), --cycle-cost in `train()` (the trained reward drops by exactly the cost per
counted cycle, nothing else moves), --eval-at / --no-best, the CLI flags."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train.cycles import COUNT_KEYS, PROMPT_UI_COLS, CycleDetector, PartDigest, sample_rng, update_stats
from sts2train.features import USED_BUFFERS, ModelSpec
from sts2train.loopbreak import ObsDigest

TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}
DECK = [(11, 0), (11, 0), (12, 0), (13, 1)]           # (card id, upgrade)


@pytest.fixture(scope="module")
def lib():
    from sts2vec import load, schema_of

    schema = schema_of(load())
    return schema, ModelSpec.from_schema(schema)


class Scene:
    """Hand-built single-slot observations of the library's schema: a room (RestSite / Shop / Event / Map / Combat)
    and the DeckUpgrade / DeckRemove prompt entered from it."""

    def __init__(self, schema):
        self.s = schema
        self.G, self.C, self.PO, self.D = (schema.cols(b) for b in ("global_i", "cand", "prompt_options", "deck"))
        self.R, self.S = schema.cols("rest"), schema.cols("shop")
        E = schema.vocab.enums
        self.ph, self.pk, self.k = E["RunPhase"], E["ChoicePromptKind"], E["RunActionKind"]
        self.rest_opt, self.shop_cat, self.item = E["RestOption"], E["ShopCategory"], E["PromptItemKind"]

    def blank(self) -> dict:
        return {k: np.array(v, copy=True) for k, v in self.s.allocate(1).arrays.items()}

    def _cands(self, o, rows) -> None:
        o["cand"][:] = 0
        for j, (kind, oi, cid) in enumerate(rows):
            o["cand"][0, j, self.C.kind] = self.k.index_of(kind)
            o["cand"][0, j, self.C.option_index] = oi
            o["cand"][0, j, self.C.card_id] = cid
        o["cand_n"][0] = len(rows)

    def room(self, phase="RestSite", *, floor=16, hp=47, gold=100, deck=DECK, combat=False) -> dict:
        o = self.blank()
        gi = o["global_i"][0]
        gi[self.G.phase] = gi[self.G.decision_kind] = self.ph.index_of(phase)
        gi[self.G.total_floor], gi[self.G.hp], gi[self.G.max_hp], gi[self.G.gold] = floor, hp, 70, gold
        gi[self.G.deck_n], gi[self.G.is_combat] = len(deck), int(combat)
        for i, (cid, up) in enumerate(deck):
            o["deck"][0, i, self.D.card_id], o["deck"][0, i, self.D.upgrade] = cid, up
        if phase == "RestSite":
            for i, name in enumerate(("HEAL", "SMITH")):
                o["rest"][0, i, self.R.option_id] = self.rest_opt.index_of(name)
                o["rest"][0, i, self.R.enabled] = 1
            self._cands(o, [("ChooseRestOption", 0, 0), ("ChooseRestOption", 1, 0)])
        elif phase == "Shop":
            o["shop"][0, 0, self.S.category] = self.shop_cat.index_of("card_removal")
            o["shop"][0, 0, self.S.price] = 75
            o["shop"][0, 1, self.S.category] = self.shop_cat.index_of("card")
            o["shop"][0, 1, self.S.item_id] = 21
            o["shop"][0, 1, self.S.price] = 50
            self._cands(o, [("BuyEntry", 0, 0), ("BuyEntry", 1, 0), ("LeaveShop", -1, 0)])
        elif phase == "Event":
            self._cands(o, [("ChooseEventOption", 0, 0), ("ChooseEventOption", 1, 0)])
        elif phase == "Combat":
            self._cands(o, [("EndTurn", -1, 0)])
        else:
            self._cands(o, [("Travel", 0, 0)])
        return o

    def prompt(self, room: dict, kind="DeckUpgrade", *, picked: int | None = None, options=None) -> dict:
        o = {k: v.copy() for k, v in room.items()}
        gi = o["global_i"][0]
        gi[self.G.phase] = gi[self.G.decision_kind] = self.ph.index_of("Prompt")
        gi[self.G.prompt_kind] = self.pk.index_of(kind)
        gi[self.G.prompt_min] = gi[self.G.prompt_max] = gi[self.G.prompt_needs_confirm] = 1
        gi[self.G.prompt_can_cancel] = 1
        gi[self.G.prompt_picked] = int(picked is not None)
        opts = list(options if options is not None else DECK)
        gi[self.G.prompt_option_n] = len(opts)
        for i, (cid, up) in enumerate(opts):
            row = o["prompt_options"][0, i]
            row[self.PO.item_id], row[self.PO.upgrade], row[self.PO.ordinal] = cid, up, i
            row[self.PO.kind_of_item] = self.item.index_of("card")
            row[self.PO.picked] = int(picked == i)
        if picked is None:
            self._cands(o, [("PromptSelect", i, cid) for i, (cid, _) in enumerate(opts)] + [("PromptCancel", -1, 0)])
        else:
            self._cands(o, [("PromptConfirm", -1, 0), ("PromptCancel", -1, 0)])
        return o


def _run(det, seq):
    """Drive the detector through [(obs, action), ...] (observe, then commit); -> the closing mask per step."""
    out = []
    for obs, a in seq:
        out.append(bool(det.observe(obs)[0]))
        det.commit(np.array([a]), obs)
    return out


def test_part_digest_sums_to_obs_digest(lib):
    from sts2train.bench import random_obs

    schema, spec = lib
    obs = random_obs(spec, 5, np.random.default_rng(3))
    idx = np.array([0, 2, 4])
    parts = PartDigest().parts(obs, idx)
    assert parts.shape == (3, len(USED_BUFFERS))
    assert np.array_equal(parts.sum(1, dtype=np.uint64), ObsDigest()(obs, idx))
    assert PartDigest().parts(obs, np.zeros(0, dtype=np.int64)).shape == (0, len(USED_BUFFERS))


def test_rest_site_smith_cancel_cycle_counted_once(lib):
    schema, spec = lib
    sc = Scene(schema)
    R = sc.room("RestSite")
    P0, P1 = sc.prompt(R), sc.prompt(R, picked=2)
    det = CycleDetector(spec, 1, card_name=lambda i: f"C{i}", keep_events=True)
    closed = _run(det, [(R, 1), (P0, 2), (P1, 1), (R, 1), (P0, 4), (R, 0)])     # SMITH sel cancel | SMITH cancel | HEAL
    assert closed == [False, False, False, True, False, True]
    st = det.slot_totals()[0]
    assert st["cycles"] == 2 and st["cycles_rest"] == 2 and st["cycles_shop"] == 0
    e1, e2 = st["cycle_events"]
    assert e1["path"] == ["rest:SMITH", "select:C12", "cancel"] and e1["len"] == 3 and e1["step"] == 3
    assert e2["path"] == ["rest:SMITH", "cancel"] and e2["len"] == 2 and e2["step"] == 5
    assert e1["phase"] == "RestSite" and e1["prompt"] == ["DeckUpgrade"] and e1["floor"] == 16
    counts, events = det.pop()
    assert counts["cycles"] == 2 and counts["cycle_steps"] == 5 and len(events) == 2
    assert det.pop()[0]["cycles"] == 0 and not det.pop()[1]
    assert all(counts[f"rejected_{r}"] == 0 for r in ("no_op", "unverified_prompt", "room_change"))


def test_shop_removal_cancel_cycle(lib):
    schema, spec = lib
    sc = Scene(schema)
    S = sc.room("Shop")
    P0, P1 = sc.prompt(S, "DeckRemove"), sc.prompt(S, "DeckRemove", picked=0)
    det = CycleDetector(spec, 1, keep_events=True)
    assert _run(det, [(S, 0), (P0, 0), (P1, 1), (S, 2)]) == [False, False, False, True]
    ev = det.slot_totals()[0]["cycle_events"][0]
    assert ev["phase"] == "Shop" and ev["path"] == ["buy:card_removal", "select:card11", "cancel"]
    assert det.totals["cycles_shop"] == 1


def test_negative_cases_are_not_counted(lib):
    schema, spec = lib
    sc = Scene(schema)
    R = sc.room("RestSite")
    upgraded = [(11, 0), (11, 0), (12, 1), (13, 1)]
    R_up = sc.room("RestSite", deck=upgraded)                 # after a confirmed upgrade: the deck changed
    R_hp = sc.room("RestSite", hp=68)                         # after HEAL
    cases = {
        "confirmed upgrade": [(R, 1), (sc.prompt(R), 2), (sc.prompt(R, picked=2), 0), (R_up, 0)],
        "left the room": [(R, 0), (R_hp, 0), (sc.room("Map", floor=16), 0), (R, 0)],
        "fight in between": [(R, 1), (sc.room("Combat", combat=True), 0), (R, 0)],
        "event prompt cancelled": [(sc.room("Event"), 0), (sc.prompt(sc.room("Event")), 4),
                                   (sc.room("Event"), 0)],
        "floor change": [(R, 1), (sc.prompt(sc.room("RestSite", floor=17)), 4), (sc.room("RestSite", floor=17), 0)],
    }
    for name, seq in cases.items():
        det = CycleDetector(spec, 1)
        assert not any(_run(det, seq)), name
        assert det.totals["cycles"] == 0, name
    # a prompt that shows a card the deck does not hold carries new information: the revisit is rejected
    det = CycleDetector(spec, 1)
    leak = sc.prompt(R, options=DECK + [(99, 0)])
    assert not any(_run(det, [(R, 1), (leak, 5), (R, 0)]))
    assert det.totals["rejected_unverified_prompt"] == 1
    # a prompt that differs from the room outside the prompt UI (here: gold) is not the same room
    det = CycleDetector(spec, 1)
    other = sc.prompt(sc.room("RestSite", gold=99))
    assert not any(_run(det, [(R, 1), (other, 4), (R, 0)])) and det.totals["rejected_unverified_prompt"] == 1
    # a no-op at the room (the same observation right after an action there)
    det = CycleDetector(spec, 1)
    assert not any(_run(det, [(R, 1), (R, 1)])) and det.totals["rejected_no_op"] == 1


def test_window_and_ui_columns(lib):
    schema, spec = lib
    sc = Scene(schema)
    R = sc.room("RestSite")
    det = CycleDetector(spec, 1, window=3)
    P0, P1 = sc.prompt(R), sc.prompt(R, picked=1)
    # R, P0, P1, P0', R: 4 entries back > window 3 -> the room entry fell out, no cycle
    assert not any(_run(det, [(R, 1), (P0, 1), (P1, 1), (sc.prompt(R, picked=0), 1), (R, 0)]))
    assert set(PROMPT_UI_COLS) <= set(schema["global_i"].fields)
    with pytest.raises(ValueError):
        CycleDetector(spec, 1, window=1)


def test_trainer_hook_and_episode_counts(lib):
    """`after_step` (auto-reset envs): the closing mask of the transition just taken, float32; ending episodes
    put their count into the finished dicts and start over."""
    schema, spec = lib
    sc = Scene(schema)
    R, P0 = sc.room("RestSite"), sc.prompt(sc.room("RestSite"))
    two = {k: np.concatenate([a, b]) for (k, a), b in zip(R.items(), P0.values())}  # slot 0 at R, slot 1 at P0
    det = CycleDetector(spec, 2)
    det.observe(two)
    det.commit(np.array([1, 4]), two)
    nxt = {k: np.concatenate([a, b]) for (k, a), b in zip(P0.items(), R.values())}  # slot 1 cancelled back to R
    fin = []
    m = det.after_step(nxt, np.array([False, False]), fin)
    assert m.dtype == np.float32 and m.tolist() == [0.0, 0.0]   # slot 1's chain had no room observation before P0
    det.commit(np.array([4, 1]), nxt)
    m = det.after_step(two, np.array([False, False]), [])       # slot 0: P0 -cancel-> R closes; slot 1: R -> P0
    assert m.tolist() == [1.0, 0.0] and det.ep_cycles.tolist() == [1, 0]
    fin = [{"slot": 0}, {"slot": 1}]
    det.after_step(two, np.array([True, True]), fin)
    assert [e["cycles"] for e in fin] == [1, 0] and det.ep_cycles.tolist() == [0, 0]
    assert det.slot_cycles.tolist() == [1, 0]
    st = update_stats(det.pop()[0], fin, cost=0.01)
    assert st["cycles_total"] == 1 and st["episodes_with_cycle"] == 1 and st["cycle_max_per_episode"] == 1
    assert st["cycle_cost_total"] == pytest.approx(0.01) and set(COUNT_KEYS) >= {"cycles", "cycle_steps"}


def test_sample_rng_is_per_play():
    a = sample_rng(1, "SILENT-X", 0).random(4)
    assert np.array_equal(a, sample_rng(1, "SILENT-X", 0).random(4))
    assert not np.array_equal(a, sample_rng(1, "SILENT-X", 1).random(4))
    assert not np.array_equal(a, sample_rng(2, "SILENT-X", 0).random(4))


@pytest.fixture(scope="module")
def tiny_model(lib):
    from sts2train.features import bind_spec
    from sts2train.model import Policy

    _, spec = lib
    bind_spec(spec)
    torch.manual_seed(1)
    return Policy(spec).eval()


def test_eval_modes_write_cycles_and_sample_is_batch_independent(tmp_path, lib, tiny_model):
    from sts2train.evaluate import evaluate_model, mode_name, parse_mode

    _, spec = lib
    assert parse_mode("sample-off") == ("sample", False) and mode_name("greedy", True) == "greedy-on"
    with pytest.raises(ValueError):
        parse_mode("beam-on")
    one = dict(TWO_GAMES, games=TWO_GAMES["games"][1:], name="t1")
    runs = {}
    for name, ss, kw in (("s2", TWO_GAMES, {}), ("s1", one, {}), ("s2r1", TWO_GAMES, {"repeat": 1})):
        evaluate_model(tiny_model, spec, ss, str(tmp_path / name), weights_hash="t", threads=2, loop_breaker=False,
                       policy="sample", sample_seed=7, log=lambda _: None, **kw)
        runs[name] = {p.stem.split(".")[0]: json.loads(p.read_text()) for p in (tmp_path / name / "games").glob("*.json")}
    key = "SILENT-E1A0SI01"
    assert runs["s2"][key]["history_actions"] == runs["s1"][key]["history_actions"]     # batch independent
    assert runs["s2"][key]["history_actions"] != runs["s2r1"][key]["history_actions"]   # another repeat
    s = runs["s2"][key]
    assert s["eval_mode"] == {"policy": "sample", "breaker": "off", "name": "sample-off", "sample_seed": 7,
                              "repeat": 0}
    assert {"cycles", "cycles_rest", "cycles_shop", "cycle_events", "loop_breaks"} <= set(s)
    rep = json.loads((tmp_path / "s2" / "report.json").read_text())
    assert rep["cycles"]["games"] == 2 and rep["eval_mode"]["name"] == "sample-off"
    assert json.loads((tmp_path / "s2" / "config.json").read_text())["greedy"] is False
    assert all(x["replayable"] for x in runs["s2"].values())


def test_compare_merges_slices_and_pairs(tmp_path, lib, tiny_model):
    from sts2train.compare import compare, render
    from sts2train.evaluate import evaluate_model, slice_seedset

    _, spec = lib
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    for mode, br in (("off", False), ("on", True)):
        for c in ("0/2", "1/2"):
            sub, parent = slice_seedset(str(ss), c)
            assert len(sub["games"]) == 1 and parent["games"] == 2 and parent["chunk"] == c
            evaluate_model(tiny_model, spec, sub, str(tmp_path / f"{mode}-{c[0]}"), weights_hash="t", threads=2,
                           loop_breaker=br, parent_seedset=parent, log=lambda _: None)
    whole = tmp_path / "whole"
    evaluate_model(tiny_model, spec, str(ss), str(whole), weights_hash="t", threads=2, loop_breaker=False,
                   log=lambda _: None)
    r = compare([f"{tmp_path / 'off-0'},{tmp_path / 'off-1'}", f"{tmp_path / 'on-0'},{tmp_path / 'on-1'}",
                 str(whole)], ["off", "on", "whole"])
    assert [a["games"] for a in r["arms"]] == [2, 2, 2] and len(r["pairs"]) == 3
    p = r["pairs"][1]                                         # whole - off: the same greedy plays
    assert (p["a"], p["b"]) == ("off", "whole") and p["delta_mean"] == 0 and p["identical_actions"] == 2
    assert set(r["arms"][0]["funnel"]) >= {"act1_boss", "cleared"} and "paired per seed" in render(r)
    with pytest.raises(ValueError):
        compare([str(tmp_path / "off-0"), str(whole)])        # a slice is not the whole seed set


def test_report_card_mode_rows(tmp_path, monkeypatch):
    """--modes: each extra execution mode is an NN row in OUT/evals/<set>@<mode>, paired per seed against the greedy
    row, with its cycles; a second card reuses every run directory."""
    import sts2train.evaluate as E
    from sts2vec import load, schema_of

    import sts2train.reportcard as RC
    from sts2train.features import bind_spec
    from sts2train.model import Policy
    from sts2train.ppo import TrainConfig, save_checkpoint

    spec = ModelSpec.from_schema(schema_of(load()))
    bind_spec(spec)
    torch.manual_seed(1)
    ck = tmp_path / "ck.pt"
    save_checkpoint(ck, Policy(spec), spec, TrainConfig(), {"updates": 1, "env_steps": 64})
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    kw = dict(seedsets=[str(ss)], threads=2, baselines=(), modes=["greedy-on", "greedy-off", "sample-off"],
              sample_seed=3, log=lambda m: None)
    card = RC.report_card(ck, tmp_path / "card", **kw)
    runs = card["seedsets"][0]["runs"]
    assert [(r["kind"], r["label"]) for r in runs] == [
        ("nn", RC.GREEDY_LABEL), ("nn-mode", "NN greedy, loop breaker off"),
        ("nn-mode", "NN sampled (seed 3, 1 play), loop breaker off")]
    for r in runs[1:]:
        assert r["paired"]["vs"] == RC.GREEDY_LABEL and r["paired"]["games"] == 2 and r["cycles"]["games"] == 2
        assert r["eval_mode"]["name"] in ("greedy-off", "sample-off")
    assert (tmp_path / "card" / "evals" / "t2@sample-off" / "report.json").exists()
    assert card["settings"]["modes"] == ["greedy-on", "greedy-off", "sample-off"]
    assert "### Execution modes, paired per seed" in (tmp_path / "card" / "model-card.md").read_text()

    def no_play(*a, **k):
        raise AssertionError("a reusable evaluation was played again")

    monkeypatch.setattr(E, "evaluate_model", no_play)
    again = RC.report_card(ck, tmp_path / "card", **kw)
    assert all(r["reused"] for r in again["seedsets"][0]["runs"])
    with pytest.raises(ValueError):
        RC.report_card(ck, tmp_path / "card3", **dict(kw, modes=["beam-on"]))


def _given(run, **kw):
    return {"out": str(run), "envs": 8, "threads": 2, "pool": 4, "rollout": 8, "minibatch": 32, "max_updates": 2,
            "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0, "max_hours": 0.5, "torch_threads": 2, **kw}


def test_train_cycle_cost_charges_exactly_once_per_cycle(tmp_path, monkeypatch):
    """The cost path in `train()`: a detector that also reports a forced closure every third step on slot 0 (so the
    test does not depend on a random policy finding a rest site). With lr 0 both runs sample the same rollouts: the
    trained reward sum drops by exactly c x cycles_total per update (float32 rewards), the counts are equal, the
    objective's logged reward and the shaping are untouched."""
    import sts2train.ppo as P
    from sts2train.runlog import read_jsonl

    class Forced(CycleDetector):
        def after_step(self, obs, done, finished=None, **kw):
            m = super().after_step(obs, done, finished, **kw)
            self.calls = getattr(self, "calls", 0) + 1
            if self.calls % 3 == 0 and m[0] == 0:
                m[0] = 1.0
                self.totals["cycles"] += 1
                self.totals["cycles_rest"] += 1
                self.totals["cycle_steps"] += 3
            return m

    monkeypatch.setattr(P, "CycleDetector", Forced)
    rows = {}
    for c in (0.0, 0.25):
        run = tmp_path / f"c{c}"
        P.train(P.resolve_config(_given(run, lr=0.0, acq_shaping=True, cycle_cost=c)), log=lambda m: None)
        rows[c] = read_jsonl(run / "metrics.jsonl")
        assert (run / "cycles.jsonl").exists() and len(read_jsonl(run / "cycle_episodes.jsonl")) == 2
    for a, b in zip(rows[0.0], rows[0.25]):
        assert a["cycles_total"] == b["cycles_total"] > 0
        assert b["cycle_cost_total"] == pytest.approx(0.25 * b["cycles_total"]) and a["cycle_cost_total"] == 0
        assert a["reward_sum_trained"] - b["reward_sum_trained"] == pytest.approx(0.25 * b["cycles_total"], abs=1e-5)
        for k in ("reward_mean", "shaping_mean", "episodes", "progress_mean", "reward_mean_shaped"):
            assert a[k] == pytest.approx(b[k], abs=1e-9), k


def test_train_eval_at_and_no_best(tmp_path):
    from sts2train.ppo import resolve_config, train
    from sts2train.runlog import read_jsonl

    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    run = tmp_path / "run"
    s = train(resolve_config(_given(run, eval_at="u1,2", eval_at_seedset=str(ss), eval_every=1, eval_seedset=str(ss),
                                    no_best=True, eval_at_modes="greedy-on,greedy-off,sample-off")),
              log=lambda m: None)
    rows = read_jsonl(run / "evals.jsonl")
    at = [r for r in rows if r.get("eval_at")]
    assert sorted((r["update"], r["mode"]) for r in at) == [(u, m) for u in (1, 2)
                                                            for m in sorted(("greedy-on", "greedy-off", "sample-off"))]
    for r in at:
        assert (run / r["dir"] / "report.json").exists() and r["games"] == 2 and r["cycles"] is not None
        assert (run / r["checkpoint"]).name == f"pinned-{r['update']:07d}.pt" and (run / r["checkpoint"]).exists()
    assert len([r for r in rows if not r.get("eval_at")]) == 2          # the periodic monitor still runs
    assert not (run / "checkpoints" / "best.pt").exists() and s["best_eval"] is None
    cfg = json.loads((run / "config.json").read_text())["config"]
    assert cfg["eval_at"] == [1, 2] and cfg["no_best"] is True
    m = read_jsonl(run / "metrics.jsonl")
    assert all(isinstance(r["cycles_total"], int) and "reward_sum_trained" in r for r in m)


def test_config_and_cli_flags(tmp_path, monkeypatch, capsys):
    import sts2train.evaluate as E
    import sts2train.ppo as P
    from sts2train.cli import main

    assert P.resolve_config({"eval_at": "u500, 250,500"}).eval_at == (250, 500)
    assert P.resolve_config({}).cycle_cost == 0.0 and P.resolve_config({}).no_best is False
    for bad in ({"cycle_cost": -0.1}, {"eval_at": "u0"}, {"eval_at": "x"}, {"eval_at_modes": "greedy-maybe"}):
        with pytest.raises(ValueError):
            P.resolve_config(bad)
    got = {}
    monkeypatch.setattr(P, "train", lambda cfg, **kw: got.update(cfg=cfg) or {})
    assert main(["train", "--out", str(tmp_path / "r"), "--cycle-cost", "0.01", "--eval-at", "u10,20", "--no-best",
                 "--eval-at-modes", "greedy-off"]) == 0
    cfg = got["cfg"]
    assert cfg.cycle_cost == 0.01 and cfg.eval_at == (10, 20) and cfg.no_best and cfg.eval_at_modes == ("greedy-off",)
    capsys.readouterr()
    seen = {}

    def fake(*a, **k):
        seen.update(k)
        return {"headline": {"progress_mean": 0.2, "progress_ci95": [0.1, 0.3], "wins": 0, "games": 2,
                             "results": {"loss": 2}, "floor_mean": 7.0, "act_reached": {"1": 2}, "replayable": 2},
                "cycles": {"cycles": 3, "cycles_rest": 3, "cycles_shop": 0, "games_with_cycle": 1, "stuck": 0},
                "eval_mode": {"name": "sample-off"}}

    monkeypatch.setattr(E, "evaluate", fake)
    assert main(["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o"), "--policy", "sample",
                 "--breaker", "off", "--sample-seed", "3", "--repeats", "1", "--chunk", "1/2"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert (seen["policy"], seen["loop_breaker"], seen["sample_seed"], seen["chunk"]) == ("sample", False, 3, "1/2")
    assert out["eval_mode"] == "sample-off" and out["cycles"]["cycles"] == 3
    with pytest.raises(SystemExit):
        main(["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--repeats", "2"])      # greedy repeats


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
