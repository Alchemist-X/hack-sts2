#!/usr/bin/env bash
# Build the self-contained Colab training bundle: tmp/colab/sts2train-<gitsha>.tar.gz
#
#   scripts/train/make_colab_bundle.sh                 # AOT .so built on an amd64 Docker host (AOT_HOST, default raven)
#   AOT_SO=/path/Sts2Sim.Vec.so scripts/train/make_colab_bundle.sh    # reuse a linux-x64 NativeAOT library
#   scripts/train/make_colab_bundle.sh --aot-only      # only build/fetch the .so (cached under tmp/colab/aot/<key>/)
#   SAVES_DIR=tmp/train/saves-silent-a9 scripts/train/make_colab_bundle.sh   # also ship start saves (saves/)
#
# Everything is taken from the committed HEAD (git archive), never from the working tree, except the optional
# SAVES_DIR (data, never committed: scripts/train/collect_start_saves.py output). Contents:
#   lib/Sts2Sim.Vec.so        linux-x64 NativeAOT build of Sts2Sim.Vec (Dockerfile 'aot' stage); no .NET runtime needed
#   seedsets/*.json           protocol seed sets (sim/tools/eval/seedsets) for the periodic eval (STS2VEC_SEEDSETS)
#   wheels/*.whl              sts2vec and sts2train-smoke (pure-python wheels, installed with --no-deps)
#   requirements-colab.txt    numpy (torch is preinstalled on Colab and deliberately not listed)
#   run_train.sh              pip-installs the wheels, exports STS2VEC_AOT_LIB, runs `sts2vec check`, launches training
#   smoke_bundle.sh           validates the bundle on an amd64 CPU box (installs CPU torch if missing)
#   saves/                    only with SAVES_DIR=DIR: DIR's *.save files (relative paths kept) + its manifest.json,
#                             for `sts2train train --start-saves <bundle>/saves` (run_train.sh prints the path)
#   manifest.json             git sha, AOT cache key, max GLIBC symbol version of the .so, size + sha256 of every file
#
# The .so comes from the Dockerfile's 'aot' stage (`docker build --platform linux/amd64 --target aot`). This Mac has no
# Docker, so the build runs on an amd64 Docker host over ssh (AOT_HOST, default raven): git archive | ssh -> docker
# build -> docker cp -> scp back. The library only depends on sim/Sts2Sim.Core, sim/Sts2Sim.Vec, the Dockerfile and
# scripts/vec_publish_aot.sh, so it is cached under tmp/colab/aot/<key>/ keyed by those trees' git hashes.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO"
AOT_HOST="${AOT_HOST:-raven}"
AOT_REMOTE_DIR="${AOT_REMOTE_DIR:-/data/raven/hack-sts2/tmp/colab-aot}"
AOT_ONLY=0
[[ "${1:-}" == "--aot-only" ]] && AOT_ONLY=1

SHA="$(git rev-parse --short=10 HEAD)"
if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  echo "note: the working tree has uncommitted changes; the bundle is built from HEAD ($SHA) only" >&2
fi
# cache key of the native library: the trees it is built from
KEY="$(git rev-parse HEAD:sim/Sts2Sim.Core HEAD:sim/Sts2Sim.Vec HEAD:Dockerfile HEAD:scripts/vec_publish_aot.sh \
       | git hash-object --stdin | cut -c1-12)"
AOT_DIR="$REPO/tmp/colab/aot/$KEY"
SO="${AOT_SO:-$AOT_DIR/Sts2Sim.Vec.so}"

build_aot_remote() {
  local rdir="$AOT_REMOTE_DIR/$KEY"
  echo "== building the linux-x64 NativeAOT library on $AOT_HOST:$rdir (Dockerfile --target aot, key $KEY)"
  mkdir -p "$AOT_DIR"
  git archive --format=tar.gz HEAD sim knowledge/source docker scripts/vec_publish_aot.sh Dockerfile .dockerignore \
    | ssh "$AOT_HOST" "rm -rf '$rdir' && mkdir -p '$rdir/src' && tar xzf - -C '$rdir/src'"
  local t0=$SECONDS
  # shellcheck disable=SC2029
  ssh "$AOT_HOST" "set -e; cd '$rdir/src'; \
    docker build --platform linux/amd64 --target aot -t 'sts2sim-aot:$KEY' . > '$rdir/build.log' 2>&1 \
      || { tail -40 '$rdir/build.log'; exit 1; }; \
    cid=\$(docker create 'sts2sim-aot:$KEY'); docker cp \"\$cid:/out-aot/Sts2Sim.Vec.so\" '$rdir/Sts2Sim.Vec.so'; \
    docker rm \"\$cid\" > /dev/null; docker rmi 'sts2sim-aot:$KEY' > /dev/null; rm -rf '$rdir/src'; \
    objdump -T '$rdir/Sts2Sim.Vec.so' | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -1 > '$rdir/glibc.txt'; \
    file '$rdir/Sts2Sim.Vec.so' > '$rdir/file.txt'"
  scp -q "$AOT_HOST:$rdir/Sts2Sim.Vec.so" "$AOT_HOST:$rdir/glibc.txt" "$AOT_HOST:$rdir/file.txt" \
    "$AOT_HOST:$rdir/build.log" "$AOT_DIR/"
  echo "   built in $((SECONDS - t0)) s: $(cut -d: -f2- "$AOT_DIR/file.txt" | cut -c1-90)"
  echo "   max GLIBC symbol version: $(cat "$AOT_DIR/glibc.txt")"
}

