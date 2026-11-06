"""Portable paths for the archived Task099 synthetic CPU suite."""
from pathlib import Path
import importlib.util
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "code"))

import pytest
import torch
from sts2vec.schema import Schema
from sts2train.features import ModelSpec


@pytest.fixture(scope="session")
def spec():
    torch.set_num_threads(1)
    return ModelSpec.from_schema(Schema.from_json((ROOT / "public-schema.json").read_text()))


@pytest.fixture(scope="session")
def original():
    spec = importlib.util.spec_from_file_location("sts2train._reference8012", ROOT / "reference_model.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
