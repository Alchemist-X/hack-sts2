"""The vendored protocol module equals the protocol's reference implementation (sim/tools/eval, loaded by file path —
the eval uv project is not a dependency of sts2vec) and the committed seed sets load with their checksums."""

from __future__ import annotations

import importlib.util
import itertools
import json
import math
from pathlib import Path

import pytest

from sts2vec import protocol as PG

REF = Path(__file__).resolve().parents[2] / "eval" / "sts2eval" / "progress.py"


@pytest.fixture(scope="module")
def ref():
    if not REF.exists():
        pytest.skip(f"reference implementation {REF} not present")
    spec = importlib.util.spec_from_file_location("_sts2eval_progress_ref", REF)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


A0 = {"1": 17, "2": 16, "3": 15}
FIXTURE_STARTS = [None, {}, {"1": 0, "2": 17, "3": 33}, {"1": 0, "2": 17, "3": 35}, {2: 18}]
FIXTURE_ROOMS = [None, {}, A0, {"1": 17, "2": 16, "3": 16}, {"1": 17, "2": 18, "3": 15}, {1: 18}, {"2": 16}]


def test_constants_equal(ref):
    assert PG.PROTOCOL_VERSION == ref.PROTOCOL_VERSION
    assert PG.ACTS == ref.ACTS and PG.DEFAULT_ACT_ROOMS == ref.DEFAULT_ACT_ROOMS
    assert PG.DOUBLE_BOSS_ASCENSION == ref.DOUBLE_BOSS_ASCENSION
    for asc in (0, 5, 10):
        assert PG.full_run_floors(asc) == ref.full_run_floors(asc)
        for act in (1, 2, 3, 4):
            assert PG.default_act_rooms(act, asc) == ref.default_act_rooms(act, asc)


def test_compute_equals_reference_on_fixture_grid(ref):
    n = 0
    for result, act, floor, asc, starts, rooms in itertools.product(
            ("win", "loss", "capped", "stuck", "crashed", "error", None), (None, 1, 2, 3), (None, 0, 1, 2, 16, 17, 18,
                                                                                         33, 35, 40, 48, 49),
            (0, 10), FIXTURE_STARTS, FIXTURE_ROOMS):
        a = PG.compute(result, act, floor, asc, starts, rooms)
        b = ref.compute(result, act, floor, asc, starts, rooms)
        assert a == b, (result, act, floor, asc, starts, rooms)
        n += 1
    assert n > 10_000


@pytest.mark.parametrize("result,act,floor,asc,expected", [
    ("win", 3, 48, 0, 1.0), ("loss", 1, 1, 0, 0.0), ("loss", 1, 17, 0, (16 / 17) / 3),
    ("loss", 3, 48, 0, (2 + 14 / 15) / 3), ("loss", 3, 49, 10, (2 + 15 / 16) / 3),
])
def test_compute_values(result, act, floor, asc, expected):
    rooms = dict(A0, **({"3": 16} if asc >= 10 else {}))
    out = PG.compute(result, act, floor, asc, {"1": 0, "2": 17, "3": 33}, rooms)
    assert math.isclose(out["progress"], expected, abs_tol=1e-6)


@pytest.mark.parametrize("name,asc,n", [("std-a0-v1", 0, 20), ("std-a10-v1", 10, 20), ("smoke-a0-v1", 0, 10),
                                         ("a0-s20", 0, 20), ("silent-a0-v1", 0, 20), ("silent-a10-v1", 10, 20),
                                         ("silent-a10-ab-v1", 10, 512), ("silent-a10-blind-v1", 10, 256),
                                         ("silent-a10-test-v1", 10, 100)])
def test_committed_seedsets_load(name, asc, n):
    ss = PG.load_seedset(name)
    assert ss["ascension"] == asc and len(ss["games"]) == n
    ident = PG.seedset_identity(ss)
    assert ident["seedset_frozen"] and ident["seedset_sha256"] == ss["sha256"] == PG.checksum(ss)


