#!/usr/bin/env bash
# headless_launch.sh — launch/stop one text-only STS2 worker.
#
# Launch:
#   headless_launch.sh <i> [--base DIR] [--wait-s N] [--no-wait] [--record]
# Stop:
#   headless_launch.sh <i|all> --stop [--base DIR]
#
# All workers execute one shared runtime.  HOME, MCP port, recorder root, log
# and PID are process-local.  Default training mode disables the full recorder;
# --record enables it for diagnostic/human-comparison runs.  There is never a
# display server or screenshot/keyframe path.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
FORBIDDEN_PORT=15526

usage() { sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }
log()   { printf '[launch] %s\n' "$*"; }
die()   { printf '[launch] ERROR: %s\n' "$*" >&2; exit 1; }

INSTANCE=""
BASE_DIR="$REPO_ROOT/headless-instances"
WAIT_S=120
MODE="launch"
RECORDING="${STS2_HEADLESS_RECORDING:-0}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base)    BASE_DIR="${2:?--base needs a value}"; shift 2 ;;
        --wait-s)  WAIT_S="${2:?--wait-s needs a value}"; shift 2 ;;
        --no-wait) WAIT_S=0; shift ;;
        --record)  RECORDING=1; shift ;;
        --stop)    MODE="stop"; shift ;;
        -h|--help) usage; exit 0 ;;
        *)         [[ -z "$INSTANCE" ]] || die "unexpected argument: $1"
                   INSTANCE="$1"; shift ;;
    esac
done
[[ -n "$INSTANCE" ]] || { usage; die "instance number required"; }

STOP_GRACE_S=15

stop_instance() {
    local inst_dir="$1"
    local pid_file="$inst_dir/pid"
    if [[ ! -f "$pid_file" ]]; then
        log "no pid file: $inst_dir"
        return 0
    fi
    local pid
    pid="$(tr -cd '0-9' < "$pid_file")"
    if [[ -z "$pid" ]] || ! kill -0 "$pid" 2>/dev/null; then
        log "stale pid file: $pid_file"
        unlink "$pid_file"
        return 0
    fi
    local command
    command="$(ps -p "$pid" -o command= 2>/dev/null || true)"
    if [[ "$command" != *"Slay the Spire 2"* || "$command" != *"--sts2-worker="* ]]; then
        die "pid $pid does not look like a sandbox worker; refusing to signal: $command"
    fi
    log "sending SIGTERM to worker pid $pid"
    kill -TERM "$pid"
    local waited=0
    while (( waited < STOP_GRACE_S * 10 )); do
        if ! kill -0 "$pid" 2>/dev/null; then
            unlink "$pid_file"
            log "worker stopped cleanly"
            return 0
        fi
        sleep 0.1
        waited=$((waited + 1))
    done
    log "worker did not stop in ${STOP_GRACE_S}s; sending SIGKILL"
    kill -KILL "$pid" 2>/dev/null || true
    unlink "$pid_file"
}

if [[ "$MODE" == "stop" ]]; then
    if [[ "$INSTANCE" == "all" ]]; then
        for inst_dir in "$BASE_DIR"/inst*/; do
            [[ -d "$inst_dir" ]] && stop_instance "${inst_dir%/}"
        done
    else
        [[ "$INSTANCE" =~ ^[0-9]+$ ]] || die "instance must be a number or all"
        stop_instance "$BASE_DIR/inst$INSTANCE"
    fi
    exit 0
fi

[[ "$INSTANCE" =~ ^[0-9]+$ && "$INSTANCE" -ge 1 ]] || die "instance must be positive"
INST_DIR="$BASE_DIR/inst$INSTANCE"
RUNTIME_APP="$BASE_DIR/runtime/SlayTheSpire2.app"
BIN="$RUNTIME_APP/Contents/MacOS/Slay the Spire 2"
INST_HOME="$INST_DIR/home"
GAME_LOG="$INST_DIR/game.log"
PID_FILE="$INST_DIR/pid"
GODOT_LOG="$INST_HOME/Library/Application Support/SlayTheSpire2/logs/godot.log"

[[ -d "$INST_DIR" ]] || die "instance missing: $INST_DIR — run headless_provision.sh"
[[ -x "$BIN" ]] || die "shared runtime executable missing: $BIN"
[[ -f "$INST_DIR/port" ]] || die "port file missing: $INST_DIR/port"
PORT="$(tr -cd '0-9' < "$INST_DIR/port")"
[[ "$PORT" != "$FORBIDDEN_PORT" ]] || die "refusing human-game port $FORBIDDEN_PORT"

if [[ -f "$PID_FILE" ]]; then
    old_pid="$(tr -cd '0-9' < "$PID_FILE")"
    if [[ -n "$old_pid" ]] && kill -0 "$old_pid" 2>/dev/null; then
        die "worker $INSTANCE already running as pid $old_pid"
    fi
    unlink "$PID_FILE"
fi

RECORDER_DISABLED=1
if [[ "$RECORDING" == "1" ]]; then
    RECORDER_DISABLED=0
fi
log "worker $INSTANCE: shared runtime, HOME=$INST_HOME, port=$PORT, recorder=$RECORDING"

nohup env \
    HOME="$INST_HOME" \
    STS2_MCP_PORT="$PORT" \
    STS2_RECORDER_OUTPUT_ROOT="$INST_DIR/recordings" \
    STS2_RECORDER_DISABLED="$RECORDER_DISABLED" \
    STS2_RECORDER_SNAPSHOT_MIN_INTERVAL_MS="${STS2_RECORDER_SNAPSHOT_MIN_INTERVAL_MS:-800}" \
    "$BIN" --headless --force-steam=off --sts2-worker="$INSTANCE" \
    >"$GAME_LOG" 2>&1 &
GAME_PID=$!
printf '%d\n' "$GAME_PID" > "$PID_FILE"
log "started pid $GAME_PID"

if [[ "$WAIT_S" -le 0 ]]; then
    exit 0
fi

log "waiting up to ${WAIT_S}s for text API on port $PORT"
deadline=$((SECONDS + WAIT_S))
while (( SECONDS < deadline )); do
    if ! kill -0 "$GAME_PID" 2>/dev/null; then
        unlink "$PID_FILE"
        tail -20 "$GAME_LOG" 2>/dev/null | sed 's/^/[launch]   game.log: /' || true
        die "worker exited before MCP became ready; inspect $GODOT_LOG"
    fi
    if curl -sS -o /dev/null --max-time 2 \
        "http://127.0.0.1:$PORT/api/v1/singleplayer" 2>/dev/null; then
        log "worker $INSTANCE UP: http://127.0.0.1:$PORT/api/v1/singleplayer"
        exit 0
    fi
    sleep 1
done

log "timeout waiting for worker $INSTANCE; pid $GAME_PID is still running"
log "inspect $GAME_LOG and $GODOT_LOG"
exit 3
