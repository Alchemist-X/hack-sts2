#!/usr/bin/env bash
# Game-version migration runner (docs/version-migration-playbook.md as steps, with a closing checklist).
#
#   scripts/version_update.sh <old_tree> <new_tree> --new-version X.Y.Z [--old-version A.B.C] [--out DIR]
#                             [--bundles DIR] [--bundles-v2 DIR] [--game-app PATH]
#                             [--execute|--run] [--full-tests] [--check-skeletons] [--export-knowledge]
#
# Dry-run by default: the analysis steps 2-4 run (they only write under <out>), every other step prints the
# exact command, and nothing in the repository changes. With --execute (alias --run) every automatable step runs
# in order, each with its own log under <out>/logs/, and the runner stops at the first failing step. Steps:
#    1 decompile the new build                          manual
#    2 kernel map check + version delta                 auto   scripts/version_delta.py -> <out>/manifest.json, report.md
#    3 work packets                                     auto   scripts/version_stubs.py -> <out>/packets/INDEX.md
#                                                              (--check-skeletons: compile every skeleton in sim Core)
#    4 recorder hook drift                              auto   scripts/hook_drift.py --tree <new> -> <out>/hook-drift.md
#                                                              (drift is reported, not fatal: it is a port task)
#    5 inventory regeneration                           manual (rewrites sim/specs/inventory)
#    6 registry version filter                          manual (GameVersions.Default)
#    7 knowledge export                                 reminder (auto only with --export-knowledge and the game)
#    8 build (0 warnings, 0 errors)                     auto
#    9 tests: the delta's filters + content soaks       auto   (--full-tests: the full suite, as the plan asks
#                                                              for kernel deltas)
#   10 server selftest                                  auto
#   11 G0 combat diff gate                              auto with --bundles or STS2SIM_G0_BUNDLES
#   12 G3 seed replay                                   auto with --bundles-v2
#   13 harness move                                     manual
# Closing: a checklist (also <out>/CHECKLIST.md) of every step with its state and the manual work left.
# Exit status: 0 when every executed step passed, 1 at the first failing step, 2 on a usage error.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -d /opt/homebrew/opt/dotnet@9/libexec ]; then
    export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
    export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
fi

usage() { sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-2}"; }

OLD_TREE="" NEW_TREE="" OLD_VER="" NEW_VER="" OUT="" BUNDLES="${STS2SIM_G0_BUNDLES:-}" BUNDLES_V2="" RUN=0
FULL_TESTS=0 CHECK_SKELETONS=0 EXPORT_KNOWLEDGE=0
GAME_APP="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app"
while [ $# -gt 0 ]; do
    case "$1" in
        --new-version) NEW_VER="$2"; shift 2 ;;
        --old-version) OLD_VER="$2"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        --bundles) BUNDLES="$2"; shift 2 ;;
        --bundles-v2) BUNDLES_V2="$2"; shift 2 ;;
        --game-app) GAME_APP="$2"; shift 2 ;;
        --run|--execute) RUN=1; shift ;;
        --full-tests) FULL_TESTS=1; shift ;;
        --check-skeletons) CHECK_SKELETONS=1; shift ;;
        --export-knowledge) EXPORT_KNOWLEDGE=1; shift ;;
        -h|--help) usage 0 ;;
        -*) echo "unknown option: $1" >&2; usage ;;
        *) if [ -z "$OLD_TREE" ]; then OLD_TREE="$1"; elif [ -z "$NEW_TREE" ]; then NEW_TREE="$1"; else usage; fi; shift ;;
    esac
