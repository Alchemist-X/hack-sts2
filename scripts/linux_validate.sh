#!/usr/bin/env bash
# Validate the simulator, the play server and the Vec training interface on a Linux x86_64 host (default: Huginn).
#
#   scripts/linux_validate.sh [host]          # from a checkout on the Mac; host defaults to 'raven' (Huginn)
#   scripts/linux_validate.sh --attach <sha> [host]  # re-attach to a started run (poll, fetch logs) after a dropped link
#   scripts/linux_validate.sh --remote <dir>  # (internal) the part that runs on the Linux host
#
# Local part: 'git archive HEAD' -> tarball -> scp to <host>:$HOME/sts2sim-validation/<sha>/, start the remote part
# detached (nohup, so a dropped ssh session does not kill it), poll until it writes logs/DONE, copy logs/ back to
# tmp/linux-validation/<sha>/ and print the summary. Committed changes only: the tarball is HEAD, not the work tree.
#
# Remote part (no root needed): installs the .NET 9 SDK into $HOME/.dotnet (official dotnet-install.sh) and uv into
# $HOME/.local/bin if missing, then runs every step below with its own log and timing, writes logs/summary.tsv and
# logs/SUMMARY.txt, and removes the extracted tree and its build outputs (logs are kept; the SDK, uv and the NuGet
# cache stay in $HOME for the next run; LINUX_VALIDATE_PURGE=1 removes $HOME/.nuget/packages too).
#
# Env knobs: LINUX_VALIDATE_FULL_TIMEOUT (full test suite, s, default 1200), LINUX_VALIDATE_AOT_TIMEOUT (s, default 900),
# LINUX_VALIDATE_SKIP_FULL=1, LINUX_VALIDATE_SKIP_AOT=1, LINUX_VALIDATE_POLL (s, default 30).
set -uo pipefail

