#!/usr/bin/env bash
# Run the CPU-heavy verification of a commit on the team server instead of this Mac: dotnet build + the pytest suites
# of sim/tools/{train,vec,eval}, plus optional extra commands (short CPU training smokes), inside the sts2-testbox
# Docker image (sim/Dockerfile.testbox) on raven. Output streams back; the exit status is the run's.
#
#   scripts/test_remote.sh                                   # build + train, vec, eval suites of HEAD
#   scripts/test_remote.sh --suites train                    # only some suites (comma list; 'none' = build only)
#   scripts/test_remote.sh --suites none -- uv run --project sim/tools/train sts2train train ...   # a CPU smoke
#   scripts/test_remote.sh --suites train -- uv run --project sim/tools/train pytest -q -k cycles  # extra pytest
#   scripts/test_remote.sh -- bash -c 'cmd1 && cmd2'         # shell syntax: wrap the extra command in bash -c
#
# Works from any worktree: the archived tree is HEAD of the git checkout of the current directory (committed changes
# only, never the work tree), while sim/Dockerfile.testbox and this script are taken from this script's own checkout,
# so commits that predate the script can be tested too (e.g. /path/to/train-w3-remote-tests/scripts/test_remote.sh).
#
# Flow (all heavy work on the remote; locally only git archive + ssh):
#   git archive HEAD | ssh -> <root>/<name>/src (fresh each run; <name> = worktree directory name)
#   image sts2-testbox:<key>, key = hash of the Dockerfile + sim/tools/{train,vec,eval}/{pyproject.toml,uv.lock} of
#     the archived commit; built once (venvs with every locked dependency, torch = the CPU wheel of the locked
#     version), reused while those files do not change; also tagged sts2-testbox:latest
#   docker run --rm --cpus $TESTBOX_CPUS, the caller's uid, nice 10, *_NUM_THREADS / dotnet -m = $TESTBOX_CPUS:
#     the tree is copied into the image's /work (whose sim/tools/*/.venv the image pre-populated), the local packages
#     are installed editable (--no-deps), then
#       dotnet build sim/Sts2Sim.sln -c Release
#       STS2VEC_DLL=/work/sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll, UV_NO_SYNC=1 (the venvs are prebuilt):
#       uv run --project sim/tools/train pytest -q
#       uv run --project sim/tools/vec pytest -q sim/tools/vec/tests
#       uv run --project sim/tools/eval pytest -q sim/tools/eval   (path needed: eval has no collection-scoping plugin)
#       the extra command after '--' (argv, run in /work after the suites)
#   /work/tmp is <root>/<name>/tmp on the host, so outputs written under tmp/ (trainer runs, eval reports) persist.
#   Caches (NuGet, uv, pip, dotnet home) persist in <root>/cache.
#
# Env knobs:
#   TESTBOX_HOST   ssh host (default raven)
#   TESTBOX_ROOT   remote root (default /data/raven/hack-sts2/testbox)
#   TESTBOX_NAME   run directory name (default: the worktree directory name)
#   TESTBOX_CPUS   CPU quota and thread count (default 6 of raven's 8)
#   TESTBOX_MEM    container memory limit (default 24g)
#   TESTBOX_REBUILD=1  rebuild the image even if the tag exists
# No secrets are shipped: the archive is the committed tree, nothing else is copied, and no env file is mounted.
set -euo pipefail

SELF="${BASH_SOURCE[0]}"
IMAGE_REPO=sts2-testbox