done
[ -n "$OLD_TREE" ] && [ -n "$NEW_TREE" ] && [ -n "$NEW_VER" ] || usage
[ -d "$OLD_TREE" ] || { echo "old tree not found: $OLD_TREE" >&2; exit 2; }
[ -d "$NEW_TREE" ] || { echo "new tree not found: $NEW_TREE" >&2; exit 2; }
OUT="${OUT:-$REPO_ROOT/tmp/version-update/${OLD_VER:-old}-to-$NEW_VER}"
case "$OUT" in /*) ;; *) OUT="$PWD/$OUT" ;; esac
MANIFEST="$OUT/manifest.json"
LOGS="$OUT/logs"
mkdir -p "$LOGS"
CHECKLIST="$OUT/CHECKLIST.md"
: > "$LOGS/steps.tsv"

MODE=$([ "$RUN" = 1 ] && echo "EXECUTE" || echo "DRY-RUN")
T_START=$(date +%s)
CUR_STEP="" CUR_TITLE=""
step() {
    CUR_STEP="$1"; CUR_TITLE="$2"
    echo ""; echo "== [$1] $2"
}
note() { echo "   $*"; }
# state <step> <state> <detail>: one checklist row (state: done / passed / manual / reminder / skipped / FAILED)
state() { printf '%s\t%s\t%s\t%s\n' "$1" "$2" "$CUR_TITLE" "$3" >> "$LOGS/steps.tsv"; }
# cmd <0|1|2> <log name> <command...>: print the command; 1 = run it with --execute, 2 = run it in both modes
# (the analysis steps 2-4 write only under <out>); 0 = print only. Each run gets a log and a timing.
cmd() {
    local auto="$1" name="$2"; shift 2
    echo "   \$ $*"
    if { [ "$RUN" = 1 ] && [ "$auto" = 1 ]; } || [ "$auto" = 2 ]; then
        local t0 t1 rc
        t0=$(date +%s)
        set +e
        (cd "$REPO_ROOT" && "$@") > "$LOGS/$name.log" 2>&1 < /dev/null
        rc=$?
        set -e
        t1=$(date +%s)
        LAST_SECS=$((t1 - t0))
        tail -n "${VU_TAIL:-6}" "$LOGS/$name.log" | sed 's/^/   | /'
        echo "   -> rc $rc, ${LAST_SECS} s, log $LOGS/$name.log"
        LAST_RC=$rc
        return 0
    fi
    LAST_RC=""
    LAST_SECS=0
}
fail_if() {  # after cmd: stop the run when the step failed
    if [ -n "${LAST_RC:-}" ] && [ "$LAST_RC" != 0 ]; then
        state "$CUR_STEP" FAILED "rc $LAST_RC; $1 (log $LOGS/${2:-?}.log)"
        finish 1
    fi
}
plan() {  # plan <python expression over p (rerun_plan) and m (manifest)>
    [ -f "$MANIFEST" ] || { echo ""; return 0; }
    python3 -c "import json,sys; m=json.load(open(sys.argv[1])); p=m['rerun_plan']; print($1)" "$MANIFEST"
}
auto_state() {  # auto_state <detail>: 'passed' when the step ran, else 'dry-run'
    if [ -n "${LAST_RC:-}" ]; then state "$CUR_STEP" passed "$1 (${LAST_SECS} s)"; else state "$CUR_STEP" dry-run "$1"; fi
}

finish() {
    local rc="${1:-0}"
    {
        echo "# Version update checklist: ${OLD_VER:-old} -> $NEW_VER ($MODE)"
        echo ""
        echo "Old tree \`$OLD_TREE\`, new tree \`$NEW_TREE\`; output \`$OUT\`; $(date -u +%FT%TZ);"
        echo "wall $(( $(date +%s) - T_START )) s; runner exit $rc."
        echo ""
        echo "| step | state | what | detail |"
        echo "| --- | --- | --- | --- |"
        awk -F'\t' '{ gsub(/\|/, "\\|", $4); printf "| %s | %s | %s | %s |\n", $1, $2, $3, $4 }' "$LOGS/steps.tsv"
        echo ""
        echo "## Manual work left"
        echo ""
        awk -F'\t' '$2 == "manual" || $2 == "reminder" || $2 == "FAILED" || $2 == "skipped" || $2 == "action" \
            { printf "- [ ] [%s] %s: %s\n", $1, $3, $4 }' "$LOGS/steps.tsv"
    } > "$CHECKLIST"
    echo ""
    echo "================ checklist ($MODE, exit $rc) ================"
    awk -F'\t' '{ printf "  [%-4s] %-9s %s: %s\n", $1, $2, $3, $4 }' "$LOGS/steps.tsv"
    echo "checklist: $CHECKLIST"
    echo "version_update: $MODE $([ "$rc" = 0 ] && echo complete || echo STOPPED) ($(( $(date +%s) - T_START )) s)"
    exit "$rc"
}

echo "version_update ($MODE): $OLD_TREE (${OLD_VER:-?}) -> $NEW_TREE ($NEW_VER)"
echo "output: $OUT"

# ------------------------------------------------------------------------------------------------------------ 1
step 1 "Decompile the new build"
note "ILSpy with the SAME settings as the old tree (async/await decompilation on or off in both), output kept"
note "next to the old one, never overwritten; record version, game commit and sts2.dll SHA-256."
note "version_delta.py flags a settings mismatch (decompiler_style.mismatch) - re-decompile if it fires."
state 1 manual "decompile with the old tree's ILSpy settings; record version, commit, sts2.dll SHA-256"

# ------------------------------------------------------------------------------------------------------------ 2
step 2 "Kernel map + two-tree delta -> port list"
note "kernel map against the NEW tree: every changed / added kernel file must be mapped or no_port (curate"
note "sim/specs/kernel-map.json for the unmapped ones, then commit the map):"
cmd 0 kernel-map python3 scripts/kernel_map.py --tree "$NEW_TREE" --tree-version "$NEW_VER" --unmapped
cmd 2 version-delta python3 scripts/version_delta.py "$OLD_TREE" "$NEW_TREE" --old-version "${OLD_VER:-}" \
    --new-version "$NEW_VER" --out "$OUT"
fail_if "version_delta failed" version-delta
if [ -f "$MANIFEST" ]; then
    SUMMARY="$(plan '", ".join("%s %d" % (k, sum(v.values())) for k, v in sorted(m["summary"]["by_scope"].items()))')"
    note "summary: $SUMMARY"
    note "unique-edit content files: $(plan 'sum(1 for f in m["files"] if f["scope"]=="content" and f["delta"]!="cosmetic" and not f.get("bulk_only"))'); bulk patterns: $(plan 'len(m["bulk_edits"]["patterns"])'); unmapped kernel files: $(plan 'len(m.get("kernel_coverage", {}).get("unmapped", []))')"
    note "decompiler style mismatch: $(plan 'm["decompiler_style"]["mismatch"]')"
    auto_state "$SUMMARY; report $OUT/report.md"
    UNMAPPED="$(plan 'len(m.get("kernel_coverage", {}).get("unmapped", []))')"
    if [ "${UNMAPPED:-0}" != 0 ]; then
        state 2 action "$UNMAPPED changed kernel files are unmapped: curate sim/specs/kernel-map.json (report.md 'Kernel map coverage')"
    fi
    if [ "$(plan 'm["decompiler_style"]["mismatch"]')" = True ]; then
        state 2 action "decompiler style mismatch: re-decompile the new tree with the old settings, rerun"
    fi
else
    note "(no manifest yet: the plan-dependent steps below print generic commands)"
    auto_state "manifest not written yet"
fi

# ------------------------------------------------------------------------------------------------------------ 3
step 3 "Work packets (one per changed / added / removed content model)"
STUB_ARGS=()
# the skeleton compile check builds sim Core with the skeletons in a temporary directory: --execute only
[ "$CHECK_SKELETONS" = 1 ] && [ "$RUN" = 1 ] && STUB_ARGS+=(--check-skeletons)
if [ -f "$MANIFEST" ]; then
    cmd 2 version-stubs python3 scripts/version_stubs.py "$MANIFEST" --out "$OUT" ${STUB_ARGS[@]+"${STUB_ARGS[@]}"}
else
    cmd 0 version-stubs python3 scripts/version_stubs.py "$MANIFEST" --out "$OUT" ${STUB_ARGS[@]+"${STUB_ARGS[@]}"}
fi
fail_if "version_stubs failed (or a skeleton does not compile)" version-stubs
if [ -f "$OUT/packets/index.json" ]; then
    PK="$(python3 -c "import json,sys; i=json.load(open(sys.argv[1])); c=i['counts']; print('%d packets: new %d, removed %d, unique %d, bulk only %d; %d skeletons' % (len(i['packets']), c.get('new',0), c.get('removed',0), c.get('unique',0), c.get('bulk_only',0), sum(1 for r in i['packets'] if r['skeleton'])))" "$OUT/packets/index.json")"
    note "$PK; index $OUT/packets/INDEX.md"
    auto_state "$PK$([ ${#STUB_ARGS[@]} -gt 0 ] && echo '; every skeleton compiles in sim Core')"
    state 3 action "port the packets in $OUT/packets/INDEX.md (sim file per packet, spec lines, tests); W0 triage for unique edits absent from version-deltas.json"
else
    auto_state "packets not written yet"
fi

# ------------------------------------------------------------------------------------------------------------ 4
step 4 "Recorder hook drift (docs/hook-map.md + mod/src against the new tree)"
cmd 2 hook-drift python3 scripts/hook_drift.py --tree "$NEW_TREE" --json "$OUT/hook-drift.json" --md "$OUT/hook-drift.md"
if [ -f "$OUT/hook-drift.json" ]; then
    HD="$(python3 -c "import json,sys; r=json.load(open(sys.argv[1])); print('%d hook-map + %d mod targets, drift %d (%s)' % (r['targets']['hook_map'], r['targets']['mod'], r['drift'], ', '.join('%s %d' % kv for kv in sorted(r['counts'].items()))))" "$OUT/hook-drift.json")"
    DRIFT="$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['drift'])" "$OUT/hook-drift.json")"
    note "$HD; report $OUT/hook-drift.md"
    if [ "$DRIFT" != 0 ]; then
        # exit 1 = drift found: a port task for the recorder, not a runner failure
        state 4 DRIFT "$HD (${LAST_SECS} s)"
        state 4 action "$DRIFT hook target(s) drifted: update mod/src + a new docs/hook-map.md addendum, rebuild the recorder ($OUT/hook-drift.md)"
    else
        auto_state "$HD"
    fi
else
    [ "${LAST_RC:-0}" = 1 ] && LAST_RC=2
    fail_if "hook_drift did not write its report" hook-drift
    auto_state "not run"
fi

# ------------------------------------------------------------------------------------------------------------ 5
step 5 "Regenerate the inventory against the new tree (rewrites sim/specs/inventory: review the diff)"
cmd 0 inventory python3 sim/tools/inventory/extract_models.py --root "$NEW_TREE"
cmd 0 inventory-check python3 sim/tools/inventory/extract_models.py --root "$NEW_TREE" --check
note "then the derived manifests: sim/tools/inventory/classify/README.md, 'After a game update'."
state 5 manual "extract_models.py --root <new>, review, classify/run_all.py"

# ------------------------------------------------------------------------------------------------------------ 6
step 6 "Registry version filter"
CUR=$(grep -o 'Default = "[^"]*"' "$REPO_ROOT/sim/Sts2Sim.Core/Registry/Registries.cs" | head -1 || true)
note "current: GameVersions.$CUR; target: \"$NEW_VER\" (sim/Sts2Sim.Core/Registry/Registries.cs)"
if [ -f "$MANIFEST" ]; then
    note "VersionExclusive candidates (added/removed models): $(plan 'len(p["registry_version_filter"]["version_exclusive_candidates"])') $(plan '" ".join(p["registry_version_filter"]["version_exclusive_candidates"][:12])')"
fi
if [ "$CUR" = "Default = \"$NEW_VER\"" ]; then
    state 6 done "GameVersions.Default is $NEW_VER already"
else
    state 6 manual "GameVersions.Default -> \"$NEW_VER\"; [VersionExclusive] on added / removed models while both versions are served"
fi

# ------------------------------------------------------------------------------------------------------------ 7
step 7 "Knowledge export (localisation templates for the new version)"
if [ -d "$REPO_ROOT/knowledge/source/v$NEW_VER" ]; then
    note "knowledge/source/v$NEW_VER exists already"
    state 7 done "knowledge/source/v$NEW_VER present"
elif [ "$EXPORT_KNOWLEDGE" = 1 ] && [ -d "$GAME_APP" ]; then
    cmd 1 knowledge python3 scripts/export_sts2_localization.py --game-app "$GAME_APP" --output "knowledge/source/v$NEW_VER"
    fail_if "knowledge export failed" knowledge
    auto_state "knowledge/source/v$NEW_VER exported from $GAME_APP"
else
    note "reminder: export from the INSTALLED new build (the installed game must be $NEW_VER):"
    cmd 0 knowledge python3 scripts/export_sts2_localization.py --game-app "<game app>" --output "knowledge/source/v$NEW_VER"
    state 7 reminder "export knowledge/source/v$NEW_VER from the installed $NEW_VER game (--export-knowledge runs it)"
fi

# ------------------------------------------------------------------------------------------------------------ 8
step 8 "Build (0 warnings, 0 errors)"
# --no-incremental: an up-to-date project still reports its warnings (the 0-warning gate, as in gates.sh)
cmd 1 build dotnet build sim/Sts2Sim.sln -c Release --no-incremental -nologo -clp:Summary
fail_if "build failed" build
if [ "$RUN" = 1 ]; then
    W=$(grep -Eo '[0-9]+ (Warning\(s\)|个警告)' "$LOGS/build.log" | tail -1 | grep -Eo '^[0-9]+' || echo "?")
    E=$(grep -Eo '[0-9]+ (Error\(s\)|个错误)' "$LOGS/build.log" | tail -1 | grep -Eo '^[0-9]+' || echo "?")
    if [ "$W" != 0 ] || [ "$E" != 0 ]; then LAST_RC=1; fail_if "build: $W warnings, $E errors" build; fi
    auto_state "$W warnings, $E errors"
else
    auto_state "Release build"
fi

# ------------------------------------------------------------------------------------------------------------ 9
step 9 "Tests the delta touches"
FILTERS=""
if [ -f "$MANIFEST" ]; then
    FILTERS=$(plan '" ".join(p["test_filters"] + p["content_soak"] + ([p["run_soak_filter"]] if p["run_soak"] else []))')
    FULL=$(plan 'p["full_suite"]')
else
    FULL=True
fi
if [ "$FULL_TESTS" = 1 ] || [ -z "$FILTERS" ]; then
    cmd 1 tests dotnet run -c Release --no-build --project sim/Sts2Sim.Tests
    fail_if "full test suite failed" tests
    TS="full suite"
else
    # shellcheck disable=SC2086
    cmd 1 tests dotnet run -c Release --no-build --project sim/Sts2Sim.Tests -- $FILTERS
    fail_if "filtered tests failed" tests
    TS="filtered: $FILTERS"
fi
if [ "$RUN" = 1 ]; then
    TS="$TS; $(grep -E '^Summary:' "$LOGS/tests.log" | tail -1 || true)"
fi
auto_state "$TS"
if [ "$FULL" = True ] && [ "$FULL_TESTS" != 1 ]; then
    state 9 action "the plan asks for the full suite (kernel files changed): scripts/gates.sh, or rerun with --full-tests"
fi

# ----------------------------------------------------------------------------------------------------------- 10
step 10 "Server selftest (random full runs in process)"
cmd 1 selftest dotnet run -c Release --no-build --project sim/Sts2Sim.Server -- --selftest 300 --content ported_only
fail_if "selftest failed" selftest
auto_state "$([ "$RUN" = 1 ] && grep -E '^selftest ' "$LOGS/selftest.log" | tail -1 || echo '300 random runs, ported content')"

# ----------------------------------------------------------------------------------------------------------- 11
step 11 "G0 combat diff gate"
if [ -f "$MANIFEST" ]; then note "needed: $(plan 'p["g0_combat_diff"]')"; fi
if [ -n "$BUNDLES" ] && [ -d "$BUNDLES" ]; then
    cmd 1 g0 dotnet run -c Release --no-build --project sim/Sts2Sim.Diff -- "$BUNDLES" --report "$OUT/g0.json"
    fail_if "G0 failed" g0
    auto_state "$([ "$RUN" = 1 ] && grep -E '^overall:' "$LOGS/g0.log" | tail -1 || echo "bundles $BUNDLES")"
    note "old-version corpora replay with old seeding; new-version recordings (1-2 normal runs) must replay"
    note "green on the new seeding path."
    state 11 action "record 1-2 normal runs on $NEW_VER and add their bundles to the G0 set"
else
    note "pass --bundles <dir> or set STS2SIM_G0_BUNDLES (tmp/handoff/diff-bundles/bundles)"
    cmd 0 g0 dotnet run -c Release --project sim/Sts2Sim.Diff -- "<bundles>" --report "$OUT/g0.json"
    state 11 skipped "no G0 bundles: --bundles <dir> or STS2SIM_G0_BUNDLES"
fi

# ----------------------------------------------------------------------------------------------------------- 12
step 12 "G3 seed replay (whole runs)"
if [ -f "$MANIFEST" ]; then note "needed: $(plan 'p["g3_seed_replay"]')"; fi
if [ -n "$BUNDLES_V2" ]; then
    cmd 1 g3 dotnet run -c Release --no-build --project sim/Sts2Sim.Diff -- g3 "$BUNDLES_V2"
    fail_if "G3 failed" g3
    auto_state "bundles $BUNDLES_V2"
else
    note "pass --bundles-v2 <dir> (sts2rec export-bundles-v2 output); see sim/specs/diff-report-w4-run.md"
    cmd 0 g3 dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 "<bundles-v2>"
    state 12 skipped "no v2 bundles: --bundles-v2 <dir> (needs $NEW_VER recordings)"
fi

# ----------------------------------------------------------------------------------------------------------- 13
step 13 "Harness move"
note "recorder: rebuild + reinstall after the hook fixes (scripts/install_mod.sh, user-confirmed), one live check"
note "MCP rebuild: STS2_MCP_SOURCE_REPO=<repo> scripts/build_selection_mcp.sh (explicit paths in a worktree)"
note "rule evidence: python3 scripts/export_sts2_rule_evidence.py --decompiled-root <tree> --output knowledge/source/v$NEW_VER"
note "frozen save baseline: make_unlocked_save + baseline manifest SHA-256; stub-policy run_bench smoke"
note "benchmark semver: minor bump, rule-drift note = $OUT/report.md summary tables"
state 13 manual "recorder + MCP rebuild, rule evidence export, save baseline, benchmark minor bump with the rule-drift note"
CUR_TITLE="Definition of done"; state done manual "stub baseline on $NEW_VER + replay certification + diff gates green; scores pinned per game version"

finish 0
