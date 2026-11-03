#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
output="${OUTPUT_DIRECTORY:-$root/../Releases}"
if [ "$#" -eq 0 ]; then set -- win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64; fi
for runtime in "$@"; do
  single=false
  case "$runtime" in win-*) single=true;; esac
  dotnet publish "$root/PackagingAutomation.csproj" -c Release -r "$runtime" --self-contained true "-p:PublishSingleFile=$single" -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "$output/$runtime"
done
