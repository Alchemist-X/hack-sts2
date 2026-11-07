"""Resume across a schema change (`features.check_spec_compat`).

A checkpoint whose spec differs from the library's only outside the model's inputs resumes (the weights load exactly,
config.json records both encoder versions). So does one whose embedded enums only gained members appended at their end
("widened": those embedding tables keep the checkpoint's rows, the new rows and their Adam moments start at zero, and
for every candidate of a kind the checkpoint knew the policy is unchanged). Any other difference in the model's inputs
-- shape or column map of one of the 27 buffers it reads, a vocabulary size, an embedded enum that shrank, was
reordered, had a member inserted or grew without member names to verify the order, the pointer enums' members,
card_vars -- is refused before the env is built.

Real specs: Stage 1 of the 2026-11-03 training cycle trained on encoder_version fc2eb34c07bc8513
(`data/spec-<that>.json` is `ModelSpec.from_schema` of that library, built from b1d4a21); the 2026-11-04 Silent runs
trained on 5dbfa09dd9dcb5ed (`data/spec-<that>.json` is the spec stored in one of their checkpoints, s4a). The
rewards-screen macros (e9baaefc39811631 onward, README "Resume across library versions") appended TakeRewardCard /
TakeRewardAlternative to RunActionKind, so both specs widen RunActionKind 21 -> 23 on this library. Differences outside
the model's inputs are tested with a synthetic older spec (`_older`: same model inputs, another encoder_version).
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train.features import (ENUMS, MODEL_VOCABS, USED_BUFFERS, ModelSpec, SpecMismatch, check_spec_compat,
                                spec_diff, widen_spec)
from sts2train.model import ENUM_EMBEDDINGS, Policy, param_count, widen_state_dict, widened_tensors
from sts2train.ppo import (load_checkpoint, make_checkpoint, plan_run, resolve_config, restore_checkpoint, train,
                           weights_hash)
from sts2train.runlog import CheckpointManager, atomic_write_json, read_jsonl

STAGE1 = "fc2eb34c07bc8513"
PRE_MACROS = "5dbfa09dd9dcb5ed"
DATA = Path(__file__).parent / "data"
ARCH = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}
MACROS = ["TakeRewardCard", "TakeRewardAlternative"]
TWO_GAMES = {"name": "t2", "ascension": 0, "content": "ported_only",
             "games": [{"seed": "E1A0IC01", "character": "IRONCLAD"}, {"seed": "E1A0SI01", "character": "SILENT"}]}


@pytest.fixture(scope="module")
def live() -> ModelSpec:
    from sts2vec import load, schema_of

    return ModelSpec.from_schema(schema_of(load()))


def _real(ev: str) -> dict:
    return json.loads((DATA / f"spec-{ev}.json").read_text())


@pytest.fixture(scope="module")
def stage1() -> dict:
    return _real(STAGE1)


def _older(live: ModelSpec) -> dict:
    """The live spec as an older library would publish it with the same model inputs: another encoder_version, an
    enum the model does not embed missing / one member shorter, two fewer aux columns."""
    d = copy.deepcopy(live.to_json())
    d["encoder_version"] = "0123456789abcdef"
    d["enums"].pop("ItemKind", None)
    d["enums"]["EndKind"] -= 1
    aux = sorted(d["cols"]["aux"], key=d["cols"]["aux"].get)[:-2]
    d["cols"]["aux"] = {f: i for i, f in enumerate(aux)}
    d["shapes"]["aux"] = [len(aux)]
    d["aux_fields"] = aux
    return d


def test_accepts_differences_outside_the_model_inputs(live):
    old = _older(live)
    block, widened, other = spec_diff(old, live)
    assert block == [] and widened == {}
    assert any(x.startswith("encoder_version 0123456789abcdef -> ") for x in other)
    assert any(x.startswith("enum ItemKind: absent -> ") for x in other)
    assert any(x.startswith("enum EndKind: ") for x in other)
    assert any(x.startswith("buffer aux: +") for x in other)
    rec = check_spec_compat(old, live)
    assert rec["checkpoint_encoder_version"] == "0123456789abcdef"
    assert rec["library_encoder_version"] == live.encoder_version and rec["model_inputs"] == "identical"
    assert rec["widened_enums"] == {}
    assert check_spec_compat(live, live)["other_differences"] == []


def test_live_spec_records_every_embedded_enums_members(live):
    """Widening is verified by member names, so a spec records them for every enum the model embeds."""
    assert set(live.enum_names) == set(ENUMS)
    assert all(len(live.enum_names[k]) == live.enums[k] for k in ENUMS)


def _swap_cols(d, buf):
    a, b = sorted(d["cols"][buf], key=d["cols"][buf].get)[:2]
    d["cols"][buf][a], d["cols"][buf][b] = d["cols"][buf][b], d["cols"][buf][a]


def _drop_member(d, enum, name):
    """`d` as a library without the member `name` would publish it (the enum one member shorter)."""
    d["enum_names"][enum].remove(name)
    d["enums"][enum] -= 1


def _swap_members(d, enum):
    names = d["enum_names"][enum]
    names[0], names[1] = names[1], names[0]


MUTATIONS = {
    "hand rows": (lambda d: d["shapes"]["hand"].__setitem__(0, d["shapes"]["hand"][0] + 1), "buffer hand: shape"),
    "cand columns swapped": (lambda d: _swap_cols(d, "cand"), "buffer cand: columns"),
    "global_i column renamed": (lambda d: d["cols"]["global_i"].__setitem__(
        "prompt_n", d["cols"]["global_i"].pop("prompt_option_n")),
        "buffer global_i: columns -prompt_n, +prompt_option_n"),
    "used buffer missing": (lambda d: (d["shapes"].pop("enemy_powers"), d["cols"].pop("enemy_powers", None)),
                            "buffer enemy_powers: shape None"),
    "cards vocab grew": (lambda d: d["vocab"].__setitem__("cards", d["vocab"]["cards"] - 1), "vocab cards"),
    "embedded enum shrank": (lambda d: (d["enums"].__setitem__("RunActionKind", d["enums"]["RunActionKind"] + 1),
                                        d["enum_names"]["RunActionKind"].append("Gone")),
                             "enum RunActionKind: 24 -> 23"),
    "embedded enum member inserted": (lambda d: _drop_member(d, "RunActionKind", "EndTurn"),
                                      "enum RunActionKind: 22 -> 23"),
    "embedded enum reordered": (lambda d: _swap_members(d, "IntentType"), "enum IntentType members"),
    "embedded enum grew without member names": (
        lambda d: (d["enums"].__setitem__("IntentType", d["enums"]["IntentType"] - 1),
                   d["enum_names"].pop("IntentType")),
        "the checkpoint records no member names"),
    "pointer enum order": (lambda d: d["enum_names"]["PromptItemKind"].reverse(), "enum PromptItemKind members"),
    "card_vars": (lambda d: d.__setitem__("card_vars", d["card_vars"] + 1), "card_vars"),
}


@pytest.mark.parametrize("name", sorted(MUTATIONS))
def test_refuses_model_input_changes(live, name):
    mutate, expect = MUTATIONS[name]
    old = _older(live)
    mutate(old)
    block, widened, _ = spec_diff(old, live)
    assert block and any(expect in x for x in block), block
    assert widened == {}
    with pytest.raises(SpecMismatch, match="cannot resume: the checkpoint's model inputs differ") as e:
        check_spec_compat(old, live)
    assert expect in str(e.value) and "0123456789abcdef" in str(e.value)


def test_widening_does_not_excuse_other_input_changes(live, stage1):
    """A Stage-1 spec (widenable) with permuted `hand` columns: refused for the columns; the widenable enum is not
    listed as a difference."""
    bad = copy.deepcopy(stage1)
    _swap_cols(bad, "hand")
    block, widened, _ = spec_diff(bad, live)
    assert any(x.startswith("buffer hand: columns") for x in block) and list(widened) == ["RunActionKind"]
    assert not any("RunActionKind" in x for x in block), block
    with pytest.raises(SpecMismatch, match="buffer hand: columns"):
        check_spec_compat(bad, live)


def test_model_reads_exactly_the_compared_vocabularies_and_enums(live):
    """MODEL_VOCABS / ENUMS (what spec_diff treats as model inputs) are what Policy embeds: growing any of them grows
    the model; growing any other list or enum does not."""
    base = param_count(Policy(live, **ARCH))

    def grown(section, key):
        d = copy.deepcopy(live.to_json())
        d[section][key] += 1
        return param_count(Policy(ModelSpec.from_json(d), **ARCH))

    for k in MODEL_VOCABS:
        assert grown("vocab", k) > base, k
    for k in ENUMS:
        assert grown("enums", k) > base, k
    for k in set(live.vocab) - set(MODEL_VOCABS):
        assert grown("vocab", k) == base, k
    for k in set(live.enums) - set(ENUMS):
        assert grown("enums", k) == base, k
    assert set(live.shapes) >= set(USED_BUFFERS)


def test_enum_embeddings_are_exactly_the_tables_an_enum_sizes(live):
    """ENUM_EMBEDDINGS (the tables a widening grows) lists, for every embedded enum, exactly the tensors whose shape
    follows that enum's size (all optional parts on): growing the enum by 2 adds 2 rows to each of them and changes no
    other tensor."""
    assert set(ENUM_EMBEDDINGS) == set(ENUMS)
    flags = {"aux_heads": True, "deck_counts": True}
    base = {k: tuple(v.shape) for k, v in Policy(live, **ARCH, **flags).state_dict().items()}
    for e in ENUMS:
        d = copy.deepcopy(live.to_json())
        d["enums"][e] += 2
        grown = {k: tuple(v.shape) for k, v in Policy(ModelSpec.from_json(d), **ARCH, **flags).state_dict().items()}
        changed = {k for k in base if base[k] != grown.get(k)}
        assert set(grown) == set(base) and changed == set(widened_tensors([e])), (e, changed)
        for k in changed:
            assert grown[k] == (base[k][0] + 2, *base[k][1:]), (e, k)


@pytest.mark.parametrize("ev", [STAGE1, PRE_MACROS])
def test_real_pre_macro_specs_widen_run_action_kind(live, ev):
    """The real Stage-1 (fc2eb34c07bc8513) and pre-macro (5dbfa09dd9dcb5ed) specs lack the rewards-screen macro kinds
    this library appends to RunActionKind: accepted as a widening of that enum, with no other model-input difference."""
    old = _real(ev)
    assert old["encoder_version"] == ev and old["enums"]["RunActionKind"] == 21
    kinds = live.enum_names["RunActionKind"]
    assert kinds[:21] == old["enum_names"]["RunActionKind"] and kinds[21:23] == MACROS
    block, widened, other = spec_diff(old, live)
    assert block == []
    assert widened == {"RunActionKind": {"from": 21, "to": len(kinds), "added": kinds[21:]}}
    assert other[0] == f"encoder_version {ev} -> {live.encoder_version}"
    rec = check_spec_compat(old, live)
    assert rec["model_inputs"] == "widened" and rec["widened_enums"] == widened
    assert rec["checkpoint_encoder_version"] == ev and rec["other_differences"] == other
    w = widen_spec(old, live, widened)
    assert w.encoder_version == ev and spec_diff(w, live)[:2] == ([], {})
    assert param_count(Policy(w, **ARCH)) == param_count(Policy(live, **ARCH))


def _stage1_checkpoint(run: Path, spec_json: dict, updates: int = 2) -> tuple[Path, dict]:
    """A resumable training checkpoint as a session of an older library writes it: model built from the old spec, a
    few Adam steps, counters, RNG states; plus that session's config.json."""
    torch.manual_seed(1)
    spec = ModelSpec.from_json(spec_json)
    model = Policy(spec, **ARCH)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    for _ in range(2):
        loss = sum((p.float() ** 2).mean() for p in model.parameters() if p.dim() > 1)
        opt.zero_grad()
        loss.backward()
        opt.step()
    cfg = resolve_config(dict(ARCH, out=str(run), envs=16, threads=2, pool=8, rollout=4, minibatch=64,
                              torch_threads=2, log_every=100, ckpt_every=1, eval_every=0, max_hours=0.5))
    ck = make_checkpoint(model, opt, spec, cfg, {"updates": updates, "env_steps": 1000, "sessions": 1},
                         np.random.default_rng(5), torch.device("cpu"))
    path = CheckpointManager(run).save(ck, updates)
    atomic_write_json(run / "config.json", {"config": ck["config"], "encoder_version": spec.encoder_version,
                                            "sessions": [{"session": 1, "update_start": 0}]})
    return path, ck


