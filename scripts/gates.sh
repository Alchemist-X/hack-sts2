#!/usr/bin/env bash
# Automated integration gates: the checks every wave integrator used to run by hand (sim/specs/integration.md).
#
#   scripts/gates.sh                      # full gate set (full test suite incl. soaks; ~8-10 min on an M4 Pro)
#   scripts/gates.sh --quick              # filtered tests instead of the full suite (no soaks; ~2-3 min)
#   scripts/gates.sh --bench "<label>"    # also run the combat + vec benches and append a row to sim/BENCH.md
#   scripts/gates.sh --skip g0,sts2rec    # skip named steps (reported as SKIP, not FAIL)
#   scripts/gates.sh --list               # print the step names and exit
#
# Steps (each prints PASS / FAIL / SKIP with its key numbers; a summary table at the end):
#   build-release  dotnet build sim/Sts2Sim.sln -c Release --no-incremental   0 warnings, 0 errors
#   build-debug    dotnet build sim/Sts2Sim.sln -c Debug --no-incremental     0 warnings, 0 errors
#   tests          full suite (default) or --quick filters 'Server Vec Parity EndToEnd SelfConsistency W1Gate';
#                  0 failed and 'PENDING total: 0'
#   selftest       Sts2Sim.Server --selftest 100 --content ported_only: all completed, 0 crashed, 0 server errors
#   g0             Sts2Sim.Diff <bundles>: 'overall:' line has bundles 4 (agree 4) and decisions agreed 69/69
#   pytest-eval    uv run --project sim/tools/eval pytest -q sim/tools/eval
#   pytest-vec     STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q
#   sts2rec        (cd sts2rec && uv run pytest -q); a failure is rerun once (known wall-clock flake in
#                  test_text_env.py::test_vector_steps_workers_concurrently); PASS on rerun is reported as such
#   py-scripts     python3 -m unittest discover -s scripts -p 'test_*.py' (version_delta, kernel_map; stdlib only), then
#                  python3 sim/tools/inventory/gen_prompt_sites.py --check (the fast-path prompt-site table
#                  sim/Sts2Sim.Core/Choice/PromptSites.g.cs is up to date with the sim's model defs), then
#                  python3 scripts/kernel_map.py --check when the rule-truth decompile is present (map up to date,
#                  no stale curated record, no unmapped kernel file of >= 50 lines); without the tree: tests only
#   bench-combat   (--bench only) Sts2Sim.Bench: decisions/s, Clone(), StateHash()
#   bench-vec      (--bench only) DOTNET_gcServer=1 Sts2Sim.Bench -- vec 1024 8 500 0 1024: env-steps/s
#
# Logs: tmp/gates/<UTC timestamp>/<step>.log, summary.tsv (step, status, seconds, key numbers) and SUMMARY.txt.
# Exit status: 0 when no step FAILed, 1 otherwise, 2 on a usage error.
#
# Env knobs:
#   STS2SIM_G0_BUNDLES        G0 bundle dir (default: the P0 set in the sim-source-recreation-9392df worktree)
#   GATES_G0_BUNDLES          expected bundle count (default 4)
#   GATES_G0_DECISIONS        expected decision count (default 69)
#   GATES_SELFTEST_RUNS       selftest games (default 100)
#   GATES_INCREMENTAL=1       plain incremental builds (faster; warnings of up-to-date projects are not re-reported)
#   GATES_BENCH_FILE          file the --bench row is appended to (default sim/BENCH.md)
#   GATES_OUT                 log directory (default tmp/gates/<timestamp>)
#   GATES_COMMIT              commit label when the tree has no .git (scripts/ci_linux.sh)
# Portable: bash 3.2 (macOS /bin/bash) and Linux; no GNU-only tools. A step timeout is applied only when
# 'timeout' or 'gtimeout' is on PATH (GATES_TIMEOUT seconds per step, default 3600).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 2

ALL_STEPS="build-release build-debug tests selftest g0 pytest-eval pytest-vec sts2rec py-scripts bench-combat bench-vec"
QUICK_FILTERS="Server Vec Parity EndToEnd SelfConsistency W1Gate"
QUICK=0
BENCH_LABEL=""
SKIP=","

