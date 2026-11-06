"""Portable paths for the archived Task104 synthetic CPU suite."""
from pathlib import Path
import importlib.util
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "code"))

import pytest
import torch
from sts2vec.schema import Schema
from sts2train.features import ModelSpec, PUBLIC_HAND_INPUT_ARCH


@pytest.fixture(scope="session")
def schema():
    torch.set_num_threads(1)
    return Schema.from_json((ROOT / "public-schema.json").read_text())


@pytest.fixture(scope="session")
def specs(schema):
    return ModelSpec.from_schema(schema), ModelSpec.from_schema(schema, input_arch=PUBLIC_HAND_INPUT_ARCH)


@pytest.fixture(scope="session")
def original():
    spec = importlib.util.spec_from_file_location("sts2train._task104_original_c937", ROOT / "reference_model.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
