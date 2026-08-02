"""Unit tests for sts2rec.env against a stdlib http.server fake STS2MCP.

No real game: a ThreadingHTTPServer on an ephemeral port serves scripted
GET-state / POST-action responses shaped like STS2MCP's
/api/v1/singleplayer endpoint (same shapes scripts/level2_driver.py codes
against).
"""

from __future__ import annotations

import json
import threading
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

import pytest

from sts2rec.env import (
    Sts2Env,
    Sts2EnvConfig,
    Sts2EnvError,
    launch_pool,
    recycle_worker,
    stop_pool,
)


# -----------------------------------------------------------------------------
# Fake MCP server
# -----------------------------------------------------------------------------


@dataclass
class FakeMcp:
    """Scripted fake: GET pops from ``states`` (last one repeats), POST pops
    from ``post_responses`` (default ok) and records bodies in ``posted``."""

    states: list[Any] = field(default_factory=list)
    post_responses: list[tuple[int, Any]] = field(default_factory=list)
    posted: list[dict[str, Any]] = field(default_factory=list)

    def next_state(self) -> Any:
        if len(self.states) > 1:
            return self.states.pop(0)
        return self.states[0] if self.states else {"state_type": "menu"}

    def next_post_response(self) -> tuple[int, Any]:
        if self.post_responses:
            return self.post_responses.pop(0)
        return 200, {"status": "ok"}


def _make_handler(fake: FakeMcp) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args: Any) -> None:  # keep test output clean
            pass

        def _send(self, status: int, body: Any) -> None:
            raw = body.encode() if isinstance(body, str) else json.dumps(body).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def do_GET(self) -> None:
            state = fake.next_state()
            if isinstance(state, tuple):  # (status, body) override
                self._send(*state)
            else:
                self._send(200, state)

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            fake.posted.append(json.loads(self.rfile.read(length)))
            self._send(*fake.next_post_response())

    return Handler


@pytest.fixture
def fake_mcp():
    fake = FakeMcp()
    server = ThreadingHTTPServer(("127.0.0.1", 0), _make_handler(fake))
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    fake.port = server.server_address[1]  # type: ignore[attr-defined]
    yield fake
    server.shutdown()
    server.server_close()


def make_env(fake: FakeMcp, **overrides: Any) -> Sts2Env:
    config = Sts2EnvConfig(
        port=fake.port,  # type: ignore[attr-defined]
        timeout_s=overrides.pop("timeout_s", 5.0),
        **overrides,
    )
    return Sts2Env(config=config)


# -----------------------------------------------------------------------------
# construction
# -----------------------------------------------------------------------------


class TestConstruction:
    def test_requires_port_or_config(self) -> None:
        with pytest.raises(ValueError, match="port"):
            Sts2Env()

    def test_conflicting_ports_rejected(self) -> None:
        with pytest.raises(ValueError, match="conflicting"):
            Sts2Env(port=1234, config=Sts2EnvConfig(port=5678))

    def test_positional_port(self) -> None:
        env = Sts2Env(15601, timeout=3.0)
        assert env.config.port == 15601
        assert env.config.timeout_s == 3.0
        assert env.config.endpoint == "http://127.0.0.1:15601/api/v1/singleplayer"


# -----------------------------------------------------------------------------
# observe
# -----------------------------------------------------------------------------


