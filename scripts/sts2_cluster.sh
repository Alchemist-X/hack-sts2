#!/usr/bin/env bash
# N Sts2Sim play servers on one Docker host, one command (docs/docker.md "Many servers on one host",
# docs/capacity-2026-11-02.md). Local docker by default; '--host H' runs every docker/curl call as 'ssh H ...'.
#
#   scripts/sts2_cluster.sh up 8 [--host raven] [--image sts2sim:latest] [--base-port 8765]
#                               [--cpus 1] [--memory 512m] [--gc-heaps 1] [--name sts2sim] [-- SERVER_ARGS...]
#   scripts/sts2_cluster.sh status [--host raven]     # one line per server: container state, docker health, /health
#   scripts/sts2_cluster.sh urls   [--host raven]     # comma list for 'sts2eval run --urls ...'
#   scripts/sts2_cluster.sh logs 3 [--host raven] [--tail 100]
#   scripts/sts2_cluster.sh tunnel [--host raven]     # foreground ssh -L for every port (use the servers from here)
#   scripts/sts2_cluster.sh down   [--host raven]
#
# Server i (0-based) is container <name>-<i>, listening on 0.0.0.0:8765 inside and published on
# 127.0.0.1:<base-port + i> of the docker host (the server has no auth; never publish it on a public address).
# Containers carry the label sts2sim.cluster=<name>; 'down' removes exactly those. 'up' replaces an existing cluster
# of the same name, then waits until every server answers /health (timeout --wait, default 60 s).
# Resource limits are optional: --cpus/--memory map to docker's flags, --gc-heaps sets DOTNET_GCHeapCount (Server GC
# makes one heap per visible core otherwise; with many servers per host a small count keeps memory down).
# Each server is an independent process with its own games: a client must keep a game on the server that created
# it. 'sts2eval run --urls $(scripts/sts2_cluster.sh urls)' does that (game i -> server i mod N).
set -euo pipefail

usage() { sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'; }

cmd="${1:-}"
[[ -n "$cmd" ]] || { usage; exit 2; }
shift
n=""
case "$cmd" in
  up|logs) [[ $# -gt 0 && "$1" =~ ^[0-9]+$ ]] || { echo "usage: $0 $cmd <N|i> [options]" >&2; exit 2; }; n="$1"; shift ;;
  down|status|urls|tunnel) ;;
  -h|--help|help) usage; exit 0 ;;
  *) echo "unknown command: $cmd" >&2; usage >&2; exit 2 ;;
esac

HOST="" IMAGE="${STS2SIM_IMAGE:-sts2sim:latest}" BASE_PORT=8765 NAME=sts2sim CPUS="" MEMORY="" GC_HEAPS="" WAIT=60
TAIL=200 BIND_IP=127.0.0.1
SERVER_ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --image) IMAGE="$2"; shift 2 ;;
    --base-port) BASE_PORT="$2"; shift 2 ;;
    --name) NAME="$2"; shift 2 ;;
    --cpus) CPUS="$2"; shift 2 ;;
    --memory) MEMORY="$2"; shift 2 ;;
    --gc-heaps) GC_HEAPS="$2"; shift 2 ;;
    --wait) WAIT="$2"; shift 2 ;;
    --tail) TAIL="$2"; shift 2 ;;
    --bind-ip) BIND_IP="$2"; shift 2 ;;
    --) shift; SERVER_ARGS=("$@"); break ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done
[[ "$NAME" =~ ^[a-zA-Z0-9][a-zA-Z0-9_.-]*$ ]] || { echo "bad --name: $NAME" >&2; exit 2; }
[[ "$BASE_PORT" =~ ^[0-9]+$ ]] || { echo "bad --base-port: $BASE_PORT" >&2; exit 2; }
LABEL="sts2sim.cluster=$NAME"

# run a command on the docker host: locally, or through ssh with every argument shell-quoted
on_host() {
  if [[ -n "$HOST" ]]; then
    ssh -n -o BatchMode=yes "$HOST" "$(printf '%q ' "$@")"   # -n: never eat the caller's stdin (read loops)
  else
    "$@"
  fi
}
dk() { on_host docker "$@"; }

# GET http://127.0.0.1:<port>/health on the docker host; prints the body, nonzero when unreachable
probe() {
  local url="http://127.0.0.1:$1/health"
  if [[ -n "$HOST" ]]; then
    ssh -n -o BatchMode=yes "$HOST" "curl -fsS -m 3 $url 2>/dev/null || python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen(\"$url\", timeout=3).read().decode())' 2>/dev/null"
  elif command -v curl > /dev/null; then
    curl -fsS -m 3 "$url" 2>/dev/null
  else
    python3 -c "import sys,urllib.request; sys.stdout.write(urllib.request.urlopen('$url', timeout=3).read().decode())" 2>/dev/null
  fi
}

