#!/usr/bin/env bash
# Print one deterministic SHA-256 for the real Steam save tree.
# Isolated workers must leave this value unchanged across a smoke run.
set -euo pipefail

SAVE_ROOT="${1:-$HOME/Library/Application Support/SlayTheSpire2/steam}"
[[ -d "$SAVE_ROOT" ]] || {
    printf '[hash-human-saves] ERROR: save root missing: %s\n' "$SAVE_ROOT" >&2
    exit 1
}

(
    cd "$SAVE_ROOT"
    while IFS= read -r -d '' relative_path; do
        shasum -a 256 "$relative_path"
    done < <(find . -type f -print0 | LC_ALL=C sort -z)
) | shasum -a 256 | awk '{print $1}'
