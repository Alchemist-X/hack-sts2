"""DiscardPotion mask (`--mask-drop-potion`, wave 20; `model.Policy.drop_potion_masked`): on synthetic candidate
tables the mask removes exactly the legal DiscardPotion rows unless the belt is full and a potion is on offer (an
unclaimed Potion reward, a stocked affordable shop potion), never empties a decision (fallback: unmasked, counted);
off = the forward unchanged (logits equal, no parameter / arch / weights-hash change); the rollout's log-probs equal
the update's under the mask (ppo_loss ratio 1); `drop_potion_stats` counts what the rollout drew; the loop breaker
with ``skip_neg`` never leaves only masked rows; config / CLI / resume / checkpoint inheritance and the eval override;
with the live library a short train run logs the metrics and a real eval records the mask in the policy hash."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train import ppo
from sts2train.bench import load_spec, random_obs
from sts2train.features import RolloutStore, obs_to_device
from sts2train.model import NEG, Policy, param_count

DATA = Path(__file__).parent / "data"
ARCH = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}


@pytest.fixture(scope="module")
def spec_dt():
    return load_spec(str(DATA / "spec-fc2eb34c07bc8513.json"))


@pytest.fixture(scope="module")
def spec(spec_dt):
    return spec_dt[0]


def _model(spec, on=False, seed=0):
    torch.manual_seed(seed)
    m = Policy(spec, **ARCH)
    m.set_mask_drop_potion(on)
    return m.eval()


def _obs(spec, n, seed=0):
    return {k: np.ascontiguousarray(v).copy() for k, v in random_obs(spec, n, np.random.default_rng(seed)).items()}


def _blank(spec, obs, s):
    """Sample s: no potions, no rewards, no shop rows."""
    obs["potions"][s] = 0
    obs["rewards"][s] = 0
    obs["shop"][s] = 0


def _rows(spec, obs, s, kinds):
    names, C = spec.enum_names["RunActionKind"], spec.cols["cand"]
    obs["cand"][s] = 0
    obs["cand_n"][s] = len(kinds)
    for j, k in enumerate(kinds):
        obs["cand"][s, j, C["kind"]] = names.index(k)
        obs["cand"][s, j, C["A"]] = j
        for col in ("B", "hand_slot", "target_slot", "option_index"):
            obs["cand"][s, j, C[col]] = -1
        obs["cand"][s, j, C["potion_slot"]] = j if k in ("DiscardPotion", "UsePotion") else -1


def _belt(spec, obs, s, held, slots):
    obs["global_i"][s, spec.cols["global_i"]["potion_slots"]] = slots
    obs["potions"][s] = 0
    obs["potions"][s, :held, spec.cols["potions"]["potion_id"]] = 2


def _reward(spec, obs, s, rtype):
    c = spec.cols["rewards"]
    obs["rewards"][s, 0, c["reward_type"]] = spec.enum_names["RewardType"].index(rtype)
    obs["rewards"][s, 0, c["item_id"]] = 3


def _shop(spec, obs, s, cat, stocked=1, affordable=1):
    c = spec.cols["shop"]
    obs["shop"][s, 0, c["category"]] = ["", "?", "card", "colorless_card", "relic", "potion", "card_removal"].index(cat)
    obs["shop"][s, 0, c["stocked"]] = stocked
    obs["shop"][s, 0, c["affordable"]] = affordable


def _masked(model, obs):
    x = obs_to_device(obs, "cpu", max_rows=64)
    valid = torch.arange(x["cand"].shape[1]).view(1, -1) < x["cand_n"].view(-1, 1)
    m, fb = model.drop_potion_masked(x, valid)
    return m.numpy(), fb.numpy()


KINDS = ["UsePotion", "DiscardPotion", "DiscardPotion", "EndTurn"]


# ------------------------------------------------------------------------------------------------ the rule


# (potions held, slots, offer) -> the drop rows stay?
RULE_CASES = [
    (1, 3, None, False),                    # not full, nothing offered
    (3, 3, None, False),                    # full, nothing offered
    (3, 3, ("reward", "Potion"), True),     # full + an unclaimed potion reward
    (2, 3, ("reward", "Potion"), False),    # offered, belt not full
    (3, 3, ("reward", "Gold"), False),      # full, a gold reward only
    (4, 4, ("shop", "potion", 1, 1), True),     # full + a stocked, affordable shop potion
    (3, 3, ("shop", "potion", 1, 0), False),    # not affordable
    (3, 3, ("shop", "potion", 0, 0), False),    # sold out
    (3, 3, ("shop", "relic", 1, 1), False),     # full, a shop relic only
    (0, 0, None, False),                    # no belt
]


def test_mask_rule_on_synthetic_rows(spec):
    """Exactly the DiscardPotion rows go, unless full belt + offer (reward or affordable stocked shop potion)."""
    n = len(RULE_CASES)
    obs = _obs(spec, n, 1)
    for s, (held, slots, offer, _) in enumerate(RULE_CASES):
        _blank(spec, obs, s)
        _rows(spec, obs, s, KINDS)
        _belt(spec, obs, s, held, slots)
        if offer and offer[0] == "reward":
            _reward(spec, obs, s, offer[1])
        elif offer:
            _shop(spec, obs, s, offer[1], stocked=offer[2], affordable=offer[3])
    m, fb = _masked(_model(spec, True), obs)
    drop = np.array([k == "DiscardPotion" for k in KINDS])
    for s, (*_, keep) in enumerate(RULE_CASES):
        want = np.zeros(m.shape[1], dtype=bool)
        if not keep:
            want[: len(KINDS)] = drop
        assert (m[s] == want).all(), RULE_CASES[s]
    assert not fb.any()


def test_mask_only_inside_cand_n_and_fallback(spec):
    """Rows past cand_n are never 'masked'; a decision whose legal rows are all drops keeps them (fallback)."""
    obs = _obs(spec, 3, 2)
    for s in range(3):
        _blank(spec, obs, s)
        _belt(spec, obs, s, 1, 3)
    _rows(spec, obs, 0, ["DiscardPotion", "DiscardPotion"])          # only drops: fallback
    _rows(spec, obs, 1, ["EndTurn", "DiscardPotion", "DiscardPotion"])
    obs["cand_n"][1] = 1                                              # the drops lie past cand_n: not legal
    _rows(spec, obs, 2, ["DiscardPotion", "EndTurn"])
    model = _model(spec, True)
    m, fb = _masked(model, obs)
    assert fb.tolist() == [True, False, False]
    assert not m[0].any() and not m[1].any() and m[2].tolist()[:2] == [True, False]
    lg, _ = model(obs_to_device(obs, "cpu", max_rows=64))
    assert (lg[0, :2] > NEG / 10).all()                               # the fallback decision is unmasked
    assert lg[2, 0] == NEG and lg[2, 1] > NEG / 10


def test_random_batch_never_empty(spec):
    """On random tables (many drop rows, random belts): every deciding sample keeps >= 1 live row."""
    obs = _obs(spec, 256, 3)
    model = _model(spec, True)
    lg, _ = model(obs_to_device(obs, "cpu", max_rows=64))
    m, fb = _masked(model, obs)
    assert m.any()                                                     # the mask does something here
    assert ((lg > NEG / 10).sum(1) >= 1).all()


# ------------------------------------------------------------------------------------------------ off = unchanged


def test_flag_off_leaves_logits_and_weights_untouched(spec):
    obs = _obs(spec, 64, 4)
    x = obs_to_device(obs, "cpu", max_rows=64)
    off = _model(spec, False)
    ref = _model(spec, False)
    ref.mask_drop_potion = False                         # never touched vs toggled on and back off
    off.set_mask_drop_potion(True)
    off.set_mask_drop_potion(False)
    with torch.no_grad():
        l0, v0 = ref(x)
        l1, v1 = off(x)
    assert torch.equal(l0, l1) and torch.equal(v0, v1)
    on = _model(spec, True)
    with torch.no_grad():
        l2, v2 = on(x)
    m, _ = _masked(on, obs)
    m = torch.from_numpy(m)
    assert torch.equal(v0, v2)
    assert torch.equal(l2[~m], l0[~m]) and (l2[m] == NEG).all() and m.any()
    assert param_count(on) == param_count(ref) and on.arch == ref.arch
    assert ppo.weights_hash(on) == ppo.weights_hash(ref)
    assert set(on.state_dict()) == set(ref.state_dict())


def test_old_spec_without_shop_names(spec):
    """The test specs predate ShopCategory names: filled from Vocab.cs by size; a spec that cannot express the mask is
    refused when it is turned on (never silently wrong), and still builds with it off."""
    assert "ShopCategory" not in spec.enum_names and isinstance(_model(spec)._drop_ids, dict)
    import copy

    bad = copy.deepcopy(spec)
    bad.enums["ShopCategory"] = 9
    m = Policy(bad, **ARCH)
    assert isinstance(m._drop_ids, str)
    with pytest.raises(ValueError, match="mask-drop-potion"):
        m.set_mask_drop_potion(True)
    m.set_mask_drop_potion(False)


# ------------------------------------------------------------------------------------------------ rollout = update


def test_rollout_logp_equals_update_logp(spec_dt):
    spec, dt = spec_dt
    n = 128
    obs = _obs(spec, n, 5)
    model = _model(spec, True, seed=3)
    torch.manual_seed(11)
    runner = ppo.InferenceRunner(model, False, "off", "cpu")
    x = obs_to_device(obs, "cpu", max_rows=64)
    a, logp, value = runner.sample(x)
    m, _ = _masked(model, obs)
    assert m.any() and not m[np.arange(n), a.numpy()].any()          # never draws a masked row
    store = RolloutStore(spec, 1, n, 64, dt, "cpu")
    store.put(0, x, obs)
    idx = np.arange(n)
    xb = store.batch(idx)
    with torch.no_grad():
        lr, _ = runner.forward(x)
        lb, vb = model(xb)
    assert torch.equal(lb, lr) and torch.equal(vb, value)            # the update's masked logits = the rollout's
    up = torch.log_softmax(lb, -1).gather(1, a.view(-1, 1)).squeeze(1)
    # Categorical.log_prob (rollout) and log_softmax (ppo_loss) round differently (~5e-7), mask or not
    assert torch.allclose(up, logp, atol=2e-6, rtol=0)
    # a minibatch (another row cut) and the PPO loss: ratio 1, no clipping
    mb = np.sort(np.random.default_rng(0).permutation(n)[:48])
    xm = store.batch(mb)
    with torch.no_grad():
        lm, _ = model(xm)
    upm = torch.log_softmax(lm, -1).gather(1, a[mb].view(-1, 1)).squeeze(1)
    assert torch.allclose(upm, logp[mb], atol=2e-6, rtol=0)
    zeros = torch.zeros(len(mb))
    _, st = ppo.ppo_loss(model, xm, a[mb], logp[mb], torch.ones(len(mb)), zeros, zeros, clip=0.2, vf_coef=0.5,
                         ent_coef=0.01)
    names = ppo.STAT_NAMES
    assert abs(float(st[names.index("approx_kl")])) < 1e-6 and float(st[names.index("clipfrac")]) == 0.0
    # the same update without the mask sees another distribution (the test is sensitive to the mask)
    model.set_mask_drop_potion(False)
    with torch.no_grad():
        lo, _ = model(xb)
    off = torch.log_softmax(lo, -1).gather(1, a.view(-1, 1)).squeeze(1)
    hit = torch.from_numpy(m.any(1))
    assert (off[hit] < up[hit]).all() and torch.equal(off[~hit], up[~hit])


def test_drop_potion_stats(spec_dt):
    spec, dt = spec_dt
    n = 6
    obs = _obs(spec, n, 6)
    for s in range(n):
        _blank(spec, obs, s)
        _belt(spec, obs, s, 1, 3)
        _rows(spec, obs, s, KINDS)
    _rows(spec, obs, 5, ["DiscardPotion"])                            # fallback
    store = RolloutStore(spec, 2, n, 64, dt, "cpu")
    for t in range(2):
        store.put(t, obs_to_device(obs, "cpu", max_rows=64), obs)
    store.valid[:] = True
    store.valid[1, 4] = False                                         # not deciding: not counted
    store.actions[:] = 0
    store.actions[0, :3] = 1                                          # three drops taken at t = 0
    store.actions[1, 4] = 2                                           # invalid sample: ignored
    st = ppo.drop_potion_stats(_model(spec, True), store)
    assert st == {"drop_potion_actions": 3 + 2, "drop_potion_rows_masked": 2 * 5 * 2 - 2,
                  "drop_potion_mask_fallbacks": 2}
    # slot 5 takes its only (drop) row at both steps; slot 4's t = 1 sample is invalid (its rows are not counted)
    st_off = ppo.drop_potion_stats(_model(spec, False), store)
    assert st_off == {"drop_potion_actions": 5, "drop_potion_rows_masked": 0, "drop_potion_mask_fallbacks": 0}


def test_loop_breaker_skip_neg(spec):
    """Back at an observation whose only unbanned rows are masked drops: with skip_neg nothing is masked (the policy's
    row stays live); without it the breaker leaves the slot with no live row at all."""
    from sts2train.loopbreak import LoopBreaker

    obs = _obs(spec, 1, 7)
    _blank(spec, obs, 0)
    _belt(spec, obs, 0, 1, 3)
    _rows(spec, obs, 0, ["EndTurn", "DiscardPotion", "DiscardPotion"])
    model = _model(spec, True)
    with torch.no_grad():
        lg, _ = model(obs_to_device(obs, "cpu", max_rows=64))
    assert lg[0, 1] == NEG and lg[0, 2] == NEG and lg[0, 0] > NEG / 10
    for skip in (True, False):
        lb = LoopBreaker(spec, 1, skip_neg=skip)
        assert lb.apply(obs, lg) is lg
        lb.commit(np.array([0]))
        out = lb.apply(obs, lg)                                       # back here: row 0 was taken
        if skip:
            assert out is lg and not lb.masked.any()
        else:
            assert lb.masked.all() and not (out > NEG / 10).any()


# ------------------------------------------------------------------------------------------------ config / checkpoints


def test_config_cli_resume_and_checkpoint(spec, tmp_path, monkeypatch, capsys):
    from sts2train import cli
    from sts2train import evaluate as E

    assert ppo.TrainConfig().mask_drop_potion is False and ppo.resolve_config({}).mask_drop_potion is False
    with pytest.raises(ValueError):
        ppo.resolve_config({"mask_drop_potion": "yes"})
    got = {}
    monkeypatch.setattr(ppo, "train", lambda cfg, **kw: got.setdefault("cfg", cfg) and {})
    assert cli.main(["train", "--out", str(tmp_path / "r"), "--mask-drop-potion"]) == 0
    assert got["cfg"].mask_drop_potion is True
    # resume: the checkpoint's value is inherited, --no-mask-drop-potion turns it off, old checkpoints are off
    on = ppo.resolve_config({"mask_drop_potion": True})
    for cfg_ck, given, want in ((ppo.config_dict(on), {}, True), (ppo.config_dict(on), {"mask_drop_potion": False}, False),
                                ({k: v for k, v in ppo.config_dict(on).items() if k != "mask_drop_potion"}, {}, False)):
        p = tmp_path / f"ck-{len(list(tmp_path.iterdir()))}.pt"
        torch.save({"config": cfg_ck, "optimizer": {}}, p)             # arch: from the config (the smoke model)
        cfg, _, _ = ppo.plan_run(dict(given, out=str(tmp_path / "r")), str(p), log=lambda m: None)
        assert cfg.mask_drop_potion is want
    # a saved checkpoint carries it; load_checkpoint applies it (auto) or the override
    ck = tmp_path / "w.pt"
    ppo.save_checkpoint(ck, _model(spec), spec, on, {"updates": 0})
    for override, want in ((None, True), (False, False), (True, True)):
        m, _, c = ppo.load_checkpoint(ck, mask_drop_potion=override)
        assert m.mask_drop_potion is want and c["mask_drop_potion"] is want
        assert ppo.checkpoint_mask_drop_potion(c) is True
    old = tmp_path / "old.pt"
    ppo.save_checkpoint(old, _model(spec), spec, ppo.TrainConfig(), {"updates": 0})
    d = torch.load(old, weights_only=False)
    d["config"].pop("mask_drop_potion")
    torch.save(d, old)
    assert ppo.load_checkpoint(old)[0].mask_drop_potion is False
    assert ppo.load_checkpoint(old, mask_drop_potion=True)[0].mask_drop_potion is True
    # sts2train eval --mask-drop-potion auto (default) / on / off reaches evaluate()
    assert [E.mask_override(v) for v in ("auto", None, "on", "off")] == [None, None, True, False]
    with pytest.raises(ValueError):
        E.mask_override("maybe")
    seen = {}

    def fake_evaluate(seedset, checkpoint, out, **kw):
        seen.update(kw)
        return {"headline": {"progress_mean": 0.1, "progress_ci95": [0, 0.2], "wins": 0, "games": 1,
                             "results": {"loss": 1}, "floor_mean": 3.0, "act_reached": {"1": 1}, "replayable": 1},
                "drop_potion": {"mask": "on", "drop_potion_actions": 0}}

    monkeypatch.setattr(E, "evaluate", fake_evaluate)
    base = ["eval", "--seedset", "x", "--checkpoint", "ck.pt", "--out", str(tmp_path / "o")]
    assert cli.main(base) == 0 and seen["mask_drop_potion"] == "auto"
    assert cli.main(base + ["--mask-drop-potion", "off"]) == 0 and seen["mask_drop_potion"] == "off"
    out = capsys.readouterr().out
    assert '"drop_potion"' in out


def test_reportcard_reuse_checks_the_mask(tmp_path):
    import sts2train.reportcard as R

    ss = {"name": "x", "ascension": 0, "content": "ported_only", "games": [{"seed": "S1", "character": "SILENT"}]}
    d = tmp_path / "e"
    (d / "games").mkdir(parents=True)
    (d / "report.json").write_text("{}")
    from sts2vec import protocol as PG

    (d / "games" / f"{PG.game_key(ss['games'][0])}.summary.json").write_text("{}")
    cfg = {"seedset": ss, "limits": R.LIMITS, "server_version": "v"}

    def reason(**kw):
        (d / "config.json").write_text(json.dumps(cfg))
        return R.stale_reason(d, ss, library_version="v", **kw)

    assert reason() is None and reason(mask_drop_potion=False) is None
    assert "DiscardPotion mask off" in reason(mask_drop_potion=True)
    cfg["mask_drop_potion"] = True
    assert reason(mask_drop_potion=True) is None and "mask on" in reason(mask_drop_potion=False)


# ------------------------------------------------------------------------------------------------ live library


def test_train_metrics_live(tmp_path):
    """A short real run: the three metrics on every row (rows masked / fallbacks 0 with the mask off), the config
    records the flag, and the checkpoint's model loads with it."""
    pytest.importorskip("sts2vec")
    from sts2train.runlog import read_jsonl

    keys = ppo.DROP_POTION_METRICS
    for on in (True, False):
        run = tmp_path / f"run-{on}"
        ppo.train(ppo.resolve_config({"out": str(run), "envs": 8, "threads": 2, "pool": 4, "rollout": 16,
                                      "minibatch": 64, "max_updates": 2, "ckpt_every": 2, "ckpt_every_min": 0,
                                      "eval_every": 0, "max_hours": 0.5, "torch_threads": 2,
                                      "mask_drop_potion": on}), log=lambda m: None)
        rows = read_jsonl(run / "metrics.jsonl")
        assert len(rows) == 2 and all(isinstance(r[k], int) and r[k] >= 0 for r in rows for k in keys)
        if not on:
            assert all(r["drop_potion_rows_masked"] == 0 and r["drop_potion_mask_fallbacks"] == 0 for r in rows)
        assert json.loads((run / "config.json").read_text())["config"]["mask_drop_potion"] is on
        ck = sorted((run / "checkpoints").glob("ckpt-*.pt"))[-1]
        assert ppo.load_checkpoint(ck)[0].mask_drop_potion is on