def test_ab_seedset_frozen_disjoint_outside_the_training_pool():
    """silent-a10-ab-v1 (from train/harvest a570373): 512 Silent A10 games on the training generator's seed texts
    TRAIN0-SILENT-4096..4607, i.e. outside the 4,096-entry training pool (`sts2train train --pool 4096 --seed 0`:
    TRAIN0-SILENT-0..4095) and disjoint from every other committed seed set (silent-a10-v1 / -dev-v1 / -test-v1
    included)."""
    ab = PG.load_seedset("silent-a10-ab-v1")
    assert (ab["name"], ab["role"], ab["version"], ab["ascension"], ab["content"]) == \
        ("silent-a10-ab-s512", "ab", 1, 10, "ported_only")
    assert ab["games"] == [{"seed": f"TRAIN0-SILENT-{i}", "character": "SILENT"} for i in range(4096, 4608)]
    ident = PG.seedset_identity(ab)
    assert ident["seedset_frozen"] and ident["seedset_id"] == "silent-a10-ab-v1"
    assert ident["seedset_sha256"] == ab["sha256"] == PG.checksum(ab)
    seeds = {g["seed"] for g in ab["games"]}
    committed = Path(PG.__file__).resolve().parents[2] / "eval" / "seedsets"
    others = [q for q in sorted(committed.glob("*.json")) if json.loads(q.read_text()).get("id") != ab["id"]]
    assert {q.stem for q in others} >= {"silent-a10-s20", "silent-a10-dev-s128", "silent-a10-test-s100"}
    for q in others:
        assert not seeds & {g["seed"] for g in json.loads(q.read_text())["games"]}, q.name
    assert not seeds & {f"TRAIN0-SILENT-{i}" for i in range(4096)}


def test_seedset_training_pool_seed_texts():
    ok = {"name": "p", "ascension": 10, "content": "ported_only",
          "games": [{"seed": "TRAIN0-SILENT-4096", "character": "SILENT"},
                    {"seed": "C1A10SI01", "character": "SILENT"}]}
    assert PG.validate_seedset(ok) is ok
    for bad in ("TRAIN0-IRONCLAD-1", "train0-SILENT-1", "TRAIN0-SILENT-", "TRAINX-SILENT-1", "TRAIN0-SILENT-1-2",
                "TRAIN0SILENT1", "ABCDEFGHIJK", "SP0-SILENT-1"):
        with pytest.raises(PG.SeedSetError, match="seed"):
            PG.validate_seedset({**ok, "games": [{"seed": bad, "character": "SILENT"}]})


def test_run_seed_is_the_games_run_seed():
    """`run_seed` = RngSeeds.RunSeed: XxHash64 of the UTF-8 text (the C# known-answer vectors of
    Sts2Sim.Tests/RngTests.cs) and, for a text starting with `old`, the legacy 32-bit hash of the rest as uint32
    (DeterministicHash.GetDeterministicHashCodeOld vectors; EncounterRngTests: RunSeed("oldCPSVMWJPUL") =
    0xF0C22F8F)."""
    for text, want in {"": 0xEF46DB3751D8E999, "a": 0xD24EC4F1A98C6E5B, "abc": 0x44BC2CF5AD770999,
                       "Nobody inspects the spammish repetition": 0xFBCEA83C8A378BF1,
                       "0123456789ABCDEF0123456789ABCDEF": 0x51ACEF020CD423B1, "FUR_COAT": 0x1AEC5A643D318478,
                       "\u00e9" * 200: 0x90200B144DDCCD65}.items():
        assert PG.run_seed(text) == want, text
    for text, want in {"": 757602046, "a": -842352707, "ab": 938310215, "abc": -320589142, "up_front": -1234747045,
                       "3URCWVGADS": -853278557, "CPSVMWJPUL": -255709297}.items():
        assert PG._legacy_seed_hash(text) == want, text
    assert PG.run_seed("oldCPSVMWJPUL") == 0xF0C22F8F
    assert PG.run_seed("D1A10SI001") != PG.run_seed("D1A10S1001") != PG.run_seed("d1a10si001")


