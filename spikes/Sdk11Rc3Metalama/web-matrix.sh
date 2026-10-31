#!/usr/bin/env bash
# Builds WebApp, with Metalama 2026.1 and with 2027, under three .NET installations, one log per cell in $1:
#   rc3-alone  RC3 the only SDK in its dotnet root ($RC3_ALONE)
#   rc3-beside RC3 with SDK 10.0.403 installed in the same dotnet root ($RC3_BESIDE)
#   sdk10      SDK 10.0.403 from that root, net10.0 (today's consumer), in a copy pinned to it
#   rc1-alone  RC1 the only SDK in its dotnet root ($RC1_ALONE), in a copy pinned to it
#   rc3-net10  RC3 alone again, building net10.0: today's 2.x consumer once it moves to SDK 11
# Usage: RC3_ALONE=~/.dotnet-sdk11-rc3/dotnet RC3_BESIDE=~/.dotnet-sdk11-rc3-sdk10/dotnet \
#        RC1_ALONE=~/.dotnet-sdk11-rc1/dotnet NUGET_PACKAGES=<folder> ./web-matrix.sh <out-dir>
set -u
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:?out-dir}"
mkdir -p "$out"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_UI_LANGUAGE=en

# The SDK 10 and RC1 cells: the same project, in a copy whose global.json pins that SDK.
pinned() {
  local dir="$out/$1-copy"
  rm -rf "$dir"; mkdir -p "$dir"
  cp -R "$here/WebApp" "$here/Directory.Build.props" "$here/nuget.config" "$dir/"
  printf '{ "sdk": { "version": "%s", "rollForward": "disable", "allowPrerelease": true } }\n' "$2" > "$dir/global.json"
  echo "$dir"
}
copy="$(pinned sdk10 10.0.403)"
sed -i.bak 's|<TargetFramework>net11.0</TargetFramework>|<TargetFramework>net10.0</TargetFramework>|' "$copy/WebApp/WebApp.csproj"
rc1="$(pinned rc1 11.0.100-rc.1.26425.128)"
net10="$(pinned rc3-net10 11.0.100-rc.3.26522.148)"
sed -i.bak 's|<TargetFramework>net11.0</TargetFramework>|<TargetFramework>net10.0</TargetFramework>|' "$net10/WebApp/WebApp.csproj"

cell() {
  local name="$1" dotnet="$2" dir="$3"; shift 3
  export DOTNET_ROOT="$(dirname "$dotnet")"
  rm -rf "$dir/WebApp/bin" "$dir/WebApp/obj"
  "$dotnet" build-server shutdown > /dev/null 2>&1
  pkill -f VBCSCompiler 2> /dev/null; pkill -f -i 'metalama' 2> /dev/null
  (cd "$dir" && "$dotnet" build WebApp/WebApp.csproj -p:UseSharedCompilation=false -nodeReuse:false "$@") > "$out/$name.log" 2>&1
  local code=$?
  {
    echo "== $name: $(cd "$dir" && "$dotnet" --version) in $DOTNET_ROOT $* -> build exit $code"
    grep -oE '(error|warning) [A-Z]+[0-9]+: [^[]*' "$out/$name.log" | sort -u
  } | tee -a "$out/summary.txt"
}

: > "$out/summary.txt"
cell rc3-alone-2026       "${RC3_ALONE:?}"  "$here"
cell rc3-alone-2027       "$RC3_ALONE"      "$here" -p:MetalamaFramework=2027.0.5-preview
cell rc3-alone-2026.1.31   "$RC3_ALONE"      "$here" -p:MetalamaFramework=2026.1.31
cell rc3-beside-2026      "${RC3_BESIDE:?}" "$here"
cell rc3-beside-2027      "$RC3_BESIDE"     "$here" -p:MetalamaFramework=2027.0.5-preview
cell sdk10-2026           "$RC3_BESIDE"     "$copy"
cell sdk10-2027           "$RC3_BESIDE"     "$copy" -p:MetalamaFramework=2027.0.5-preview
cell rc1-alone-2026       "${RC1_ALONE:?}"  "$rc1"
cell rc1-alone-2027       "$RC1_ALONE"      "$rc1" -p:MetalamaFramework=2027.0.5-preview
cell rc3-net10-2026       "$RC3_ALONE"      "$net10"
cell rc3-net10-2027       "$RC3_ALONE"      "$net10" -p:MetalamaFramework=2027.0.5-preview