# ---------------------------------------------------------------------------------------------------------- remote
remote_main() {
  local work="$1"
  local src="$work/src" logs="$work/logs"
  mkdir -p "$logs"
  : > "$logs/summary.tsv"
  rm -f "$logs/DONE"

  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  export UV_NO_PROGRESS=1
  # A dangling ~/.cache/uv symlink (Huginn: -> /data/raven/cache/users/<user>/uv, target removed) makes every uv
  # command fail with 'Failed to initialize cache ... File exists'; use a run-local cache then (removed at cleanup).
  if [[ -z "${UV_CACHE_DIR:-}" && -L "$HOME/.cache/uv" && ! -e "$HOME/.cache/uv" ]]; then
    export UV_CACHE_DIR="$work/uv-cache"
    echo "note: $HOME/.cache/uv is a dangling symlink ($(readlink "$HOME/.cache/uv")); UV_CACHE_DIR=$UV_CACHE_DIR" \
      > "$logs/uv-cache-note.log"
  fi

  # step <name> <timeout-seconds|0> <command...>: run in $src, log to logs/<name>.log, append name/rc/seconds to the tsv
  step() {
    local name="$1" limit="$2"; shift 2
    local log="$logs/$name.log" t0 t1 rc
    echo "=== $name: $*" | tee -a "$logs/steps.log"
    t0=$(date +%s.%N)
    if [[ "$limit" != 0 ]]; then
      (cd "$src" && timeout --kill-after=30 "$limit" "$@") > "$log" 2>&1 < /dev/null; rc=$?
    else
      (cd "$src" && "$@") > "$log" 2>&1 < /dev/null; rc=$?
    fi
    t1=$(date +%s.%N)
    local secs; secs=$(awk -v a="$t0" -v b="$t1" 'BEGIN{printf "%.1f", b-a}')
    printf '%s\t%s\t%s\n' "$name" "$rc" "$secs" >> "$logs/summary.tsv"
    echo "    rc=$rc ${secs}s" | tee -a "$logs/steps.log"
    return $rc
  }

  {
    echo "date: $(date -u +%FT%TZ)"
    echo "host: $(hostname)"
    uname -a
    grep PRETTY_NAME /etc/os-release
    lscpu | grep -E 'Model name|^CPU\(s\)|Thread\(s\) per core|Core\(s\) per socket|Socket\(s\)|L3'
    free -g
    df -h "$HOME"
    uptime
    locale 2>/dev/null | head -3
  } > "$logs/machine.log" 2>&1

  # --- toolchain (user-level only)
  if ! "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^9\.'; then
    step 00-install-dotnet 900 bash -c "curl -fsSL https://dot.net/v1/dotnet-install.sh -o '$work/dotnet-install.sh' && \
      bash '$work/dotnet-install.sh' --channel 9.0 --install-dir '$HOME/.dotnet' && rm -f '$work/dotnet-install.sh'"
  else
    printf '00-install-dotnet\t0\t0.0\n' >> "$logs/summary.tsv"; echo "dotnet 9 SDK already in \$HOME/.dotnet" > "$logs/00-install-dotnet.log"
  fi
  if ! command -v uv > /dev/null; then
    step 00-install-uv 300 bash -c "curl -LsSf https://astral.sh/uv/install.sh | env UV_NO_MODIFY_PATH=1 sh"
  else
    printf '00-install-uv\t0\t0.0\n' >> "$logs/summary.tsv"; echo "uv already at $(command -v uv)" > "$logs/00-install-uv.log"
  fi
  {
    echo "dotnet: $(command -v dotnet)"; dotnet --info 2>&1 | head -40
    echo "uv: $(command -v uv) $(uv --version 2>&1)"
    echo "python3: $(python3 --version 2>&1)"
    echo "clang: $(command -v clang || echo missing)  gcc: $(command -v gcc || echo missing) $(gcc -dumpfullversion 2>/dev/null)"
  } > "$logs/versions.log" 2>&1

  # --- build + tests
  step 01-build 1200 dotnet build sim/Sts2Sim.sln -c Release
  step 02-tests-filtered 1800 dotnet run -c Release --no-build --project sim/Sts2Sim.Tests -- Server Vec EndToEnd SelfConsistency W1Gate
  if [[ "${LINUX_VALIDATE_SKIP_FULL:-0}" != 1 ]]; then
    step 03-tests-full "${LINUX_VALIDATE_FULL_TIMEOUT:-1200}" dotnet run -c Release --no-build --project sim/Sts2Sim.Tests
  fi
  step 04-selftest 1200 dotnet run -c Release --no-build --project sim/Sts2Sim.Server -- --selftest 300 --content ported_only

  # --- benches (C#)
  step 05-bench-combat 300 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench
  export DOTNET_gcServer=1
  step 06-bench-vec-1024x8 600 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 1024 8 500 0 1024
  # 500 batches is mostly JIT warm-up on a 4-core x86 VM (tier-up competes with the 8 workers); 3000 shows steady state
  step 06b-bench-vec-1024x8-3000 900 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 1024 8 3000 0 1024
  step 07-bench-vec-1024x1 900 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 1024 1 500 0 1024
  step 08-bench-vec-4096x8 900 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 4096 8 500 0 1024
  step 09-bench-vec-64x8 300 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec 64 8 500 0 1024
  unset DOTNET_gcServer

  # --- Python (hostfxr path)
  STS2VEC_REQUIRE_FULL=1 step 10-pytest-vec 1800 uv run --project sim/tools/vec pytest -q
  step 11-sts2vec-check 300 uv run --project sim/tools/vec sts2vec check
  # hostfxr discovery with no DOTNET_ROOT and no dotnet on PATH: the loader must find $HOME/.dotnet by itself
  step 11b-loader-discovery 300 env -u DOTNET_ROOT PATH="$HOME/.local/bin:/usr/bin:/bin" \
    uv run --project sim/tools/vec python -c \
    "from sts2vec import load; L = load(); print('hostfxr', L.hostfxr, 'root', L.dotnet_root, L.version(), L.gc_mode)"
  step 12-sts2vec-bench-1024x8 900 uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500
  # The path argument matters: without it pytest, started from the repository root with no config file there,
  # collects every test_*.py in the repository, including sim/tools/vec/tests (numpy is not in the eval env).
  step 13-pytest-eval 1200 uv run --project sim/tools/eval pytest -q sim/tools/eval

  # --- NativeAOT (optional; needs a C toolchain: clang, or gcc via the script's fallback)
  if [[ "${LINUX_VALIDATE_SKIP_AOT:-0}" != 1 ]]; then
    if step 14-aot-publish "${LINUX_VALIDATE_AOT_TIMEOUT:-900}" bash scripts/vec_publish_aot.sh; then
      STS2VEC_AOT_LIB="$src/sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.so" step 15-aot-check 300 uv run --project sim/tools/vec sts2vec check
      STS2VEC_AOT_LIB="$src/sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.so" step 16-aot-bench-1024x8 900 \
        uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500
    fi
  fi

  summarize "$logs" > "$logs/SUMMARY.txt"
  du -sh "$src" "$HOME/.dotnet" "$HOME/.nuget/packages" "$HOME/.cache/uv" 2>/dev/null >> "$logs/SUMMARY.txt"

  # --- cleanup: build outputs and the extracted tree; keep logs
  rm -rf "$src" "$work/src.tar.gz" "$work/uv-cache"
  if [[ "${LINUX_VALIDATE_PURGE:-0}" == 1 ]]; then rm -rf "$HOME/.nuget/packages"; fi
  df -h "$HOME" | tail -1 >> "$logs/SUMMARY.txt"
  date -u +%FT%TZ > "$logs/DONE"
}