def test_older_checkpoint_weights_load_on_this_library(tmp_path, live):
    """Strict load of an older library's checkpoint (same model inputs) into a model built from the live spec: every
    tensor, Adam state included."""
    path, ck = _stage1_checkpoint(tmp_path / "run", _older(live))
    model = Policy(live, **ARCH)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    restore_checkpoint(torch.load(path, weights_only=False), model, opt, np.random.default_rng(0),
                       torch.device("cpu"))
    assert weights_hash(model) == ck["weights_hash"]
    assert len(opt.state) == len(ck["optimizer"]["state"]) > 0
    for p_ in opt.state.values():
        assert int(p_["step"]) == 2


def test_widened_checkpoint_keeps_old_rows_and_zero_new_rows(tmp_path, live, stage1):
    """Restoring a Stage-1 checkpoint (21 RunActionKind members) into a model of the live spec: a strict load fails;
    widened, every tensor equals the checkpoint's except the RunActionKind tables, whose first 21 rows are the
    checkpoint's and whose new rows are zero -- in the weights and in Adam's moments (step counts kept)."""
    path, ck = _stage1_checkpoint(tmp_path / "run", stage1)
    model = Policy(live, **ARCH)
    with pytest.raises(RuntimeError, match="size mismatch"):
        Policy(live, **ARCH).load_state_dict(ck["model"])
    rec = check_spec_compat(stage1, live)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    restore_checkpoint(torch.load(path, weights_only=False), model, opt, np.random.default_rng(0),
                       torch.device("cpu"), widened=rec["widened_enums"])
    keys = set(widened_tensors(rec["widened_enums"]))
    assert keys == {"kind_emb.weight", "c_kind.weight"}
    n = len(live.enum_names["RunActionKind"])
    for k, v in model.state_dict().items():
        old = ck["model"][k]
        if k in keys:
            assert v.shape[0] == n and torch.equal(v[:21], old) and not v[21:].any(), k
        else:
            assert torch.equal(v, old), k
    ost = ck["optimizer"]["state"]
    assert len(opt.state) == len(ost) > 0
    with_state = set()
    for i, (k, p_) in enumerate(model.named_parameters()):
        if i not in ost:
            assert p_ not in opt.state, k
            continue
        with_state.add(k)
        st = opt.state[p_]
        assert int(st["step"]) == 2
        for m in ("exp_avg", "exp_avg_sq"):
            if k in keys:
                assert torch.equal(st[m][:21], ost[i][m]) and not st[m][21:].any(), (k, m)
            else:
                assert torch.equal(st[m], ost[i][m]), (k, m)
    assert keys <= with_state


