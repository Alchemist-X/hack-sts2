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
dotnet build "$BUILD_ROOT/STS2_MCP.csproj" -c Release --nologo -p:STS2GameDir="$GAME_DIR" -p:ContinuousIntegrationBuild=true
cp "$BUILD_ROOT/mod_manifest.json" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.json"
"$SCRIPT_DIR/build_headless_mcp.sh" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.dll" "$OUTPUT_DIR"
shasum -a 256 "$OUTPUT_DIR/STS2_MCP.dll"
