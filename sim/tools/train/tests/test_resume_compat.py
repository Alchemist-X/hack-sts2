"""Resume across a schema change (`features.check_spec_compat`).

A checkpoint whose spec differs from the library's only outside the model's inputs resumes (the weights load exactly,
config.json records both encoder versions); any difference in the model's inputs -- shape or column map of one of the 27
buffers it reads, a vocabulary / embedded-enum size, the pointer enums' members, card_vars -- is refused before the env
is built. Stage 1 of the 2026-11-03 training cycle trains on encoder_version fc2eb34c07bc8513; `data/spec-<that>.json`
is `ModelSpec.from_schema` of that library (built from b1d4a21), so these tests also pin that its checkpoints resume
on this library. If a schema change ever alters a model input on purpose, those checkpoints can no longer resume:
drop the Stage-1 pin together with a note in the README rather than loosening the check.
"""

from __future__ import annotations

import copy
import json
from pathlib import Path

import numpy as np
import pytest
import torch

from sts2train.features import (ENUMS, MODEL_VOCABS, USED_BUFFERS, ModelSpec, SpecMismatch, check_spec_compat,
                                spec_diff)
from sts2train.model import Policy, param_count
from sts2train.ppo import (load_checkpoint, make_checkpoint, plan_run, resolve_config, restore_checkpoint, train,
                           weights_hash)
from sts2train.runlog import CheckpointManager, atomic_write_json, read_jsonl

STAGE1 = "fc2eb34c07bc8513"
STAGE1_SPEC = Path(__file__).parent / "data" / f"spec-{STAGE1}.json"
ARCH = {"d": 32, "s": 64, "depth": 1, "heads": 2, "ff_mult": 2}


@pytest.fixture(scope="module")
def live() -> ModelSpec:
    from sts2vec import load, schema_of

    return ModelSpec.from_schema(schema_of(load()))


@pytest.fixture(scope="module")
def stage1() -> dict:
    return json.loads(STAGE1_SPEC.read_text())


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
    block, other = spec_diff(old, live)
    assert block == []
    assert any(x.startswith("encoder_version 0123456789abcdef -> ") for x in other)
    assert any(x.startswith("enum ItemKind: absent -> ") for x in other)
    assert any(x.startswith("enum EndKind: ") for x in other)
    assert any(x.startswith("buffer aux: +") for x in other)
    rec = check_spec_compat(old, live)
    assert rec["checkpoint_encoder_version"] == "0123456789abcdef"
    assert rec["library_encoder_version"] == live.encoder_version and rec["model_inputs"] == "identical"
    assert check_spec_compat(live, live)["other_differences"] == []


def _swap_cols(d, buf):
    a, b = sorted(d["cols"][buf], key=d["cols"][buf].get)[:2]
    d["cols"][buf][a], d["cols"][buf][b] = d["cols"][buf][b], d["cols"][buf][a]


MUTATIONS = {
    "hand rows": (lambda d: d["shapes"]["hand"].__setitem__(0, d["shapes"]["hand"][0] + 1), "buffer hand: shape"),
    "cand columns swapped": (lambda d: _swap_cols(d, "cand"), "buffer cand: columns"),
    "global_i column renamed": (lambda d: d["cols"]["global_i"].__setitem__(
        "prompt_n", d["cols"]["global_i"].pop("prompt_option_n")),
        "buffer global_i: columns -prompt_n, +prompt_option_n"),
    "used buffer missing": (lambda d: (d["shapes"].pop("enemy_powers"), d["cols"].pop("enemy_powers", None)),
                            "buffer enemy_powers: shape None"),
    "cards vocab": (lambda d: d["vocab"].__setitem__("cards", d["vocab"]["cards"] - 1), "vocab cards"),
    "embedded enum": (lambda d: d["enums"].__setitem__("RunActionKind", d["enums"]["RunActionKind"] + 1),
                      "enum RunActionKind"),
    "pointer enum order": (lambda d: d["enum_names"]["PromptItemKind"].reverse(), "enum PromptItemKind members"),
    "card_vars": (lambda d: d.__setitem__("card_vars", d["card_vars"] + 1), "card_vars"),
}


