"""Gym-style environment wrapper over the STS2MCP HTTP API.

``Sts2Env`` talks to one game instance's STS2MCP mod (the same
``GET/POST /api/v1/singleplayer`` endpoint that ``scripts/level2_driver.py``
drives) and exposes ``observe`` / ``step`` / ``reset`` / ``is_alive``.
``launch_pool`` shells out to ``scripts/headless_provision.sh`` +
``scripts/headless_launch.sh`` to bring up N isolated headless processes over
one shared runtime.  Workers have independent HOME/ports/logs but no private
2.2 GiB app clone.  The provisioner builds a sandbox-only MCP binary which
disables optional Harmony UI patches that deadlock during Godot's headless mod
initialization and reads its port from the process environment.

Stdlib only (urllib/subprocess) — request/error patterns mirror
``scripts/level2_driver.py``.

reset() semantics (important): the singleplayer API exposes **no abandon
action** (abandon exists only in the multiplayer submenu — see
level2_driver.py's run_driver note against STS2MCP docs raw-full.md), so a
reset while a run is in progress raises ``Sts2EnvError`` instead of silently
doing nothing. From the main menu, reset drives
menu -> singleplayer -> standard -> character pick -> embark.
"""

from __future__ import annotations

import json
import subprocess
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .errors import Sts2RecError

DEFAULT_TIMEOUT_S = 10.0
DEFAULT_POOL_BASE_PORT = 15600  # instance i listens on base + i (provision script)
MAX_RAW_ECHO = 1000
_META_MENU_OPTIONS = frozenset(
    {"back", "confirm", "embark", "unready", "random", "quit", "settings"}
)


class Sts2EnvError(Sts2RecError):
    """HTTP/shape/protocol failure talking to STS2MCP.

    ``payload`` carries the raw response (decoded JSON dict or ``{"raw": str}``)
    so callers can inspect exactly what the API returned.
    """

    def __init__(self, message: str, payload: Any = None) -> None:
        super().__init__(message)
        self.payload = payload


@dataclass(frozen=True)
class Sts2EnvConfig:
    """Immutable connection + reset policy configuration for one instance."""

    port: int
    host: str = "127.0.0.1"
    timeout_s: float = DEFAULT_TIMEOUT_S
    reset_character: str = "IRONCLAD"
    reset_timeout_s: float = 60.0
    reset_poll_interval_s: float = 0.5

    @property
    def endpoint(self) -> str:
        return f"http://{self.host}:{self.port}/api/v1/singleplayer"


@dataclass(frozen=True)
class PoolConfig:
    """Immutable configuration for ``launch_pool``."""

    base_dir: Path
    scripts_dir: Path
    base_port: int = DEFAULT_POOL_BASE_PORT
    provision_timeout_s: float = 1800.0  # cp -R fallback of a large bundle is slow
    boot_timeout_s: float = 180.0
    env_config: Sts2EnvConfig = field(
        default_factory=lambda: Sts2EnvConfig(port=DEFAULT_POOL_BASE_PORT + 1)
    )


def _echo(raw: str) -> str:
    return raw[:MAX_RAW_ECHO] + ("..." if len(raw) > MAX_RAW_ECHO else "")