usage() { sed -n '2,41p' "$0" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --quick) QUICK=1 ;;
    --bench) [[ $# -ge 2 && -n "$2" ]] || { echo "--bench needs a label" >&2; exit 2; }; BENCH_LABEL="$2"; shift ;;
    --bench=*) BENCH_LABEL="${1#--bench=}" ;;
    --skip) [[ $# -ge 2 ]] || { echo "--skip needs a list" >&2; exit 2; }; SKIP="$SKIP$2,"; shift ;;
    --skip=*) SKIP="$SKIP${1#--skip=}," ;;
    --list) for s in $ALL_STEPS; do echo "$s"; done; exit 0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done
for s in $(echo "$SKIP" | tr ',' ' '); do
  case " $ALL_STEPS " in *" $s "*) ;; *) echo "--skip: unknown step '$s' (see --list)" >&2; exit 2 ;; esac
done

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${GATES_OUT:-$REPO_ROOT/tmp/gates/$STAMP}"
mkdir -p "$OUT" || exit 2
: > "$OUT/summary.tsv"
G0_DIR="${STS2SIM_G0_BUNDLES:-/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/sim-source-recreation-9392df/tmp/handoff/diff-bundles/bundles}"
G0_WANT_BUNDLES="${GATES_G0_BUNDLES:-4}"
G0_WANT_DECISIONS="${GATES_G0_DECISIONS:-69}"
SELFTEST_RUNS="${GATES_SELFTEST_RUNS:-100}"
BENCH_FILE="${GATES_BENCH_FILE:-$REPO_ROOT/sim/BENCH.md}"
STEP_TIMEOUT="${GATES_TIMEOUT:-3600}"
TIMEOUT_BIN="$(command -v timeout || command -v gtimeout || true)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 UV_NO_PROGRESS=1
NOINC="--no-incremental"; [[ "${GATES_INCREMENTAL:-0}" == 1 ]] && NOINC=""

# GATES_COMMIT labels a tree without .git (scripts/ci_linux.sh runs on a 'git archive' export)
COMMIT="${GATES_COMMIT:-$(git rev-parse --short HEAD 2>/dev/null || echo unknown)}"
DIRTY=""; [[ -z "${GATES_COMMIT:-}" && -n "$(git status --porcelain --untracked-files=no 2>/dev/null)" ]] && DIRTY="+dirty"
loadavg() { (uptime 2>/dev/null || true) | sed -n 's/.*load average[s]*: *//p' | tr -d ','; }
{
  echo "gates: commit $COMMIT$DIRTY, $(date -u +%FT%TZ), host $(hostname), $(uname -sm)"
  echo "mode: $([[ $QUICK == 1 ]] && echo quick || echo full)${BENCH_LABEL:+, bench '$BENCH_LABEL'}; skip: ${SKIP//,/ }"
  echo "load average: $(loadavg)"
  echo "dotnet: $(dotnet --version 2>&1 | head -1); uv: $(uv --version 2>&1 | head -1)"
  echo "logs: $OUT"
} | tee "$OUT/header.txt"
echo

# ------------------------------------------------------------------------------------------------- step plumbing
# run_log <step> <command...>: run from the repo root, stdout+stderr to <step>.log; sets RC and SECS.
RC=0; SECS=0
run_log() {
  local name="$1"; shift
  local t0 t1
  t0=$(date +%s)
  if [[ -n "$TIMEOUT_BIN" ]]; then
    "$TIMEOUT_BIN" "$STEP_TIMEOUT" "$@" > "$OUT/$name.log" 2>&1 < /dev/null; RC=$?
  else
    "$@" > "$OUT/$name.log" 2>&1 < /dev/null; RC=$?
  fi
  t1=$(date +%s)
  SECS=$((t1 - t0))
}

ANY_FAIL=0
# record <step> <PASS|FAIL|SKIP> <seconds> <key numbers>
record() {
  local name="$1" status="$2" secs="$3" info="$4"
  printf '%s\t%s\t%s\t%s\n' "$name" "$status" "$secs" "$info" >> "$OUT/summary.tsv"
  printf '%-14s %-4s %5ss  %s\n' "$name" "$status" "$secs" "$info"
  [[ "$status" == FAIL ]] && ANY_FAIL=1
  return 0
}

skipped() { case "$SKIP" in *",$1,"*) return 0 ;; esac; return 1; }

# First capture group of the last line matching an anchored ERE: grab <file> <'^...(group)...$'-style regex>.
# '#' is the sed delimiter: the patterns contain '/' (69/69, env-steps/s) and '|' (alternation).
grab() { sed -nE "s#$2#\\1#p" "$1" 2>/dev/null | tail -1; }

