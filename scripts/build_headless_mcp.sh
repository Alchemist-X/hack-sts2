#!/usr/bin/env bash
# Build a sandbox-only STS2_MCP binary that skips its optional Harmony UI patches.
# The quarantined source DLL and the human game bundle are never modified.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
INPUT="${1:-$REPO_ROOT/.mcp-stashed/STS2_MCP.dll}"
OUTPUT_DIR="${2:-$REPO_ROOT/headless-instances/mcp-headless}"
export STS2_GAME_DATA_DIR="${STS2_GAME_DATA_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64}"

[[ -f "$INPUT" ]] || { printf 'missing MCP input: %s\n' "$INPUT" >&2; exit 1; }
[[ -d "$STS2_GAME_DATA_DIR" ]] || { printf 'missing game assembly dir: %s\n' "$STS2_GAME_DATA_DIR" >&2; exit 1; }
mkdir -p "$OUTPUT_DIR"

dotnet run --project "$SCRIPT_DIR/McpHeadlessPatcher/McpHeadlessPatcher.csproj" -- \
  "$INPUT" "$OUTPUT_DIR/STS2_MCP.dll"
cp "$(dirname "$INPUT")/STS2_MCP.json" "$OUTPUT_DIR/STS2_MCP.json"
printf 'sandbox MCP ready: %s\n' "$OUTPUT_DIR"
