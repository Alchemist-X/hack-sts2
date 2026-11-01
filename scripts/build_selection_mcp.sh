#!/usr/bin/env bash
# Build the sandbox-only worker MCP (never install it into the human game):
# upstream STS2MCP at the lock commit + the ordered, sha256-pinned patch list in
# config/sts2mcp.lock.json (selection state, rule evidence, action receipts,
# seeded standard embark, eval API, game-version compat), then the headless/port
# rewrite (build_headless_mcp.sh). Only the game build the lock pins is accepted.
#
# The build embeds no repository metadata (SourceLink off, PathMap), so the
# sandbox DLL sha256 depends only on the inputs and is checked against the lock.
#
# Usage: build_selection_mcp.sh [OUTPUT_DIR]   (default artifacts/mcp/rule-evidence-v1/sandbox)
# Env:   STS2_MCP_SOURCE_REPO   STS2MCP clone (default: beside the main checkout)
#        STS2_GAME_INSTALL_DIR  game install (default: the macOS Steam install)
#        STS2_MCP_ALLOW_UNPINNED_GAME=1  migration work only: build against a game
#                               build the lock does not pin; the sha check only warns
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
source "$SCRIPT_DIR/mcp_build_env.sh"
mcp_resolve_game
mcp_check_game
mcp_check_sdk
mcp_resolve_source_repo

BUILD_ROOT="$REPO_ROOT/artifacts/mcp/rule-evidence-v1/source"
OUTPUT_DIR="${1:-$REPO_ROOT/artifacts/mcp/rule-evidence-v1/sandbox}"
case "$OUTPUT_DIR" in
    "$GAME_DIR"*|*/SlayTheSpire2.app/*) mcp_die "refusing an output dir inside a game bundle: $OUTPUT_DIR" ;;
esac
mkdir -p "$BUILD_ROOT" "$OUTPUT_DIR"
# Start from an empty tree: the patches add files (McpMod.Eval.cs, ...) that a
# leftover copy would make `patch --forward` skip or reverse.
find "$BUILD_ROOT" -mindepth 1 -depth -delete
git -C "$SOURCE_REPO" archive "$COMMIT" | tar -x -C "$BUILD_ROOT"
while IFS=$'\t' read -r rel want; do
    got="$(shasum -a 256 "$REPO_ROOT/$rel" | awk '{print $1}')"
    [[ "$got" == "$want" ]] || mcp_die "$rel sha256 $got != lock $want"
    mcp_log "applying $rel"
    patch --batch --forward --fuzz=0 -d "$BUILD_ROOT" -p1 < "$REPO_ROOT/$rel"
done < <(mcp_lock '"\n".join(p["path"] + "\t" + p["sha256"] for p in d["patches"])')

dotnet build "$BUILD_ROOT/STS2_MCP.csproj" -c Release --nologo \
    -p:STS2GameDir="$GAME_DIR" -p:STS2GameDataDir="$STS2_GAME_DATA_DIR" \
    -p:Deterministic=true -p:ContinuousIntegrationBuild=true \
    -p:PathMap="$BUILD_ROOT=/src/STS2MCP" \
    -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false \
    -p:IncludeSourceRevisionInInformationalVersion=false
cp "$BUILD_ROOT/mod_manifest.json" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.json"
"$SCRIPT_DIR/build_headless_mcp.sh" "$BUILD_ROOT/bin/Release/net9.0/STS2_MCP.dll" "$OUTPUT_DIR"

ACTUAL="$(shasum -a 256 "$OUTPUT_DIR/STS2_MCP.dll" | awk '{print $1}')"
PLATFORM="$(basename "$STS2_GAME_DATA_DIR")"
EXPECTED="$(mcp_lock "d['sandbox_build']['dll_sha256'].get('$PLATFORM')")"
if [[ -z "$EXPECTED" ]]; then
    mcp_log "no sandbox DLL sha256 pinned for $PLATFORM; validate this build, then pin $ACTUAL"
elif [[ "$ACTUAL" != "$EXPECTED" ]]; then
    message="sandbox DLL sha256 $ACTUAL != lock $EXPECTED ($PLATFORM)"
    [[ "$MCP_UNPINNED_GAME" == "1" ]] || mcp_die "$message; if the inputs changed on purpose, validate the build and update sandbox_build.dll_sha256"
    mcp_log "WARNING: $message (unpinned game build)"
else
    mcp_log "sandbox DLL matches the lock ($PLATFORM)"
fi
shasum -a 256 "$OUTPUT_DIR/STS2_MCP.dll"
