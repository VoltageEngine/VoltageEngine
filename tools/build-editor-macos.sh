#!/bin/bash
set -euo pipefail

usage() {
    echo "Usage: $0 [-c Editor-Debug|Editor-Release] [-r osx-x64|osx-arm64] [--no-restore]"
}

configuration=Editor-Debug
case "$(uname -m)" in
    arm64) runtime=osx-arm64 ;;
    x86_64) runtime=osx-x64 ;;
    *) echo "Unsupported architecture" >&2; exit 1 ;;
esac
no_restore=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            configuration=$2; shift 2 ;;
        -r|--runtime)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            runtime=$2; shift 2 ;;
        --no-restore) no_restore=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done
[[ $(uname -s) == Darwin ]] || { echo "This command builds the editor on macOS" >&2; exit 1; }
[[ $configuration == Editor-Debug || $configuration == Editor-Release ]] || { usage >&2; exit 2; }
[[ $runtime == osx-x64 || $runtime == osx-arm64 ]] || { usage >&2; exit 2; }

repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
lock_dir="$repo_dir/Voltage.Editor/obj/.macos-build-lock"
mkdir -p -- "$(dirname -- "$lock_dir")"
if ! mkdir -- "$lock_dir" 2>/dev/null; then
    echo "Another editor build holds $lock_dir" >&2
    echo "If it was interrupted, verify it has stopped before removing that directory" >&2
    exit 1
fi
trap 'rm -f -- "$lock_dir/pid"; rmdir -- "$lock_dir"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
echo $$ > "$lock_dir/pid"

if pgrep -x Voltage.Editor >/dev/null || pgrep -f 'dotnet .*Voltage\.Editor\.dll([[:space:]]|$)' >/dev/null; then
    echo "Close Voltage Editor before rebuilding its binaries" >&2
    exit 1
fi

cd -- "$repo_dir"
build_args=(build Voltage.Editor/Voltage.Editor.csproj
    -c "$configuration" -r "$runtime"
    --disable-build-servers -m:1 /nodeReuse:false
    -p:UseSharedCompilation=false -p:BuildInParallel=false)
if [[ $no_restore == true ]]; then build_args+=(--no-restore); fi
DOTNET_CLI_USE_MSBUILD_SERVER=false dotnet "${build_args[@]}"