# MSBuild summary, English or Chinese ('0 Warning(s)' / '0 个警告'; 'N Error(s)' / 'N 个错误').
build_counts() {
  local log="$1"
  W=$(grab "$log" '^[[:space:]]*([0-9]+) (Warning\(s\)|个警告).*')
  E=$(grab "$log" '^[[:space:]]*([0-9]+) (Error\(s\)|个错误).*')
  # Fallback: count distinct diagnostics when the summary block is absent (e.g. a crash before it).
  if [[ -z "$W" ]]; then W=$(grep -E ': warning [A-Z]+[0-9]+' "$log" 2>/dev/null | sort -u | wc -l | tr -d ' '); W="${W:-?}"; fi
  if [[ -z "$E" ]]; then E=$(grep -E ': error [A-Z]+[0-9]+' "$log" 2>/dev/null | sort -u | wc -l | tr -d ' '); E="${E:-?}"; fi
}

BUILD_OK=1

# ------------------------------------------------------------------------------------------------------- steps
step_build() {
  local name="$1" cfg="$2"
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  run_log "$name" dotnet build sim/Sts2Sim.sln -c "$cfg" $NOINC
  build_counts "$OUT/$name.log"
  local info="$cfg: $W warnings, $E errors (rc $RC)"
  if [[ $RC == 0 && "$W" == 0 && "$E" == 0 ]]; then
    record "$name" PASS "$SECS" "$info"
  else
    [[ "$cfg" == Release ]] && BUILD_OK=0
    grep -E ': (warning|error) [A-Z]+[0-9]+' "$OUT/$name.log" | sort -u | head -5 | sed 's/^/    /'
    record "$name" FAIL "$SECS" "$info"
  fi
}

step_tests() {
  local name=tests
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  if [[ $BUILD_OK != 1 ]]; then record "$name" FAIL 0 "not run: Release build failed"; return; fi
  local scope
  if [[ $QUICK == 1 ]]; then
    scope="quick ($QUICK_FILTERS)"
    # shellcheck disable=SC2086
    run_log "$name" dotnet run -c Release --no-build --project sim/Sts2Sim.Tests -- $QUICK_FILTERS
  else
    scope="full suite"
    run_log "$name" dotnet run -c Release --no-build --project sim/Sts2Sim.Tests
  fi
  local log="$OUT/$name.log" p f t ms pend
  p=$(grab "$log" '^Summary: ([0-9]+) passed.*')
  f=$(grab "$log" '^Summary: [0-9]+ passed, ([0-9]+) failed.*')
  t=$(grab "$log" '^Summary: .* ([0-9]+) total.*')
  ms=$(grab "$log" '^Summary: .*total \(([0-9]+) ms\).*')
  pend=$(grab "$log" '^PENDING total: ([0-9]+).*')
  local info="$scope: ${p:-?}/${t:-?} passed, ${f:-?} failed, PENDING ${pend:-?}, $(( ${ms:-0} / 1000 )) s in runner (rc $RC)"
  if [[ $RC == 0 && -n "$p" && "$f" == 0 && "$p" == "$t" && "$pend" == 0 ]]; then
    record "$name" PASS "$SECS" "$info"
  else
    sed -n '/^Failed tests:/,/^PENDING total/p' "$log" | head -12 | sed 's/^/    /'
    record "$name" FAIL "$SECS" "$info"
  fi
}

step_selftest() {
  local name=selftest
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  if [[ $BUILD_OK != 1 ]]; then record "$name" FAIL 0 "not run: Release build failed"; return; fi
  run_log "$name" dotnet run -c Release --no-build --project sim/Sts2Sim.Server -- \
    --selftest "$SELFTEST_RUNS" --content ported_only
  local log="$OUT/$name.log" runs comp crashed capped serr mean
  runs=$(grab "$log" '^selftest .*: ([0-9]+) runs.*')
  comp=$(grab "$log" '^selftest .*completed ([0-9]+).*')
  crashed=$(grab "$log" '^selftest .*crashed ([0-9]+).*')
  capped=$(grab "$log" '^selftest .*capped ([0-9]+).*')
  serr=$(grab "$log" '^selftest .*server errors ([0-9]+).*')
  mean=$(grab "$log" '^selftest .*mean steps ([0-9.]+).*')
  local info="${comp:-?}/${runs:-?} completed, ${crashed:-?} crashed, ${capped:-?} capped, ${serr:-?} server errors, mean ${mean:-?} steps (rc $RC)"
  if [[ $RC == 0 && "$runs" == "$SELFTEST_RUNS" && "$comp" == "$runs" && "$crashed" == 0 && "$serr" == 0 ]]; then
    record "$name" PASS "$SECS" "$info"
  else
    tail -5 "$log" | sed 's/^/    /'
    record "$name" FAIL "$SECS" "$info"
  fi
}