def test_widened_model_plays_like_the_checkpoint_on_known_kinds(live, stage1):
    """On real observations of this library, rewards-screen macro rows included (the checkpoint's own model can only
    read those as its last kind), the widened model's value and its logit of every candidate whose kind the checkpoint
    knew (< 21) equal the checkpoint model's bit for bit."""
    from sts2vec import PoolSpec, VecEnv, random_actions

    from sts2train.features import bind_spec, obs_to_torch

    torch.manual_seed(2)
    old = Policy(ModelSpec.from_json(stage1), **ARCH).eval()
    widened = check_spec_compat(stage1, live)["widened_enums"]
    new = Policy(widen_spec(stage1, live, widened), **ARCH).eval()
    new.load_state_dict(widen_state_dict(old.state_dict(), new, widened))
    bind_spec(live)
    kind_col = live.cols["cand"]["kind"]
    rng = np.random.default_rng(0)
    macro_rows = compared = 0
    with VecEnv(32, threads=2, seed_text="WIDEN", pool=PoolSpec(count=32, seed_prefix="WIDEN")) as env, \
            torch.no_grad():
        obs = env.reset()
        for _ in range(160):
            x = obs_to_torch(obs)
            lo, vo = old(x)
            ln, vn = new(x)
            kinds = x["cand"][..., kind_col]
            valid = torch.arange(kinds.shape[1]).view(1, -1) < x["cand_n"].view(-1, 1)
            known = valid & (kinds < 21)
            assert torch.equal(lo[known], ln[known]) and torch.equal(vo, vn)
            macro_rows += int((valid & (kinds >= 21)).sum())
            compared += int(known.sum())
            obs, *_ = env.step(random_actions(rng, obs["cand_n"]))
    assert macro_rows > 0 and compared > 1000, (macro_rows, compared)


