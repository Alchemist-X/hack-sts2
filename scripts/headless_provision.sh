#!/usr/bin/env bash
# headless_provision.sh — provision N text-only STS2 workers over one runtime.
#
# Disk layout:
#   <base>/runtime/SlayTheSpire2.app/   ONE small shared overlay runtime
#   <base>/inst<i>/                     small process-local state only
#     home/                             isolated Godot user:// and saves
#     recordings/                       optional full recorder output
#     trajectories/                     compact training JSONL output
#     port, pid, game.log               worker metadata
#     SlayTheSpire2.app -> ../runtime/... compatibility symlink (no clone)
#
# v0.107.1 ships a 1.8 GiB Godot PCK plus a 171 MiB executable.  The overlay
# clonefiles only the executable and symlinks immutable Resources/Frameworks
# from STS2_GAME_APP (or the normal install).  It is ~172 MiB logical, adds
# almost no APFS physical blocks, and all workers share it.  Reimplementing the
# transition rules would create a different game.
#
# The sandbox-only MCP DLL reads STS2_MCP_PORT from each process environment.
# The recorder reads STS2_RECORDER_* variables, so neither mod needs a private
# mods directory.  The real app and human save tree are read-only inputs.
#
# Usage: headless_provision.sh [N] [BASE_DIR]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

N="${1:-1}"
BASE_DIR="${2:-$REPO_ROOT/headless-instances}"
PORT_BASE="${STS2_HEADLESS_PORT_BASE:-15600}"

REAL_APP="${STS2_GAME_APP:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app}"
REAL_SAVE_ROOT="$HOME/Library/Application Support/SlayTheSpire2"
RUNTIME_ROOT="$BASE_DIR/runtime"
RUNTIME_APP="$RUNTIME_ROOT/SlayTheSpire2.app"
RUNTIME_BUILD="$BASE_DIR/runtime-build"

log()  { printf '[provision] %s\n' "$*"; }
die()  { printf '[provision] ERROR: %s\n' "$*" >&2; exit 1; }

[[ "$N" =~ ^[0-9]+$ && "$N" -ge 1 ]] || die "N must be a positive integer, got '$N'"
[[ -d "$REAL_APP" ]] || die "real app bundle not found: $REAL_APP"
command -v python3 >/dev/null || die "python3 is required"
command -v dotnet >/dev/null || die "dotnet is required"

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
    log "WARNING: no real settings.save found; synthesizing a minimal one"
fi

create_overlay_runtime() {
    local dst="$1"
    local contents="$dst/Contents"
    mkdir -p "$contents/MacOS"
    if cp -c "$REAL_APP/Contents/MacOS/Slay the Spire 2" \
        "$contents/MacOS/Slay the Spire 2" 2>/dev/null; then
        log "runtime: executable clonefile OK"
    else
        log "runtime: clonefile unavailable; copying the 171 MiB executable once"
        cp "$REAL_APP/Contents/MacOS/Slay the Spire 2" \
            "$contents/MacOS/Slay the Spire 2"
    fi
    cp "$REAL_APP/Contents/Info.plist" "$contents/Info.plist"
    cp "$REAL_APP/Contents/PkgInfo" "$contents/PkgInfo"
    ln -s "$REAL_APP/Contents/Resources" "$contents/Resources"
    ln -s "$REAL_APP/Contents/Frameworks" "$contents/Frameworks"
    : > "$contents/.text-runtime-overlay-v1"
}