class Sts2Env:
    """One live STS2 instance seen through its STS2MCP HTTP endpoint."""

    def __init__(
        self,
        port: int | None = None,
        timeout: float = DEFAULT_TIMEOUT_S,
        *,
        config: Sts2EnvConfig | None = None,
    ) -> None:
        if config is None:
            if port is None:
                raise ValueError("Sts2Env requires either port= or config=")
            config = Sts2EnvConfig(port=port, timeout_s=timeout)
        elif port is not None and port != config.port:
            raise ValueError(
                f"conflicting ports: port={port} vs config.port={config.port}"
            )
        self.config = config

    # -- HTTP layer (mirrors scripts/level2_driver.py) -----------------------

    def _decode(self, raw: str, context: str) -> dict[str, Any]:
        try:
            decoded = json.loads(raw)
        except json.JSONDecodeError as error:
            raise Sts2EnvError(
                f"{context}: response is not JSON ({error}).\n"
                f"raw response: {_echo(raw)}\n"
                "Likely an STS2MCP version mismatch.",
                payload={"raw": _echo(raw)},
            ) from error
        if not isinstance(decoded, dict):
            raise Sts2EnvError(
                f"{context}: expected a JSON object, got {type(decoded).__name__}.\n"
                f"raw response: {_echo(raw)}",
                payload={"raw": _echo(raw)},
            )
        return decoded

    def observe(self) -> dict[str, Any]:
        """GET the current game state. Raises Sts2EnvError on any failure."""
        url = self.config.endpoint
        try:
            with urllib.request.urlopen(url, timeout=self.config.timeout_s) as response:
                raw = response.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as error:
            body = error.read().decode("utf-8", errors="replace")
            raise Sts2EnvError(
                f"GET {url} -> HTTP {error.code}.\nbody: {_echo(body)}\n"
                "HTTP 409 means a multiplayer run is active (SP-only API).",
                payload={"http_status": error.code, "raw": _echo(body)},
            ) from error
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            raise Sts2EnvError(
                f"GET {url} failed: {error}\n"
                "Is the instance running? scripts/headless_launch.sh health-checks "
                "this port; see <inst>/game.log and the instance godot.log.",
            ) from error
        return self._decode(raw, f"GET {url}")

    def step(self, action: dict[str, Any]) -> dict[str, Any]:
        """POST an action, then return the fresh post-action state.

        Raises Sts2EnvError (with the raw payload attached) when the transport
        fails, the HTTP status is not 200, or the API reports an error.
        """
        if not isinstance(action, dict) or "action" not in action:
            raise Sts2EnvError(
                f"step() needs an action dict with an 'action' key, got: {action!r}"
            )
        url = self.config.endpoint
        request = urllib.request.Request(
            url,
            data=json.dumps(action).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.config.timeout_s) as response:
                raw = response.read().decode("utf-8", errors="replace")
                status = response.status
        except urllib.error.HTTPError as error:
            raw = error.read().decode("utf-8", errors="replace")
            status = error.code
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            raise Sts2EnvError(
                f"POST {url} {json.dumps(action)} failed at transport level: {error}"
            ) from error
        try:
            decoded = self._decode(raw, f"POST {url}")
        except Sts2EnvError:
            decoded = {"raw": _echo(raw)}
        if status != 200 or decoded.get("status") != "ok":
            raise Sts2EnvError(
                f"POST {url} {json.dumps(action)} -> HTTP {status}, "
                f"API said: {_echo(json.dumps(decoded))}",
                payload=decoded,
            )
        return self.observe()

    def is_alive(self) -> bool:
        """True iff the endpoint answers a state GET with a JSON object."""
        try:
            self.observe()
        except Sts2EnvError:
            return False
        return True

    # -- reset ----------------------------------------------------------------

    def reset(self, character: str | None = None) -> dict[str, Any]:
        """Best-effort reset: drive menu -> embark, return the first run state.

        * In a run already: raises Sts2EnvError — the SP API cannot abandon a
          run (no abandon action outside the multiplayer submenu).
        * In the menu: walks main -> singleplayer -> standard ->
          character_select (picks ``character``/config default) -> embark and
          returns the first non-menu state.
        """
        wanted = (character or self.config.reset_character).upper()
        deadline = time.monotonic() + self.config.reset_timeout_s
        character_picked = False
        state = self.observe()
        # A completed episode is safe to advance back to the menu.  Active
        # rooms still cannot be abandoned through the public single-player API.
        while state.get("state_type") == "game_over" and time.monotonic() < deadline:
            try:
                state = self.step({"action": "proceed"})
            except Sts2EnvError:
                time.sleep(self.config.reset_poll_interval_s)
                state = self.observe()
            if state.get("state_type") == "game_over":
                time.sleep(self.config.reset_poll_interval_s)
        if state.get("state_type") not in ("menu", None):
            raise Sts2EnvError(
                "reset() called mid-run (state_type="
                f"{state.get('state_type')!r}) but the SP API has no abandon "
                "action; finish or lose the run first, or restart the instance "
                "via scripts/headless_launch.sh --stop && relaunch.",
                payload=state,
            )
        while time.monotonic() < deadline:
            state = self.observe()
            state_type = state.get("state_type")
            if state_type != "menu":
                return state  # embarked — first in-run state
            action, next_character_picked = self._decide_reset_menu(
                state, wanted, character_picked
            )
            if action is None:
                time.sleep(self.config.reset_poll_interval_s)
                continue
            try:
                self.step(action)
                character_picked = next_character_picked
                # ForceClick/menu transitions are queued on Godot's main
                # thread. Without a settle interval the next menu action can
                # overtake character selection (observed as NECROBINDER reset
                # embarking the default IRONCLAD).
                time.sleep(self.config.reset_poll_interval_s)
            except Sts2EnvError:
                # Menu may be animating; re-poll until the deadline.
                # Do not commit character_picked: a locked/not-yet-loaded
                # target can fail while the default IRONCLAD remains selected.
                time.sleep(self.config.reset_poll_interval_s)
        raise Sts2EnvError(
            f"reset() did not reach an in-run state within "
            f"{self.config.reset_timeout_s:.0f}s; last state: "
            f"{_echo(json.dumps(state))}",
            payload=state,
        )

    @staticmethod
    def _decide_reset_menu(
        state: dict[str, Any], character: str, character_picked: bool
    ) -> tuple[dict[str, Any] | None, bool]:
        """Menu policy for reset(); mirrors level2_driver.decide_menu."""
        screen = state.get("menu_screen", "main")
        names: list[str] = []
        for option in state.get("options") or []:
            if isinstance(option, str):
                names.append(option)
            elif isinstance(option, dict) and option.get("enabled", True):
                name = option.get("name")
                if isinstance(name, str):
                    names.append(name)
        lowered = [name.lower() for name in names]

        def select(option: str) -> dict[str, Any]:
            return {"action": "menu_select", "option": option}

        if screen == "main" and "singleplayer" in lowered:
            return select("singleplayer"), character_picked
        if screen == "singleplayer" and "standard" in lowered:
            return select("standard"), character_picked
        if screen == "character_select":
            if not character_picked:
                pickable = [n for n in names if n.lower() not in _META_MENU_OPTIONS]
                preferred = [n for n in pickable if character in n.upper()]
                # Character buttons stream in as assets finish loading. Never
                # fall back to the first visible button: early in that window
                # it is usually IRONCLAD, which silently defeated overrides
                # such as NECROBINDER.
                if preferred:
                    return select(preferred[0]), True
            if character_picked:
                for choice in ("embark", "confirm"):
                    if choice in lowered:
                        return select(choice), character_picked
        if screen == "tutorial_prompt" and "no" in lowered:
            return select("no"), character_picked
        if screen == "popup" and lowered:
            pick = "ignore" if "ignore" in lowered else lowered[0]
            return select(pick), character_picked
        if "back" in lowered:
            return select("back"), character_picked
        return None, character_picked


