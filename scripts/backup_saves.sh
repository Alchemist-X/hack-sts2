#!/usr/bin/env bash
# Snapshot the entire STS2 user-data dir before any modded/driven session.
# Never touches originals; writes a timestamped tar.gz under ./backups/.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
USERDATA="${STS2UserData:-$HOME/Library/Application Support/SlayTheSpire2}"
BACKUP_DIR="$REPO_ROOT/backups"

if [[ ! -d "$USERDATA" ]]; then
  echo "error: STS2 user data not found at: $USERDATA" >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"
STAMP="$(date +%Y%m%dT%H%M%S)"
OUT="$BACKUP_DIR/sts2-userdata-$STAMP.tar.gz"

tar -czf "$OUT" -C "$(dirname "$USERDATA")" "$(basename "$USERDATA")"
echo "backed up: $OUT ($(du -h "$OUT" | cut -f1))"