if [[ ! -f "$SO" ]]; then
  if [[ -n "${AOT_SO:-}" ]]; then echo "AOT_SO=$AOT_SO does not exist" >&2; exit 1; fi
  build_aot_remote
else
  echo "== NativeAOT library: $SO (cached)"
fi
if [[ "$AOT_ONLY" == 1 ]]; then echo "$SO"; exit 0; fi
if [[ -n "${SAVES_DIR:-}" ]]; then
  SAVES_DIR="$(cd "$SAVES_DIR" 2> /dev/null && pwd)" || { echo "SAVES_DIR=${SAVES_DIR} is not a directory" >&2; exit 1; }
  if [[ -z "$(find "$SAVES_DIR" -type f -name '*.save' -print -quit)" ]]; then
    echo "SAVES_DIR=$SAVES_DIR holds no *.save files" >&2
    exit 1
  fi
fi

# ------------------------------------------------------------------------------------------------ bundle tree
NAME="sts2train-$SHA"
STAGE="$REPO/tmp/colab/stage"
B="$STAGE/$NAME"
rm -rf "$STAGE"
mkdir -p "$B/lib" "$B/seedsets" "$B/wheels" "$STAGE/src"
git archive HEAD sim/tools/vec sim/tools/train sim/tools/eval/seedsets scripts/train | tar xf - -C "$STAGE/src"

cp "$SO" "$B/lib/Sts2Sim.Vec.so"
cp "$STAGE/src/sim/tools/eval/seedsets/"*.json "$B/seedsets/"
for p in vec train; do
  uv build --quiet --wheel --out-dir "$B/wheels" "$STAGE/src/sim/tools/$p"
done
rm -f "$B/wheels/.gitignore"          # uv build drops one into its output dir
for f in run_train.sh smoke_bundle.sh requirements-colab.txt; do
  cp "$STAGE/src/scripts/train/$f" "$B/$f"
done
chmod 0755 "$B/run_train.sh" "$B/smoke_bundle.sh"
if [[ -n "${SAVES_DIR:-}" ]]; then
  # start saves (data): every *.save under SAVES_DIR with its relative path, plus the collector's manifest.json
  python3 - "$SAVES_DIR" "$B/saves" <<'PY'
import shutil, sys
from pathlib import Path
src, dst = Path(sys.argv[1]), Path(sys.argv[2])
files = sorted(p for p in src.rglob("*.save") if p.is_file())
if (src / "manifest.json").is_file():
    files.append(src / "manifest.json")
for p in files:
    q = dst / p.relative_to(src)
    q.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(p, q)                       # bytes only: no xattrs / AppleDouble files
n = sum(1 for p in files if p.suffix == ".save")
b = sum(p.stat().st_size for p in files if p.suffix == ".save")
print(f"== start saves: {n} files, {b:,} bytes, from {src}")
PY
fi

GLIBC="$(cat "$(dirname "$SO")/glibc.txt" 2> /dev/null || echo unknown)"
python3 - "$B" "$SHA" "$KEY" "$GLIBC" "${SAVES_DIR:-}" <<'PY'
import hashlib, json, os, sys, time
root, sha, key, glibc, saves_dir = sys.argv[1:6]
files = {}
for dp, _, fns in os.walk(root):
    for fn in sorted(fns):
        p = os.path.join(dp, fn)
        with open(p, "rb") as f:
            files[os.path.relpath(p, root)] = {"bytes": os.path.getsize(p), "sha256": hashlib.sha256(f.read()).hexdigest()}
saves = [v for k, v in files.items() if k.startswith("saves" + os.sep) and k.endswith(".save")]
manifest = {"bundle": os.path.basename(root), "git_sha": sha, "aot_key": key, "aot_target": "linux-x64 NativeAOT",
            "aot_glibc_max": glibc, "built_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "saves": {"count": len(saves), "bytes": sum(v["bytes"] for v in saves), "source_dir": saves_dir}
            if saves else None,
            "files": dict(sorted(files.items()))}
with open(os.path.join(root, "manifest.json"), "w") as f:
    json.dump(manifest, f, indent=1)
PY
OUT="$REPO/tmp/colab/$NAME.tar.gz"
TAR_FLAGS=()
if tar --version 2> /dev/null | grep -q bsdtar; then
  # macOS bsdtar: no AppleDouble ._* files / xattr headers (a ._x.whl would break `pip install wheels/*.whl`)
  TAR_FLAGS=(--no-mac-metadata --no-xattrs --uid 0 --gid 0)
fi
COPYFILE_DISABLE=1 tar "${TAR_FLAGS[@]}" -czf "$OUT" -C "$STAGE" "$NAME"
rm -rf "$STAGE"
echo "== wrote $OUT ($(du -h "$OUT" | cut -f1), $(wc -c < "$OUT" | tr -d ' ') bytes)"
tar tzvf "$OUT" | awk '$NF ~ /\/saves\/.*\.save$/ {n++; b += $5; next} {printf "   %10s  %s\n", $5, $NF}
  END {if (n) printf "   %10s  %s/saves/*.save (%d files)\n", b, "'"$NAME"'", n}'
