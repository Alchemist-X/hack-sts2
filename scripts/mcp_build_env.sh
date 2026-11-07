#!/usr/bin/env bash
# Shared by the MCP build scripts; source it after SCRIPT_DIR and REPO_ROOT are set.
#
# Everything version-specific comes from config/sts2mcp.lock.json: the upstream
# commit, the ordered sha256-pinned patch list, the game build the patches target
# (release_info.json version + sts2.dll sha256 per assembly dir) and the .NET SDK.
# A game update therefore fails here, with a pointer to the migration playbook,
# instead of in the C# compiler or at runtime.

MCP_LOCK_FILE="$REPO_ROOT/config/sts2mcp.lock.json"

mcp_die() { printf '[mcp-build] ERROR: %s\n' "$*" >&2; exit 1; }
mcp_log() { printf '[mcp-build] %s\n' "$*" >&2; }

# Print one lock value: mcp_lock 'd["commit"]' (None prints as an empty line).
mcp_lock() {
    [[ -f "$MCP_LOCK_FILE" ]] || mcp_die "lock file missing: $MCP_LOCK_FILE"
    python3 -I -c 'import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
value = eval(sys.argv[2], {}, {"d": d})
print("" if value is None else value)' "$MCP_LOCK_FILE" "$1"
}

# GAME_DIR and STS2_GAME_DATA_DIR (exported; build_headless_mcp.sh reads it too).
mcp_resolve_game() {
    GAME_DIR="${STS2_GAME_INSTALL_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
    if [[ -z "${STS2_GAME_DATA_DIR:-}" ]]; then
        if [[ -d "$GAME_DIR/SlayTheSpire2.app" ]]; then
            STS2_GAME_DATA_DIR="$GAME_DIR/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
        else
            STS2_GAME_DATA_DIR="$GAME_DIR/data_sts2_linuxbsd_x86_64"
        fi
    fi
    export STS2_GAME_DATA_DIR
    [[ -f "$STS2_GAME_DATA_DIR/sts2.dll" ]] \
        || mcp_die "game assemblies missing: $STS2_GAME_DATA_DIR/sts2.dll (set STS2_GAME_INSTALL_DIR)"
}

# The installed game's version from its release_info.json, or "unknown".
mcp_game_version() {
    local candidate
    for candidate in "$GAME_DIR/SlayTheSpire2.app/Contents/Resources/release_info.json" \
                     "$GAME_DIR/release_info.json" \
                     "$(dirname "$STS2_GAME_DATA_DIR")/release_info.json"; do
        if [[ -f "$candidate" ]]; then
            python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["version"])' "$candidate"
            return 0
        fi
    done
    printf 'unknown\n'
}

# Refuse a game build the lock does not pin. STS2_MCP_ALLOW_UNPINNED_GAME=1 is for
# migration work only (see what breaks on a new build); it sets MCP_UNPINNED_GAME=1.
mcp_check_game() {
    local want_version want_sha got_version got_sha platform
    platform="$(basename "$STS2_GAME_DATA_DIR")"
    want_version="$(mcp_lock 'd["game"]["version"]')"
    want_sha="$(mcp_lock "d['game']['sts2_dll_sha256'].get('$platform')")"
    got_version="$(mcp_game_version)"
    got_sha="$(shasum -a 256 "$STS2_GAME_DATA_DIR/sts2.dll" | awk '{print $1}')"
    MCP_UNPINNED_GAME=0
    if [[ "${got_version#v}" == "${want_version#v}" && ( -z "$want_sha" || "$got_sha" == "$want_sha" ) ]]; then
        mcp_log "game $got_version ($platform sts2.dll $got_sha) matches the lock"
        return 0
    fi
    local message="installed game $got_version ($platform sts2.dll $got_sha) is not the build the lock pins ($want_version, sts2.dll ${want_sha:-unpinned for $platform})"
    if [[ "${STS2_MCP_ALLOW_UNPINNED_GAME:-0}" == "1" ]]; then
        mcp_log "WARNING: $message; building anyway because STS2_MCP_ALLOW_UNPINNED_GAME=1 (migration work only)"
        MCP_UNPINNED_GAME=1
        return 0
    fi
    mcp_die "$message. A game update needs a migration (docs/version-migration-playbook.md); STS2_MCP_ALLOW_UNPINNED_GAME=1 builds anyway to see what breaks"
}

mcp_check_sdk() {
    local want got
    want="$(mcp_lock 'd["build"]["dotnet_sdk"]')"
    command -v dotnet >/dev/null || mcp_die "dotnet is required (SDK $want)"
    got="$(dotnet --version)"
    [[ "$got" == "$want" ]] || mcp_die "requires .NET SDK $want (dotnet --version: $got)"
}

# SOURCE_REPO and COMMIT. The STS2MCP clone sits beside the *main* checkout; from a
# linked worktree dirname(REPO_ROOT) is the worktrees dir, so follow
# .git (gitdir) -> commondir to the main checkout instead of guessing.
mcp_resolve_source_repo() {
    local main="$REPO_ROOT" gitdir common
    if [[ -f "$REPO_ROOT/.git" ]]; then
        gitdir="$(sed -n 's/^gitdir: //p' "$REPO_ROOT/.git")"
        [[ "$gitdir" == /* ]] || gitdir="$REPO_ROOT/$gitdir"
        if [[ -f "$gitdir/commondir" ]]; then
            common="$(cd "$gitdir" && cd "$(cat commondir)" && pwd)"
            main="$(dirname "$common")"
        fi
    fi
    SOURCE_REPO="${STS2_MCP_SOURCE_REPO:-$(dirname "$main")/STS2MCP}"
    [[ -e "$SOURCE_REPO/.git" ]] \
        || mcp_die "STS2MCP checkout not found: $SOURCE_REPO (set STS2_MCP_SOURCE_REPO or clone $(mcp_lock 'd["repository"]') there)"
    COMMIT="$(mcp_lock 'd["commit"]')"
}
