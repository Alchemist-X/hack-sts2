#!/usr/bin/env bash
# Build the Sts2Recorder mod against the local game install and copy it into
# the game's mods/ directory. macOS-first; STS2GameDir overridable via env.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STS2_GAME_DIR="${STS2GameDir:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
APP_BUNDLE="$STS2_GAME_DIR/SlayTheSpire2.app"
MODS_DIR="${STS2ModsDir:-$APP_BUNDLE/Contents/MacOS/mods}"

if [[ ! -d "$STS2_GAME_DIR" ]]; then
  echo "error: game install not found at: $STS2_GAME_DIR" >&2
  echo "       set STS2GameDir to your 'Slay the Spire 2' directory" >&2
  exit 1
fi

DOTNET="$(command -v dotnet || true)"
if [[ -z "$DOTNET" && -x /opt/homebrew/opt/dotnet@9/bin/dotnet ]]; then
  DOTNET=/opt/homebrew/opt/dotnet@9/bin/dotnet
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
fi
if [[ -z "$DOTNET" ]]; then
  echo "error: dotnet not found (brew install dotnet@9)" >&2
  exit 1
fi

RELEASE_INFO="$APP_BUNDLE/Contents/Resources/release_info.json"
if [[ -f "$RELEASE_INFO" ]]; then
  echo "game build: $(cat "$RELEASE_INFO")"
fi

echo "building Sts2Recorder against: $STS2_GAME_DIR"
"$DOTNET" build "$REPO_ROOT/mod/Sts2Recorder.csproj" -c Release \
  -p:STS2GameDir="$STS2_GAME_DIR"

DLL="$REPO_ROOT/mod/bin/Release/net9.0/Sts2Recorder.dll"
if [[ ! -f "$DLL" ]]; then
  echo "error: build produced no DLL at $DLL" >&2
  exit 1
fi

mkdir -p "$MODS_DIR"
cp "$DLL" "$MODS_DIR/"
cp "$REPO_ROOT/mod/Sts2Recorder.json" "$MODS_DIR/"

echo "installed to: $MODS_DIR"
echo "next: launch STS2 with 'Load with Mods' and accept the mod consent dialog."
