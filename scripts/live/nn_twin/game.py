"""What the runner needs from "the game" -- the real game (``mcp.McpGame``) or its dry-run stand-in
(``standin.PlayServerGame``)."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .semantics import Act, View


@dataclass
class Observation:
    raw: dict                       # the side's native state (MCP state / play-server observation)
    view: View
    acts: list[Act]
    settled: bool = True
    ms: float = 0.0                 # time spent waiting for a settled frame
    info: dict = field(default_factory=dict)


@dataclass
class ExecResult:
    ok: bool
    wire: Any = None                # what was sent (MCP JSON body / token)
    error: str | None = None
    ms: float = 0.0
    notes: list[str] = field(default_factory=list)


class GameError(RuntimeError):
    pass


class Game:
    """Interface. `observe` returns a settled decision frame; `execute` runs one act and waits for its outcome."""

    name = "game"
    supports_saves = False

    def observe(self) -> Observation:
        raise NotImplementedError

    def execute(self, act: Act) -> ExecResult:
        raise NotImplementedError

    def run_info(self) -> dict:
        """{"seed", "character", "ascension"} of the run in progress, as far as the game can tell."""
        return {}

    def latest_save(self) -> Path | None:
        """The game's current_run.save (real game only)."""
        return None

    def result(self) -> dict:
        return {}

    def close(self) -> None:
        pass