# -----------------------------------------------------------------------------
# Pool launcher
# -----------------------------------------------------------------------------


def _default_scripts_dir() -> Path:
    return Path(__file__).resolve().parents[2] / "scripts"


def _run_script(cmd: list[str], timeout_s: float, what: str) -> None:
    try:
        result = subprocess.run(
            cmd, capture_output=True, text=True, timeout=timeout_s, check=False
        )
    except (subprocess.TimeoutExpired, OSError) as error:
        raise Sts2EnvError(f"{what} failed to run ({cmd[0]}): {error}") from error
    if result.returncode != 0:
        raise Sts2EnvError(
            f"{what} exited {result.returncode}.\n"
            f"cmd: {' '.join(cmd)}\n"
            f"stdout: {_echo(result.stdout)}\n"
            f"stderr: {_echo(result.stderr)}"
        )


def launch_pool(
    n: int,
    base_dir: str | Path | None = None,
    *,
    config: PoolConfig | None = None,
    recording: bool = False,
) -> list[Sts2Env]:
    """Provision + launch ``n`` isolated headless instances; return their envs.

    Shells out to scripts/headless_provision.sh and scripts/headless_launch.sh.
    Launches are started non-blocking (--no-wait) and then health-polled here
    until every instance answers or ``boot_timeout_s`` elapses.  Full recorder
    hooks are disabled by default because compact text trajectories should be
    written by the training wrapper; set ``recording=True`` for diagnostics.

    Verified on macOS arm64 / game v0.107.1 with Steam disabled.  If an
    instance never answers, this raises Sts2EnvError pointing at
    <inst>/game.log and the instance godot.log.
    """
    if n < 1:
        raise ValueError(f"n must be >= 1, got {n}")
    if config is None:
        resolved_base = Path(
            base_dir
            if base_dir is not None
            else _default_scripts_dir().parent / "headless-instances"
        )
        config = PoolConfig(base_dir=resolved_base, scripts_dir=_default_scripts_dir())
    elif base_dir is not None and Path(base_dir) != config.base_dir:
        raise ValueError(
            f"conflicting base dirs: {base_dir} vs config.base_dir={config.base_dir}"
        )

    provision = config.scripts_dir / "headless_provision.sh"
    launch = config.scripts_dir / "headless_launch.sh"
    for script in (provision, launch):
        if not script.is_file():
            raise Sts2EnvError(f"required script missing: {script}")

    _run_script(
        ["bash", str(provision), str(n), str(config.base_dir)],
        config.provision_timeout_s,
        "headless_provision.sh",
    )
    for i in range(1, n + 1):
        launch_cmd = [
            "bash",
            str(launch),
            str(i),
            "--base",
            str(config.base_dir),
            "--no-wait",
        ]
        if recording:
            launch_cmd.append("--record")
        _run_script(
            launch_cmd,
            60.0,
            f"headless_launch.sh (instance {i})",
        )

    envs = [
        Sts2Env(
            config=Sts2EnvConfig(
                port=config.base_port + i,
                timeout_s=config.env_config.timeout_s,
                reset_character=config.env_config.reset_character,
                reset_timeout_s=config.env_config.reset_timeout_s,
                reset_poll_interval_s=config.env_config.reset_poll_interval_s,
            )
        )
        for i in range(1, n + 1)
    ]
    deadline = time.monotonic() + config.boot_timeout_s
    pending = dict(enumerate(envs, start=1))
    while pending and time.monotonic() < deadline:
        for i in [*pending]:
            if pending[i].is_alive():
                del pending[i]
        if pending:
            time.sleep(2.0)
    if pending:
        details = ", ".join(
            f"inst{i} (port {env.config.port})" for i, env in pending.items()
        )
        raise Sts2EnvError(
            "headless boot did not come up for "
            f"{details} within {config.boot_timeout_s:.0f}s — inspect "
            f"{config.base_dir}/inst<i>/game.log and "
            f"{config.base_dir}/inst<i>/home/Library/Application Support/"
            "SlayTheSpire2/logs/godot.log to see how far boot got, then stop "
            "strays with scripts/headless_launch.sh all --stop."
        )
    return envs


