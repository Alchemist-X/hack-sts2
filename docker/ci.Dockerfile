# syntax=docker/dockerfile:1
# Toolchain image for the Linux gate run (scripts/ci_linux.sh, .github/workflows/gates.yml): the .NET 9 SDK plus
# python3 and uv, nothing from the repository. The checkout is bind-mounted at /work at run time, so one image
# serves every commit and a source edit never rebuilds it.
#   docker build -f docker/ci.Dockerfile -t sts2sim-ci docker/
#   docker run --rm -v "$PWD:/work" -w /work --user "$(id -u):$(id -g)" -e HOME=/work/.ci-home \
#       sts2sim-ci bash scripts/gates.sh --quick --skip g0
# Same base tags as the product image (Dockerfile): SDK 9.0 (Debian 12, python3 3.11) and uv 0.11.7.
ARG DOTNET_TAG=9.0
ARG UV_VERSION=0.11.7

FROM ghcr.io/astral-sh/uv:${UV_VERSION} AS uv

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG}
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 \
 && rm -rf /var/lib/apt/lists/*
COPY --from=uv /uv /uvx /usr/local/bin/
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    UV_PYTHON_DOWNLOADS=never \
    UV_PYTHON=/usr/bin/python3 \
    UV_LINK_MODE=copy \
    UV_NO_PROGRESS=1 \
    PYTHONDONTWRITEBYTECODE=1
WORKDIR /work
CMD ["bash", "scripts/gates.sh", "--quick", "--skip", "g0"]