delete_generated_bundle() {
    local target="$1"
    [[ -d "$target" && ! -L "$target" ]] || return 0
    case "$target" in
        "$RUNTIME_ROOT"/*.generated-legacy.app) ;;
        *) die "refusing to delete unexpected generated path: $target" ;;
    esac
    find "$target" -depth -delete
}

write_settings() {
    local template="$1" dst="$2"
    mkdir -p "$(dirname "$dst")"
    TEMPLATE="$template" DST="$dst" python3 - <<'PYEOF'
import json
import os

template = os.environ["TEMPLATE"]
dst = os.environ["DST"]
if template and os.path.isfile(template):
    with open(template, "r", encoding="utf-8") as handle:
        settings = json.load(handle)
else:
    settings = {"schema_version": 5}
settings = {
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
    json.dump(settings, handle, indent=2)
    handle.write("\n")
PYEOF
}

seed_progress() {
    local save_root="$1"
    local source_dir="$REAL_SAVE_ROOT/steam/$STEAM_ID/modded/profile1/saves"
    local dst_dir="$save_root/default/1/modded/profile1/saves"
    local marker="$dst_dir/.human-profile-seed-v1"
    mkdir -p "$dst_dir"
    if [[ -f "$marker" ]]; then
        return 0
    fi
    local name
    for name in progress.save prefs.save; do
        if [[ -f "$source_dir/$name" ]]; then
            cp "$source_dir/$name" "$dst_dir/$name"
            log "  profile seed: $name (unlock/preferences only)"
        fi
    done
    : > "$marker"
}

mkdir -p "$BASE_DIR" "$RUNTIME_BUILD"
# Replacing shared DLLs while any worker has them loaded makes the pool
# non-reproducible.  Require an explicit pool stop before reprovisioning.
for pid_file in "$BASE_DIR"/inst*/pid; do
    [[ -f "$pid_file" ]] || continue
    active_pid="$(tr -cd '0-9' < "$pid_file")"
    if [[ -n "$active_pid" ]] && kill -0 "$active_pid" 2>/dev/null; then
        die "worker pid $active_pid is active; stop the pool before provisioning"
    fi
done
OVERLAY_MARKER="$RUNTIME_APP/Contents/.text-runtime-overlay-v1"
if [[ ! -d "$RUNTIME_APP" ]]; then
    legacy="$BASE_DIR/inst1/SlayTheSpire2.app"
    if [[ -d "$legacy" && ! -L "$legacy" ]]; then
        log "legacy inst1 bundle found; it will be replaced by a shared overlay"
    fi
    create_overlay_runtime "$RUNTIME_APP"
    if [[ -d "$legacy" && ! -L "$legacy" ]]; then
        legacy_app="$RUNTIME_ROOT/SlayTheSpire2.generated-legacy.app"
        [[ ! -e "$legacy_app" ]] || die "stale legacy path: $legacy_app"
        mv "$legacy" "$legacy_app"
        delete_generated_bundle "$legacy_app"
    fi
elif [[ ! -f "$OVERLAY_MARKER" ]]; then
    log "migrating the 2.2 GiB full shared clone to a ~172 MiB overlay"
    next_app="$RUNTIME_ROOT/SlayTheSpire2.next.app"
    legacy_app="$RUNTIME_ROOT/SlayTheSpire2.generated-legacy.app"
    [[ ! -e "$next_app" ]] || die "stale overlay staging path: $next_app"
    [[ ! -e "$legacy_app" ]] || die "stale legacy path: $legacy_app"
    create_overlay_runtime "$next_app"
    mv "$RUNTIME_APP" "$legacy_app"
    mv "$next_app" "$RUNTIME_APP"
    delete_generated_bundle "$legacy_app"
else
    log "runtime: refreshing overlay executable against $REAL_APP"
    next_bin="$RUNTIME_APP/Contents/MacOS/Slay the Spire 2.next"
    if ! cp -c "$REAL_APP/Contents/MacOS/Slay the Spire 2" "$next_bin" 2>/dev/null; then
        cp "$REAL_APP/Contents/MacOS/Slay the Spire 2" "$next_bin"
    fi
    mv "$next_bin" "$RUNTIME_APP/Contents/MacOS/Slay the Spire 2"
    cp "$REAL_APP/Contents/Info.plist" "$RUNTIME_APP/Contents/Info.plist"
fi
[[ "$RUNTIME_APP" != "$REAL_APP" ]] || die "runtime resolved to the real app"
RUNTIME_BIN="$RUNTIME_APP/Contents/MacOS/Slay the Spire 2"
[[ -x "$RUNTIME_BIN" ]] || die "runtime executable missing: $RUNTIME_BIN"

MCP_LOCK_FILE="$REPO_ROOT/config/sts2mcp.lock.json"
MCP_COMMIT="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["commit"])' "$MCP_LOCK_FILE")"
MCP_SOURCE_DLL="$REPO_ROOT/artifacts/mcp/$MCP_COMMIT/STS2_MCP.dll"
if [[ ! -f "$MCP_SOURCE_DLL" ]]; then
    log "pinned upstream MCP artifact is missing; building it now"
    "$SCRIPT_DIR/build_upstream_mcp.sh"
fi

log "building process-configurable worker MCP from pinned commit $MCP_COMMIT"
MCP_BUILD="$RUNTIME_BUILD/mcp"
"$SCRIPT_DIR/build_headless_mcp.sh" "$MCP_SOURCE_DLL" "$MCP_BUILD"
dotnet build "$REPO_ROOT/mod/Sts2Recorder.csproj" -c Release --nologo >/dev/null

RUNTIME_MODS="$RUNTIME_APP/Contents/MacOS/mods"
mkdir -p "$RUNTIME_MODS"
# Only remove known sandbox mod files; never recursively clear an unresolved path.
for name in STS2_MCP.dll STS2_MCP.json STS2_MCP.conf \
            Sts2Recorder.dll Sts2Recorder.json Sts2Recorder.conf; do
    [[ ! -e "$RUNTIME_MODS/$name" ]] || unlink "$RUNTIME_MODS/$name"
done
cp "$MCP_BUILD/STS2_MCP.dll" "$MCP_BUILD/STS2_MCP.json" "$RUNTIME_MODS/"
cp "$REPO_ROOT/mod/bin/Release/net9.0/Sts2Recorder.dll" "$RUNTIME_MODS/"
cp "$REPO_ROOT/mod/Sts2Recorder.json" "$RUNTIME_MODS/"
printf '{\n  "port": 15526\n}\n' > "$RUNTIME_MODS/STS2_MCP.conf"
printf '{\n  "snapshot_min_interval_ms": 800\n}\n' > "$RUNTIME_MODS/Sts2Recorder.conf"

log "provisioning $N lightweight worker(s); ports $((PORT_BASE + 1))..$((PORT_BASE + N))"
for i in $(seq 1 "$N"); do
    inst="$BASE_DIR/inst$i"
    port=$((PORT_BASE + i))
    mkdir -p "$inst/home" "$inst/recordings" "$inst/trajectories"

    # Compatibility path for old tooling.  A symlink is a few bytes and every
    # worker points at the same runtime; launch.sh does not depend on it.
    compat="$inst/SlayTheSpire2.app"
    if [[ -L "$compat" ]]; then
        unlink "$compat"
    elif [[ -d "$compat" ]]; then
        die "legacy private bundle remains at $compat; move it into runtime or remove it"
    fi
    ln -s "../runtime/SlayTheSpire2.app" "$compat"

    save_root="$inst/home/Library/Application Support/SlayTheSpire2"
    write_settings "$REAL_SETTINGS" "$save_root/default/1/settings.save"
    write_settings "$REAL_SETTINGS" "$save_root/steam/$STEAM_ID/settings.save"
    seed_progress "$save_root"
    mkdir -p "$save_root/logs"
    printf '%d\n' "$port" > "$inst/port"
    log "  inst$i: port=$port, private data only (runtime shared)"
done

RUNTIME_LOGICAL="$(du -sh "$RUNTIME_APP" | awk '{print $1}')"
log "done: one shared overlay runtime=$RUNTIME_LOGICAL; per-worker dirs contain no game bundle"
log "next: scripts/headless_launch.sh <i> --base '$BASE_DIR'"