step_g0() {
  local name=g0
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  if [[ $BUILD_OK != 1 ]]; then record "$name" FAIL 0 "not run: Release build failed"; return; fi
  if [[ ! -d "$G0_DIR" ]]; then
    record "$name" FAIL 0 "bundle dir missing: $G0_DIR (set STS2SIM_G0_BUNDLES, or --skip g0)"; return
  fi
  run_log "$name" dotnet run -c Release --no-build --project sim/Sts2Sim.Diff -- "$G0_DIR" --report "$OUT/g0-report.json"
  local log="$OUT/$name.log" b agree hard err exc dec cmp unobs out
  b=$(grab "$log" '^overall: bundles ([0-9]+) .*')
  agree=$(grab "$log" '^overall: bundles [0-9]+ \(agree ([0-9]+),.*')
  hard=$(grab "$log" '^overall: .*hard divergence ([0-9]+).*')
  err=$(grab "$log" '^overall: .*, error ([0-9]+),.*')
  exc=$(grab "$log" '^overall: .*sim exception ([0-9]+)\).*')
  dec=$(grab "$log" '^overall: .*decisions agreed ([0-9]+)/[0-9]+.*')
  cmp=$(grab "$log" '^overall: .*decisions agreed [0-9]+/([0-9]+).*')
  unobs=$(grab "$log" '^overall: .*, ([0-9]+) unobserved.*')
  out=$(grab "$log" '^overall: .*outcome agreed ([0-9]+/[0-9]+).*')
  local info="bundles ${agree:-?}/${b:-?} agree (hard ${hard:-?}, error ${err:-?}, sim exception ${exc:-?}); decisions ${dec:-?}/${cmp:-?}, ${unobs:-?} unobserved; outcome ${out:-?} (rc $RC)"
  if [[ $RC == 0 && "$b" == "$G0_WANT_BUNDLES" && "$agree" == "$b" && "$dec" == "$G0_WANT_DECISIONS" \
        && "$cmp" == "$G0_WANT_DECISIONS" ]]; then
    record "$name" PASS "$SECS" "$info"
  else
    grep -E '^(overall|  v)' "$log" | head -5 | sed 's/^/    /'
    record "$name" FAIL "$SECS" "$info (want $G0_WANT_BUNDLES/$G0_WANT_BUNDLES, $G0_WANT_DECISIONS/$G0_WANT_DECISIONS)"
  fi
}

# pytest's last line: '== 43 passed, 1 skipped in 3.2s ==' or '43 passed in 3.2s'
pytest_line() { grep -E '[0-9]+ (passed|failed|error)' "$1" 2>/dev/null | tail -1 | sed -E 's/=+//g; s/^ +| +$//g'; }
pytest_ok() {  # rc 0 and no 'failed' / 'error' counts
  [[ $RC == 0 ]] || return 1
  local line; line="$(pytest_line "$1")"
  [[ -n "$line" ]] || return 1
  echo "$line" | grep -qE '[0-9]+ (failed|errors?)' && return 1
  return 0
}

step_pytest() {
  local name="$1" dir="$2"; shift 2
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  if ! command -v uv > /dev/null; then record "$name" FAIL 0 "uv not on PATH"; return; fi
  if [[ "$dir" == . ]]; then
    run_log "$name" "$@"
  else
    run_log "$name" bash -c 'cd "$1" && shift && exec "$@"' _ "$dir" "$@"
  fi
  local log="$OUT/$name.log"
  if pytest_ok "$log"; then
    record "$name" PASS "$SECS" "$(pytest_line "$log")"
    return
  fi
  if [[ "$name" == sts2rec ]]; then
    # One rerun for the known wall-clock flake; the first log is kept.
    local first secs1="$SECS"; first="$(pytest_line "$log")"
    mv "$log" "$OUT/$name.first.log"
    run_log "$name" bash -c 'cd "$1" && shift && exec "$@"' _ "$dir" "$@"
    local failed
    failed=$(grep -E '^FAILED ' "$OUT/$name.first.log" | sed -E 's/^FAILED ([^ ]+).*/\1/' | tr '\n' ' ')
    if pytest_ok "$log"; then
      record "$name" PASS "$((secs1 + SECS))" "$(pytest_line "$log") on rerun (first run: $first; ${failed:-?})"
      return
    fi
  fi
  grep -E '^(FAILED|ERROR) ' "$log" | head -8 | sed 's/^/    /'
  record "$name" FAIL "$SECS" "$(pytest_line "$log") (rc $RC)"
}