@pytest.mark.parametrize("name", sorted(MUTATIONS))
def test_refuses_model_input_changes(live, name):
    mutate, expect = MUTATIONS[name]
    old = _older(live)
    mutate(old)
    block, _ = spec_diff(old, live)
    assert block and any(expect in x for x in block), block
    with pytest.raises(SpecMismatch, match="cannot resume: the checkpoint's model inputs differ") as e:
        check_spec_compat(old, live)
    assert expect in str(e.value) and "0123456789abcdef" in str(e.value)


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


def test_stage1_spec_is_resume_compatible(live, stage1):
    """The real fc2eb34c07bc8513 spec (Stage 1) differs from this library's only outside the model's inputs."""
    assert stage1["encoder_version"] == STAGE1
    rec = check_spec_compat(stage1, live)
    assert rec["model_inputs"] == "identical" and rec["checkpoint_encoder_version"] == STAGE1
    if live.encoder_version != STAGE1:
        assert rec["other_differences"][0] == f"encoder_version {STAGE1} -> {live.encoder_version}"


def _stage1_checkpoint(run: Path, spec_json: dict, updates: int = 2) -> tuple[Path, dict]:
    """A resumable training checkpoint as a Stage-1 session writes it: model built from the old spec, a few Adam
    steps, counters, RNG states; plus that session's config.json."""
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


def test_stage1_checkpoint_weights_load_on_this_library(tmp_path, live, stage1):
    """Strict load of a Stage-1 checkpoint into a model built from the live spec: every tensor, Adam state included."""
    path, ck = _stage1_checkpoint(tmp_path / "run", stage1)
    model = Policy(live, **ARCH)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    restore_checkpoint(torch.load(path, weights_only=False), model, opt, np.random.default_rng(0),
                       torch.device("cpu"))
    assert weights_hash(model) == ck["weights_hash"]
    assert len(opt.state) == len(ck["optimizer"]["state"]) > 0
    for p_ in opt.state.values():
        assert int(p_["step"]) == 2


def test_resume_stage1_checkpoint_records_both_versions(tmp_path, live, stage1):
    """`--resume auto` on a run directory whose newest checkpoint has the Stage-1 spec: one more update on this
    library; config.json / summary.json record both encoder versions; the new checkpoint carries the live spec."""
    run = tmp_path / "run"
    _, ck = _stage1_checkpoint(run, stage1)
    logs: list[str] = []
    cfg, ck2, ck2_path = plan_run({"out": str(run), "max_updates": 3}, "auto", log=logs.append)
    assert ck2_path.name == "ckpt-0000002.pt" and ck2["spec"]["encoder_version"] == STAGE1
    s = train(cfg, log=logs.append, resume=ck2, resume_path=ck2_path)
    assert s["update_start"] == 2 and s["updates"] == 3 and s["session"] == 2
    assert s["checkpoint_encoder_version"] == STAGE1 and s["encoder_version"] == live.encoder_version
    conf = json.loads((run / "config.json").read_text())
    assert conf["encoder_version"] == live.encoder_version and conf["checkpoint_encoder_version"] == STAGE1
    assert conf["resume_compat"]["model_inputs"] == "identical"
    assert conf["resume_compat"]["checkpoint_encoder_version"] == STAGE1
    assert [x.get("encoder_version") for x in conf["sessions"]] == [None, live.encoder_version]
    assert conf["sessions"][1]["checkpoint_encoder_version"] == STAGE1
    assert "floor_step_cap" in conf["env"]
    if live.encoder_version != STAGE1:
        assert any(m.startswith(f"resume across encoder versions: checkpoint {STAGE1} -> ") for m in logs), logs
    _, spec3, ck3 = load_checkpoint(run / "checkpoints" / "ckpt-0000003.pt")
    assert spec3.encoder_version == live.encoder_version and ck3["updates"] == 3
    assert [r["update"] for r in read_jsonl(run / "metrics.jsonl")] == [3]
    assert "floor_stall" in read_jsonl(run / "metrics.jsonl")[0]


def test_resume_refuses_incompatible_checkpoint_before_the_env(tmp_path, live, stage1, capsys):
    """A checkpoint with permuted `hand` columns (parameter shapes still match: a strict load would succeed and the
    model would silently misread every hand card) is refused, and the run directory is left as it was."""
    from sts2train.cli import main

    run = tmp_path / "run"
    bad = copy.deepcopy(stage1)
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
