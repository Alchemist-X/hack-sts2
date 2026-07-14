#!/usr/bin/env bash
# headless_launch.sh — launch (or stop) one provisioned headless STS2 instance.
#
# Launch:  headless_launch.sh <i> [--base DIR] [--wait-s N] [--no-wait]
# Stop:    headless_launch.sh <i> --stop [--base DIR]
#          headless_launch.sh all --stop [--base DIR]
#
# What launching does:
#   HOME=<base>/inst<i>/home exec of the CLONED bundle's binary with
#     --headless        Godot 4 headless DisplayServer. The game code is
#                       headless-aware: Logger.cs checks for "--headless",
#                       NGame.InitializeGraphicsPreferences and
#                       NBackgroundModeHandler skip display work when
#                       DisplayServer.GetName() == "headless".
#     --force-steam=off Skips SteamAPI init entirely (NGame.InitializePlatform:
#                       value "off" short-circuits before SteamInitializer.
#                       Initialize). Without this, a failed Steam init shows a
#                       modal error popup and quits — fatal for headless.
#   stdout/stderr -> <base>/inst<i>/game.log
#   then waits for the instance's STS2MCP port to answer HTTP.
#
# Game-understood CLI args (v0.107.1 decompile, Core/Helpers/CommandLineHelper.cs
# — parses "--key=value" / "--key value" / bare "--key" from OS.GetCmdlineArgs):
#   --nomods              skip mod loading entirely (ModManager.Initialize)
#   --force-steam[=on|off] force/skip Steam init (NGame.InitializePlatform)
#   --autoslay            built-in autoplayer — DEAD in release builds
#                         (gated on !IsReleaseGame(), which returns true)
#   --seed <s>            seed for autoslay
#   --log-file <p>        autoslay log destination
#   --bootstrap           debug scene bootstrapper (NGame)
#   --fastmp[=...]        multiplayer test fast-path
#   --clientId <n>        local player id when Steam is off
#                         (NullPlatformUtilStrategy — also picks the
#                         "default/<n>/" save dir; provisioning seeds n=1)
#   --force-sentry        force Sentry reporting on
#   +connect_lobby <id>   Steam lobby join
#
# ============================================================================
# LOUD WARNING — FIRST LIVE RUN IS EXPECTED TO NEED ITERATION
# ============================================================================
# Steam will NOT be running under the isolated HOME. Consequences:
#   * With --force-steam=off the decompile says Steam init is skipped cleanly
#     and saves fall back to <home>/.../SlayTheSpire2/default/1/ — but this is
#     UNVERIFIED against the real binary (possible Steam DRM stub, missing
#     steam_appid.txt behavior, etc.).
#   * FMOD/audio device behavior in --headless is UNVERIFIED; audio init could
#     hang or crash the boot.
#   * The consent popup path (NConfirmModLoadingPopup) needs a renderer; we
#     pre-seed consent so it *should* never appear headless — UNVERIFIED.
# Diagnose a failed boot in this order:
#   1. <base>/inst<i>/game.log                      (process stdout/stderr)
#   2. <base>/inst<i>/home/Library/Application Support/SlayTheSpire2/logs/
#      godot.log                                    (game's own log — shows how
#                                                    far boot got: Steam skip
#                                                    line, "RUNNING MODDED!",
#                                                    "[STS2 MCP] ... started")
#   3. curl -sS http://127.0.0.1:<port>/api/v1/singleplayer
# ============================================================================
#
# SAFETY: refuses to touch the real bundle, and refuses port 15526 (the real,
# currently-running game instance).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

FORBIDDEN_PORT=15526
REAL_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"

usage() { sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }
log()   { printf '[launch] %s\n' "$*"; }
die()   { printf '[launch] ERROR: %s\n' "$*" >&2; exit 1; }

INSTANCE=""
BASE_DIR="$REPO_ROOT/headless-instances"
WAIT_S=120
MODE="launch"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base)    BASE_DIR="${2:?--base needs a value}"; shift 2 ;;
        --wait-s)  WAIT_S="${2:?--wait-s needs a value}"; shift 2 ;;
        --no-wait) WAIT_S=0; shift ;;
        --stop)    MODE="stop"; shift ;;
        -h|--help) usage; exit 0 ;;
        *)         [[ -z "$INSTANCE" ]] || die "unexpected argument: $1"
                   INSTANCE="$1"; shift ;;
    esac
done
[[ -n "$INSTANCE" ]] || { usage; die "instance number (or 'all' with --stop) required"; }

# --------------------------------------------------------------------------
# --stop mode: graceful shutdown by the instance's unique clone path.
#
# SIGTERM first: Godot treats it as a quit request, so the game runs its
# normal shutdown path and the recorder's CleanUp/Dispose flushes the JSONL
# streams + manifest (no torn final line). Only if the process is still
# alive after STOP_GRACE_S seconds do we escalate to SIGKILL, which can
# leave the recorder's in-flight line torn (manifest stays incomplete=true;
# `sts2rec validate` tolerates that torn final line as a warning).
# --------------------------------------------------------------------------
STOP_GRACE_S=15

