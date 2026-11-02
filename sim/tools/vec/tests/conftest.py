"""Shared fixtures.

Tests that need the real V1 encoder / V2 env-core (non-empty candidates, filled observations) use the ``full``
fixture: against the scaffold's zero-filled stubs they skip with the reason ``needs V1/V2``; once V1/V2 are merged they
run automatically. ``STS2VEC_REQUIRE_FULL=1`` turns those skips into failures (integrator gate: nothing may skip).
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import numpy as np
import pytest

from sts2vec import PoolSpec, Schema, VecEnv, load, schema_of

NEEDS = "needs V1/V2"
SIM_DIR = Path(__file__).resolve().parents[3]
FIXTURE_SAVE = SIM_DIR / "Sts2Sim.Tests" / "Fixtures" / "run_save_a8_silent_boss1.save"
SMALL_POOL = PoolSpec(characters=("IRONCLAD", "SILENT"), count=2)


@pytest.fixture(scope="session")
def lib():
    return load()


@pytest.fixture(scope="session")
def schema(lib) -> Schema:
    return schema_of(lib)


_full_state: dict = {}


def full_build_reason() -> str | None:
    """None when the loaded build has a real encoder and candidate table, else why not."""
    if "reason" not in _full_state:
        with VecEnv(4, threads=2, pool=SMALL_POOL) as env:
            obs = env.reset()
            hp = obs["global_i"][:, env.schema.col("global_i", "hp")]
            if int(obs["cand_n"].max()) == 0:
                _full_state["reason"] = "cand_n == 0 after reset (stub CandidateTable)"
            elif int(hp.max()) == 0:
                _full_state["reason"] = "global_i.hp == 0 after reset (stub ObservationEncoder)"
            else:
                _full_state["reason"] = None
    return _full_state["reason"]


@pytest.fixture
def full():
    reason = full_build_reason()
    if reason is not None:
        if os.environ.get("STS2VEC_REQUIRE_FULL") == "1":
            pytest.fail(f"STS2VEC_REQUIRE_FULL=1 but {reason}")
        pytest.skip(NEEDS)


@pytest.fixture
def small_env():
    with VecEnv(4, threads=2, pool=SMALL_POOL) as env:
        yield env


def python_cmd() -> list[str]:
    return [sys.executable]


def random_policy_rng(seed: int = 0) -> np.random.Generator:
    return np.random.default_rng(seed)