class TestObserve:
    def test_returns_state_dict(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [{"state_type": "map", "run": {"floor": 3}}]
        state = make_env(fake_mcp).observe()
        assert state == {"state_type": "map", "run": {"floor": 3}}

    def test_non_json_raises_with_raw_payload(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [(200, "<html>boom</html>")]
        with pytest.raises(Sts2EnvError, match="not JSON") as excinfo:
            make_env(fake_mcp).observe()
        assert excinfo.value.payload == {"raw": "<html>boom</html>"}

    def test_non_object_json_raises(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [(200, "[1, 2]")]
        with pytest.raises(Sts2EnvError, match="expected a JSON object"):
            make_env(fake_mcp).observe()

    def test_http_error_raises_with_status(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [(409, json.dumps({"error": "multiplayer active"}))]
        with pytest.raises(Sts2EnvError, match="HTTP 409") as excinfo:
            make_env(fake_mcp).observe()
        assert excinfo.value.payload["http_status"] == 409

    def test_connection_refused_raises(self) -> None:
        env = Sts2Env(config=Sts2EnvConfig(port=1, timeout_s=0.5))
        with pytest.raises(Sts2EnvError, match="failed"):
            env.observe()


# -----------------------------------------------------------------------------
# step
# -----------------------------------------------------------------------------


class TestStep:
    def test_ok_posts_and_returns_new_state(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [{"state_type": "rewards"}]
        new_state = make_env(fake_mcp).step({"action": "end_turn"})
        assert fake_mcp.posted == [{"action": "end_turn"}]
        assert new_state == {"state_type": "rewards"}

    def test_api_error_status_raises_with_payload(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.post_responses = [
            (200, {"status": "error", "message": "card not playable"})
        ]
        with pytest.raises(Sts2EnvError, match="card not playable") as excinfo:
            make_env(fake_mcp).step({"action": "play_card", "card_index": 0})
        assert excinfo.value.payload == {
            "status": "error",
            "message": "card not playable",
        }

    def test_http_400_raises_with_payload(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.post_responses = [(400, {"status": "error", "message": "bad index"})]
        with pytest.raises(Sts2EnvError, match="HTTP 400") as excinfo:
            make_env(fake_mcp).step({"action": "claim_reward", "index": 99})
        assert excinfo.value.payload["message"] == "bad index"

    def test_non_json_post_response_raises_with_raw(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.post_responses = [(500, "internal blowup")]
        with pytest.raises(Sts2EnvError, match="HTTP 500") as excinfo:
            make_env(fake_mcp).step({"action": "proceed"})
        assert excinfo.value.payload == {"raw": "internal blowup"}

    def test_invalid_action_shape_rejected_locally(self, fake_mcp: FakeMcp) -> None:
        with pytest.raises(Sts2EnvError, match="'action' key"):
            make_env(fake_mcp).step({"card_index": 0})
        assert fake_mcp.posted == []

    def test_transport_failure_raises(self) -> None:
        env = Sts2Env(config=Sts2EnvConfig(port=1, timeout_s=0.5))
        with pytest.raises(Sts2EnvError, match="transport"):
            env.step({"action": "proceed"})


# -----------------------------------------------------------------------------
# is_alive
# -----------------------------------------------------------------------------


class TestIsAlive:
    def test_true_when_serving(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [{"state_type": "menu"}]
        assert make_env(fake_mcp).is_alive() is True

    def test_false_when_down(self) -> None:
        env = Sts2Env(config=Sts2EnvConfig(port=1, timeout_s=0.5))
        assert env.is_alive() is False

    def test_false_on_garbage_response(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [(200, "not json")]
        assert make_env(fake_mcp).is_alive() is False


# -----------------------------------------------------------------------------
# reset
# -----------------------------------------------------------------------------


def menu(screen: str, options: list[str]) -> dict[str, Any]:
    return {"state_type": "menu", "menu_screen": screen, "options": options}


class TestReset:
    def test_mid_run_raises(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [{"state_type": "monster"}]
        with pytest.raises(Sts2EnvError, match="no abandon") as excinfo:
            make_env(fake_mcp).reset()
        assert excinfo.value.payload == {"state_type": "monster"}
        assert fake_mcp.posted == []

    def test_completed_run_can_proceed_to_menu_and_reset(self, fake_mcp: FakeMcp) -> None:
        game_over = {"state_type": "game_over", "game_over": {"win": True}}
        main = menu("main", ["singleplayer"])
        sp = menu("singleplayer", ["standard"])
        chars = menu("character_select", ["Ironclad", "embark"])
        fake_mcp.states = [
            game_over,  # reset pre-check -> POST proceed
            main,       # proceed step observe
            main,       # menu loop -> singleplayer
            sp,         # step observe
            sp,         # loop -> standard
            chars,      # step observe
            chars,      # loop -> character
            chars,      # step observe
            chars,      # loop -> embark
            {"state_type": "map", "map": {"next_options": []}},
            {"state_type": "map", "map": {"next_options": []}},
        ]
        result = make_env(fake_mcp, reset_poll_interval_s=0.001).reset()
        assert result["state_type"] == "map"
        assert fake_mcp.posted[0] == {"action": "proceed"}

    def test_drives_menu_to_embark(self, fake_mcp: FakeMcp) -> None:
        main = menu("main", ["singleplayer", "multiplayer", "quit"])
        sp = menu("singleplayer", ["standard", "back"])
        chars = menu("character_select", ["Ironclad", "Silent", "embark", "back"])
        # Each successful step() consumes one extra GET (its post-action
        # observe), so every screen appears twice in the script.
        fake_mcp.states = [
            main,  # reset() pre-check
            main,  # loop poll        -> POST singleplayer
            sp,  # step's observe
            sp,  # loop poll          -> POST standard
            chars,  # step's observe
            chars,  # loop poll       -> POST Ironclad
            chars,  # step's observe
            chars,  # loop poll       -> POST embark
            {"state_type": "map", "run": {"floor": 0}},  # step's observe
            {"state_type": "map", "run": {"floor": 0}},  # loop poll -> return
        ]
        state = make_env(fake_mcp).reset()
        assert state["state_type"] == "map"
        assert [p["option"] for p in fake_mcp.posted] == [
            "singleplayer",
            "standard",
            "Ironclad",
            "embark",
        ]

    def test_character_override(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [
            menu("character_select", ["Ironclad", "Silent", "embark"]),
            menu("character_select", ["Ironclad", "Silent", "embark"]),
            menu("character_select", ["Ironclad", "Silent", "embark"]),
            {"state_type": "map"},
        ]
        make_env(fake_mcp).reset(character="SILENT")
        assert fake_mcp.posted[0]["option"] == "Silent"

    def test_waits_for_requested_character_instead_of_early_fallback(
        self, fake_mcp: FakeMcp
    ) -> None:
        early = menu("character_select", ["Ironclad", "embark"])
        ready = menu("character_select", ["Ironclad", "Necrobinder", "embark"])
        fake_mcp.states = [early, early, ready, ready, ready, {"state_type": "map"}]
        make_env(fake_mcp, reset_poll_interval_s=0.01).reset(character="NECROBINDER")
        assert fake_mcp.posted[0]["option"] == "Necrobinder"

    def test_object_options_and_disabled_filtering(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [
            menu("main", []),
            {
                "state_type": "menu",
                "menu_screen": "main",
                "options": [
                    {"name": "singleplayer", "enabled": False},
                    {"name": "back", "enabled": True},
                ],
            },
            {"state_type": "map"},
        ]
        make_env(fake_mcp).reset()
        # singleplayer disabled -> falls through to "back"
        assert fake_mcp.posted[0]["option"] == "back"

    def test_times_out_when_stuck_in_menu(self, fake_mcp: FakeMcp) -> None:
        fake_mcp.states = [menu("unknown_screen", [])]
        env = Sts2Env(
            config=Sts2EnvConfig(
                port=fake_mcp.port,  # type: ignore[attr-defined]
                timeout_s=5.0,
                reset_timeout_s=0.4,
                reset_poll_interval_s=0.05,
            )
        )
        with pytest.raises(Sts2EnvError, match="did not reach"):
            env.reset()


# -----------------------------------------------------------------------------
# launch_pool (script plumbing only — no game, stub scripts)
# -----------------------------------------------------------------------------


class TestLaunchPool:
    def test_rejects_bad_n(self) -> None:
        with pytest.raises(ValueError, match="n must be"):
            launch_pool(0)

    def test_missing_scripts_raise(self, tmp_path) -> None:
        from sts2rec.env import PoolConfig

        config = PoolConfig(
            base_dir=tmp_path / "instances", scripts_dir=tmp_path / "nowhere"
        )
        with pytest.raises(Sts2EnvError, match="required script missing"):
            launch_pool(1, config=config)

    def test_provision_failure_surfaces_output(self, tmp_path) -> None:
        from sts2rec.env import PoolConfig

        scripts = tmp_path / "scripts"
        scripts.mkdir()
        (scripts / "headless_provision.sh").write_text(
            "#!/bin/bash\necho boom-stdout\necho boom-stderr >&2\nexit 7\n"
        )
        (scripts / "headless_launch.sh").write_text("#!/bin/bash\nexit 0\n")
        config = PoolConfig(base_dir=tmp_path / "instances", scripts_dir=scripts)
        with pytest.raises(Sts2EnvError, match="exited 7") as excinfo:
            launch_pool(1, config=config)
        assert "boom-stderr" in str(excinfo.value)

    def test_boot_timeout_raises_diagnostic_message(
        self, tmp_path, fake_mcp: FakeMcp
    ) -> None:
        from sts2rec.env import PoolConfig

        scripts = tmp_path / "scripts"
        scripts.mkdir()
        (scripts / "headless_provision.sh").write_text("#!/bin/bash\nexit 0\n")
        (scripts / "headless_launch.sh").write_text("#!/bin/bash\nexit 0\n")
        config = PoolConfig(
            base_dir=tmp_path / "instances",
            scripts_dir=scripts,
            base_port=1,  # port 2: nothing listens there
            boot_timeout_s=0.5,
        )
        with pytest.raises(Sts2EnvError, match="headless boot did not come up"):
            launch_pool(1, config=config)

    def test_pool_returns_envs_when_ports_answer(
        self, tmp_path, fake_mcp: FakeMcp
    ) -> None:
        from sts2rec.env import PoolConfig

        fake_mcp.states = [{"state_type": "menu"}]
        scripts = tmp_path / "scripts"
        scripts.mkdir()
        provision = scripts / "headless_provision.sh"
        provision.write_text(
            '#!/bin/bash\necho "$@" > "$(dirname "$0")/provision_args.txt"\nexit 0\n'
        )
        (scripts / "headless_launch.sh").write_text("#!/bin/bash\nexit 0\n")
        port = fake_mcp.port  # type: ignore[attr-defined]
        config = PoolConfig(
            base_dir=tmp_path / "instances",
            scripts_dir=scripts,
            base_port=port - 1,  # instance 1 -> exactly the fake's port
            boot_timeout_s=5.0,
        )
        envs = launch_pool(1, config=config)
        assert len(envs) == 1
        assert envs[0].config.port == port
        assert envs[0].is_alive()

    def test_recording_flag_is_forwarded_to_launch(self, tmp_path, fake_mcp: FakeMcp) -> None:
        from sts2rec.env import PoolConfig

        fake_mcp.states = [{"state_type": "menu"}]
        scripts = tmp_path / "scripts"
        scripts.mkdir()
        (scripts / "headless_provision.sh").write_text("#!/bin/bash\nexit 0\n")
        launch = scripts / "headless_launch.sh"
        launch.write_text(
            '#!/bin/bash\necho "$@" >> "$(dirname "$0")/launch_args.txt"\nexit 0\n'
        )
        port = fake_mcp.port  # type: ignore[attr-defined]
        config = PoolConfig(
            base_dir=tmp_path / "instances",
            scripts_dir=scripts,
            base_port=port - 1,
            boot_timeout_s=2.0,
        )
        launch_pool(1, config=config, recording=True)
        assert "--record" in (scripts / "launch_args.txt").read_text()

    def test_stop_pool_uses_pid_safe_stop_script(self, tmp_path) -> None:
        scripts = tmp_path / "scripts"
        scripts.mkdir()
        launch = scripts / "headless_launch.sh"
        launch.write_text(
            '#!/bin/bash\necho "$@" > "$(dirname "$0")/stop_args.txt"\nexit 0\n'
        )
        stop_pool(tmp_path / "instances", scripts_dir=scripts)
        args = (scripts / "stop_args.txt").read_text()
        assert args.startswith("all --stop --base")

    def test_recycle_worker_clears_only_sandbox_run(
        self, tmp_path, fake_mcp: FakeMcp
    ) -> None:
        fake_mcp.states = [{"state_type": "menu"}]
        scripts = tmp_path / "scripts"
        scripts.mkdir()
        launch = scripts / "headless_launch.sh"
        launch.write_text(
            '#!/bin/bash\necho "$@" >> "$(dirname "$0")/recycle_args.txt"\nexit 0\n'
        )
        inst = tmp_path / "instances" / "inst1"
        saves = inst / "home" / "Library" / "Application Support" / "SlayTheSpire2" / "default" / "1" / "modded" / "profile1" / "saves"
        saves.mkdir(parents=True)
        current_run = saves / "current_run.save"
        progress = saves / "progress.save"
        current_run.write_text("sandbox run")
        progress.write_text("keep me")
        (inst / "port").write_text(str(fake_mcp.port))  # type: ignore[attr-defined]
        env = recycle_worker(
            1,
            tmp_path / "instances",
            scripts_dir=scripts,
            clear_run=True,
            boot_timeout_s=2.0,
        )
        assert env.is_alive()
        assert not current_run.exists()
        assert progress.read_text() == "keep me"
        commands = (scripts / "recycle_args.txt").read_text().splitlines()
        assert "--stop" in commands[0]
        assert "--no-wait" in commands[1]
