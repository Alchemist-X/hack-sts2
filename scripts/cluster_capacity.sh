#!/usr/bin/env bash
# Capacity measurements of one Docker host (docs/capacity-2026-11-02.md). Run ON the docker host, from a checkout
# (or 'git archive' tree) of the commit the image was built from:
#
#   scripts/cluster_capacity.sh OUT_DIR [play|runner|vec|all]     # default all
#
# Env: IMAGE (sts2sim:latest), BASE_PORT (18765), RUNS (200 random games per client), PLAY_CONFIGS ("1:1 4:1 8:1 1:4
# 1:8 8:2" = servers:clients-per-server), RUNNER_SERVERS ("1 4 8"), RUNNER_CONCURRENCY (32), RUNNER_GAMES (200),
# VEC_CONFIGS ("1024:4 1024:8 4096:4 4096:8"), VEC_STEPS_1024 (500), VEC_STEPS_4096 (300).
# Output: OUT_DIR/{play,runner,vec}.tsv plus one log per run. Brings its cluster ('capacity') down at the end.
set -euo pipefail

OUT="${1:?usage: $0 OUT_DIR [play|runner|vec|all]}"
WHAT="${2:-all}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${IMAGE:-sts2sim:latest}"
BASE_PORT="${BASE_PORT:-18765}"
RUNS="${RUNS:-200}"
CL=("$REPO/scripts/sts2_cluster.sh")
NAME=capacity
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
NCPU="$(nproc)"
trap '"${CL[@]}" down --name "$NAME" > /dev/null 2>&1 || true' EXIT

now() { date +%s.%N; }
mem_peak_mib() {  # cgroup v2 peak memory of a container, MiB
  docker exec "$1" cat /sys/fs/cgroup/memory.peak 2> /dev/null | awk '{printf "%.1f", $1 / 1048576}'
}
mem_now_mib() {
  docker exec "$1" cat /sys/fs/cgroup/memory.current 2> /dev/null | awk '{printf "%.1f", $1 / 1048576}'
}

play() {
  local tsv="$OUT/play.tsv"
  printf 'servers\tclients_per_server\tgames\tsteps\twall_s\tgames_per_s\tgames_per_s_per_server\tgames_per_s_per_vcpu\tsteps_per_s\tbad\tcpu_pct_sample\tmem_now_mib_per_server\tmem_peak_mib_max\n' > "$tsv"
  for cfg in ${PLAY_CONFIGS:-1:1 4:1 8:1 1:4 1:8 8:2}; do
    local n="${cfg%%:*}" c="${cfg##*:}"
    "${CL[@]}" up "$n" --name "$NAME" --image "$IMAGE" --base-port "$BASE_PORT" > "$OUT/play-$n-$c.up.log"
    local pids=() t0 t1 k=0
    t0="$(now)"
    for ((i = 0; i < n; i++)); do
      for ((j = 0; j < c; j++)); do
        python3 "$REPO/sim/tools/play/sim_play.py" random --runs "$RUNS" --seed $((k * 1000000 + 1)) \
          --url "http://127.0.0.1:$((BASE_PORT + i))" > "$OUT/play-$n-$c.client$k.log" 2>&1 &
        pids+=($!)
        k=$((k + 1))
      done
    done
    sleep 8
    # one CPU sample of all server containers while the clients run (sum of docker's per-container CPU %)
    local cpu
    cpu="$(docker stats --no-stream --format '{{.Name}} {{.CPUPerc}}' | awk -v p="$NAME-" 'index($1, p) == 1 {gsub("%", "", $2); s += $2} END {printf "%.0f", s}')"
    local bad=0
    for p in "${pids[@]}"; do wait "$p" || bad=$((bad + 1)); done
    t1="$(now)"
    local games steps mem_now=0 mem_peak=0
    games=$(cat "$OUT"/play-"$n"-"$c".client*.log | grep -c '^run ')
    steps=$(grep -h '^summary:' "$OUT"/play-"$n"-"$c".client*.log | sed 's/.*total_steps=//' | awk '{s += $1} END {print s}')
    for ((i = 0; i < n; i++)); do
      mem_now="$(awk -v a="$mem_now" -v b="$(mem_now_mib "$NAME-$i")" 'BEGIN {printf "%.1f", a + b}')"
      mem_peak="$(awk -v a="$mem_peak" -v b="$(mem_peak_mib "$NAME-$i")" 'BEGIN {printf "%.1f", (b > a ? b : a)}')"
    done
    awk -v n="$n" -v c="$c" -v g="$games" -v s="$steps" -v t0="$t0" -v t1="$t1" -v cpus="$NCPU" -v bad="$bad" \
        -v cpu="$cpu" -v mn="$mem_now" -v mp="$mem_peak" 'BEGIN {
      w = t1 - t0; printf "%d\t%d\t%d\t%d\t%.1f\t%.2f\t%.2f\t%.2f\t%.0f\t%d\t%s\t%.1f\t%s\n",
        n, c, g, s, w, g / w, g / w / n, g / w / cpus, s / w, bad, cpu, mn / n, mp }' >> "$tsv"
    tail -n 1 "$tsv"
  done
  "${CL[@]}" down --name "$NAME" > /dev/null
}

