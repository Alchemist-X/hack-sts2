#!/usr/bin/env bash
# Publish Sts2Sim.Vec as a NativeAOT shared library (CONTRACTS.md §D0 D1: optional, same exports as the hostfxr path).
#
#   scripts/vec_publish_aot.sh            # -> sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.dylib (macOS) / .so (Linux)
#   STS2VEC_AOT_LIB=sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.dylib uv run --project sim/tools/vec sts2vec check
#
# Settings that worked in the E2 binding study: PublishAot + NativeLib=Shared, TrimmerRootAssembly Sts2Sim.Core
# (the registries are reflection-built), ServerGarbageCollection, InvariantGlobalization. The Homebrew-built ILCompiler
# links openssl/brotli, so LIBRARY_PATH must include /opt/homebrew/lib, and the target machine needs those libraries.
# Builds into its own artifacts dir (bin/aot-artifacts) so the normal Release build's obj/ is not touched.
# No csproj is edited: the AOT-only item (TrimmerRootAssembly) is injected through CustomAfterMicrosoftCommonTargets.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$REPO_ROOT/sim/Sts2Sim.Vec/Sts2Sim.Vec.csproj"
OUT="$REPO_ROOT/sim/Sts2Sim.Vec/bin/aot"
ART="$REPO_ROOT/sim/Sts2Sim.Vec/bin/aot-artifacts"

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) RID="${RID:-osx-arm64}"; EXT=dylib ;;
  Darwin-x86_64) RID="${RID:-osx-x64}"; EXT=dylib ;;
  Linux-x86_64) RID="${RID:-linux-x64}"; EXT=so ;;
  Linux-aarch64) RID="${RID:-linux-arm64}"; EXT=so ;;
  *) echo "unsupported platform $(uname -s)-$(uname -m); set RID" >&2; exit 2 ;;
esac

if [[ -d /opt/homebrew/lib ]]; then
  export LIBRARY_PATH="/opt/homebrew/lib${LIBRARY_PATH:+:$LIBRARY_PATH}"
fi
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet@9/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
fi

mkdir -p "$OUT" "$ART"
TARGETS="$ART/sts2vec.aot.targets"
cat > "$TARGETS" <<'EOF'
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Sts2Sim.Vec'">
    <TrimmerRootAssembly Include="Sts2Sim.Core" />
  </ItemGroup>
</Project>
EOF

echo "publishing $PROJ for $RID (NativeAOT, NativeLib=Shared) ..."
dotnet publish "$PROJ" -c Release -r "$RID" --artifacts-path "$ART" \
  -p:PublishAot=true -p:NativeLib=Shared -p:ServerGarbageCollection=true -p:ConcurrentGarbageCollection=false \
  -p:InvariantGlobalization=true -p:IlcGenerateStackTraceData=true -p:SuppressTrimAnalysisWarnings=true \
  -p:CustomAfterMicrosoftCommonTargets="$TARGETS" -nologo

LIB="$(find "$ART/publish" -name "Sts2Sim.Vec.$EXT" -type f -not -path "*.dSYM/*" | head -n 1)"
if [[ -z "$LIB" ]]; then
  echo "no Sts2Sim.Vec.$EXT under $ART/publish" >&2
  exit 1
fi
cp "$LIB" "$OUT/Sts2Sim.Vec.$EXT"
echo "wrote $OUT/Sts2Sim.Vec.$EXT ($(du -h "$OUT/Sts2Sim.Vec.$EXT" | cut -f1))"
echo "use: STS2VEC_AOT_LIB=$OUT/Sts2Sim.Vec.$EXT uv run --project sim/tools/vec sts2vec check"
