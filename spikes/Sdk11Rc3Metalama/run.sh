#!/usr/bin/env bash
# Runs every row of the README's table on the RC3 SDK and writes one log per row to $1.
# Usage: DOTNET=~/.dotnet-sdk11-rc3/dotnet DOTNET10=~/.dotnet-sdk11-rc3-sdk10/dotnet \
#        NUGET_PACKAGES=<empty folder> ./run.sh <out-dir>
# DOTNET10 is a host with the .NET 10 runtime, to run Multi's net10.0 output; RC3 alone has none.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:?out-dir}"
dotnet="${DOTNET:?the RC3 dotnet}"
export DOTNET_ROOT="$(dirname "$dotnet")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_UI_LANGUAGE=en
mkdir -p "$out"
cd "$here"

row() {
  local name="$1" project="$2"; shift 2
  rm -rf App/bin App/obj UnionLib/bin UnionLib/obj Multi/bin Multi/obj WebApp/bin WebApp/obj
  "$dotnet" build-server shutdown > /dev/null 2>&1
  pkill -f VBCSCompiler 2> /dev/null; pkill -f -i 'metalama' 2> /dev/null
  "$dotnet" build "$project/$project.csproj" -p:UseSharedCompilation=false -nodeReuse:false "$@" > "$out/$name.log" 2>&1
  local code=$?
  {
    echo "== $name: $project $* -> build exit $code"
    # A multi-targeted project's diagnostic keeps its target as {net10.0}; the project path is dropped.
    grep -E '(error|warning) [A-Z]+[0-9]+: ' "$out/$name.log" \
      | sed -E 's/^.*((error|warning) [A-Z]+[0-9]+: )/\1/; s/ \[[^]]*::TargetFramework=([^]]+)\]$/ {\1}/; s/ \[[^]]*\]$//' \
      | sort -u
    if [ $code -eq 0 ] && [ "$project" = App ]; then
      echo "-- run"
      "$dotnet" App/bin/Debug/net11.0/App.dll 2>&1
    fi
    if [ $code -eq 0 ] && [ "$project" = Multi ]; then
      echo "-- run"
      DOTNET_ROOT="$(dirname "${DOTNET10:?a host with the .NET 10 runtime}")" "$DOTNET10" Multi/bin/Debug/net10.0/Multi.dll 2>&1
      "$dotnet" Multi/bin/Debug/net11.0/Multi.dll 2>&1
    fi
  } | tee -a "$out/summary.txt"
}

"$dotnet" --version | tee "$out/summary.txt"
row control-no-metalama        App -p:NoGenerators=true -p:DeclareUnion=true -p:MatchUnion=true
row 1-plain                    App
row 2-version                  App -p:ProbeVersion=true
row 2b-version-2026.1.31      App -p:MetalamaFramework=2026.1.31 -p:ProbeVersion=true
row 3-declare-union            App -p:DeclareUnion=true
row 4a-hold-library-union      App -p:HoldUnion=true
row 4b-match-library-union     App -p:MatchUnion=true
row 6-2027-plain               App -p:MetalamaFramework=2027.0.5-preview
row 6-2027-version             App -p:MetalamaFramework=2027.0.5-preview -p:ProbeVersion=true
row 6-2027-declare-union       App -p:MetalamaFramework=2027.0.5-preview -p:DeclareUnion=true
row 6-2027-match-union         App -p:MetalamaFramework=2027.0.5-preview -p:MatchUnion=true
row 7-multi-2027-version       Multi -p:MetalamaFramework=2027.0.5-preview -p:ProbeVersion=true
row 7-multi-2027-plain         Multi -p:MetalamaFramework=2027.0.5-preview
row 7-multi-2027-if-union      Multi -p:MetalamaFramework=2027.0.5-preview -p:IfUnion=true
row 7-multi-2027-excluded-file Multi -p:MetalamaFramework=2027.0.5-preview -p:ExcludedFile=true
row 7-multi-2026-version       Multi -p:ProbeVersion=true
row 7-multi-2026-if-union      Multi -p:IfUnion=true
row 7-multi-2026-excluded-file Multi -p:ExcludedFile=true