step_py_scripts() {
  local name=py-scripts
  if skipped "$name"; then record "$name" SKIP 0 "skipped"; return; fi
  run_log "$name" python3 -m unittest discover -s scripts -p 'test_*.py'
  local log="$OUT/$name.log" n ok=0 secs="$SECS" km info
  n=$(grab "$log" '^Ran ([0-9]+) tests? .*')
  [[ $RC == 0 ]] && grep -qE '^OK' "$log" && ok=1
  info="unittest ${n:-?} tests $([[ $ok == 1 ]] && echo OK || echo FAILED)"
  local ps
  python3 sim/tools/inventory/gen_prompt_sites.py --check > "$OUT/prompt-sites-check.log" 2>&1 < /dev/null; ps=$?
  info="$info; prompt_sites --check $([[ $ps == 0 ]] && echo OK || echo FAILED)"
  [[ $ps == 0 ]] || { ok=0; grep -E '^error:' "$OUT/prompt-sites-check.log" | head -6 | sed 's/^/    /'; }
  local tree="${STS2_DECOMPILE_ROOT:-/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled}"
  if [[ -d "$tree" ]]; then
    python3 scripts/kernel_map.py --check --tree "$tree" > "$OUT/kernel-map-check.log" 2>&1 < /dev/null; km=$?
    info="$info; kernel_map --check $([[ $km == 0 ]] && echo OK || echo FAILED): $(head -1 "$OUT/kernel-map-check.log" | sed 's/^kernel-map: //')"
    [[ $km == 0 ]] || { ok=0; grep -E '^(check|stale):' "$OUT/kernel-map-check.log" | head -6 | sed 's/^/    /'; }
  else
    info="$info; kernel_map --check skipped (no decompile tree)"
  fi
  if [[ $ok == 1 ]]; then record "$name" PASS "$secs" "$info"; else
    grep -E '^(FAIL|ERROR):' "$log" | head -6 | sed 's/^/    /'; record "$name" FAIL "$secs" "$info"; fi
}

BENCH_COMBAT=""; BENCH_VEC=""
step_bench_combat() {
  local name=bench-combat
  if [[ -z "$BENCH_LABEL" ]] || skipped "$name"; then record "$name" SKIP 0 "$([[ -z "$BENCH_LABEL" ]] && echo 'no --bench' || echo skipped)"; return; fi
  if [[ $BUILD_OK != 1 ]]; then record "$name" FAIL 0 "not run: Release build failed"; return; fi
  local la; la="$(loadavg)"
  run_log "$name" dotnet run -c Release --no-build --project sim/Sts2Sim.Bench
  local log="$OUT/$name.log" dps clone hash eps
  dps=$(grab "$log" '^playouts: .*=> ([0-9]+) decisions/s.*')
  clone=$(grab "$log" '^clone: .*=> clone ~([0-9.]+) us/clone.*')
  hash=$(grab "$log" '^statehash: .*=> ([0-9.]+) us/call.*')
  eps=$(grab "$log" '^playouts: .*decisions, ([0-9]+) episodes.*')
  local info="${dps:-?} decisions/s, Clone ${clone:-?} us, StateHash ${hash:-?} us, ${eps:-?} episodes in 5 s"
  if [[ $RC == 0 && -n "$dps" ]]; then
    BENCH_COMBAT="$(fmt_int "$dps")|$clone us|$hash us|$la"
    record "$name" PASS "$SECS" "$info"
  else
    record "$name" FAIL "$SECS" "$info (rc $RC)"
  fi
}