def test_resume_older_checkpoint_records_both_versions(tmp_path, live):
    """`--resume auto` on a run directory whose newest checkpoint has an older library's spec (same model inputs): one
    more update on this library; config.json / summary.json record both encoder versions; the new checkpoint carries
    the live spec."""
    run = tmp_path / "run"
    old = _older(live)
    OLD = old["encoder_version"]
    _, ck = _stage1_checkpoint(run, old)
    logs: list[str] = []
    cfg, ck2, ck2_path = plan_run({"out": str(run), "max_updates": 3}, "auto", log=logs.append)
    assert ck2_path.name == "ckpt-0000002.pt" and ck2["spec"]["encoder_version"] == OLD
    s = train(cfg, log=logs.append, resume=ck2, resume_path=ck2_path)
    assert s["update_start"] == 2 and s["updates"] == 3 and s["session"] == 2
    assert s["checkpoint_encoder_version"] == OLD and s["encoder_version"] == live.encoder_version
    conf = json.loads((run / "config.json").read_text())
    assert conf["encoder_version"] == live.encoder_version and conf["checkpoint_encoder_version"] == OLD
    assert conf["resume_compat"]["model_inputs"] == "identical" and conf["resume_compat"]["widened_enums"] == {}
    assert conf["resume_compat"]["checkpoint_encoder_version"] == OLD
    assert [x.get("encoder_version") for x in conf["sessions"]] == [None, live.encoder_version]
    assert conf["sessions"][1]["checkpoint_encoder_version"] == OLD
    assert conf["sessions"][1]["model_inputs"] == "identical"
    assert "floor_step_cap" in conf["env"]
    assert any(m.startswith(f"resume across encoder versions: checkpoint {OLD} -> ") for m in logs), logs
    _, spec3, ck3 = load_checkpoint(run / "checkpoints" / "ckpt-0000003.pt")
    assert spec3.encoder_version == live.encoder_version and ck3["updates"] == 3
    assert [r["update"] for r in read_jsonl(run / "metrics.jsonl")] == [3]
    assert "floor_stall" in read_jsonl(run / "metrics.jsonl")[0]


