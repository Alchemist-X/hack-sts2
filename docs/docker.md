# Running Sts2Sim in containers

One image (`Dockerfile` at the repo root) carries everything needed to evaluate agents against the simulator:

- the HTTP play server `Sts2Sim.Server` (LLM agents; API in `sim/PLAY.md`),
- `sts2vec`, the batched numpy interface for neural nets (in-process .NET hosting; `sim/VEC.md`, `sim/CONTRACTS.md`
  Part D),
- `Sts2Sim.Bench` (C# benches, including the C-ABI `vec` bench),
- `sts2eval` (the LLM / random evaluation runner, `sim/tools/eval/README.md`) and `sim_play.py` (shell client),
- the rules text (`knowledge/source`, the game's localisation used for card/relic/power descriptions).

The simulator is architecture-neutral IL and the base images are multi-arch, so the same Dockerfile builds on
linux/amd64 and linux/arm64 (validated on both: arm64 on the Tokyo VM and amd64 on Huginn, below).

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
2. `aot` (from `build`, `VEC_AOT=1` by default): installs `clang` and `zlib1g-dev` and runs
   `scripts/vec_publish_aot.sh`, which publishes `Sts2Sim.Vec` as a NativeAOT shared library for the build platform
   (`linux-x64` on amd64, `linux-arm64` on arm64; 13 MB, about 80 s on Huginn). `--build-arg VEC_AOT=0` skips it
   (the image then has only the hostfxr path).
3. `runtime` (`mcr.microsoft.com/dotnet/runtime:9.0`, Debian 12): adds `python3` (3.11), `tini` and `uv`
   (0.11.7, copied from `ghcr.io/astral-sh/uv`), copies the staged binaries, `sim/PLAY.md`, `sim/VEC.md`,
   `sim/CONTRACTS.md`, `sim/tools/{vec,eval,play}`, `knowledge/source` and the AOT library
   (`/app/sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.so`), and creates the `sts2vec` and
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
| `STS2VEC_DLL` | `/app/sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll` | managed library sts2vec hosts (hostfxr path) |
| `STS2VEC_AOT_LIB` | set by the entrypoint to `$STS2VEC_AOT_DEFAULT` when unset and the file exists | NativeAOT library sts2vec loads first; `-e STS2VEC_AOT_LIB=` (empty) selects the hostfxr path |
| `STS2VEC_AOT_DEFAULT` | `/app/sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.so` | where the `aot` stage put the library |
| `DOTNET_gcServer` | `1` | Server GC for the server and benches (sts2vec gets it from `sts2vec.runtimeconfig.json` as well; it refuses Workstation GC) |
| `DOTNET_GCHeapCount` | unset | cap Server GC heaps (one per visible core by default) when many replicas share a host |
| `ANTHROPIC_API_KEY` | unset | for `eval run --policy claude`; or mount a key file and pass `--api-key-file` |

Healthcheck: `GET http://127.0.0.1:$STS2SIM_PORT/health` every 15 s (start period 30 s); only meaningful in `server`
mode (compose disables it for the one-shot services). `docker stop` sends SIGTERM; the server stops its listener
and exits 0 (measured 0.27 s).

Outputs land under `/data` (the working directory). It is not declared as a `VOLUME`, so one-shot runs leave no
anonymous volumes; mount a volume to keep crash logs or eval reports: `-v sts2sim-data:/data`, or a host
directory with a matching user: `-v "$PWD/tmp/docker:/data" --user "$(id -u):$(id -g)"` (create the directory
first). `sts2eval` writes its default report directory to `<repo root>/tmp/eval/<run_id>`; in the image
`/app/tmp` is a symlink to `/data/tmp` (the `eval` entry point creates `/data/tmp` on a fresh mount), so reports
printed as `/app/tmp/eval/<run_id>/report.md` live in `/data/tmp/eval/<run_id>`.

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

**Several servers, one runner: `--urls`.** `sts2eval run --urls http://127.0.0.1:8765,http://127.0.0.1:8766,...`
spreads one seed set over several servers: game *i* runs on server *i* mod N for its whole life, and every game
summary records its `server_url` (`config.json`: `server_urls`, `server_versions`). With `scripts/sts2_cluster.sh`
(next section) the list is `--urls "$(scripts/sts2_cluster.sh urls)"`. For the LLM policy the bottleneck is the
model, so one server is enough; `--urls` is for fast policies (random, small NNs) and for spreading load.

**sts2vec - in-process, no server.** `sts2vec` hosts the simulator inside the Python process through hostfxr
(`DOTNET_ROOT=/usr/share/dotnet`, the runtime image's root) and steps N envs per call; there is no network
endpoint. Run training code inside the image (`docker run --rm -v "$PWD/train:/work" sts2sim python
/work/train.py`; `python` on the image's PATH is the sts2vec venv's interpreter, with numpy and `import sts2vec`),
or build an image `FROM sts2sim` that adds the training dependencies. Parallelism: `VecEnv(num_envs, threads=T)` inside one container; scale
out with one container per host or per NUMA node rather than many small ones (Server GC and the env's worker
threads are sized for the cores the container sees; limit them with `--cpus` and `threads=`).

## Many servers on one host: `scripts/sts2_cluster.sh`

One command starts N play servers on a Docker host, locally or over ssh (`--host raven` runs every `docker` and
`curl` call as `ssh raven ...`). Server *i* is container `<name>-<i>` (default name `sts2sim`), published on
`127.0.0.1:<base-port + i>` of the docker host, labelled `sts2sim.cluster=<name>`.

```bash
scripts/sts2_cluster.sh up 8 --host raven --image sts2sim:latest --base-port 8765   # replaces a running cluster, waits for /health
scripts/sts2_cluster.sh status --host raven      # container state, docker health, live /health per server (exit 1 if one is down)
scripts/sts2_cluster.sh urls --host raven        # http://127.0.0.1:8765,...  (for sts2eval --urls)
scripts/sts2_cluster.sh logs 3 --host raven --tail 50
scripts/sts2_cluster.sh tunnel --host raven      # ssh -L for every port: use the remote servers from this machine
scripts/sts2_cluster.sh down --host raven
# optional limits per server: --cpus 1 --memory 512m --gc-heaps 1 (DOTNET_GCHeapCount); server args after '--'
scripts/sts2_cluster.sh up 4 -- --reveal-moves
```

Runner on the same host as the servers (no tunnel; the image's runner on the host network):

```bash
docker run --rm --network host --user "$(id -u):$(id -g)" -v "$PWD/out:/data" sts2sim:latest \
    eval run --seedset a0-s20 --policy random --concurrency 32 --urls "$(scripts/sts2_cluster.sh urls)" --out /data/run
```

Runner on a laptop: `scripts/sts2_cluster.sh tunnel --host raven &` and then `sts2eval run ... --urls "$(scripts/sts2_cluster.sh urls --host raven)"`
(validated: 10 games, 5 per server, 0 crashes, 10/10 replayable). Measured capacity per server, per core and per
container: `docs/capacity-2026-11-02.md`; `scripts/cluster_capacity.sh OUT_DIR` repeats the measurements on a host.

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

## Validation, amd64 (Huginn, 2026-11-02)

Host: Huginn (`ssh raven`), **linux/amd64**, Intel Xeon Platinum KVM guest, 4 cores x 2 threads = **8 vCPU**,
30 GiB RAM, Ubuntu 24.04.4, kernel 6.8, Docker 29.9.2 (default `docker` builder, BuildKit v0.34.0, containerd image
store on overlayfs, cgroup v2, data-root `/data/raven/docker`), compose v5.6.1, buildx v0.38.1. Image built from
`git archive` of `ebc055f` unpacked under `/data/raven/hack-sts2/tmp/sts2sim-docker/ebc055f/` (Huginn's root fs has
about 3 GB free, so nothing went to `$HOME`); container .NET runtime 9.0.22, Python 3.11.2. Compare: the arm64 rows
above (2 vCPU) and the native Huginn rows in `docs/linux-validation-2026-11-01.md` (same machine, no container).

| Measurement | Result (amd64 container) | Native Huginn / Tokyo arm64 container |
| --- | --- | --- |
| cold build (`RUN_TESTS=Server`, empty cache, base-image pulls) | **79.3 s** total: sdk image pull 33.0 s, runtime pull 12.2 s, apt 15.6 s, restore 1.9 s, `dotnet build` 19.2 s (0 warnings, 0 errors), Server tests 39/39 7.2 s, uv sync 2.9 s, export 6.7 s | native build 20.4 s / Tokyo cold 73 s |
| warm rebuild after a C# source change | 32.0 s (`dotnet build` 19.6 s, 0 warnings) | Tokyo 41.9 s |
| rebuild with only `STS2SIM_VERSION` changed | 3.0 s | Tokyo 3.3 s |
| image size | **563 MB** (`docker images` disk usage), 147 MB content | Tokyo 578 MB / 143 MB |
| `selftest 200` | 200/200 completed, 0 crashed, 0 capped, 0 server errors, mean 69.7 steps, 18.6 s (19.0 s with container start); compose `selftest` profile 18.5 s | native `--selftest 300` 23.8 s / Tokyo 13.3 s |
| server start to first `/health` 200 | 0.41 s | Tokyo 0.26 s |
| `sim_play.py random --runs 20` from the host via `127.0.0.1:18765` | 20 games, 1,379 steps, 0 crashes, 4.19 s | Tokyo 4.2 s, 1,379 steps |
| same from a second container (`--network container:<server>`) | 20 games, 1,379 steps, 0 crashes, 3.18 s | Tokyo 3.1 s |
| healthcheck / memory / stop | `healthy`; 93.5 MiB after 40 games; `docker stop` 0.25 s, exit 0 | Tokyo 140 MiB, 0.27 s |
| `vec-check` | OK: threads 1 vs 8 byte-identical (64 envs x 200 steps), 6.8 s | native OK / Tokyo 6.4 s |
| `vec-bench --envs 1024 --threads 8 --steps 500` (Python, hostfxr, pool 64) | **19,140** and 21,065 env-steps/s (53.5 / 48.6 ms/batch), 270-297 resets/s, OK 504,778 / RESET 7,222 | native 21,556 / Tokyo (1024 x 4) 15,664 |
| `bench vec 1024 8 500` (C# through the C ABI, pool 1024) | **27,956** and 28,278 env-steps/s (36.6 / 36.2 ms/batch), pool warm 11.6-11.9 s, 7,286 episodes, 0 errors | native 28,072 / Tokyo (1024 x 4) 19,178 |
| `bench vec 1024 8 3000` (steady state) | **50,900** env-steps/s (20.1 ms/batch), 44,417 episodes | native 50,452 |
| `bench` (combat, NIBBITS_WEAK, 5 s) | 41,203 decisions/s, Clone 8.551 us, StateHash 4.481 us, sink `40788C706BFA21E0` | native 41,400 / 8.595 / 4.532 us, same sink |
| compose: 2 replicas on 18765-18766 | both up in 0.76 s, both `healthy`; 10 random games on each, 687 steps, 2.70 s each | Tokyo 2.1 s |
| compose `vec` profile (`VEC_THREADS=8`, 1024 envs, 500 steps) | 18,116 env-steps/s | - |
| compose `eval` profile: `run --seedset a0-s10 --policy random --concurrency 10 --spawn` | first run **failed** (`PermissionError: '/app/tmp'`, see below); after the fix 10 games, 0 crashes, 10/10 replayable, 2.64 s; also with a fresh host bind mount (`--user`) 2.39 s and with no volume 2.24 s | Tokyo 2.0 s |

The container costs nothing measurable against the native Huginn run: every throughput row is within run-to-run noise
of the native row, and the episode counts, status counts, `total_steps` of the seeded random games and the combat hash
sink are identical across x86_64 and arm64 and between container and host. The same caveat as native applies: the
500-step vec windows are mostly tiered-JIT warm-up on 4 cores (500 steps: 28k, 3000 steps: 51k).

**Fix made.** `sts2eval`'s default output directory is `<repo root>/tmp/eval/<run_id>`, which in the image is
`/app/tmp`; `/app` is root-owned and the image runs as uid 1654, so `eval run ... --spawn` without `--out` failed
with `PermissionError`. This is not architecture-specific. The Dockerfile now links `/app/tmp -> /data/tmp`
(created and owned by `app`), and the `eval` entry point runs `mkdir -p /data/tmp` so a fresh bind mount on `/data`
works too. Nothing else needed changing for amd64.

Cleanup: compose project `sts2simval` brought down with `-v` (its volume and network removed), scratch containers
removed, `docker builder prune -f` (2.08 GB -> 607 MB) then `docker builder prune -af` (0 B), the tarball and
unpacked tree deleted. Left on Huginn: the image `sts2sim:validate` (563 MB, built from `ebc055f` plus this fix,
`/health` version `ebc055f+fix`) and the four build logs in `/data/raven/hack-sts2/tmp/sts2sim-docker/ebc055f/`.
