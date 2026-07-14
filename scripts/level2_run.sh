#!/usr/bin/env bash
# Level-2 live verification, end to end:
# launch modded game -> wait for mod banner + MCP port -> drive a scripted run
# via STS2MCP -> diff injected actions vs recorded trajectory -> report.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME_PROC="Slay the Spire 2"
LOG="$HOME/Library/Application Support/SlayTheSpire2/logs/godot.log"
MCP_PORT="${STS2MCP_PORT:-15526}"
OUTPUT_ROOT="${STS2RecOutputRoot:-$HOME/Library/Application Support/Sts2Recorder}"
RUN_OUT="${1:-$REPO_ROOT/level2-runs/$(date +%Y%m%dT%H%M%S)}"
FLOORS="${FLOORS:-3}"

mkdir -p "$RUN_OUT"
echo "== Level 2 run: output -> $RUN_OUT"

if pgrep -x "$GAME_PROC" > /dev/null; then
  echo "error: game already running — quit it first" >&2
  exit 1
fi

LOG_MARK=$(stat -f %m "$LOG" 2>/dev/null || echo 0)
echo "== launching game via Steam"
open "steam://rungameid/2868840"

echo "== waiting for game process (120s)"
ok=""
for _ in $(seq 1 120); do
  pgrep -x "$GAME_PROC" > /dev/null && ok=1 && break
  sleep 1
done
[[ -z "$ok" ]] && { echo "error: game never started" >&2; exit 1; }

echo "== waiting for RUNNING MODDED banner (90s)"
ok=""
for _ in $(seq 1 90); do
  if [[ "$(stat -f %m "$LOG" 2>/dev/null || echo 0)" -gt "$LOG_MARK" ]] \
     && grep -q "RUNNING MODDED" "$LOG" 2>/dev/null; then ok=1; break; fi
  sleep 1
done
if [[ -z "$ok" ]]; then
  echo "error: no RUNNING MODDED banner; recent log:" >&2
  tail -20 "$LOG" >&2
  exit 1
fi
grep "RUNNING MODDED\|Sts2Recorder\|STS2 MCP\|STS2_MCP" "$LOG" | tail -5

echo "== waiting for MCP HTTP (60s)"
ok=""
for _ in $(seq 1 60); do
  curl -s -m 2 "http://localhost:$MCP_PORT/api/v1/singleplayer" > /dev/null 2>&1 && ok=1 && break
  sleep 1
done
[[ -z "$ok" ]] && { echo "error: MCP port $MCP_PORT never came up" >&2; exit 1; }
echo "MCP alive"

echo "== driving scripted run ($FLOORS floors)"
python3 "$REPO_ROOT/scripts/level2_driver.py" --port "$MCP_PORT" --floors "$FLOORS" \
  --out "$RUN_OUT" 2>&1 | tee "$RUN_OUT/driver.log"
DRIVER_EXIT=${PIPESTATUS[0]}
echo "driver exit: $DRIVER_EXIT"

echo "== quitting game (graceful, lets recorder flush)"
osascript -e "tell application \"$GAME_PROC\" to quit" 2>/dev/null || pkill -x "$GAME_PROC"
for _ in $(seq 1 15); do pgrep -x "$GAME_PROC" > /dev/null || break; sleep 1; done
pgrep -x "$GAME_PROC" > /dev/null && pkill -9 -x "$GAME_PROC"

SESSION=$(ls -td "$OUTPUT_ROOT/sessions/"*/ 2>/dev/null | head -1)
if [[ -z "$SESSION" ]]; then
  echo "error: no recorded session found under $OUTPUT_ROOT/sessions/" >&2
  exit 1
fi
echo "== newest session: $SESSION"
ls -la "$SESSION"

echo "== diffing injected vs recorded"
python3 "$REPO_ROOT/scripts/level2_diff.py" --session "$SESSION" \
  --injected "$RUN_OUT/injected_actions.jsonl" 2>&1 | tee "$RUN_OUT/diff_report.txt"
DIFF_EXIT=${PIPESTATUS[0]}

echo ""
echo "== SUMMARY: driver=$DRIVER_EXIT diff=$DIFF_EXIT session=$SESSION report=$RUN_OUT/diff_report.txt"
exit "$DIFF_EXIT"