runner() {
  local tsv="$OUT/runner.tsv" ss="$OUT/runner-seedset.json"
  python3 - "$ss" "${RUNNER_GAMES:-200}" <<'EOF'
import json, sys
chars = ["IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT"]
n = int(sys.argv[2])
games = [{"seed": f"CAP{i:04d}", "character": chars[i % len(chars)]} for i in range(n)]
json.dump({"name": f"capacity-a0-{n}", "ascension": 0, "content": "ported_only", "games": games}, open(sys.argv[1], "w"))
EOF
  chmod a+r "$ss"
  printf 'servers\tconcurrency\tgames\tsteps\twall_s\tgames_per_s\tsteps_per_s\tgames_per_server\tresults\n' > "$tsv"
  for n in ${RUNNER_SERVERS:-1 4 8}; do
    "${CL[@]}" up "$n" --name "$NAME" --image "$IMAGE" --base-port "$BASE_PORT" > "$OUT/runner-$n.up.log"
    local urls d="$OUT/runner-$n"
    urls="$("${CL[@]}" urls --name "$NAME")"
    rm -rf "$d"; mkdir -p "$d"; chmod a+rwx "$d"
    # the runner in its own container on the host network (talks to the published loopback ports)
    docker run --rm --network host --user "$(id -u):$(id -g)" -v "$OUT:/cap" -v "$d:/data" "$IMAGE" eval run --seedset /cap/runner-seedset.json \
      --policy random --concurrency "${RUNNER_CONCURRENCY:-32}" --urls "$urls" --out /data/run \
      --progress-interval 0 > "$OUT/runner-$n.log" 2>&1 || true
    python3 - "$d/run" "$n" "${RUNNER_CONCURRENCY:-32}" >> "$tsv" <<'EOF'
import collections, glob, json, sys
d, n, c = sys.argv[1], sys.argv[2], sys.argv[3]
ss = [json.load(open(f)) for f in glob.glob(f"{d}/games/*.summary.json")]
wall = json.load(open(f"{d}/run.json"))["wall_seconds"]
steps = sum(s["steps"] for s in ss)
per = collections.Counter(s["server_url"] for s in ss)
res = collections.Counter(s["result"] for s in ss)
print(f"{n}\t{c}\t{len(ss)}\t{steps}\t{wall:.1f}\t{len(ss) / wall:.2f}\t{steps / wall:.0f}\t"
      f"{'/'.join(str(per[k]) for k in sorted(per))}\t{','.join(f'{k}={v}' for k, v in sorted(res.items()))}")
EOF
    tail -n 1 "$tsv"
  done
  "${CL[@]}" down --name "$NAME" > /dev/null
}

vec() {
  local tsv="$OUT/vec.tsv"
  printf 'library\tenvs\tthreads\tsteps\tenv_steps_per_s\tper_vcpu\tms_per_batch\tbuffers_mb\tmem_peak_mib\tsim_errors\n' > "$tsv"
  local cfgs="${VEC_CONFIGS:-1024:4 1024:8 4096:4 4096:8}"
  for lib in aot hostfxr; do
    for cfg in $cfgs; do
      local e="${cfg%%:*}" t="${cfg##*:}" s env=()
      s="${VEC_STEPS_1024:-500}"; [[ "$e" -ge 4096 ]] && s="${VEC_STEPS_4096:-300}"
      [[ "$lib" == hostfxr ]] && env=(-e STS2VEC_AOT_LIB=)
      [[ "$lib" == hostfxr && "$cfg" != "1024:8" && "$cfg" != "4096:8" ]] && continue   # hostfxr: reference rows only
      local log="$OUT/vec-$lib-$e-$t.log"
      docker run --rm ${env[@]+"${env[@]}"} "$IMAGE" sh -c \
        "sts2vec bench --envs $e --threads $t --steps $s --json && cat /sys/fs/cgroup/memory.peak" > "$log" 2>&1
      python3 - "$log" "$lib" "$NCPU" >> "$tsv" <<'EOF'
import json, sys
lines = open(sys.argv[1]).read().split("\n")
r = json.loads(next(l for l in lines if l.startswith("{")))
peak = int([l for l in lines if l.strip().isdigit()][-1]) / 1048576
bad = r["status_counts"].get("SIM_ERROR", 0) + r["status_counts"].get("CONTENT_GAP", 0)
print(f"{sys.argv[2]}\t{r['envs']}\t{r['threads']}\t{r['steps']}\t{r['env_steps_per_s']:.0f}\t"
      f"{r['env_steps_per_s'] / min(int(sys.argv[3]), r['threads']):.0f}\t{r['ms_per_batch']:.1f}\t{r['buffers_mb']:.0f}\t"
      f"{peak:.0f}\t{bad}")
EOF
      tail -n 1 "$tsv"
    done
  done
}

case "$WHAT" in
  play) play ;;
  runner) runner ;;
  vec) vec ;;
  all) play; runner; vec ;;
  *) echo "unknown section: $WHAT" >&2; exit 2 ;;
esac
chmod -R a+rX "$OUT" 2> /dev/null || true
echo "results in $OUT"
