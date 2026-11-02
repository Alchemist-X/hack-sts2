"""pytest plugin (entry point ``pytest11``): keep ``uv run --project sim/tools/train pytest`` scoped to this package.

Same rule as ``sts2vec._pytest_plugin``: when pytest starts above ``sim/tools/train`` (e.g. the repository root, as in
the gate command) without paths and finds no config file, collection is limited to ``sim/tools/train``. Explicit paths,
running inside the project, or an interpreter from another uv project's environment are left alone.
"""

from __future__ import annotations

import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]


def _other_project_env() -> bool:
    env_parent = Path(sys.prefix).resolve().parent
    return env_parent != PROJECT and (env_parent / "pyproject.toml").exists()


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