step_bench_vec() {
  local name=bench-vec
  if [[ -z "$BENCH_LABEL" ]] || skipped "$name"; then record "$name" SKIP 0 "$([[ -z "$BENCH_LABEL" ]] && echo 'no --bench' || echo skipped)"; return; fi
  if [[ $BUILD_OK != 1 ]]; then record "$name" FAIL 0 "not run: Release build failed"; return; fi
  local la; la="$(loadavg)"
  run_log "$name" env DOTNET_gcServer=1 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 1024 8 500 0 1024
  local log="$OUT/$name.log" sps avg mn mx rps ended bad
  sps=$(grab "$log" '^vec: env-steps/s=([0-9]+) .*')
  avg=$(grab "$log" '^vec: env-steps/s=.* avg=([0-9.]+) .*')
  mn=$(grab "$log" '^vec: env-steps/s=.* min=([0-9.]+) .*')
  mx=$(grab "$log" '^vec: env-steps/s=.* max=([0-9.]+) .*')
  rps=$(grab "$log" '^vec: env-steps/s=.* resets/s=([0-9]+) .*')
  ended=$(grab "$log" '^vec: env-steps/s=.* episodes ended=([0-9]+) .*')
  bad=$(sed -nE 's/^vec: env-steps\/s=.*(truncated=[0-9]+ sim_error=[0-9]+ content_gap=[0-9]+ illegal=[0-9]+).*/\1/p' "$log" | tail -1)
  local info="1024x8x500: ${sps:-?} env-steps/s, ms/batch ${avg:-?} (${mn:-?}-${mx:-?}), ${ended:-?} episodes; ${bad:-?}"
  if [[ $RC == 0 && -n "$sps" ]] && echo "$bad" | grep -qE 'sim_error=0 content_gap=0 illegal=0'; then
    BENCH_VEC="$(fmt_int "$sps")|$avg / $mn / $mx|$(fmt_int "${rps:-0}")|$ended|$la"
    record "$name" PASS "$SECS" "$info"
  else
    record "$name" FAIL "$SECS" "$info (rc $RC)"
  fi
}

fmt_int() { echo "$1" | awk '{s=$1; r=""; while (length(s) > 3) {r="," substr(s, length(s)-2) r; s=substr(s, 1, length(s)-3)} print s r}'; }

append_bench() {
  [[ -n "$BENCH_LABEL" ]] || return 0
  [[ -n "$BENCH_COMBAT$BENCH_VEC" ]] || { echo "bench: nothing to append"; return 0; }
  local today; today="$(date +%F)"
  {
    echo
    echo "## $BENCH_LABEL ($today, \`scripts/gates.sh --bench\`, commit \`$COMMIT$DIRTY\`)"
    echo
    echo "$(uname -sm), $(dotnet --version 2>/dev/null | head -1); Release. Combat: NIBBITS_WEAK, 1 run, 5 s. Vec: A0,"
    echo "pool 1024, Server GC, 500 batches. Load average sampled before each bench."
    if [[ -n "$BENCH_COMBAT" ]]; then
      echo
      echo "| Row | decisions/s | Clone() | StateHash() | load avg |"
      echo "|---|---|---|---|---|"
      echo "| $BENCH_LABEL | $(echo "$BENCH_COMBAT" | sed 's/|/ | /g') |"
    fi
    if [[ -n "$BENCH_VEC" ]]; then
      echo
      echo "| Row | N | threads | env-steps/s | ms/batch (avg / min / max) | resets/s | episodes ended | load avg |"
      echo "|---|---|---|---|---|---|---|---|"
      echo "| $BENCH_LABEL | 1024 | 8 | $(echo "$BENCH_VEC" | sed 's/|/ | /g') |"
    fi
  } >> "$BENCH_FILE"
  echo "bench: appended '$BENCH_LABEL' to $BENCH_FILE"
}

# --------------------------------------------------------------------------------------------------------- main
T0=$(date +%s)
step_build build-release Release
step_build build-debug Debug
step_tests
step_selftest
step_g0
step_pytest pytest-eval . uv run --project sim/tools/eval pytest -q sim/tools/eval
step_pytest pytest-vec . env STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q
step_pytest sts2rec sts2rec uv run pytest -q
step_py_scripts
step_bench_combat
step_bench_vec
append_bench

{
  echo
  echo "== gates summary ($COMMIT$DIRTY, $([[ $QUICK == 1 ]] && echo quick || echo full), $(( $(date +%s) - T0 )) s) =="
  printf '%-14s %-4s %6s  %s\n' step status secs "key numbers"
  while IFS="$(printf '\t')" read -r n s t i; do printf '%-14s %-4s %6s  %s\n' "$n" "$s" "$t" "$i"; done < "$OUT/summary.tsv"
  echo "result: $([[ $ANY_FAIL == 0 ]] && echo PASS || echo FAIL)   logs: $OUT"
} | tee "$OUT/SUMMARY.txt"
exit "$ANY_FAIL"
