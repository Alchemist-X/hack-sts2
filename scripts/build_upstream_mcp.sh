#!/usr/bin/env bash
# Reproducibly build the STS2MCP commit pinned in config/sts2mcp.lock.json.
# The source checkout is read-only: git archive materializes a clean, fixed-path
# build tree under ignored artifacts. The fixed path makes portable PDB inputs stable.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
LOCK_FILE="$REPO_ROOT/config/sts2mcp.lock.json"
SOURCE_REPO="${STS2_MCP_SOURCE_REPO:-$(dirname "$REPO_ROOT")/STS2MCP}"
GAME_DIR="${STS2_GAME_INSTALL_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"

die() { printf '[build-upstream-mcp] ERROR: %s\n' "$*" >&2; exit 1; }
log() { printf '[build-upstream-mcp] %s\n' "$*"; }

[[ -f "$LOCK_FILE" ]] || die "lock file missing: $LOCK_FILE"
[[ -d "$SOURCE_REPO/.git" ]] || die "source repository missing: $SOURCE_REPO"
[[ -d "$GAME_DIR/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64" ]] \
    || die "game assembly directory missing below: $GAME_DIR"
command -v git >/dev/null || die "git is required"
command -v dotnet >/dev/null || die "dotnet is required"
command -v python3 >/dev/null || die "python3 is required"

readarray_value() {
    local expression="$1"
    python3 -c 'import json,sys; print(eval(sys.argv[2], {}, {"d": json.load(open(sys.argv[1], encoding="utf-8"))}))' \
        "$LOCK_FILE" "$expression"
}

COMMIT="$(readarray_value 'd["commit"]')"
EXPECTED_SHA="$(readarray_value 'd["artifact"]["sha256"]')"
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
