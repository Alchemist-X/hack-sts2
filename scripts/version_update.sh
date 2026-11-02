#!/usr/bin/env bash
# Game-version migration checklist runner (docs/version-migration-playbook.md as steps).
#
#   scripts/version_update.sh <old_tree> <new_tree> --new-version X.Y.Z [--old-version A.B.C]
#                             [--out DIR] [--bundles DIR] [--bundles-v2 DIR] [--game-app PATH] [--run]
#
# Dry-run by default: every step prints what to do and the exact command; nothing is executed except read-only
# probes (current registry version, existing knowledge export, an existing delta manifest). With --run the
# automatable steps execute in order and the runner stops at the first failure:
#   2 version delta (scripts/version_delta.py)      6 build (0 warnings 0 errors)
#   5 knowledge export (needs the installed game)   7 filtered tests + content soak, full suite when the plan says so
#   8 server selftest                               9 G0 combat diff gate (needs --bundles)
#  10 G3 seed replay (needs --bundles-v2)
# Steps 1, 3, 4, 11 stay manual (decompile, inventory regeneration review, registry edit, harness move); the
# runner prints them with the delta's numbers so the checklist reads in one place.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -d /opt/homebrew/opt/dotnet@9/libexec ]; then
    export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
    export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
fi

usage() { sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-2}"; }

OLD_TREE="" NEW_TREE="" OLD_VER="" NEW_VER="" OUT="" BUNDLES="" BUNDLES_V2="" RUN=0
GAME_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
while [ $# -gt 0 ]; do
    case "$1" in
        --new-version) NEW_VER="$2"; shift 2 ;;
        --old-version) OLD_VER="$2"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        --bundles) BUNDLES="$2"; shift 2 ;;
        --bundles-v2) BUNDLES_V2="$2"; shift 2 ;;
        --game-app) GAME_APP="$2"; shift 2 ;;
        --run) RUN=1; shift ;;
        -h|--help) usage 0 ;;
        -*) echo "unknown option: $1" >&2; usage ;;
        *) if [ -z "$OLD_TREE" ]; then OLD_TREE="$1"; elif [ -z "$NEW_TREE" ]; then NEW_TREE="$1"; else usage; fi; shift ;;
    esac