def test_resume_widened_checkpoint_records_the_widening(tmp_path, live, stage1):
    """`--resume auto` on a run whose newest checkpoint has the Stage-1 spec: one more update on this library;
    config.json's resume_compat (and the session entry) say widened, RunActionKind 21 -> 23, the log says so, and the
    next checkpoint carries the live spec and the widened tables."""
    run = tmp_path / "run"
    _stage1_checkpoint(run, stage1)
    logs: list[str] = []
    cfg, ck2, ck2_path = plan_run({"out": str(run), "max_updates": 3}, "auto", log=logs.append)
    s = train(cfg, log=logs.append, resume=ck2, resume_path=ck2_path)
    assert s["update_start"] == 2 and s["updates"] == 3 and s["checkpoint_encoder_version"] == STAGE1
    kinds = live.enum_names["RunActionKind"]
    want = {"RunActionKind": {"from": 21, "to": len(kinds), "added": kinds[21:]}}
    conf = json.loads((run / "config.json").read_text())
    rc = conf["resume_compat"]
    assert rc["model_inputs"] == "widened" and rc["widened_enums"] == want
    assert rc["checkpoint_encoder_version"] == STAGE1 and conf["checkpoint_encoder_version"] == STAGE1
    assert conf["sessions"][1]["model_inputs"] == "widened" and conf["sessions"][1]["widened_enums"] == want
    assert any(m.startswith(f"resume across encoder versions: checkpoint {STAGE1} -> ")
               and "widened enums: RunActionKind 21 -> " in m for m in logs), logs
    assert any(m.startswith(f"resumed from {ck2_path}") and "; widened RunActionKind 21 -> " in m for m in logs), logs
    _, spec3, ck3 = load_checkpoint(run / "checkpoints" / "ckpt-0000003.pt")
    assert spec3.encoder_version == live.encoder_version and spec3.enums["RunActionKind"] == len(kinds)
    assert ck3["updates"] == 3 and ck3["model"]["c_kind.weight"].shape[0] == len(kinds)
    assert [r["update"] for r in read_jsonl(run / "metrics.jsonl")] == [3]


