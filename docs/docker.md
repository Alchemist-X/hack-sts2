# Running Sts2Sim in containers

One image (`Dockerfile` at the repo root) carries everything needed to evaluate agents against the simulator:

- the HTTP play server `Sts2Sim.Server` (LLM agents; API in `sim/PLAY.md`),
- `sts2vec`, the batched numpy interface for neural nets (in-process .NET hosting; `sim/VEC.md`, `sim/CONTRACTS.md`
  Part D),
- `Sts2Sim.Bench` (C# benches, including the C-ABI `vec` bench),
- `sts2eval` (the LLM / random evaluation runner, `sim/tools/eval/README.md`) and `sim_play.py` (shell client),
- the rules text (`knowledge/source`, the game's localisation used for card/relic/power descriptions).

The simulator is architecture-neutral IL and the base images are multi-arch, so the same Dockerfile builds on
linux/amd64 and linux/arm64 (validated on arm64, below).

## Build

```bash
docker build -t sts2sim --build-arg STS2SIM_VERSION=$(git rev-parse --short HEAD) .
# optional: run a test filter inside the build stage (fails the build on a red test)
docker build -t sts2sim --build-arg STS2SIM_VERSION=$(git rev-parse --short HEAD) --build-arg RUN_TESTS=Server .
```

Stages:

1. `build` (`mcr.microsoft.com/dotnet/sdk:9.0`): restores from the solution and project files only (cached layer),
   then `dotnet build sim/Sts2Sim.sln -c Release -warnaserror` (the repo's 0-warning gate holds inside the image
   too) and, with `RUN_TESTS=<filter>`, the custom test runner. It stages the Server, Vec and Bench
   `bin/Release/net9.0` trees with the repository layout.
2. `runtime` (`mcr.microsoft.com/dotnet/runtime:9.0`, Debian 12): adds `python3` (3.11), `tini` and `uv`
   (0.11.7, copied from `ghcr.io/astral-sh/uv`), copies the staged binaries, `sim/PLAY.md`, `sim/VEC.md`,
   `sim/CONTRACTS.md`, `sim/tools/{vec,eval,play}` and `knowledge/source`, and creates the `sts2vec` and
   `sts2eval` venvs from the committed `uv.lock` files (`uv sync --frozen --no-dev`).

The image mirrors the checkout under `/app` (`/app/sim/Sts2Sim.Server/bin/Release/net9.0/...`), so every default
path the tools use (sts2vec's dll discovery, `sts2eval --spawn`, rules-text discovery) works unchanged. It runs as
the non-root `app` user (uid 1654, shipped by the .NET images) with working directory `/data`, under `tini`.
There is no git in the image: `STS2SIM_VERSION` (build arg, default `unknown`) is what `/health` reports and
what `sts2eval` records as the server version, so always pass it.

`.dockerignore` keeps the context to source: no `.git`, `.claude`, `tmp/`, `output/`, `bin/`, `obj/`, venvs,
trajectories (`*.jsonl` outside `knowledge/`, `*.mcr`, `*.run`, `*.save`, except the committed test fixtures),
recorder sessions, datasets, checkpoints, `.env*` or key files.

## Entry points

`docker run --rm sts2sim <mode> [args...]` (`help` prints this list):

| Mode | Runs |
| --- | --- |
| `server` (default) | `Sts2Sim.Server --bind $STS2SIM_BIND --port $STS2SIM_PORT --crash-log $STS2SIM_CRASH_LOG` + args (e.g. `--reveal-moves`) |
| `selftest [N]` | N random full runs through the play-service path (default 200); exit 1 on any crash |
| `vec-check [args]` | `sts2vec check` (schema/buffers, determinism threads 1 vs 8) |
| `vec-bench [args]` | `sts2vec bench` (e.g. `--envs 1024 --threads 4 --steps 500 --json`) |
| `sts2vec [args]` | the `sts2vec` CLI (`bench`, `check`, `digest`) |
| `bench [args]` | `Sts2Sim.Bench` (no args: combat bench; `vec 1024 4 500`; `run-soak ...`) |
| `eval [args]` | `sts2eval` (e.g. `eval run --seedset a0-s10 --policy random --spawn`) |
| `play [args]` | `sim_play.py` (e.g. `play random --runs 20 --url http://127.0.0.1:8765`) |
| `sh` / `bash` | a shell; anything else is executed as a command |

## Ports and environment

The server listens on container port **8765** on all interfaces (`--bind 0.0.0.0`; the server's own default
stays `127.0.0.1`). It has **no authentication**: publish it on the host's loopback (`-p 127.0.0.1:8765:8765`, the
compose default) unless the network is private.

| Variable | Default | Effect |
| --- | --- | --- |
| `STS2SIM_BIND` | `0.0.0.0` | server `--bind` (`127.0.0.1` = container-internal only) |
| `STS2SIM_PORT` | `8765` | server `--port` (the healthcheck follows it) |
| `STS2SIM_CRASH_LOG` | `/data/play-crashes-<hostname>.jsonl` | crash repros (`selftest`: `/data/selftest-crashes.jsonl`); one file per replica |
| `STS2SIM_VERSION` | build arg | version in `/health`, crash repros and eval configs |
| `STS2SIM_KNOWLEDGE` | `/app/knowledge/source` | rules text directory |
| `STS2VEC_DLL` | `/app/sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll` | managed library sts2vec hosts |
| `DOTNET_gcServer` | `1` | Server GC for the server and benches (sts2vec gets it from `sts2vec.runtimeconfig.json` as well; it refuses Workstation GC) |
| `DOTNET_GCHeapCount` | unset | cap Server GC heaps (one per visible core by default) when many replicas share a host |
| `ANTHROPIC_API_KEY` | unset | for `eval run --policy claude`; or mount a key file and pass `--api-key-file` |

Healthcheck: `GET http://127.0.0.1:$STS2SIM_PORT/health` every 15 s (start period 30 s); only meaningful in `server`
mode (compose disables it for the one-shot services). `docker stop` sends SIGTERM; the server stops its listener
and exits 0 (measured 0.27 s).

Outputs land under `/data` (the working directory). It is not declared as a `VOLUME`, so one-shot runs leave no
anonymous volumes; mount a volume to keep crash logs or eval reports: `-v sts2sim-data:/data`, or a host
directory with a matching user: `-v "$PWD/tmp/docker:/data" --user "$(id -u):$(id -g)"` (create the directory
first).

## Running

```bash
# one server
docker run -d --name sts2sim-1 -p 127.0.0.1:8765:8765 sts2sim
curl -s http://127.0.0.1:8765/health                      # {"ok":true,"version":"<sha>","games":0}
python3 sim/tools/play/sim_play.py random --runs 20 --url http://127.0.0.1:8765

# N independent servers with fixed ports, plain docker
for i in 0 1 2 3; do docker run -d --name sts2sim-$i -p 127.0.0.1:$((8765+i)):8765 sts2sim; done

# numeric env / benches (one-shot)
docker run --rm sts2sim vec-check
docker run --rm sts2sim vec-bench --envs 1024 --threads 4
docker run --rm sts2sim selftest 200
```

### docker compose

`docker-compose.yml` defines:

| Service | Profile | What |
| --- | --- | --- |
| `sim` | (default) | `SIM_REPLICAS` play servers (default 4), host ports `SIM_PORTS` (default `8765-8768`, one per replica) on `SIM_HOST_IP` (default `127.0.0.1`); builds the image `SIM_IMAGE` (default `sts2sim:local`) |
| `vec` | `vec` | `vec-bench --envs $VEC_ENVS --threads $VEC_THREADS --steps $VEC_STEPS` (1024 / 4 / 500); any other mode as the run command |
| `selftest` | `selftest` | `selftest $SELFTEST_RUNS` (200) |
| `eval` | `eval` | the `sts2eval` CLI (arguments after the service name) |

```bash
STS2SIM_VERSION=$(git rev-parse --short HEAD) docker compose build
docker compose up -d                                     # 4 servers on 127.0.0.1:8765..8768
SIM_REPLICAS=8 SIM_PORTS=8765-8772 docker compose up -d   # 8 servers
docker compose --profile vec run --rm vec                # bench; or: ... run --rm vec vec-check
docker compose --profile selftest run --rm selftest
docker compose --profile eval run --rm eval run --seedset a0-s10 --policy random --concurrency 10 --spawn
docker compose --profile vec --profile eval down -v
```

Compose assigns the host ports of a range to replicas in no fixed order; `docker compose ps` shows the mapping
(the plain `docker run` loop above gives fixed ones). All replicas share the named volume `sim-data`; crash logs
are per replica (hostname in the file name).

## How the LLM runner and sts2vec connect

**LLM runner (`sts2eval`) - one server per runner, many games per server.** Game ids (`r1`, `r2`, ...) live in
one server process, so a runner must talk to the replica that created its games; never put a round-robin load
balancer or a compose service name with several replicas behind one runner URL. One server runs many games at
once (thread-pool request handling, one lock per game), so the normal setup is one `sts2eval` process with
`--concurrency N` against one server:

```bash
# host runner -> containerised server
uv run --project sim/tools/eval sts2eval run --seedset a0-s20 --policy claude --model claude-sonnet-5-5 \
    --concurrency 20 --url http://127.0.0.1:8765 --api-key-file ~/.config/sts2eval/anthropic_api_key
# runner and server in one container (the runner spawns a private server on a free port)
docker run --rm -v sts2sim-data:/data -e ANTHROPIC_API_KEY sts2sim eval run --seedset a0-s10 --policy random --spawn
```

Replicas are for several independent runners (different models or seed sets in parallel: runner *k* uses
`--url http://127.0.0.1:$((8765+k))`) or for isolating crash logs; they do not speed up one runner, whose wall time
is bound by model latency, not the simulator (a random-policy game takes about 0.2 s of server time). The API
key never goes into the image: pass `-e ANTHROPIC_API_KEY` (taken from the caller's environment) or mount the key
file read-only (`-v ~/.config/sts2eval/anthropic_api_key:/run/secrets/anthropic_api_key:ro` and
`--api-key-file /run/secrets/anthropic_api_key`).

**sts2vec - in-process, no server.** `sts2vec` hosts the simulator inside the Python process through hostfxr
(`DOTNET_ROOT=/usr/share/dotnet`, the runtime image's root) and steps N envs per call; there is no network
endpoint. Run training code inside the image (`docker run --rm -v "$PWD/train:/work" sts2sim python
/work/train.py`; `python` on the image's PATH is the sts2vec venv's interpreter, with numpy and `import sts2vec`),
or build an image `FROM sts2sim` that adds the training dependencies. Parallelism: `VecEnv(num_envs, threads=T)` inside one container; scale
out with one container per host or per NUMA node rather than many small ones (Server GC and the env's worker
threads are sized for the cores the container sees; limit them with `--cpus` and `threads=`).

## Validation (2026-11-02, Tokyo GCP VM)

Host: GCP `instance-tokyo-0701-predict-raven`, **linux/arm64, 2 vCPU**, 7.75 GiB RAM, Debian 13, Docker 29.6.1,
compose v5.3.0, buildx 0.35.0 (a dedicated `docker-container` builder, removed afterwards). Image built from
`git archive` of this branch; container .NET runtime 9.0.22. The VM's production containers kept running
throughout and were not touched.

| Measurement | Result |
| --- | --- |
| cold build (fresh builder: base-image pulls, restore, solution build, `RUN_TESTS=Server`, uv sync, export) | **73 s** total; `dotnet build` 29.7 s, 0 warnings, 0 errors; Server tests 39/39 pass in the build stage (5.1 s) |
| warm rebuild after a C# source change (restore and apt layers cached) | 41.9 s (`dotnet build` 30.9 s, 0 warnings) |
| rebuild with only `STS2SIM_VERSION` changed | 3.3 s (every build step cached; the version is a late `ENV`) |
| image size | **578 MB** (`docker images`); 143 MB content size (`docker image inspect .Size`) |
| `selftest 200` | 200/200 completed, 0 crashed, 0 capped, 0 server errors, mean 69.7 steps/run, 13.3 s (13.6 s with container start) |
| server start to first `/health` 200 | 0.26 s (`docker run -d` to answer through the published port) |
| `sim_play.py random --runs 20` from the VM host via `127.0.0.1:18765` | 20 games, 1,379 steps, 0 crashes, 4.2 s |
| same from a second container (`--network container:<server>`) | 20 games, 1,366 steps, 0 crashes, 3.1 s |
| healthcheck / memory | `healthy`; 140 MiB RSS after 40 games |
| `docker stop` | 0.27 s, exit 0 (SIGTERM handled) |
| `vec-check` | OK: schema/buffers consistent, threads 1 vs 8 byte-identical (64 envs x 200 steps), 6.4 s |
| `vec-bench --envs 1024 --threads 4` (Python, pool 64) | **15,664 env-steps/s** (65.4 ms/batch, 27.3 MB obs/batch; a second run 15,344); 221 resets/s; 0 SIM_ERROR / CONTENT_GAP |
| `vec-bench --envs 1024 --threads 2` | 16,258 env-steps/s (2 threads match the 2 vCPUs) |
| `bench vec 1024 4 500` (C# through the C ABI, pool 1024) | 19,178 env-steps/s, 273 resets/s, 0 errors |
| compose: 2 replicas on 18765-18766 | both healthy; 10 random games on the second replica in 2.1 s |
| compose `vec` profile (256 envs x 2 threads x 100 steps) | 7,471 env-steps/s |
| compose `eval` profile: `run --seedset a0-s10 --policy random --concurrency 10 --spawn` | 10 games, 0 crashes, 10/10 replayable histories, 2.0 s |
| final image (this commit's Dockerfile) smoke | `/health` reports the build-arg version; 20 random games 4.4 s; `selftest 50` 0 crashes; `vec-check` OK; `docker stop` exit 0; `vec-bench 1024 x 4` 16,446 env-steps/s on the previous build of the same code |

For scale: the same `sts2vec bench` reaches 73k env-steps/s and the C# bench 116k at 1024 envs x 8 threads on
an M4 Pro (`sim/BENCH.md`); the 2-vCPU VM gives about 8-10k env-steps/s per vCPU, and more threads than vCPUs
do not help (1024 x 4: 15.7k, 1024 x 2: 16.3k). Measure `vec-bench` on the target host before planning a
training run.

Validation procedure (repeatable): `git archive --format=tar.gz HEAD` -> `gcloud compute scp` -> unpack in a
scratch directory -> `docker buildx create --name <tmp> --driver docker-container` -> `docker buildx build
--builder <tmp> --load -t sts2sim:validate ...` under `nohup` -> the runs above (scratch container names, compose
project `-p sts2simval`, host ports 18765-18766 on loopback) -> cleanup: `docker rmi` of the tag and of the
untagged images that earlier `--load`s of the same tag left behind, `docker buildx rm <tmp>` (drops its build
cache), `docker rmi moby/buildkit:buildx-stable-1`, the anonymous volume of the pre-fix image's server run, the
scratch directory. Afterwards the VM's image, volume and build-cache totals matched the values before the run.