stop_instance() {
    local inst_dir="$1"
    local pattern="$inst_dir/SlayTheSpire2.app"
    local pids
    pids="$(pgrep -f "$pattern" 2>/dev/null || true)"
    if [[ -z "$pids" ]]; then
        log "no running process matched $pattern"
        return 0
    fi

    log "sending SIGTERM to pid(s) ${pids//$'\n'/ } (graceful quit; recorder flushes on Dispose)"
    # shellcheck disable=SC2086  # pids is intentionally word-split
    kill -TERM $pids 2>/dev/null || true

    local waited=0
    while (( waited < STOP_GRACE_S * 10 )); do
        if ! pgrep -f "$pattern" >/dev/null 2>&1; then
            log "STOPPED via SIGTERM after ~$((waited / 10))s: $pattern"
            return 0
        fi
        sleep 0.1
        waited=$((waited + 1))
    done

    log "still alive after ${STOP_GRACE_S}s; escalating to SIGKILL: $pattern"
    pkill -9 -f "$pattern" 2>/dev/null || true
    sleep 0.5
    if pgrep -f "$pattern" >/dev/null 2>&1; then
        log "WARNING: process(es) still present after SIGKILL: $pattern"
        return 1
    fi
    log "STOPPED via SIGKILL (recorder flush did NOT run; expect a torn final JSONL line): $pattern"
}

if [[ "$MODE" == "stop" ]]; then
    if [[ "$INSTANCE" == "all" ]]; then
        for inst_dir in "$BASE_DIR"/inst*/; do
            [[ -d "$inst_dir" ]] && stop_instance "${inst_dir%/}"
        done
    else
        [[ "$INSTANCE" =~ ^[0-9]+$ ]] || die "instance must be a number or 'all'"
        stop_instance "$BASE_DIR/inst$INSTANCE"
    fi
    exit 0
fi

# --------------------------------------------------------------------------
# launch mode
# --------------------------------------------------------------------------
[[ "$INSTANCE" =~ ^[0-9]+$ ]] || die "instance must be a positive integer, got '$INSTANCE'"
INST_DIR="$BASE_DIR/inst$INSTANCE"
APP="$INST_DIR/SlayTheSpire2.app"
BIN="$APP/Contents/MacOS/Slay the Spire 2"
CONF="$APP/Contents/MacOS/mods/STS2_MCP.conf"
INST_HOME="$INST_DIR/home"
GAME_LOG="$INST_DIR/game.log"
GODOT_LOG="$INST_HOME/Library/Application Support/SlayTheSpire2/logs/godot.log"

[[ -d "$INST_DIR" ]] || die "instance dir missing: $INST_DIR — run scripts/headless_provision.sh first"
[[ -x "$BIN" ]]      || die "instance binary missing/not executable: $BIN"
[[ -f "$CONF" ]]     || die "instance MCP conf missing: $CONF"
[[ "$APP" != "$REAL_APP" ]] || die "refusing to launch the REAL bundle: $REAL_APP"

PORT="$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['port'])" "$CONF")" \
    || die "could not read port from $CONF"
[[ "$PORT" != "$FORBIDDEN_PORT" ]] \
    || die "instance conf points at port $FORBIDDEN_PORT — that is the REAL game's port"

if pgrep -f "$APP" >/dev/null 2>&1; then
    die "instance $INSTANCE already running (pgrep -f '$APP'); use --stop first"
fi

log "instance $INSTANCE: HOME=$INST_HOME port=$PORT"
log "launching: '$BIN' --headless --force-steam=off  (log: $GAME_LOG)"

HOME="$INST_HOME" nohup "$BIN" --headless --force-steam=off \
    >"$GAME_LOG" 2>&1 &
GAME_PID=$!
log "started pid $GAME_PID"

if [[ "$WAIT_S" -le 0 ]]; then
    log "--no-wait: not health-checking. Poll http://127.0.0.1:$PORT/api/v1/singleplayer yourself."
    exit 0
fi

# Health-wait loop: any HTTP response (even an error status) from the MCP mod
# means the game booted far enough to load mods and open its listener.
log "waiting up to ${WAIT_S}s for http://127.0.0.1:$PORT/api/v1/singleplayer ..."
deadline=$((SECONDS + WAIT_S))
while (( SECONDS < deadline )); do
    if ! kill -0 "$GAME_PID" 2>/dev/null; then
        log "game process $GAME_PID exited early. Diagnosis:"
        log "  tail -50 '$GAME_LOG'"
        log "  tail -50 '$GODOT_LOG'"
        tail -20 "$GAME_LOG" 2>/dev/null | sed 's/^/[launch]   game.log: /' || true
        die "instance $INSTANCE did not survive boot (headless boot is UNVERIFIED — see script header)"
    fi
    if curl -sS -o /dev/null --max-time 2 "http://127.0.0.1:$PORT/api/v1/singleplayer" 2>/dev/null; then
        log "instance $INSTANCE is UP: MCP answering on port $PORT (pid $GAME_PID)"
        exit 0
    fi
    sleep 1
done

log "TIMEOUT: no HTTP response on port $PORT after ${WAIT_S}s (pid $GAME_PID still alive)."
log "The game may be stuck pre-mod-load. Diagnosis order:"
log "  1. tail -50 '$GAME_LOG'"
log "  2. tail -50 '$GODOT_LOG'   # how far did boot get?"
log "  3. curl -v http://127.0.0.1:$PORT/api/v1/singleplayer"
log "Stop it with: $0 $INSTANCE --stop --base '$BASE_DIR'"
exit 3
