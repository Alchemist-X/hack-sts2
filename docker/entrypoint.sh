#!/bin/sh
# Entrypoint of the sts2sim image (docs/docker.md). First argument picks the mode; the rest is passed through.
set -eu

SIM=/app/sim
SERVER_DLL="$SIM/Sts2Sim.Server/bin/Release/net9.0/Sts2Sim.Server.dll"
BENCH_DLL="$SIM/Sts2Sim.Bench/bin/Release/net9.0/Sts2Sim.Bench.dll"
VENV_VEC="$SIM/tools/vec/.venv/bin"
VENV_EVAL="$SIM/tools/eval/.venv/bin"

usage() {
  cat <<'EOF'
sts2sim image entry points:
  server [ARGS...]        play server on $STS2SIM_BIND:$STS2SIM_PORT (default 0.0.0.0:8765; extra args go to
                          Sts2Sim.Server, e.g. --reveal-moves; crash log $STS2SIM_CRASH_LOG, default
                          /data/play-crashes-<hostname>.jsonl, one file per replica)
  selftest [N] [ARGS...]  N random full runs through the play-service path (default 200), exit 1 on any crash
  vec-check [ARGS...]     sts2vec check   (schema/buffers + determinism threads 1 vs 8); sts2vec uses the NativeAOT
                          library when the image has it ($STS2VEC_AOT_LIB; set it empty for the hostfxr path)
  vec-bench [ARGS...]     sts2vec bench   (e.g. --envs 1024 --threads 4 --steps 500 [--json])
  sts2vec [ARGS...]       the sts2vec CLI (bench | check | digest)
  bench [ARGS...]         Sts2Sim.Bench (C# benches: no args = combat bench, 'vec 1024 8 500', 'run-soak ...')
  eval [ARGS...]          sts2eval (LLM / random evaluation runner; e.g. eval run --seedset a0-s10 --spawn)
  play [ARGS...]          sim_play.py shell client (e.g. play random --runs 20 --url http://host:8765)
  sh | bash [ARGS...]     a shell
  help                    this text
Anything else is executed as a command.
EOF
}

# NativeAOT by default for sts2vec when the image has the library (Dockerfile 'aot' stage) and the caller did not set
# STS2VEC_AOT_LIB; an explicitly empty STS2VEC_AOT_LIB (-e STS2VEC_AOT_LIB=) keeps the hostfxr path (STS2VEC_DLL).
if [ -z "${STS2VEC_AOT_LIB+set}" ] && [ -n "${STS2VEC_AOT_DEFAULT:-}" ] && [ -f "$STS2VEC_AOT_DEFAULT" ]; then
  export STS2VEC_AOT_LIB="$STS2VEC_AOT_DEFAULT"
fi

mode="${1:-server}"
if [ "$#" -gt 0 ]; then shift; fi

case "$mode" in
  server)
    exec dotnet "$SERVER_DLL" --bind "${STS2SIM_BIND:-0.0.0.0}" --port "${STS2SIM_PORT:-8765}" \
      --crash-log "${STS2SIM_CRASH_LOG:-/data/play-crashes-$(cat /proc/sys/kernel/hostname).jsonl}" "$@"
    ;;
  selftest)
    n=200
    case "${1:-}" in
      ''|-*) ;;
      *) n="$1"; shift ;;
    esac
    exec dotnet "$SERVER_DLL" --crash-log "${STS2SIM_CRASH_LOG:-/data/selftest-crashes.jsonl}" --selftest "$n" "$@"
    ;;
  vec-check)
    exec "$VENV_VEC/sts2vec" check "$@"
    ;;
  vec-bench)
    exec "$VENV_VEC/sts2vec" bench "$@"
    ;;
  sts2vec)
    exec "$VENV_VEC/sts2vec" "$@"
    ;;
  bench)
    exec dotnet "$BENCH_DLL" "$@"
    ;;
  eval|sts2eval)
    # default outputs go to /app/tmp -> /data/tmp (a fresh bind mount on /data has no tmp/ yet)
    mkdir -p /data/tmp 2>/dev/null || true
    exec "$VENV_EVAL/sts2eval" "$@"
    ;;
  play)
    exec python3 "$SIM/tools/play/sim_play.py" "$@"
    ;;
  sh|bash)
    exec /bin/bash "$@"
    ;;
  help|-h|--help)
    usage
    ;;
  *)
    exec "$mode" "$@"
    ;;
esac
