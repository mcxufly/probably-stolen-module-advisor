#!/usr/bin/env bash
set -euo pipefail
ROOT="$(dirname "$(readlink -f "$0")")"
export DOTNET_CLI_HOME="$ROOT/.dotnet"
export NUGET_PACKAGES="$ROOT/.nuget"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
dotnet build "$ROOT/ModuleAdvisor.csproj" -c Release "$@"
python3 "$ROOT/tools/package.py"