def stop_pool(
    base_dir: str | Path | None = None,
    *,
    scripts_dir: str | Path | None = None,
    timeout_s: float = 60.0,
) -> None:
    """Gracefully stop every worker using its process-local PID file."""
    resolved_base = Path(
        base_dir
        if base_dir is not None
        else _default_scripts_dir().parent / "headless-instances"
    )
    resolved_scripts = Path(scripts_dir) if scripts_dir else _default_scripts_dir()
    launch = resolved_scripts / "headless_launch.sh"
    if not launch.is_file():
        raise Sts2EnvError(f"required script missing: {launch}")
    _run_script(
        ["bash", str(launch), "all", "--stop", "--base", str(resolved_base)],
        timeout_s,
        "headless_launch.sh --stop",
    )


def recycle_worker(
    instance: int,
    base_dir: str | Path,
    *,
    scripts_dir: str | Path | None = None,
    clear_run: bool = False,
    recording: bool = False,
    boot_timeout_s: float = 180.0,
) -> Sts2Env:
    """Restart one worker, optionally discarding only its sandbox current run.

    ``clear_run`` is the recovery/reset primitive for timed-out policies.  It
    deletes ``current_run.save`` only below ``<base>/instN/home``; human saves
    and progress/prefs are outside the target set.  It is not an exact
    mid-combat checkpoint/restore mechanism.
    """
    if instance < 1:
        raise ValueError(f"instance must be >= 1, got {instance}")
    resolved_base = Path(base_dir).resolve()
    resolved_scripts = Path(scripts_dir) if scripts_dir else _default_scripts_dir()
    launch = resolved_scripts / "headless_launch.sh"
    inst_dir = resolved_base / f"inst{instance}"
    port_path = inst_dir / "port"
    if not launch.is_file():
        raise Sts2EnvError(f"required script missing: {launch}")
    if not port_path.is_file():
        raise Sts2EnvError(f"worker port file missing: {port_path}")
    _run_script(
        ["bash", str(launch), str(instance), "--stop", "--base", str(resolved_base)],
        60.0,
        f"stop worker {instance}",
    )
    if clear_run:
        home = (inst_dir / "home").resolve()
        if not home.is_dir() or resolved_base not in home.parents:
            raise Sts2EnvError(f"unsafe or missing sandbox HOME: {home}")
        for save in home.rglob("current_run.save"):
            resolved_save = save.resolve()
            if home not in resolved_save.parents:
                raise Sts2EnvError(f"refusing to delete save outside sandbox HOME: {save}")
            save.unlink()
    command = [
        "bash",
        str(launch),
        str(instance),
        "--base",
        str(resolved_base),
        "--no-wait",
    ]
    if recording:
        command.append("--record")
    _run_script(command, 60.0, f"launch worker {instance}")
    try:
        port = int(port_path.read_text(encoding="utf-8").strip())
    except (OSError, ValueError) as error:
        raise Sts2EnvError(f"invalid worker port file {port_path}: {error}") from error
    env = Sts2Env(port)
    deadline = time.monotonic() + boot_timeout_s
    while time.monotonic() < deadline:
        if env.is_alive():
            return env
        time.sleep(1.0)
    raise Sts2EnvError(
        f"recycled worker {instance} did not answer on port {port} within "
        f"{boot_timeout_s:.0f}s; inspect {inst_dir / 'game.log'}"
    )
