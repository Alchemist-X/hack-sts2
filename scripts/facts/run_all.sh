#!/usr/bin/env bash
# Rebuild the STS2 fact DB for one game version.
#   bash scripts/facts/run_all.sh [--version 0.107.1] [--refresh] [--offline]
# --refresh  re-download wiki/codex pages even if cached (artifacts/facts-cache/, git-ignored)
# --offline  skip network fetchers; rebuild from the existing cache + game data only
# Requires STS2_GAME_INSTALL_DIR (read-only) and artifacts/knowledge/decompiled-v<version>/.
set -euo pipefail
cd "$(dirname "$0")/../.."
VERSION=0.107.1; REFRESH=(); OFFLINE=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2;;
    --refresh) REFRESH=(--refresh); shift;;
    --offline) OFFLINE=1; shift;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done
if [[ $OFFLINE -eq 0 ]]; then
  python3 scripts/facts/fetch_wiki.py --version "$VERSION" "${REFRESH[@]}"
  python3 scripts/facts/fetch_codex.py --version "$VERSION" "${REFRESH[@]}"
fi
python3 scripts/facts/game_truth.py --version "$VERSION"
python3 scripts/facts/build_facts.py --version "$VERSION"
python3 scripts/facts/lookup.py --version "$VERSION" --id QUEEN --id BASH --max-chars 800 >/dev/null
echo "OK: knowledge/facts/v$VERSION/"
