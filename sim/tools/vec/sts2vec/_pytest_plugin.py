"""pytest plugin (entry point ``pytest11``): keep ``uv run --project sim/tools/vec pytest`` scoped to this package.

When pytest is started from a directory above ``sim/tools/vec`` without paths (e.g. the repository root, as in the
gate command), it finds no config file and would collect every ``test_*.py`` in the repository — including other uv
projects' tests whose packages are not installed in this environment. In exactly that case (no config file found, no
paths given) this plugin limits collection to ``sim/tools/vec``. Any explicit path, or running inside the project, is
left alone. When the interpreter belongs to another uv project's environment (a project that depends on sts2vec, e.g.
sim/tools/train, whose `.venv` sits next to its own pyproject.toml), the plugin stays out of the way so that project's
own scoping applies.
"""

from __future__ import annotations

import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]


def _other_project_env() -> bool:
    env_parent = Path(sys.prefix).resolve().parent
    return env_parent != PROJECT and (env_parent / "pyproject.toml").exists()


def pytest_configure(config):
    # registered here as well as in pyproject.toml, which is not read when pytest starts above the project
    config.addinivalue_line("markers", "full: needs the V1 encoder and V2 env-core (skip reason 'needs V1/V2')")
    config.addinivalue_line("markers", "slow: long-running (soak / multi-process)")


def pytest_ignore_collect(collection_path, config):
    if config.inipath is not None or _other_project_env():
        return None
    source = getattr(config, "args_source", None)
    if source is None or source.name != "INVOCATION_DIR":
        return None
    p = Path(collection_path).resolve()
    if p == PROJECT or PROJECT in p.parents or p in PROJECT.parents:
        return None
    return True