# ------------------------------------------------------------------------------------------------- in-container
in_container() {
  local suites="$1"; shift
  local cpus="${TESTBOX_CPUS:-6}" rc=0 t0 st
  local -a failed=() times=()
  cd /work
  # the tree (src is a fresh git archive) into the image's /work, keeping the prebuilt .venv dirs
  tar -C /src -cf - . | tar -C /work -x --no-overwrite-dir -m -f -
  export STS2VEC_DLL=/work/sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll
  export UV_NO_SYNC=1
  step() {   # step <label> <cmd...>: run niced, time, record the first failure
    local label="$1"; shift
    echo; echo "==== [$label] $*"
    t0=$SECONDS
    set +e; nice -n 10 "$@"; st=$?; set -e
    echo "==== [$label] exit $st in $((SECONDS - t0)) s"
    times+=("$label $((SECONDS - t0)) s exit=$st")
    if (( st != 0 )); then failed+=("$label"); (( rc == 0 )) && rc=$st; fi
    return 0
  }
  step install bash -c 'set -e
    uv pip install -q --python sim/tools/train/.venv --no-deps -e sim/tools/vec -e sim/tools/eval -e sim/tools/train
    uv pip install -q --python sim/tools/vec/.venv --no-deps -e sim/tools/vec
    uv pip install -q --python sim/tools/eval/.venv --no-deps -e sim/tools/eval
    sim/tools/train/.venv/bin/python -c "import torch; print(\"torch\", torch.__version__, \"threads\", torch.get_num_threads())"'
  if (( rc == 0 )); then
    step build dotnet build sim/Sts2Sim.sln -c Release -nologo "-m:$cpus" -clp:Summary
  fi
  if (( rc == 0 )); then
    if [[ ",$suites," == *",train,"* ]]; then step train uv run --project sim/tools/train pytest -q; fi
    if [[ ",$suites," == *",vec,"* ]]; then step vec uv run --project sim/tools/vec pytest -q sim/tools/vec/tests; fi
    if [[ ",$suites," == *",eval,"* ]]; then step eval uv run --project sim/tools/eval pytest -q sim/tools/eval; fi
    if (( $# > 0 )); then step extra "$@"; fi
  fi
  echo; echo "==== summary"
  printf '  %s\n' "${times[@]}"
  if (( rc != 0 )); then echo "  FAILED: ${failed[*]}"; else echo "  all passed"; fi
  return "$rc"
}

# ------------------------------------------------------------------------------------------------- remote driver
remote_driver() {
  local dir="$1" suites="$2"; shift 2
  local root box key ctx log img t0
  root="$(dirname "$dir")"; box="$dir/box"
  local cpus="${TESTBOX_CPUS:-6}" mem="${TESTBOX_MEM:-24g}"
  exec 9> "$dir/.lock"
  flock -n 9 || { echo "another test_remote run holds $dir/.lock" >&2; exit 75; }
  mkdir -p "$root/cache/uv" "$root/cache/nuget" "$root/cache/pip" "$root/cache/home" "$dir/tmp" "$root/images"

  # image key: the Dockerfile + the uv project files of the archived commit
  key="$( { cat "$box/Dockerfile.testbox"
            for p in train vec eval; do cat "$dir/src/sim/tools/$p/pyproject.toml" "$dir/src/sim/tools/$p/uv.lock"; done
          } | sha256sum | cut -c1-12)"
  img="$IMAGE_REPO:$key"
  if [[ "${TESTBOX_REBUILD:-0}" == 1 ]] || ! docker image inspect "$img" > /dev/null 2>&1; then
    ctx="$root/images/$key"; log="$root/images/build-$key.log"
    rm -rf "$ctx"; mkdir -p "$ctx"
    (cd "$dir/src" && cp --parents sim/tools/{train,vec,eval}/{pyproject.toml,uv.lock} "$ctx/")
    echo "== building $img (log $log)"
    t0=$SECONDS
    if ! nice -n 10 docker build --progress=plain -f "$box/Dockerfile.testbox" -t "$img" "$ctx" > "$log" 2>&1; then
      tail -40 "$log"; echo "image build failed" >&2; exit 1
    fi
    docker tag "$img" "$IMAGE_REPO:latest"
    rm -rf "$ctx"
    echo "== built $img in $((SECONDS - t0)) s, $(docker image inspect -f '{{.Size}}' "$img" | numfmt --to=iec)"
  else
    echo "== image $img (cached)"
  fi

  CNAME="testbox-$(basename "$dir")-$$"     # global: the EXIT trap runs after this function returned (set -u)
  trap 'docker rm -f "${CNAME:-}" > /dev/null 2>&1 || true' EXIT HUP INT TERM
  docker run --rm --name "$CNAME" --init \
    --cpus "$cpus" --memory "$mem" \
    --user "$(id -u):$(id -g)" \
    -v "$dir/src:/src:ro" -v "$dir/tmp:/work/tmp" -v "$root/cache:/cache" -v "$box:/box:ro" \
    -e HOME=/cache/home -e DOTNET_CLI_HOME=/cache/home -e NUGET_PACKAGES=/cache/nuget \
    -e UV_CACHE_DIR=/cache/uv -e PIP_CACHE_DIR=/cache/pip \
    -e TESTBOX_CPUS="$cpus" -e OMP_NUM_THREADS="$cpus" -e MKL_NUM_THREADS="$cpus" \
    -e OPENBLAS_NUM_THREADS="$cpus" -e DOTNET_PROCESSOR_COUNT="$cpus" \
    "$img" bash /box/test_remote.sh --in-container "$suites" "$@"
}

