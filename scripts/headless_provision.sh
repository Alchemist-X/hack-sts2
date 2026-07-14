#!/usr/bin/env bash
# headless_provision.sh — provision N isolated headless STS2 instances.
#
# For each instance i in 1..N under <base>/inst<i>/:
#   SlayTheSpire2.app/          APFS clone (cp -Rc) of the real bundle; plain
#                               cp -R fallback if clonefile is unsupported.
#     Contents/MacOS/mods/      fresh per-instance mods dir:
#       STS2_MCP.dll/.json      copied from the REAL bundle's mods dir
#       STS2_MCP.conf           {"port": 15600+i}    (McpMod.LoadPort reads the
#                               conf sitting next to its own DLL)
#       Sts2Recorder.dll/.json  copied from the REAL bundle's mods dir
#       Sts2Recorder.conf       {"output_root": "<base>/inst<i>/recordings"}
#                               (RecorderMod.LoadOutputRoot, same next-to-DLL rule)
#   home/                       isolated $HOME for the instance
#     Library/Application Support/SlayTheSpire2/default/1/settings.save
#     Library/Application Support/SlayTheSpire2/steam/<steamid>/settings.save
#   recordings/                 recorder output root
#
# Save-path rationale (v0.107.1 decompile, Core/Saves/UserDataPathProvider.cs +
# Core/Platform/PlatformUtil.cs): settings.save lives at the account-scoped
# base path "user://<platform>/<userId>/settings.save".  Without Steam,
# PlatformUtil.PrimaryPlatform == PlatformType.None -> directory "default"
# (non-editor build) and NullPlatformUtilStrategy.LocalPlayerId == 1 (unless
# --clientId overrides it), i.e. "default/1/".  We seed BOTH default/1/ and
# steam/<steamid>/ so consent is pre-granted whichever way the boot goes.
# Consent flag: settings.save mod_settings.mods_enabled == true
# (Core/Modding/ModSettings.cs [JsonPropertyName("mods_enabled")], gated in
# ModManager.TryLoadMod via PlayerAgreedToModLoading).
#
# SAFETY: the real bundle and the real save tree are only ever READ.
# This script never launches anything and never touches port 15526.
#
# Usage: headless_provision.sh [N] [BASE_DIR]
#   N        number of instances (default 1)
#   BASE_DIR instance root (default <repo>/headless-instances)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

N="${1:-1}"
BASE_DIR="${2:-$REPO_ROOT/headless-instances}"
PORT_BASE="${STS2_HEADLESS_PORT_BASE:-15600}"

REAL_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
REAL_MODS="$REAL_APP/Contents/MacOS/mods"
REAL_SAVE_ROOT="$HOME/Library/Application Support/SlayTheSpire2"

log()  { printf '[provision] %s\n' "$*"; }
die()  { printf '[provision] ERROR: %s\n' "$*" >&2; exit 1; }

[[ "$N" =~ ^[0-9]+$ && "$N" -ge 1 ]] || die "N must be a positive integer, got '$N'"
[[ -d "$REAL_APP" ]]  || die "real app bundle not found: $REAL_APP"
[[ -d "$REAL_MODS" ]] || die "real mods dir not found: $REAL_MODS"
for mod_file in STS2_MCP.dll STS2_MCP.json Sts2Recorder.dll Sts2Recorder.json; do
    [[ -f "$REAL_MODS/$mod_file" ]] || die "missing $REAL_MODS/$mod_file"
done
command -v python3 >/dev/null || die "python3 is required (settings.save patching)"