# "<name> <host port>" per cluster container, ordered by index
members() {
  dk ps -a --filter "label=$LABEL" --format '{{.Label "sts2sim.index"}} {{.Names}} {{.Label "sts2sim.port"}}' \
    | sort -n | awk '{print $2, $3}'
}

case "$cmd" in
  up)
    (( n >= 1 )) || { echo "N must be >= 1" >&2; exit 2; }
    dk image inspect "$IMAGE" > /dev/null 2>&1 || { echo "image $IMAGE not found on ${HOST:-this host}" >&2; exit 1; }
    old="$(dk ps -aq --filter "label=$LABEL" | tr '\n' ' ')"
    if [[ -n "${old// /}" ]]; then
      echo "replacing existing cluster '$NAME' ($(wc -w <<< "$old" | tr -d ' ') containers)"
      # shellcheck disable=SC2086
      dk rm -f $old > /dev/null
    fi
    for ((i = 0; i < n; i++)); do
      port=$((BASE_PORT + i))
      args=(run -d --name "$NAME-$i" --label "$LABEL" --label "sts2sim.index=$i" --label "sts2sim.port=$port"
            -p "$BIND_IP:$port:8765" --restart unless-stopped -e "STS2SIM_CRASH_LOG=/data/play-crashes-$NAME-$i.jsonl")
      [[ -n "$CPUS" ]] && args+=(--cpus "$CPUS")
      [[ -n "$MEMORY" ]] && args+=(--memory "$MEMORY")
      [[ -n "$GC_HEAPS" ]] && args+=(-e "DOTNET_GCHeapCount=$GC_HEAPS")
      args+=("$IMAGE" server ${SERVER_ARGS[@]+"${SERVER_ARGS[@]}"})
      dk "${args[@]}" > /dev/null
    done
    echo "started $n servers ($IMAGE) on ${HOST:-localhost} ports $BASE_PORT-$((BASE_PORT + n - 1)); waiting for /health ..."
    t0=$SECONDS
    for ((i = 0; i < n; i++)); do
      port=$((BASE_PORT + i))
      until probe "$port" > /dev/null; do
        if (( SECONDS - t0 > WAIT )); then
          echo "server $i (port $port) not healthy after ${WAIT}s; 'docker logs $NAME-$i':" >&2
          dk logs --tail 30 "$NAME-$i" >&2 || true
          exit 1
        fi
        sleep 0.3
      done
    done
    echo "all $n healthy in $((SECONDS - t0)) s"
    "$0" urls ${HOST:+--host "$HOST"} --name "$NAME"
    ;;
  down)
    ids="$(dk ps -aq --filter "label=$LABEL" | tr '\n' ' ')"
    if [[ -z "${ids// /}" ]]; then echo "no containers in cluster '$NAME'"; exit 0; fi
    # shellcheck disable=SC2086
    dk rm -f $ids > /dev/null
    echo "removed $(wc -w <<< "$ids" | tr -d ' ') containers of cluster '$NAME'"
    ;;
  status)
    rows="$(members)"
    [[ -n "$rows" ]] || { echo "no containers in cluster '$NAME'"; exit 1; }
    bad=0
    printf '%-14s %-6s %-9s %-10s %s\n' CONTAINER PORT STATE DOCKER /health
    while read -r cname port; do
      st="$(dk inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}' "$cname")"
      body="$(probe "$port" || true)"
      [[ -n "$body" ]] || { body="UNREACHABLE"; bad=1; }
      printf '%-14s %-6s %-9s %-10s %s\n' "$cname" "$port" ${st} "$body"
    done <<< "$rows"
    exit $bad
    ;;
  urls)
    rows="$(members)"
    [[ -n "$rows" ]] || { echo "no containers in cluster '$NAME'" >&2; exit 1; }
    awk '{printf "%shttp://127.0.0.1:%s", (NR > 1 ? "," : ""), $2} END {print ""}' <<< "$rows"
    ;;
  logs)
    dk logs --tail "$TAIL" "$NAME-$n"
    ;;
  tunnel)
    [[ -n "$HOST" ]] || { echo "tunnel needs --host" >&2; exit 2; }
    rows="$(members)"
    [[ -n "$rows" ]] || { echo "no containers in cluster '$NAME'" >&2; exit 1; }
    fwd=()
    while read -r _ port; do fwd+=(-L "127.0.0.1:$port:127.0.0.1:$port"); done <<< "$rows"
    echo "forwarding $(wc -l <<< "$rows" | tr -d ' ') ports from $HOST to 127.0.0.1 (Ctrl-C to stop)"
    exec ssh -N -o ExitOnForwardFailure=yes "${fwd[@]}" "$HOST"
    ;;
esac
