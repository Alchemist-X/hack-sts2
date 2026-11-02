#!/usr/bin/env bash
# Linux CI: run scripts/gates.sh inside the toolchain container (docker/ci.Dockerfile: .NET 9 SDK + python3 + uv)
# on a 'git archive' export of HEAD. Local docker when available, otherwise a Linux host over ssh (default
# 'raven' = Huginn, where docker is installed and the user is in the docker group).
#
#   scripts/ci_linux.sh                       # gates.sh --quick --skip g0 (default gate arguments)
#   scripts/ci_linux.sh -- --skip g0          # anything after '--' replaces the gate arguments (here: full suite)
#   scripts/ci_linux.sh --host raven          # force the ssh path (default when 'docker info' fails locally)
#   scripts/ci_linux.sh --local               # force local docker
#   scripts/ci_linux.sh --attach <sha> [--host H]   # re-attach to a started remote run (poll, fetch logs)
#   scripts/ci_linux.sh --remote <workdir> <commit> [gates args...]   # (internal) the part next to docker
#
# Flow: git archive HEAD -> <workdir>/src.tar.gz -> (scp) -> extract to <workdir>/src -> docker build the
# toolchain image (cached after the first run) -> docker run with src bind-mounted at /work, the caller's uid/gid,
# HOME inside the tree and persistent NuGet / uv caches -> gates.sh -> logs/ (gates logs, image.log, timing.tsv,
# SUMMARY.txt) -> the extracted tree is removed (CI_LINUX_KEEP=1 keeps it). The remote part runs under nohup, so
# a dropped ssh link does not stop it; the local part polls for logs/DONE and copies logs/ back to
# tmp/ci-linux/<sha>/. Committed changes only: the export is HEAD, not the work tree. Exit status = gates.sh's.
#
# Env knobs:
#   CI_LINUX_HOST          ssh host (default raven)
#   CI_LINUX_REMOTE_BASE   remote work root (default /data/raven/hack-sts2/tmp/ci-linux; Huginn's root fs is small)
#   CI_LINUX_CACHE         NuGet / uv cache root, kept between runs (default <work root>/cache)
#   CI_LINUX_IMAGE         toolchain image tag (default sts2sim-ci:latest)
#   CI_LINUX_POLL          poll interval, s (default 20)
#   CI_LINUX_KEEP=1        keep the extracted tree after the run
#   CI_LINUX_CLEANUP       1 = remove the image and the build-cache records the run created (default 1 over ssh,
#                          0 locally). Huginn keeps docker's snapshots and build cache on the ~3 GB root fs.
#   CI_LINUX_MIN_FREE_MB   refuse to pull the SDK image with less free space on / (default 2500)
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${CI_LINUX_IMAGE:-sts2sim-ci:latest}"

