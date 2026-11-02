#!/usr/bin/env bash
# Build reviewable sandbox-only selection and rule-evidence patches.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SOURCE_REPO="${STS2_MCP_SOURCE_REPO:-$(dirname "$REPO_ROOT")/STS2MCP}"
GAME_DIR="${STS2_GAME_INSTALL_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
COMMIT="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["commit"])' "$REPO_ROOT/config/sts2mcp.lock.json")"
BUILD_ROOT="$REPO_ROOT/artifacts/mcp/rule-evidence-v1/source"
OUTPUT_DIR="${1:-$REPO_ROOT/artifacts/mcp/rule-evidence-v1/sandbox}"
mkdir -p "$BUILD_ROOT" "$OUTPUT_DIR"
git -C "$SOURCE_REPO" archive "$COMMIT" | tar -x -C "$BUILD_ROOT"
# This generated source is absent from the upstream archive; remove the previous
# build's copy so a repeat build never reverses a new-file patch.
rm -f "$BUILD_ROOT/McpMod.RuleEvidence.cs"
patch --batch --forward -d "$BUILD_ROOT" -p1 < "$REPO_ROOT/patches/sts2mcp-selection-state.patch"
patch --batch --forward -d "$BUILD_ROOT" -p1 < "$REPO_ROOT/patches/sts2mcp-rule-evidence.patch"
# Game v0.111.x API drift (same substitutions as training/adapt_mcp.py on the
# 2026-10-27 macOS build): Hook.ModifyDamage gained a CardPlay? parameter,
# StartRunLobby.MaxPlayers became the _maxPlayers field, and
# LoadRunLobby.ConnectedPlayerIds became PlayerIds. Gated on the installed
# version so v0.107.x builds are byte-for-byte unchanged.
GAME_VERSION="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("version",""))' \
    "$GAME_DIR/SlayTheSpire2.app/Contents/Resources/release_info.json" 2>/dev/null || true)"
if [[ "$GAME_VERSION" == v0.111.* ]]; then
  python3 - "$BUILD_ROOT" <<'PYEOF'
import sys
from pathlib import Path
root = Path(sys.argv[1])
subs = {
    "McpMod.RuleEvidence.cs": [("damage.BaseValue, damage.Props, card,",
                                "damage.BaseValue, damage.Props, card, null,")],
    "McpMod.StateBuilder.cs": [("lobby.MaxPlayers", 'GetInstanceFieldValue(lobby, "_maxPlayers")'),
                               ("lobby.ConnectedPlayerIds", "lobby.PlayerIds.ToList()")],
}
for name, pairs in subs.items():
    path = root / name
    text = path.read_text()
    for old, new in pairs:
        if old not in text:
            sys.exit(f"v0.111 adapt: missing expected contract in {name}: {old}")
        text = text.replace(old, new)
    path.write_text(text)
print("v0.111 API adapt applied")
PYEOF
fi
dotnet build "$BUILD_ROOT/STS2_MCP.csproj" -c Release --nologo -p:STS2GameDir="$GAME_DIR" -p:ContinuousIntegrationBuild=true
cp "$BUILD_ROOT/mod_manifest.json" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.json"
"$SCRIPT_DIR/build_headless_mcp.sh" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.dll" "$OUTPUT_DIR"
shasum -a 256 "$OUTPUT_DIR/STS2_MCP.dll"
