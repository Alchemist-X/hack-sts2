"""sim/ENV_SPEC.md: the schema-generated block is up to date, covers every buffer and column, and the page stays
within its one-page budget."""

from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

GEN = Path(__file__).resolve().parents[1] / "tools" / "gen_env_spec.py"


@pytest.fixture(scope="module")
def gen():
    spec = importlib.util.spec_from_file_location("_gen_env_spec", GEN)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def test_generated_block_is_up_to_date(gen, schema):
    text = gen.SPEC.read_text(encoding="utf-8")
    assert gen.splice(text, gen.render(schema)) == text, \
        "sim/ENV_SPEC.md is stale: run `uv run --project sim/tools/vec python sim/tools/vec/tools/gen_env_spec.py`"


def test_every_column_is_described(gen, schema):
    block = gen.render(schema)    # raises SystemExit on any undescribed / unknown column
    for b in schema.buffers:
        assert f"`{b.name}`" in block
    assert schema.encoder_version in block


def test_render_rejects_drift(gen, schema, monkeypatch):
    d = dict(gen.DESCR)
    name, (row, cols) = "rest", d["rest"]
    monkeypatch.setitem(gen.DESCR, name, (row, cols[:1]))          # drop the description of `enabled`
    with pytest.raises(SystemExit, match="undescribed"):
        gen.render(schema)
    monkeypatch.setitem(gen.DESCR, name, (row, cols + [("no_such_col", "0/1", "x")]))
    with pytest.raises(SystemExit, match="not in the schema"):
        gen.render(schema)


def test_page_budget(gen):
    assert len(gen.SPEC.read_text(encoding="utf-8").splitlines()) <= 400


def test_check_cli(gen, capsys):
    assert gen.main(["--check"]) == 0
    assert "up to date" in capsys.readouterr().out
