#!/bin/bash
# Reproduces the CMTK1007 race (README.md): N value objects in one namespace, one of them with a
# declared {Name}Extensions class. The consumer is built repeatedly, and a run in which the aspect
# introduced the class anyway instead of reporting CMTK1007 counts as a miss: CS0260 when the class
# was introduced beside the declared one, LAMA0531 when Metalama's own check refused it.
#   usage: stress.sh <runs> <types> [extra msbuild args, e.g. -p:MetalamaConcurrentBuildEnabled=false]
set -u
export DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1
RUNS=${1:-12}; TYPES=${2:-80}; shift 2 2>/dev/null || shift $#

# The repository root, found by walking up to the solution marker, never by a positional path.
ROOT=$(cd "$(dirname "$0")" && pwd)
while [ ! -f "$ROOT/CodoMetis.TypeKit.slnx" ]; do
  [ "$ROOT" = "/" ] && { echo "No CodoMetis.TypeKit.slnx above $0" >&2; exit 1; }
  ROOT=$(dirname "$ROOT")
done
DIR=$ROOT/artifacts/stress-cmtk1007
rm -rf "$DIR"; mkdir -p "$DIR"

cat > "$DIR/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$ROOT/src/CodoMetis.TypeKit.Generators/CodoMetis.TypeKit.Generators.csproj" />
    <ProjectReference Include="$ROOT/src/CodoMetis.TypeKit.Analyzers/CodoMetis.TypeKit.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
EOF
# Stop MSBuild's upward search here, as the build-outcome tests do.
echo '<Project />' > "$DIR/Directory.Build.props"
echo '<Project />' > "$DIR/Directory.Build.targets"
echo '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' > "$DIR/Directory.Packages.props"

{
  echo 'using CodoMetis.TypeKit.ValueObjects;'
  echo 'namespace Stress;'
  for i in $(seq 0 $((TYPES - 1))); do echo "public readonly partial record struct V$i : IValue<int>;"; done
  echo 'public static class V7Extensions { }'
} > "$DIR/Declarations.cs"

# One restore and full build, so the timed runs can go --no-dependencies like the fixture does.
dotnet build "$DIR" -nodeReuse:false -clp:NoSummary "$@" > "$DIR/first.log" 2>&1

misses=0; other=0
for run in $(seq 1 "$RUNS"); do
  out=$(dotnet build "$DIR" --no-incremental --no-dependencies -p:RestoreRecursive=false -nodeReuse:false -clp:NoSummary "$@" 2>&1)
  if echo "$out" | grep -q "error CMTK1007: 'V7'"; then
    verdict=reported
  elif echo "$out" | grep -qE "CS0260|LAMA0531"; then
    verdict=MISSED; misses=$((misses + 1))
  else
    verdict=other; other=$((other + 1)); echo "$out" | grep -E "error" | head -5
  fi
  echo "run $run: $verdict"
done
echo "SUMMARY types=$TYPES runs=$RUNS missed=$misses other=$other args='$*'"
rm -rf "$DIR"