done
[ -n "$OLD_TREE" ] && [ -n "$NEW_TREE" ] && [ -n "$NEW_VER" ] || usage
[ -d "$OLD_TREE" ] || { echo "old tree not found: $OLD_TREE" >&2; exit 2; }
[ -d "$NEW_TREE" ] || { echo "new tree not found: $NEW_TREE" >&2; exit 2; }
OUT="${OUT:-$REPO_ROOT/tmp/version-delta/${OLD_VER:-old}-to-$NEW_VER}"
case "$OUT" in /*) ;; *) OUT="$PWD/$OUT" ;; esac
MANIFEST="$OUT/manifest.json"

MODE=$([ "$RUN" = 1 ] && echo "RUN" || echo "DRY-RUN")
step() { echo ""; echo "== [$1] $2"; }
note() { echo "   $*"; }
cmd() {  # cmd <automatable:0|1> <command...>
    local auto="$1"; shift
    echo "   \$ $*"
    if [ "$RUN" = 1 ] && [ "$auto" = 1 ]; then
        (cd "$REPO_ROOT" && "$@") || { echo ""; echo "version_update: FAIL at: $*"; exit 1; }
    fi
}
plan() {  # plan <python expression over p (rerun_plan) and m (manifest)>
    [ -f "$MANIFEST" ] || { echo ""; return 0; }
    python3 -c "import json,sys; m=json.load(open(sys.argv[1])); p=m['rerun_plan']; print($1)" "$MANIFEST"
}

echo "version_update ($MODE): $OLD_TREE ($OLD_VER) -> $NEW_TREE ($NEW_VER)"
echo "delta output: $OUT"

step 1 "Decompile the new build (manual)"
note "ILSpy with the SAME settings as the old tree (async/await decompilation on or off in both), output kept"
note "next to the old one, never overwritten; record version, game commit and sts2.dll SHA-256."
note "version_delta.py flags a settings mismatch (decompiler_style.mismatch) - re-decompile if it fires."

step 2 "Two-tree delta -> port list (automated)"
cmd 1 python3 scripts/version_delta.py "$OLD_TREE" "$NEW_TREE" --old-version "${OLD_VER:-}" --new-version "$NEW_VER" --out "$OUT"
if [ -f "$MANIFEST" ]; then
    note "summary: $(plan '", ".join("%s %d" % (k, sum(v.values())) for k, v in sorted(m["summary"]["by_scope"].items()))')"
    note "unique-edit content files: $(plan 'sum(1 for f in m["files"] if f["scope"]=="content" and f["delta"]!="cosmetic" and not f.get("bulk_only"))'); bulk patterns: $(plan 'len(m["bulk_edits"]["patterns"])')"
    note "decompiler style mismatch: $(plan 'm["decompiler_style"]["mismatch"]')"
    note "read $OUT/report.md: Bulk edits, Changed content/kernel files, Spec rules to re-read."
else
    note "(no manifest yet: the plan-dependent steps below print generic commands)"
fi

step 3 "Regenerate the inventory against the new tree (manual review: rewrites sim/specs/inventory)"
cmd 0 python3 sim/tools/inventory/extract_models.py --root "$NEW_TREE"
cmd 0 python3 sim/tools/inventory/extract_models.py --root "$NEW_TREE" --check
note "then the derived manifests: sim/tools/inventory/classify/README.md, 'After a game update'."

step 4 "Registry version filter (manual edit)"
CUR=$(grep -o 'Default = "[^"]*"' "$REPO_ROOT/sim/Sts2Sim.Core/Registry/Registries.cs" | head -1 || true)
note "current: GameVersions.$CUR; target: \"$NEW_VER\" (sim/Sts2Sim.Core/Registry/Registries.cs)"
if [ "$CUR" = "Default = \"$NEW_VER\"" ]; then note "already at the target version"; fi
if [ -f "$MANIFEST" ]; then
    note "must move: $(plan 'p["registry_version_filter"]["must_move"]'); VersionExclusive candidates (added/removed models): $(plan 'len(p["registry_version_filter"]["version_exclusive_candidates"])')"
    note "  $(plan '" ".join(p["registry_version_filter"]["version_exclusive_candidates"][:20])')"
fi
note "port added models, delete removed ones, or tag [VersionExclusive(\"<ver>\")] when both versions stay served."

step 5 "Knowledge export (localisation templates, per version)"
if [ -d "$REPO_ROOT/knowledge/source/v$NEW_VER" ]; then
    note "knowledge/source/v$NEW_VER exists already (manifest game_version: $(python3 -c "import json,sys; print(json.load(open(sys.argv[1])).get('game_version'))" "$REPO_ROOT/knowledge/source/v$NEW_VER/manifest.json" 2>/dev/null || echo '?'))"
elif [ -d "$GAME_APP" ]; then
    cmd 1 python3 scripts/export_sts2_localization.py --game-app "$GAME_APP" --output "knowledge/source/v$NEW_VER"
else
    note "installed game not found at $GAME_APP - run on the game machine:"
    cmd 0 python3 scripts/export_sts2_localization.py --output "knowledge/source/v$NEW_VER"
fi

step 6 "Build (0 warnings 0 errors)"
cmd 1 dotnet build sim/Sts2Sim.sln -c Release

step 7 "Tests the delta touches"
if [ -f "$MANIFEST" ]; then
    FILTERS=$(plan '" ".join(p["test_filters"] + p["content_soak"] + ([p["run_soak_filter"]] if p["run_soak"] else []))')
    FULL=$(plan 'p["full_suite"]')
    if [ "$FULL" = "True" ]; then
        note "kernel files changed: run the full suite (about 6-7 min, includes the 10^4-run soak); the filters"
        note "below are the tests that reference the changed models, for iterating on the port:"
        # shellcheck disable=SC2086
        [ -n "$FILTERS" ] && cmd 0 dotnet run -c Release --project sim/Sts2Sim.Tests -- $FILTERS
        cmd 1 dotnet run -c Release --project sim/Sts2Sim.Tests
    elif [ -n "$FILTERS" ]; then
        # shellcheck disable=SC2086
        cmd 1 dotnet run -c Release --project sim/Sts2Sim.Tests -- $FILTERS
    else
        note "no test references the changed models"
    fi
else
    cmd 1 dotnet run -c Release --project sim/Sts2Sim.Tests
fi

step 8 "Server selftest (random full runs in process)"
cmd 1 dotnet run -c Release --project sim/Sts2Sim.Server -- --selftest 300 --content ported_only

step 9 "G0 combat diff gate"
if [ -f "$MANIFEST" ]; then note "needed: $(plan 'p["g0_combat_diff"]')"; fi
if [ -n "$BUNDLES" ]; then
    cmd 1 dotnet run -c Release --project sim/Sts2Sim.Diff -- "$BUNDLES" --report "$OUT/g0.json"
else
    note "pass --bundles <dir> (tmp/handoff/diff-bundles/bundles); old-version corpora replay with old seeding,"
    note "new-version recordings (1-2 normal runs) must replay green on the new seeding path."
    cmd 0 dotnet run -c Release --project sim/Sts2Sim.Diff -- "<bundles>" --report "$OUT/g0.json"
fi

step 10 "G3 seed replay (whole runs)"
if [ -f "$MANIFEST" ]; then note "needed: $(plan 'p["g3_seed_replay"]')"; fi
if [ -n "$BUNDLES_V2" ]; then
    cmd 1 dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 "$BUNDLES_V2"
else
    note "pass --bundles-v2 <dir> (sts2rec export-bundles-v2 output); see sim/specs/diff-report-w4-run.md"
    cmd 0 dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 "<bundles-v2>"
fi

step 11 "Harness move (manual; playbook step 5)"
note "MCP rebuild: STS2_MCP_SOURCE_REPO=<repo> scripts/build_selection_mcp.sh (explicit paths in a worktree)"
note "rule evidence: python3 scripts/export_sts2_rule_evidence.py --decompiled-root <tree> --output knowledge/source/v$NEW_VER"
note "frozen save baseline: make_unlocked_save + baseline manifest SHA-256; stub-policy run_bench smoke"
note "benchmark semver: minor bump, rule-drift note = $OUT/report.md summary tables"

step done "Definition of done"
note "stub baseline runs on $NEW_VER + replay certification + diff gates green; scores pinned per game version."
echo ""
echo "version_update: $MODE complete"