# Key numbers out of the step logs (pure text; runs on either side).
summarize() {
  local logs="$1"
  echo "step	rc	seconds"
  cat "$logs/summary.tsv"
  echo
  grep -hE 'Warning\(s\)|Error\(s\)' "$logs/01-build.log" 2>/dev/null | sed 's/^ */build: /'
  for f in 02-tests-filtered 03-tests-full; do
    [[ -f "$logs/$f.log" ]] && echo "$f: $(grep -E '^Summary:' "$logs/$f.log" | tail -1)"
  done
  [[ -f "$logs/04-selftest.log" ]] && echo "selftest: $(tail -3 "$logs/04-selftest.log" | tr '\n' ' ')"
  [[ -f "$logs/05-bench-combat.log" ]] && grep -hiE 'decisions/s|us/clone|us/call' "$logs/05-bench-combat.log" | sed 's/^/combat: /'
  for f in "$logs"/0[6-9]*-bench-vec-*.log; do
    [[ -f "$f" ]] && grep -hiE 'env-steps/s' "$f" | head -3 | sed "s|^|$(basename "$f" .log): |"
  done
  for f in 10-pytest-vec 13-pytest-eval; do
    [[ -f "$logs/$f.log" ]] && echo "$f: $(grep -E '(passed|failed|error)' "$logs/$f.log" | tail -1)"
  done
  for f in 11-sts2vec-check 11b-loader-discovery 15-aot-check; do
    [[ -f "$logs/$f.log" ]] && echo "$f: $(tail -2 "$logs/$f.log" | tr '\n' ' ')"
  done
  for f in 12-sts2vec-bench-1024x8 16-aot-bench-1024x8; do
    [[ -f "$logs/$f.log" ]] && grep -hiE 'env-steps/s' "$logs/$f.log" | head -2 | sed "s|^|$f: |"
  done
  [[ -f "$logs/14-aot-publish.log" ]] && echo "aot: $(grep -E '^wrote |error' "$logs/14-aot-publish.log" | head -3 | tr '\n' ' ')"
  return 0
}

if [[ "${1:-}" == "--remote" ]]; then
  remote_main "$2"
  exit 0