def test_blind_seedset_frozen_and_disjoint():
    """silent-a10-blind-v1: 256 Silent A10 games B1A10SI001..256 (10-character game seeds), role blind, frozen
    2026-11-04 with the protocol checksum; no seed text and no run seed in common with any other committed set, the
    legacy training pools (TRAIN0-<character>-0..8191) or the start of the seed stream family
    (SP0..SP3-SILENT-0..4095)."""
    b = PG.load_seedset("silent-a10-blind-v1")
    assert (b["name"], b["role"], b["version"], b["ascension"], b["content"], b["frozen"]) == \
        ("silent-a10-blind-s256", "blind", 1, 10, "ported_only", "2026-11-04")
    assert b["games"] == [{"seed": f"B1A10SI{i:03d}", "character": "SILENT"} for i in range(1, 257)]
    assert b["sha256"] == PG.checksum(b) and PG.seedset_identity(b)["seedset_frozen"]
    mine = {PG.run_seed(g["seed"]) for g in b["games"]}
    assert len(mine) == 256
    other_texts, other_seeds = set(), set()
    for q, d in PG.frozen_seedsets():
        if d.get("id") == b["id"]:
            continue
        other_texts |= {g["seed"] for g in d["games"]}
        other_seeds |= {PG.run_seed(g["seed"]) for g in d["games"]}
    assert not {g["seed"] for g in b["games"]} & other_texts and not mine & other_seeds
    chars = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")
    pools = {PG.run_seed(f"TRAIN0-{c}-{i}") for c in chars for i in range(8192)}
    stream = {PG.run_seed(f"SP{k}-SILENT-{n}") for k in range(4) for n in range(4096)}
    assert not mine & pools and not mine & stream


def test_test100_downgraded_to_dev2_with_its_checksum():
    t = PG.load_seedset("silent-a10-test-v1")
    assert t["role"] == "dev2" and t["sha256"] == PG.checksum(t) == \
        "2ce58e94585257f0aa3175d78503c82384da756a25cd203027f17cccbc3295de"


def test_blind_access_gate(tmp_path, monkeypatch):
    """`require_blind_access`: a set with any blind game (by run seed, whatever its own role) is refused without
    allow_blind + purpose; allowed, one line per access goes to out/blind-access.jsonl. The allowed path is exercised
    on a synthetic blind set (STS2VEC_SEEDSETS), never on the frozen one."""
    b = PG.load_seedset("silent-a10-blind-v1")
    with pytest.raises(PG.BlindSetError, match="silent-a10-blind-v1"):
        PG.require_blind_access(b)
    two = {"name": "x", "ascension": 0, "content": "ported_only", "games": b["games"][:2]}
    assert PG.blind_overlap(two)[0]["games"] == 2
    with pytest.raises(PG.BlindSetError):
        PG.require_blind_access(two, allow_blind=True)              # no purpose
    assert PG.require_blind_access(PG.load_seedset("silent-a10-dev-v1")) is None
    syn = tmp_path / "sets"
    syn.mkdir()
    games = [{"seed": "Z9TESTBL01", "character": "SILENT"}, {"seed": "Z9TESTBL02", "character": "SILENT"}]
    (syn / "syn-blind.json").write_text(json.dumps({"name": "syn-blind", "id": "syn-blind-v1", "role": "blind",
                                                    "ascension": 10, "content": "ported_only", "games": games}))
    monkeypatch.setenv("STS2VEC_SEEDSETS", str(syn))
    probe = {"name": "probe", "ascension": 10, "content": "ported_only", "games": games[:1]}
    with pytest.raises(PG.BlindSetError, match="syn-blind-v1"):
        PG.require_blind_access(probe)
    out = tmp_path / "out"
    row = PG.require_blind_access(probe, allow_blind=True, purpose="unit test", out_dir=out, weights_hash="w")
    lines = [json.loads(x) for x in (out / "blind-access.jsonl").read_text().splitlines()]
    assert lines == [row] and row["purpose"] == "unit test" and row["weights_hash"] == "w"
    assert row["seedset"]["sha256"] == PG.checksum(probe) and row["blind_sets"][0]["id"] == "syn-blind-v1"


def test_seedset_errors(tmp_path):
    with pytest.raises(PG.SeedSetError):
        PG.load_seedset("no-such-set")
    bad = tmp_path / "bad.json"
    bad.write_text('{"name": "b", "ascension": 0, "content": "ported_only", "games": [{"seed": "x", "character": "IRONCLAD"}]}')
    with pytest.raises(PG.SeedSetError):
        PG.load_seedset(str(bad))


def test_wilson_and_ci():
    lo, hi = PG.wilson(0, 20)
    assert lo == 0.0 and math.isclose(hi, 0.161, abs_tol=1e-3)
    assert PG.mean_ci95([0.5]) == (0.5, 0.5)