# ---------------------------------------------------------------------------------------------------------- remote
remote_main() {
  local work="$1" commit="$2"; shift 2
  local logs="$work/logs" src="$work/src"
  local cache="${CI_LINUX_CACHE:-$(dirname "$work")/cache}"
  [[ "$cache" == /* ]] || cache="$PWD/$cache"
  mkdir -p "$logs" "$cache/nuget" "$cache/uv"
  rm -f "$logs/DONE"
  : > "$logs/timing.tsv"
  local t_all t0 t1 rc_img rc
  t_all=$(date +%s)
  {
    echo "ci_linux: commit $commit, $(date -u +%FT%TZ), host $(hostname), $(uname -sm), $(nproc 2>/dev/null || echo ?) cpus"
    echo "gates args: $*"
    echo "image: $IMAGE; cache: $cache"
    echo "docker: $(docker version --format '{{.Server.Version}}' 2>&1 | head -1)"
    echo "load average: $(uptime | sed -n 's/.*load average[s]*: *//p')"
  } > "$logs/header.txt"

  t0=$(date +%s)
  rm -rf "$src"; mkdir -p "$src"
  tar -xzf "$work/src.tar.gz" -C "$src"; rc=$?
  t1=$(date +%s); printf 'extract\t%s\t%s\n' "$rc" "$((t1 - t0))" >> "$logs/timing.tsv"

  # Docker's image / build-cache storage may sit on a small root fs even when the data root does not (Huginn:
  # containerd snapshotter on / with ~3 GB free). The SDK image needs ~2 GB there; refuse rather than fill it.
  local had_image=0 free_mb cache_before=""
  docker image inspect "$IMAGE" > /dev/null 2>&1 && had_image=1
  free_mb=$(df -Pk / | awk 'NR==2 {print int($4/1024)}')
  echo "root fs free: ${free_mb} MB; image cached: $had_image" >> "$logs/header.txt"
  if [[ $had_image == 0 && "${free_mb:-0}" -lt "${CI_LINUX_MIN_FREE_MB:-2500}" ]]; then
    echo "ci_linux: only ${free_mb} MB free on / (docker storage); need ${CI_LINUX_MIN_FREE_MB:-2500} MB to pull" \
      "the SDK image. Free space or set CI_LINUX_MIN_FREE_MB." | tee "$logs/SUMMARY.txt"
    printf 'preflight\t3\t0\n' >> "$logs/timing.tsv"
    rm -rf "$src"; chmod -R a+rX "$work" 2>/dev/null || true
    echo 3 > "$logs/DONE"
    return 3
  fi
  cache_before=" $(docker buildx du --format '{{.ID}}' 2>/dev/null | tr '\n' ' ') "

  t0=$(date +%s)
  docker build -f "$src/docker/ci.Dockerfile" -t "$IMAGE" "$src/docker" > "$logs/image.log" 2>&1; rc_img=$?
  t1=$(date +%s); printf 'image\t%s\t%s\n' "$rc_img" "$((t1 - t0))" >> "$logs/timing.tsv"

  rc=1
  if [[ $rc_img == 0 ]]; then
    t0=$(date +%s)
    mkdir -p "$work/tmp"
    # /tmp of the container on the work volume: MSBuild, pytest and uv temp files stay off the root fs
    docker run --rm --name "sts2sim-ci-$commit-$$" \
      --user "$(id -u):$(id -g)" \
      -v "$src:/work" -v "$work/tmp:/tmp" -v "$cache/nuget:/cache/nuget" -v "$cache/uv:/cache/uv" -w /work \
      -e HOME=/work/.ci-home -e NUGET_PACKAGES=/cache/nuget -e UV_CACHE_DIR=/cache/uv \
      -e GATES_COMMIT="$commit" -e GATES_OUT=/work/.gates-out \
      "$IMAGE" bash scripts/gates.sh "$@" > "$logs/gates.log" 2>&1 < /dev/null; rc=$?
    t1=$(date +%s); printf 'gates\t%s\t%s\n' "$rc" "$((t1 - t0))" >> "$logs/timing.tsv"
    if [[ -d "$src/.gates-out" ]]; then
      rm -rf "$logs/gates"; cp -r "$src/.gates-out" "$logs/gates"
    fi
  fi
  printf 'total\t%s\t%s\n' "$rc" "$(( $(date +%s) - t_all ))" >> "$logs/timing.tsv"

  {
    cat "$logs/header.txt"
    echo
    echo "step timings (step, rc, seconds):"
    sed 's/^/  /' "$logs/timing.tsv"
    echo
    if [[ -f "$logs/gates/SUMMARY.txt" ]]; then cat "$logs/gates/SUMMARY.txt"; else tail -40 "$logs/gates.log" 2>/dev/null; fi
  } > "$logs/SUMMARY.txt"
  if [[ "${CI_LINUX_KEEP:-0}" != 1 ]]; then
    # the container wrote as the caller's uid, so a plain rm suffices
    rm -rf "$src" "$work/src.tar.gz" "$work/tmp"
  fi
  if [[ "${CI_LINUX_CLEANUP:-0}" == 1 && $had_image == 0 ]]; then
    # give the root fs back: drop the image and only the build-cache records this run created
    docker image rm "$IMAGE" > /dev/null 2>&1
    # a parent record (the pulled base layers) is prunable only after its children: repeat until nothing goes
    local id n=0 pass left
    for pass in 1 2 3 4 5 6; do
      left=0
      for id in $(docker buildx du --format '{{.ID}}' 2>/dev/null); do
        case "$cache_before" in *" $id "*) ;; *) left=$((left + 1)); docker buildx prune -f --filter "id=$id" > /dev/null 2>&1 && n=$((n + 1)) ;; esac
      done
      [[ $left == 0 ]] && break
    done
    echo "cleanup: image removed, build cache of this run pruned ($n prune calls); root fs free now" \
      "$(df -Pk / | awk 'NR==2 {print int($4/1024)}') MB" >> "$logs/SUMMARY.txt"
  fi
  chmod -R a+rX "$work" 2>/dev/null || true
  echo "$rc" > "$logs/DONE"
  return "$rc"
}