def test_eval_records_mask_live(tmp_path, capsys):
    """sts2train eval of a checkpoint trained with the mask: auto plays with it (policy hash +mask-drop-potion, drop
    counts in report.json), --mask-drop-potion off without."""
    pytest.importorskip("sts2vec")
    from sts2vec import load, schema_of

    from sts2train.cli import main
    from sts2train.features import ModelSpec, bind_spec

    lib = ModelSpec.from_schema(schema_of(load()))
    bind_spec(lib)
    torch.manual_seed(1)
    ck = tmp_path / "ck.pt"
    ppo.save_checkpoint(ck, Policy(lib), lib, ppo.resolve_config({"mask_drop_potion": True}), {"updates": 0})
    ss = tmp_path / "t1.json"
    ss.write_text(json.dumps({"name": "t1", "ascension": 0, "content": "ported_only",
                              "games": [{"seed": "E1A0SI01", "character": "SILENT"}]}))
    for mode, on in (("auto", True), ("off", False)):
        out = tmp_path / f"ev-{mode}"
        assert main(["eval", "--seedset", str(ss), "--checkpoint", str(ck), "--out", str(out), "--threads", "1",
                     "--mask-drop-potion", mode]) == 0
        capsys.readouterr()
        cfg = json.loads((out / "config.json").read_text())
        assert cfg["mask_drop_potion"] is on
        assert cfg["mask_drop_potion_source"] == {"mode": mode, "checkpoint": True, "effective": on}
        assert cfg["prompt_hash"].endswith("+mask-drop-potion") == on
        rep = json.loads((out / "report.json").read_text())
        assert rep["drop_potion"]["mask"] == ("on" if on else "off")
        if not on:
            assert rep["drop_potion"]["drop_potion_rows_masked"] == 0


@pytest.fixture(autouse=True)
def _restore_threads():
    n = torch.get_num_threads()
    yield
    torch.set_num_threads(n)
