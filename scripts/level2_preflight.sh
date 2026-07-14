#!/usr/bin/env bash
# Level-2 preflight: verify everything a live-game verification run needs.
#
# Checks (each prints ok/MISSING; exits non-zero listing every missing item):
#   1. game process running (pgrep)
#   2. STS2MCP HTTP API alive on localhost (GET /api/v1/singleplayer)
#   3. recorder output root exists + newest recorded session dir
#   4. game version (release_info.json inside the app bundle)
#   5. mods installed in the app bundle mods/ dir (Sts2Recorder + STS2MCP)
#   6. saves backup exists under <repo>/backups/ (scripts/backup_saves.sh)
#
# Env overrides (same names as install_mod.sh / backup_saves.sh):
#   STS2GameDir, STS2ModsDir, STS2UserData, STS2RecOutputRoot, STS2MCP_PORT
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STS2_GAME_DIR="${STS2GameDir:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
APP_BUNDLE="$STS2_GAME_DIR/SlayTheSpire2.app"
MODS_DIR="${STS2ModsDir:-$APP_BUNDLE/Contents/MacOS/mods}"
RELEASE_INFO="$APP_BUNDLE/Contents/Resources/release_info.json"
RECORDER_ROOT="${STS2RecOutputRoot:-$HOME/Library/Application Support/Sts2Recorder}"
SESSIONS_DIR="$RECORDER_ROOT/sessions"
BACKUP_DIR="$REPO_ROOT/backups"
MCP_PORT="${STS2MCP_PORT:-15526}"
MCP_URL="http://127.0.0.1:$MCP_PORT/api/v1/singleplayer"

MISSING=()

note_ok()      { printf '  ok      %s\n' "$1"; }
note_missing() { printf '  MISSING %s\n' "$1"; MISSING+=("$1"); }

echo "== level2 preflight =="

# 1. game process ------------------------------------------------------------
echo "[1/6] game process"
GAME_PIDS="$(pgrep -f 'SlayTheSpire2' || true)"
if [[ -n "$GAME_PIDS" ]]; then
  note_ok "game running (pid(s): $(echo "$GAME_PIDS" | tr '\n' ' ' | sed 's/ $//'))"
else
  note_missing "game process (pgrep -f SlayTheSpire2 found nothing — launch STS2 with 'Load with Mods')"
fi

# 2. STS2MCP HTTP ------------------------------------------------------------
echo "[2/6] STS2MCP HTTP API ($MCP_URL)"
MCP_BODY="$(curl -fsS --max-time 5 "$MCP_URL" 2>/dev/null || true)"
if [[ -n "$MCP_BODY" ]]; then
  STATE_TYPE="$(printf '%s' "$MCP_BODY" \
    | sed -n 's/.*"state_type"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
  note_ok "HTTP alive (state_type: ${STATE_TYPE:-unparseable — first 120 bytes: ${MCP_BODY:0:120}})"
else
  note_missing "STS2MCP HTTP API on port $MCP_PORT (is the STS2_MCP mod installed and the game running?)"
fi

# 3. recorder output root + newest session -----------------------------------
echo "[3/6] recorder output root ($RECORDER_ROOT)"
if [[ -d "$RECORDER_ROOT" ]]; then
  note_ok "output root exists"
  if [[ -d "$SESSIONS_DIR" ]]; then
    NEWEST_SESSION="$(ls -1t "$SESSIONS_DIR" 2>/dev/null | head -1 || true)"
    if [[ -n "$NEWEST_SESSION" ]]; then
      note_ok "newest session: $SESSIONS_DIR/$NEWEST_SESSION"
    else
      note_missing "recorded sessions in $SESSIONS_DIR (dir exists but is empty — has the recorder ever run?)"
    fi
  else
    note_missing "sessions dir $SESSIONS_DIR (recorder has not created it yet)"
  fi
else
  note_missing "recorder output root $RECORDER_ROOT (Sts2Recorder mod never ran, or set STS2RecOutputRoot)"
fi

# 4. game version ------------------------------------------------------------
echo "[4/6] game version"
if [[ -f "$RELEASE_INFO" ]]; then
  note_ok "release_info.json: $(tr -d '\n' < "$RELEASE_INFO" | tr -s ' ')"
else
  note_missing "release_info.json at $RELEASE_INFO (game install not found — set STS2GameDir?)"
fi

# 5. mods installed ----------------------------------------------------------
echo "[5/6] mods in app bundle ($MODS_DIR)"
if [[ -d "$MODS_DIR" ]]; then
  MOD_LIST="$(find "$MODS_DIR" -maxdepth 2 \( -name '*.dll' -o -name '*.json' \) 2>/dev/null | sed "s|$MODS_DIR/||")"
  if [[ -n "$MOD_LIST" ]]; then
    echo "$MOD_LIST" | sed 's/^/          - /'
  fi
  if echo "$MOD_LIST" | grep -qi 'Sts2Recorder\.dll'; then
    note_ok "Sts2Recorder installed"
  else
    note_missing "Sts2Recorder.dll in $MODS_DIR (run scripts/install_mod.sh)"
  fi
  if echo "$MOD_LIST" | grep -qiE '(sts2[_-]?mcp|mcpmod)'; then
    note_ok "STS2MCP installed"
  else
    note_missing "STS2MCP mod in $MODS_DIR (the level-2 driver needs its HTTP API)"
  fi
else
  note_missing "mods dir $MODS_DIR (game install not found or mods never installed — set STS2GameDir/STS2ModsDir)"
fi

# 6. saves backup ------------------------------------------------------------
echo "[6/6] saves backup ($BACKUP_DIR)"
NEWEST_BACKUP="$(ls -1t "$BACKUP_DIR"/sts2-userdata-*.tar.gz 2>/dev/null | head -1 || true)"
if [[ -n "$NEWEST_BACKUP" ]]; then
  note_ok "newest backup: $NEWEST_BACKUP"
else
  note_missing "saves backup in $BACKUP_DIR (run scripts/backup_saves.sh before driving the game)"
fi

# summary ---------------------------------------------------------------------
echo ""
if [[ ${#MISSING[@]} -eq 0 ]]; then
  echo "preflight: OK — ready for scripts/level2_driver.py"
  exit 0
fi
echo "preflight: FAIL — ${#MISSING[@]} item(s) missing:"
for item in "${MISSING[@]}"; do
  echo "  - $item"
done
exit 1