def test_eval_widened_checkpoint(tmp_path, stage1):
    """`sts2train eval` of a Stage-1 checkpoint on this library runs it widened (no refusal): every game replays and
    config.json records the widening next to the checkpoint's encoder_version and weights hash."""
    from sts2train.cli import main

    path, ck = _stage1_checkpoint(tmp_path / "run", stage1)
    ss = tmp_path / "t2.json"
    ss.write_text(json.dumps(TWO_GAMES))
    out = tmp_path / "ev"
    assert main(["eval", "--seedset", str(ss), "--checkpoint", str(path), "--out", str(out), "--threads", "2"]) == 0
    conf = json.loads((out / "config.json").read_text())
    assert conf["checkpoint_encoder_version"] == STAGE1 and conf["weights_hash"] == ck["weights_hash"]
    assert conf["spec_compat"]["model_inputs"] == "widened"
    assert list(conf["spec_compat"]["widened_enums"]) == ["RunActionKind"]
    rep = json.loads((out / "report.json").read_text())
    assert rep["headline"]["games"] == 2 and rep["headline"]["replayable"] == 2


def test_resume_refuses_incompatible_checkpoint_before_the_env(tmp_path, live, capsys):
    """A checkpoint with permuted `hand` columns (parameter shapes still match: a strict load would succeed and the
    model would silently misread every hand card) is refused, and the run directory is left as it was."""
    from sts2train.cli import main

    run = tmp_path / "run"
    bad = _older(live)
    _swap_cols(bad, "hand")
    _stage1_checkpoint(run, bad)
    before = {p.name: p.read_bytes() for p in run.iterdir() if p.is_file()}
    cfg, ck, ck_path = plan_run({"out": str(run), "max_updates": 3}, "auto", log=lambda m: None)
    with pytest.raises(SpecMismatch, match="buffer hand: columns"):
        train(cfg, log=lambda m: None, resume=ck, resume_path=ck_path)
    assert main(["train", "--resume", "auto", "--out", str(run), "--max-updates", "3"]) == 2
    assert "cannot resume: the checkpoint's model inputs differ" in capsys.readouterr().out
    after = {p.name: p.read_bytes() for p in run.iterdir() if p.is_file() and p.name != "train.log"}
    assert after == before and not (run / "status.json").exists() and not (run / "metrics.jsonl").exists()