# ------------------------------------------------------------------------------------------------- dispatch
case "${1:-}" in
  --in-container) shift; in_container "$@"; exit $? ;;
  --remote-driver) shift; remote_driver "$@"; exit $? ;;
esac

SUITES="train,vec,eval"
while (( $# > 0 )); do
  case "$1" in
    --suites) SUITES="$2"; shift 2 ;;
    --) shift; break ;;
    -h|--help) sed -n '2,/^set -euo/p' "$SELF" | sed '$d'; exit 0 ;;
    *) echo "unknown option: $1 (extra commands go after '--')" >&2; exit 2 ;;
  esac
done
[[ "$SUITES" == none ]] && SUITES=""

HOST="${TESTBOX_HOST:-raven}"
ROOT="${TESTBOX_ROOT:-/data/raven/hack-sts2/testbox}"
TOP="$(git rev-parse --show-toplevel)"
NAME="${TESTBOX_NAME:-$(basename "$TOP")}"
NAME="${NAME//[^A-Za-z0-9._-]/_}"
DIR="$ROOT/$NAME"
REV="$(git -C "$TOP" rev-parse --verify "HEAD^{commit}")"
HERE="$(cd "$(dirname "$SELF")/.." && pwd)"
if [[ -n "$(git -C "$TOP" status --porcelain --untracked-files=no)" ]]; then
  echo "note: uncommitted changes are not tested; the run uses HEAD ($(git -C "$TOP" rev-parse --short "$REV"))" >&2
fi
echo "== $NAME @ $(git -C "$TOP" log -1 --format='%h %s' "$REV" | cut -c1-100) -> $HOST:$DIR"

T0=$SECONDS
# upload: the archived tree to <dir>/src (fresh each run), this script + the Dockerfile to <dir>/box
# shellcheck disable=SC2029
git -C "$TOP" archive --format=tar.gz "$REV" | ssh "$HOST" "set -e; mkdir -p '$DIR'; cd '$DIR'; \
  exec 9> .lock; flock -n 9 || { echo 'another test_remote run holds $DIR/.lock' >&2; exit 75; }; \
  rm -rf src.new; mkdir src.new; tar xzf - -C src.new; rm -rf src; mv src.new src"
TAR_FLAGS=()
tar --version 2> /dev/null | grep -q bsdtar && TAR_FLAGS=(--no-mac-metadata --no-xattrs)   # macOS: no ._* / xattrs
COPYFILE_DISABLE=1 tar "${TAR_FLAGS[@]}" -C "$HERE" -cf - scripts/test_remote.sh sim/Dockerfile.testbox \
  | ssh "$HOST" "set -e; cd '$DIR'; rm -rf box.new; mkdir box.new; tar xf - -C box.new; \
    mv box.new/scripts/test_remote.sh box.new/sim/Dockerfile.testbox box.new/; rmdir box.new/scripts box.new/sim; \
    rm -rf box; mv box.new box"
echo "== uploaded in $((SECONDS - T0)) s"

REMOTE_ARGS="$(printf '%q ' "$DIR" "$SUITES" "$@")"
set +e
# shellcheck disable=SC2029
ssh "$HOST" "bash '$DIR/box/test_remote.sh' --remote-driver $REMOTE_ARGS"
RC=$?
set -e
echo "== $NAME: exit $RC, wall $((SECONDS - T0)) s"
exit "$RC"
