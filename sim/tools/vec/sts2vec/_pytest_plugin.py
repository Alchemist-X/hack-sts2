"""pytest plugin (entry point ``pytest11``): keep ``uv run --project sim/tools/vec pytest`` scoped to this package.

When pytest is started from a directory above ``sim/tools/vec`` without paths (e.g. the repository root, as in the
gate command), it finds no config file and would collect every ``test_*.py`` in the repository — including other uv
projects' tests whose packages are not installed in this environment. In exactly that case (no config file found, no
paths given) this plugin limits collection to ``sim/tools/vec``. Any explicit path, or running inside the project, is
left alone.
"""

from __future__ import annotations

from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]


def pytest_configure(config):
    # registered here as well as in pyproject.toml, which is not read when pytest starts above the project
    config.addinivalue_line("markers", "full: needs the V1 encoder and V2 env-core (skip reason 'needs V1/V2')")
    config.addinivalue_line("markers", "slow: long-running (soak / multi-process)")


def pytest_ignore_collect(collection_path, config):
    if config.inipath is not None:
        return None
    source = getattr(config, "args_source", None)
    if source is None or source.name != "INVOCATION_DIR":
        return None
    p = Path(collection_path).resolve()
    if p == PROJECT or PROJECT in p.parents or p in PROJECT.parents:
        return None
    return True