if [[ "${1:-}" == --remote ]]; then
  shift
  [[ $# -ge 2 ]] || { echo "--remote needs <workdir> <commit>" >&2; exit 2; }
  remote_main "$@"
  exit $?
fi

# ----------------------------------------------------------------------------------------------------------- local
usage() { sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'; }
HOST="${CI_LINUX_HOST:-raven}"
MODE=auto
ATTACH=""
GATES_ARGS=(--quick --skip g0)
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) [[ $# -ge 2 ]] || { usage >&2; exit 2; }; HOST="$2"; MODE=ssh; shift 2 ;;
    --local) MODE=local; shift ;;
    --attach) [[ $# -ge 2 ]] || { usage >&2; exit 2; }; ATTACH="$2"; shift 2 ;;
    --) shift; GATES_ARGS=("$@"); break ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done
if [[ "$MODE" == auto ]]; then
  if command -v docker > /dev/null 2>&1 && docker info > /dev/null 2>&1; then MODE=local; else MODE=ssh; fi
fi

cd "$REPO_ROOT" || exit 2
SHA="${ATTACH:-$(git rev-parse --short HEAD)}"
LOCAL_DIR="$REPO_ROOT/tmp/ci-linux/$SHA"
mkdir -p "$LOCAL_DIR"
T_START=$(date +%s)

if [[ -z "$ATTACH" ]]; then
  if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "ci_linux: note: uncommitted changes are NOT in the run (the export is HEAD $SHA)"
  fi
  git archive --format=tar.gz -o "$LOCAL_DIR/src.tar.gz" HEAD || exit 2
fi

if [[ "$MODE" == local ]]; then
  echo "ci_linux: local docker, commit $SHA, gates ${GATES_ARGS[*]}"
  remote_main "$LOCAL_DIR" "$SHA" "${GATES_ARGS[@]}"; RC=$?
  cat "$LOCAL_DIR/logs/SUMMARY.txt"
  echo "ci_linux: logs in $LOCAL_DIR/logs (wall $(( $(date +%s) - T_START )) s)"
  exit "$RC"
fi

REMOTE_BASE="${CI_LINUX_REMOTE_BASE:-/data/raven/hack-sts2/tmp/ci-linux}"
REMOTE_DIR="$REMOTE_BASE/$SHA"
POLL="${CI_LINUX_POLL:-20}"
if [[ -z "$ATTACH" ]]; then
  echo "ci_linux: host $HOST:$REMOTE_DIR, commit $SHA, gates ${GATES_ARGS[*]}"
  ssh "$HOST" "mkdir -p '$REMOTE_DIR' && rm -rf '$REMOTE_DIR/logs'" || exit 2
  scp -q "$LOCAL_DIR/src.tar.gz" "$REPO_ROOT/scripts/ci_linux.sh" "$HOST:$REMOTE_DIR/" || exit 2
  QARGS=""
  for a in "${GATES_ARGS[@]}"; do QARGS="$QARGS '${a//\'/}'"; done
  # the detached job runs in its own subshell with every fd redirected, so this ssh session returns at once
  # (a backgrounded 'cd && nohup ...' list keeps the session's fds and can hold the connection open)
  ssh -n "$HOST" "cd '$REMOTE_DIR' && (CI_LINUX_IMAGE='$IMAGE' CI_LINUX_CLEANUP='${CI_LINUX_CLEANUP:-1}' \
    CI_LINUX_MIN_FREE_MB='${CI_LINUX_MIN_FREE_MB:-2500}' nohup bash ci_linux.sh --remote '$REMOTE_DIR' '$SHA' $QARGS \
    > '$REMOTE_DIR/remote.out' 2>&1 < /dev/null &)" < /dev/null || exit 2
  echo "ci_linux: started; re-attach with: scripts/ci_linux.sh --attach $SHA --host $HOST"
fi

while :; do
  if ssh -o ConnectTimeout=20 "$HOST" "test -f '$REMOTE_DIR/logs/DONE'" 2>/dev/null; then break; fi
  sleep "$POLL"
done
rm -rf "$LOCAL_DIR/logs"
scp -q -r "$HOST:$REMOTE_DIR/logs" "$LOCAL_DIR/" || exit 2
RC=$(cat "$LOCAL_DIR/logs/DONE")
cat "$LOCAL_DIR/logs/SUMMARY.txt"
echo "ci_linux: logs in $LOCAL_DIR/logs and $HOST:$REMOTE_DIR/logs (wall $(( $(date +%s) - T_START )) s)"
exit "$RC"
