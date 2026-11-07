#!/usr/bin/env bash
# Reproducibly build the STS2MCP commit pinned in config/sts2mcp.lock.json.
# The source checkout is read-only: git archive materializes a clean, fixed-path
# build tree under ignored artifacts. The fixed path makes portable PDB inputs stable.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
source "$SCRIPT_DIR/mcp_build_env.sh"

die() { printf '[build-upstream-mcp] ERROR: %s\n' "$*" >&2; exit 1; }
log() { printf '[build-upstream-mcp] %s\n' "$*"; }

command -v git >/dev/null || die "git is required"
command -v dotnet >/dev/null || die "dotnet is required"
command -v python3 >/dev/null || die "python3 is required"
mcp_resolve_game
mcp_resolve_source_repo

# The pinned artifact is the *unpatched* upstream build for one game version.
# Unpatched upstream does not compile on later builds (v0.111.0 removed
# StartRunLobby.MaxPlayers and LoadRunLobby.ConnectedPlayerIds); workers never
# need it: build_selection_mcp.sh builds them from source with the compat patches.
ARTIFACT_GAME="$(mcp_lock 'd["artifact"].get("game_version")')"
INSTALLED_GAME="$(mcp_game_version)"
if [[ -n "$ARTIFACT_GAME" && "${INSTALLED_GAME#v}" != "${ARTIFACT_GAME#v}" ]]; then
    die "the pinned upstream artifact is the unpatched build for game $ARTIFACT_GAME; installed game is $INSTALLED_GAME. Workers use scripts/build_selection_mcp.sh instead"
fi

COMMIT="$(mcp_lock 'd["commit"]')"
EXPECTED_SHA="$(mcp_lock 'd["artifact"]["sha256"]')"
OUTPUT_DIR="$REPO_ROOT/artifacts/mcp/$COMMIT"

git -C "$SOURCE_REPO" cat-file -e "$COMMIT^{commit}" 2>/dev/null \
    || die "pinned commit $COMMIT is not present; run: git -C '$SOURCE_REPO' fetch origin main"

BUILD_ROOT="$REPO_ROOT/artifacts/mcp/.build/$COMMIT"
case "$BUILD_ROOT" in
    "$REPO_ROOT"/artifacts/mcp/.build/*) ;;
    *) die "refusing unexpected build path: $BUILD_ROOT" ;;
esac
mkdir -p "$BUILD_ROOT"
find "$BUILD_ROOT" -mindepth 1 -depth -delete

log "materializing clean source at commit $COMMIT"
git -C "$SOURCE_REPO" archive "$COMMIT" | tar -x -C "$BUILD_ROOT"

log "building Release with $(dotnet --version) against $GAME_DIR"
dotnet build "$BUILD_ROOT/STS2_MCP.csproj" -c Release --nologo \
    -p:STS2GameDir="$GAME_DIR" \
    -p:ContinuousIntegrationBuild=true \
    -p:Deterministic=true \
    -p:PathMap="$BUILD_ROOT=/src/STS2MCP"

BUILD_DIR="$BUILD_ROOT/bin/Release/net9.0"
[[ -f "$BUILD_DIR/STS2_MCP.dll" ]] || die "build did not produce STS2_MCP.dll"
mkdir -p "$OUTPUT_DIR"
cp "$BUILD_DIR/STS2_MCP.dll" "$OUTPUT_DIR/STS2_MCP.dll"
cp "$BUILD_DIR/STS2_MCP.pdb" "$OUTPUT_DIR/STS2_MCP.pdb"
cp "$BUILD_DIR/STS2_MCP.deps.json" "$OUTPUT_DIR/STS2_MCP.deps.json"
cp "$BUILD_ROOT/mod_manifest.json" "$OUTPUT_DIR/STS2_MCP.json"

ACTUAL_SHA="$(shasum -a 256 "$OUTPUT_DIR/STS2_MCP.dll" | awk '{print $1}')"
[[ "$ACTUAL_SHA" == "$EXPECTED_SHA" ]] \
    || die "DLL hash mismatch: expected $EXPECTED_SHA, got $ACTUAL_SHA"

log "verified $OUTPUT_DIR/STS2_MCP.dll"
log "sha256=$ACTUAL_SHA"
