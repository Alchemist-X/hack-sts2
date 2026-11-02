#!/usr/bin/env bash
# Build and run the Sts2Sim play server in the background.
#
#   scripts/sim_server.sh            build Release, start (nohup -> tmp/sim-server.log), wait for /health
#   scripts/sim_server.sh stop       stop the server started by this script
#   scripts/sim_server.sh restart    stop + start
#   scripts/sim_server.sh status     pid and /health
#
# Env: SIM_PLAY_PORT (default 8765), SIM_SERVER_PROJECT (path to the server .csproj; auto-detected
# under sim/ otherwise), SIM_SERVER_WAIT (seconds to wait for /health, default 60).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${SIM_PLAY_PORT:-8765}"
TMP="$ROOT/tmp"
PIDFILE="$TMP/sim-server.pid"
LOG="$TMP/sim-server.log"
URL="http://127.0.0.1:$PORT"
WAIT="${SIM_SERVER_WAIT:-60}"

find_project() {
  if [[ -n "${SIM_SERVER_PROJECT:-}" ]]; then
    echo "$SIM_SERVER_PROJECT"; return
  fi
  local p
  for p in "$ROOT"/sim/Sts2Sim.Server/*.csproj "$ROOT"/sim/Sts2Sim.Play/*.csproj; do
    [[ -f "$p" ]] && { echo "$p"; return; }
  done
  p="$(find "$ROOT/sim" -maxdepth 2 \( -name '*Server*.csproj' -o -name '*Play*.csproj' \) | head -n1)"
  [[ -n "$p" ]] && { echo "$p"; return; }
  echo "error: no server project found under sim/ (set SIM_SERVER_PROJECT)" >&2
  return 1
}

# The pidfile holds the launched pid and, once healthy, the listening pid: some dotnet
# launchers (e.g. Homebrew's) are wrappers that do not exec the real host.
running_pid() {
  [[ -f "$PIDFILE" ]] || return 1
  local pid alive=()
  for pid in $(cat "$PIDFILE"); do kill -0 "$pid" 2>/dev/null && alive+=("$pid") || true; done
  [[ ${#alive[@]} -gt 0 ]] && { echo "${alive[*]}"; return 0; }
  rm -f "$PIDFILE"; return 1
}

listener_pid() { lsof -nP -t -iTCP:"$PORT" -sTCP:LISTEN 2>/dev/null | head -n1 || true; }

healthy() { curl -sf --max-time 2 "$URL/health" >/dev/null 2>&1; }

start() {
  mkdir -p "$TMP"
  local pid
  if pid="$(running_pid)"; then
    echo "already running: pid $pid ($URL)"; healthy && curl -s "$URL/health" && echo; return 0
  fi
  if healthy; then
    echo "error: something else already answers on $URL; set SIM_PLAY_PORT" >&2; return 1
  fi
  local proj name dir dll
  proj="$(find_project)"
  name="$(basename "$proj" .csproj)"
  dir="$(dirname "$proj")"
  echo "building $name (Release)..."
  dotnet build "$proj" -c Release -nologo -v quiet >"$TMP/sim-server-build.log" 2>&1 || {
    tail -n 40 "$TMP/sim-server-build.log" >&2; echo "error: build failed (tmp/sim-server-build.log)" >&2; return 1; }
  dll="$(find "$dir/bin/Release" -name "$name.dll" -path '*/net*' | head -n1)"
  [[ -n "$dll" ]] || { echo "error: $name.dll not found under $dir/bin/Release" >&2; return 1; }

  local args=()
  [[ "$PORT" != "8765" ]] && args=(--port "$PORT")
  echo "starting $dll on $URL (log: tmp/sim-server.log)"
  (cd "$ROOT" && SIM_PLAY_PORT="$PORT" SIM_VERSION="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)" \
    nohup dotnet "$dll" ${args[@]+"${args[@]}"} >"$LOG" 2>&1 </dev/null &
    echo $! >"$PIDFILE")
  pid="$(cat "$PIDFILE")"
  echo "pid $pid"

  local i
  for ((i = 0; i < WAIT * 4; i++)); do
    if healthy; then
      local lp; lp="$(listener_pid)"
      if [[ -n "$lp" && "$lp" != "$pid" ]]; then echo "$pid $lp" >"$PIDFILE"; echo "listener pid $lp"; fi
      curl -s "$URL/health"; echo; return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      echo "error: server exited during startup; last log lines:" >&2; tail -n 30 "$LOG" >&2
      rm -f "$PIDFILE"; return 1
    fi
    sleep 0.25
  done
  echo "error: no /health answer after ${WAIT}s (pid $pid still running; see tmp/sim-server.log)" >&2
  return 1
}

stop() {
  local pids p i any
  if ! pids="$(running_pid)"; then echo "not running"; return 0; fi
  for p in $pids; do kill "$p" 2>/dev/null || true; done
  for ((i = 0; i < 40; i++)); do
    any=0
    for p in $pids; do kill -0 "$p" 2>/dev/null && any=1; done
    [[ $any -eq 0 ]] && break
    sleep 0.25
  done
  for p in $pids; do kill -0 "$p" 2>/dev/null && kill -9 "$p" 2>/dev/null || true; done
  rm -f "$PIDFILE"
  echo "stopped pid $pids"
}

status() {
  local pid
  if pid="$(running_pid)"; then echo "running: pid $pid"; else echo "not running (no pidfile)"; fi
  if healthy; then curl -s "$URL/health"; echo; else echo "no /health answer on $URL"; fi
}

case "${1:-start}" in
  start) start ;;
  stop) stop ;;
  restart) stop; start ;;
  status) status ;;
  *) echo "usage: $0 [start|stop|restart|status]" >&2; exit 2 ;;
esac