# Locate the real Steam settings.save (any steam/<id>/ account dir).
find_real_settings() {
    local candidate
    for candidate in "$REAL_SAVE_ROOT"/steam/*/settings.save; do
        if [[ -f "$candidate" ]]; then
            printf '%s' "$candidate"
            return 0
        fi
    done
    return 1
}

REAL_SETTINGS="$(find_real_settings || true)"
if [[ -n "$REAL_SETTINGS" ]]; then
    STEAM_ID="$(basename "$(dirname "$REAL_SETTINGS")")"
    log "template settings.save: $REAL_SETTINGS (steam id $STEAM_ID)"
else
    STEAM_ID="76561198323101015"
    log "WARNING: no real settings.save found; will synthesize a minimal one"
fi

# clone_app <dst> — APFS clonefile copy of the real bundle, cp -R fallback.
clone_app() {
    local dst="$1"
    if cp -Rc "$REAL_APP" "$dst" 2>/dev/null; then
        log "  app: APFS clone OK (cp -Rc)"
    else
        rm -rf "$dst"
        log "  app: cp -Rc unsupported here, falling back to plain cp -R (slow, full copy)"
        cp -R "$REAL_APP" "$dst"
    fi
}

# write_settings <template-or-empty> <dst> — copy the real settings.save
# structure and force: mods_enabled=true (+ mod_list entries for our two mods),
# seen_ea_disclaimer=true (skip EA popup), skip_intro_logo=true, windowed.
write_settings() {
    local template="$1" dst="$2"
    mkdir -p "$(dirname "$dst")"
    TEMPLATE="$template" DST="$dst" python3 - <<'PYEOF'
import json
import os
import sys

template = os.environ["TEMPLATE"]
dst = os.environ["DST"]

if template and os.path.isfile(template):
    with open(template, "r", encoding="utf-8") as handle:
        settings = json.load(handle)
else:
    # Minimal fallback mirroring Core/Saves/SettingsSave.cs defaults.
    settings = {"schema_version": 5}

patched = {
    **settings,
    "mod_settings": {
        "mods_enabled": True,
        "mod_list": [
            {"id": "STS2_MCP", "is_enabled": True, "source": "mods_directory"},
            {"id": "Sts2Recorder", "is_enabled": True, "source": "mods_directory"},
        ],
    },
    "seen_ea_disclaimer": True,
    "skip_intro_logo": True,
    "fullscreen": False,
}

with open(dst, "w", encoding="utf-8") as handle:
    json.dump(patched, handle, indent=2)
    handle.write("\n")
sys.stderr.write(f"[provision]   settings: {dst}\n")
PYEOF
}

log "provisioning $N instance(s) under $BASE_DIR (ports $((PORT_BASE + 1))..$((PORT_BASE + N)))"
mkdir -p "$BASE_DIR"

for i in $(seq 1 "$N"); do
    inst="$BASE_DIR/inst$i"
    port=$((PORT_BASE + i))
    app="$inst/SlayTheSpire2.app"
    mods="$app/Contents/MacOS/mods"
    log "instance $i -> $inst (MCP port $port)"
    mkdir -p "$inst" "$inst/recordings"

    if [[ -d "$app" ]]; then
        log "  app: clone already present, keeping it (rm -rf $app to force re-clone)"
    else
        clone_app "$app"
    fi
    [[ -x "$app/Contents/MacOS/Slay the Spire 2" ]] \
        || die "clone at $app has no executable Contents/MacOS/'Slay the Spire 2'"

    # Fresh mods dir with per-instance configs (always refreshed).
    rm -rf "$mods"
    mkdir -p "$mods"
    for mod_file in STS2_MCP.dll STS2_MCP.json Sts2Recorder.dll Sts2Recorder.json; do
        cp "$REAL_MODS/$mod_file" "$mods/$mod_file"
    done
    printf '{\n  "port": %d\n}\n' "$port" > "$mods/STS2_MCP.conf"
    printf '{\n  "output_root": "%s"\n}\n' "$inst/recordings" > "$mods/Sts2Recorder.conf"
    log "  mods: STS2_MCP(port=$port) + Sts2Recorder(output_root=$inst/recordings)"

    # Isolated home with consent pre-seeded at BOTH plausible save locations.
    save_root="$inst/home/Library/Application Support/SlayTheSpire2"
    write_settings "$REAL_SETTINGS" "$save_root/default/1/settings.save"
    write_settings "$REAL_SETTINGS" "$save_root/steam/$STEAM_ID/settings.save"
    mkdir -p "$save_root/logs"

    printf '%d\n' "$port" > "$inst/port"
done

log "done. next: scripts/headless_launch.sh <i> --base '$BASE_DIR'"