fi

# ----------------------------------------------------------------------------------------------------------- local
# Usage: [host]                 archive HEAD, upload, start the remote run, then attach
#        --attach <sha> [host]  only poll an already-started run and fetch its logs (after a dropped connection)
ATTACH=""
if [[ "${1:-}" == "--attach" ]]; then ATTACH="$2"; shift 2; fi
HOST="${1:-raven}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"
SHA="${ATTACH:-$(git rev-parse --short HEAD)}"
LOCAL_OUT="$REPO_ROOT/tmp/linux-validation/$SHA"
mkdir -p "$LOCAL_OUT"
POLL="${LINUX_VALIDATE_POLL:-30}"
# Remote paths are relative to the remote $HOME (ssh/scp start there).
RDIR="sts2sim-validation/$SHA"

if [[ -z "$ATTACH" ]]; then
  TARBALL="$LOCAL_OUT/src.tar.gz"
  [[ -n "$(git status --porcelain)" ]] && echo "note: uncommitted changes are NOT validated (tarball = HEAD $SHA)" >&2
  git archive --format=tar.gz -o "$TARBALL" HEAD
  echo "archive: $TARBALL ($(du -h "$TARBALL" | cut -f1)) -> $HOST:$RDIR/"
  ssh -o BatchMode=yes "$HOST" "rm -rf '$RDIR/src' && mkdir -p '$RDIR/src' '$RDIR/logs'" || exit 1
  scp -q "$TARBALL" "$HOST:$RDIR/src.tar.gz" || exit 1
  rm -f "$TARBALL"
  # Forward the env knobs explicitly (ssh does not pass the local environment).
  KNOBS=""
  for k in LINUX_VALIDATE_FULL_TIMEOUT LINUX_VALIDATE_AOT_TIMEOUT LINUX_VALIDATE_SKIP_FULL LINUX_VALIDATE_SKIP_AOT LINUX_VALIDATE_PURGE; do
    [[ -n "${!k:-}" ]] && KNOBS+="$k=${!k} "
  done
  # Only the nohup command goes to the background: an '&' after an '&&' list backgrounds the whole list with the ssh
  # channel still open as its stdout, and ssh then never returns.
  ssh -o BatchMode=yes "$HOST" "cd '$RDIR' && tar xzf src.tar.gz -C src || exit 1
    $KNOBS nohup bash src/scripts/linux_validate.sh --remote \"\$HOME/$RDIR\" > logs/driver.log 2>&1 < /dev/null &
    echo started pid \$!" || exit 1
fi

echo "attached to $HOST:$RDIR; polling every ${POLL}s (progress: ssh $HOST tail -f $RDIR/logs/steps.log;" \
  "re-attach: scripts/linux_validate.sh --attach $SHA $HOST)"
T0=$(date +%s)
last=""
while true; do
  status="$(ssh -o BatchMode=yes -o ConnectTimeout=15 "$HOST" "test -f '$RDIR/logs/DONE' && echo DONE; tail -1 '$RDIR/logs/steps.log' 2>/dev/null" 2>/dev/null)"
  if [[ "$status" != "$last" ]]; then echo "[$(( $(date +%s) - T0 ))s] ${status//$'\n'/ | }"; last="$status"; fi
  [[ "$status" == DONE* ]] && break
  sleep "$POLL"
done

rm -rf "$LOCAL_OUT/logs"
scp -q -r "$HOST:$RDIR/logs" "$LOCAL_OUT/" || exit 1
echo
echo "logs: $LOCAL_OUT/logs"
cat "$LOCAL_OUT/logs/SUMMARY.txt"
# Nonzero exit when a required step failed (AOT and the full suite are informational).
awk -F'\t' '$2 != 0 && $1 !~ /^(03-tests-full|1[4-6]-aot)/ {bad=1} END {exit bad}' "$LOCAL_OUT/logs/summary.tsv"
