#!/usr/bin/env bash
# Runs every row of the README's table on the RC3 SDK and writes one log per row to $1.
# Usage: DOTNET=~/.dotnet-sdk11-rc3/dotnet NUGET_PACKAGES=<empty folder> ./run.sh <out-dir>
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
  rm -rf App/bin App/obj UnionLib/bin UnionLib/obj WebApp/bin WebApp/obj
  "$dotnet" build-server shutdown > /dev/null 2>&1
  pkill -f VBCSCompiler 2> /dev/null; pkill -f -i 'metalama' 2> /dev/null
  "$dotnet" build "$project/$project.csproj" -p:UseSharedCompilation=false -nodeReuse:false "$@" > "$out/$name.log" 2>&1
  local code=$?
  {
    echo "== $name: $project $* -> build exit $code"
    grep -oE '(error|warning) [A-Z]+[0-9]+: [^[]*' "$out/$name.log" | sort -u
    if [ $code -eq 0 ] && [ "$project" = App ]; then
      echo "-- run"
      "$dotnet" App/bin/Debug/net11.0/App.dll 2>&1
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
