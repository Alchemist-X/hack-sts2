#!/usr/bin/env bash
# recording_preflight.sh — read-only checks before recording human runs. Changes nothing.
#   1. game version (release_info.json) == v0.111.0, the only version the simulator implements
#   2. mods/ holds only Sts2Recorder (no STS2_MCP, no QuickSL, nothing else)
#   3. mods/Sts2Recorder.conf sets decision_capture_combat_inputs: true
#   4. installed Sts2Recorder.dll == install_mod.sh build output (sha256), when both exist
# Exit 0 only when every check passes. Paths follow install_mod.sh / mcp_mod.sh (STS2GameDir, STS2ModsDir).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WANT_VERSION="${STS2_WANT_VERSION:-v0.111.0}"
STS2_GAME_DIR="${STS2GameDir:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
APP_BUNDLE="$STS2_GAME_DIR/SlayTheSpire2.app"
MODS_DIR="${STS2ModsDir:-$APP_BUNDLE/Contents/MacOS/mods}"
RELEASE_INFO="$APP_BUNDLE/Contents/Resources/release_info.json"
CONF="$MODS_DIR/Sts2Recorder.conf"
BUILT_DLL="$REPO_ROOT/mod/bin/Release/net9.0/Sts2Recorder.dll"
INST_DLL="$MODS_DIR/Sts2Recorder.dll"

fails=0
report() { # report PASS|FAIL|SKIP name observed
  printf '%-4s  %-22s %s\n' "$1" "$2" "$3"
  [[ "$1" == FAIL ]] && fails=$((fails + 1))
}

# 1. game version
if [[ -f "$RELEASE_INFO" ]]; then
  ver=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("version",""))' "$RELEASE_INFO" 2>/dev/null)
  if [[ "$ver" == "$WANT_VERSION" ]]; then report PASS game-version "$ver"
  else report FAIL game-version "${ver:-unreadable} (want $WANT_VERSION; switch the Steam branch)"; fi
else
  report FAIL game-version "release_info.json not found at $RELEASE_INFO"
fi

# 2. mods dir contents
if [[ -d "$MODS_DIR" ]]; then
  others=$(ls -A "$MODS_DIR" | grep -vE '^Sts2Recorder\.(dll|json|conf|pdb)$' | grep -vx '.DS_Store' | tr '\n' ' ')
  if [[ ! -f "$INST_DLL" ]]; then report FAIL mods-only-recorder "Sts2Recorder.dll missing; others: ${others:-none}"
  elif [[ -n "$others" ]]; then report FAIL mods-only-recorder "extra entries: $others(remove; STS2_MCP via scripts/mcp_mod.sh off)"
  else report PASS mods-only-recorder "$(ls -A "$MODS_DIR" | grep -vx '.DS_Store' | tr '\n' ' ')"; fi
else
  report FAIL mods-only-recorder "mods dir not found at $MODS_DIR"
fi

# 3. recorder conf
if [[ -f "$CONF" ]]; then
  val=$(python3 -c 'import json,sys; v=json.load(open(sys.argv[1])).get("decision_capture_combat_inputs"); print("absent" if v is None else json.dumps(v))' "$CONF" 2>/dev/null || echo unparsable)
  if [[ "$val" == true ]]; then report PASS conf-combat-capture "decision_capture_combat_inputs=true"
  else report FAIL conf-combat-capture "decision_capture_combat_inputs=$val (want true; conf: $(tr -d '\n ' < "$CONF"))"; fi
else
  report FAIL conf-combat-capture "Sts2Recorder.conf not found at $CONF"
fi

# 4. installed dll vs build output
if [[ -f "$INST_DLL" && -f "$BUILT_DLL" ]]; then
  hi=$(shasum -a 256 "$INST_DLL" | cut -c1-12); hb=$(shasum -a 256 "$BUILT_DLL" | cut -c1-12)
  if [[ "$hi" == "$hb" ]]; then report PASS dll-matches-build "sha256 $hi"
  else report FAIL dll-matches-build "installed $hi != build $hb ($BUILT_DLL; rerun scripts/install_mod.sh)"; fi
else
  report SKIP dll-matches-build "installed=$([[ -f $INST_DLL ]] && echo yes || echo no) build=$([[ -f $BUILT_DLL ]] && echo yes || echo no)"
fi

if (( fails == 0 )); then echo "VERDICT: OK to record human runs."; exit 0
else echo "VERDICT: NOT ready to record ($fails check(s) failed); fix the FAIL lines first."; exit 1; fi
