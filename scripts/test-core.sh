#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then
    dotnet_bin="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
    dotnet_bin="$HOME/.dotnet/dotnet"
else
    printf '%s\n' 'Установите .NET SDK, указанный в global.json.' >&2
    exit 1
fi

cd "$repo_root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$dotnet_bin" test tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj -c Release -p:RestoreLockedMode=true "$@"
