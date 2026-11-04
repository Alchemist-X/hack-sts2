"""The vendored protocol module equals the protocol's reference implementation (sim/tools/eval, loaded by file path —
the eval uv project is not a dependency of sts2vec) and the committed seed sets load with their checksums."""

from __future__ import annotations

import importlib.util
import itertools
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
                                         ("a0-s20", 0, 20), ("silent-a0-v1", 0, 20), ("silent-a10-v1", 10, 20)])
def test_committed_seedsets_load(name, asc, n):
    ss = PG.load_seedset(name)
    assert ss["ascension"] == asc and len(ss["games"]) == n
    ident = PG.seedset_identity(ss)
    assert ident["seedset_frozen"] and ident["seedset_sha256"] == ss["sha256"] == PG.checksum(ss)


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
