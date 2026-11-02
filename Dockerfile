# syntax=docker/dockerfile:1
# Sts2Sim container: HTTP play server (LLM agents), sts2vec (batched numeric env for NNs), benches, eval runner.
# Build:  docker build -t sts2sim --build-arg STS2SIM_VERSION=$(git rev-parse --short HEAD) .
# Run:    docker run --rm -p 127.0.0.1:8765:8765 sts2sim            (server; usage in docs/docker.md)
# Multi-arch: the base images are multi-arch (linux/amd64, linux/arm64); the simulator is architecture-neutral IL.

ARG DOTNET_TAG=9.0
ARG UV_VERSION=0.11.7

# ---------------------------------------------------------------------------------------------------------------
# build: dotnet build of the whole solution (Release, warnings are errors: the repo's 0-warning gate)
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
WORKDIR /src
# restore layer: only the solution and project files, so a source edit does not re-download packages
COPY sim/Sts2Sim.sln sim/
COPY sim/Sts2Sim.Core/Sts2Sim.Core.csproj sim/Sts2Sim.Core/
COPY sim/Sts2Sim.Diff/Sts2Sim.Diff.csproj sim/Sts2Sim.Diff/
COPY sim/Sts2Sim.Server/Sts2Sim.Server.csproj sim/Sts2Sim.Server/
COPY sim/Sts2Sim.Vec/Sts2Sim.Vec.csproj sim/Sts2Sim.Vec/
COPY sim/Sts2Sim.Bench/Sts2Sim.Bench.csproj sim/Sts2Sim.Bench/
COPY sim/Sts2Sim.Tests/Sts2Sim.Tests.csproj sim/Sts2Sim.Tests/
RUN dotnet restore sim/Sts2Sim.sln
COPY sim/ sim/
COPY knowledge/source/ knowledge/source/
RUN dotnet build sim/Sts2Sim.sln -c Release --no-restore -warnaserror -nologo -clp:Summary
# Optional in-build test gate: --build-arg RUN_TESTS=Server (the value is the test runner's name filter)
ARG RUN_TESTS=
RUN if [ -n "$RUN_TESTS" ]; then dotnet sim/Sts2Sim.Tests/bin/Release/net9.0/Sts2Sim.Tests.dll $RUN_TESTS; fi
# Stage the runtime tree with the repository layout, so every default path (sts2vec's dll discovery,
# sts2eval --spawn, rules-text discovery) resolves inside the image exactly as in a checkout.
RUN set -eux; \
    for p in Server Vec Bench; do \
      mkdir -p /out/sim/Sts2Sim.$p/bin/Release; \
      cp -r sim/Sts2Sim.$p/bin/Release/net9.0 /out/sim/Sts2Sim.$p/bin/Release/; \
    done; \
    cp sim/Sts2Sim.sln sim/PLAY.md sim/VEC.md sim/CONTRACTS.md /out/sim/

# ---------------------------------------------------------------------------------------------------------------
FROM ghcr.io/astral-sh/uv:${UV_VERSION} AS uv

# ---------------------------------------------------------------------------------------------------------------
# runtime: .NET runtime (ships hostfxr, which sts2vec uses to host the simulator in-process) + python3 + uv venvs
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_TAG} AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 tini \
 && rm -rf /var/lib/apt/lists/*
COPY --from=uv /uv /uvx /usr/local/bin/

ENV DOTNET_gcServer=1 \
    DOTNET_gcConcurrent=0 \
    DOTNET_TieredPGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_ROOT=/usr/share/dotnet \
    STS2SIM_KNOWLEDGE=/app/knowledge/source \
    STS2VEC_DLL=/app/sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll \
    STS2SIM_BIND=0.0.0.0 \
    STS2SIM_PORT=8765 \
    UV_PYTHON_DOWNLOADS=never \
    UV_PYTHON=/usr/bin/python3 \
    UV_LINK_MODE=copy \
    UV_FROZEN=1 \
    UV_NO_CACHE=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app
COPY --from=build /out/ /app/
COPY knowledge/source/ /app/knowledge/source/
COPY sim/tools/vec/ /app/sim/tools/vec/
COPY sim/tools/eval/ /app/sim/tools/eval/
COPY sim/tools/play/ /app/sim/tools/play/
COPY docker/entrypoint.sh /usr/local/bin/sts2sim
# Python venvs from the committed lockfiles (no dev group). The project packages are installed editable, so
# sts2vec's repo-relative discovery points at /app/sim.
# /app/tmp -> /data/tmp: sts2eval's default output dir is <repo root>/tmp/eval/<run_id>, and /app is root-owned.
RUN set -eux; \
    chmod 0755 /usr/local/bin/sts2sim; \
    uv sync --frozen --no-dev --project /app/sim/tools/vec; \
    uv sync --frozen --no-dev --project /app/sim/tools/eval; \
    mkdir -p /data/tmp; \
    ln -s /data/tmp /app/tmp; \
    chown -R "$APP_UID" /data /app/sim/tools

# version string for /health (the image has no git); declared late so a new value re-runs no build step
ARG STS2SIM_VERSION=unknown
ENV STS2SIM_VERSION=${STS2SIM_VERSION}

# non-root: the 'app' user (uid $APP_UID = 1654) that the .NET 8+ images ship
USER $APP_UID
ENV PATH="/app/sim/tools/vec/.venv/bin:/app/sim/tools/eval/.venv/bin:${PATH}"
# working directory for relative outputs: crash logs, sts2eval's tmp/eval/<run_id>. Not a VOLUME on purpose
# (no anonymous volume per container); mount one to keep outputs: -v sts2sim-data:/data
WORKDIR /data
EXPOSE 8765

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["python3", "-c", "import os,urllib.request; urllib.request.urlopen('http://127.0.0.1:%s/health' % os.environ.get('STS2SIM_PORT','8765'), timeout=4).read()"]

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/sts2sim"]
CMD ["server"]
