"""Spawn and stop a Sts2Sim.Server process (--spawn). Builds nothing: the Release dll must exist."""

from __future__ import annotations

import os
import shutil
import signal
import socket
import subprocess
import time
import urllib.request
from pathlib import Path

DLL_REL = Path("sim/Sts2Sim.Server/bin/Release/net9.0/Sts2Sim.Server.dll")
BUILD_CMD = "dotnet build sim/Sts2Sim.sln -c Release"


def repo_root(start: Path | None = None) -> Path:
    p = (start or Path(__file__)).resolve()
    for d in [p, *p.parents]:
        if (d / "sim" / "Sts2Sim.sln").exists():
            return d
    raise FileNotFoundError("repo root (a directory containing sim/Sts2Sim.sln) not found above " + str(p))


def server_dll(root: Path | None = None) -> Path:
    return (root or repo_root()) / DLL_REL


def git_sha(root: Path | None = None) -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root or repo_root(), capture_output=True,
                             text=True, timeout=10)
        sha = out.stdout.strip()
        if out.returncode != 0 or not sha:
            return "unknown"
        dirty = subprocess.run(["git", "status", "--porcelain", "--untracked-files=no"], cwd=root or repo_root(),
                               capture_output=True, text=True, timeout=10).stdout.strip()
        return sha + ("-dirty" if dirty else "")
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class SpawnedServer:
    def __init__(self, out_dir: Path, port: int | None = None, root: Path | None = None):
        self.root = root or repo_root()
        self.dll = server_dll(self.root)
        if not self.dll.exists():
            raise FileNotFoundError(f"server dll not found: {self.dll}\nbuild it first: {BUILD_CMD}")
        if shutil.which("dotnet") is None:
            raise FileNotFoundError("dotnet not on PATH")
        self.out_dir = Path(out_dir)
        self.port = port or free_port()
        self.url = f"http://127.0.0.1:{self.port}"
        self.proc: subprocess.Popen | None = None
        self._log = None

    def start(self, wait_s: float = 60.0) -> "SpawnedServer":
        self.out_dir.mkdir(parents=True, exist_ok=True)
        self._log = open(self.out_dir / "server.log", "ab")
        cmd = ["dotnet", str(self.dll), "--port", str(self.port),
               "--crash-log", str((self.out_dir / "crashes.jsonl").resolve())]
        self.proc = subprocess.Popen(cmd, cwd=self.root, stdout=self._log, stderr=subprocess.STDOUT,
                                     stdin=subprocess.DEVNULL, start_new_session=True)
        deadline = time.monotonic() + wait_s
        while time.monotonic() < deadline:
            if self.proc.poll() is not None:
                raise RuntimeError(f"server exited with code {self.proc.returncode}; see {self.out_dir / 'server.log'}")
            try:
                with urllib.request.urlopen(self.url + "/health", timeout=2) as r:
                    if r.status == 200:
                        return self
            except OSError:
                pass
            time.sleep(0.2)
        self.stop()
        raise TimeoutError(f"server did not answer /health within {wait_s:.0f}s on {self.url}")

    def stop(self) -> None:
        p, self.proc = self.proc, None
        if p is not None and p.poll() is None:
            try:
                os.killpg(p.pid, signal.SIGINT)  # HttpHost handles Ctrl-C and stops cleanly
                p.wait(timeout=5)
            except (subprocess.TimeoutExpired, ProcessLookupError, PermissionError):
                try:
                    os.killpg(p.pid, signal.SIGKILL)
                except (ProcessLookupError, PermissionError):
                    pass
                try:
                    p.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    pass
        if self._log is not None:
            self._log.close()
            self._log = None

    def __enter__(self):
        return self.start()

    def __exit__(self, *exc):
        self.stop()
