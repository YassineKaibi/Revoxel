#!/usr/bin/env bash
# Revoxel gate (CLAUDE.md "Gate", D-035). Stops at the first failing step.
#   bash scripts/gate.sh          steps 1 to 5
#   bash scripts/gate.sh --build  steps 1 to 6 (adds the Windows x64 build)
set -euo pipefail

cd "$(dirname "$0")/.."

build=false
for arg in "$@"; do
    case "$arg" in
        --build) build=true ;;
        *)
            echo "gate: unknown argument '$arg' (only --build is accepted)" >&2
            exit 2
            ;;
    esac
done

version_file="ProjectSettings/ProjectVersion.txt"
editor_version="$(sed -n 's/^m_EditorVersion: *//p' "$version_file" | tr -d '[:space:]')"
if [[ -z "$editor_version" ]]; then
    echo "gate: could not read m_EditorVersion from $version_file" >&2
    exit 1
fi

mkdir -p Logs

step() {
    echo
    echo "==> $*"
}

# Runs one unity test mode and tells "tests failed" apart from "no verdict".
run_tests() {
    local mode="$1"
    local results="$2"
    local code=0
    unity test --mode "$mode" -o "$results" --non-interactive || code=$?
    case "$code" in
        0) echo "gate: $mode tests passed ($results)" ;;
        8)
            echo "gate: $mode TESTS FAILED (exit 8). Fix the code. Results: $results" >&2
            exit 8
            ;;
        *)
            echo "gate: $mode NO VERDICT (exit $code): compile error, license, editor crash or timeout." >&2
            echo "gate: do not touch the tests; read the editor log: Logs/Editor.log" >&2
            exit "$code"
            ;;
    esac
}

step "1/6 Preflight: unity doctor --ci"
unity doctor --ci --format human

step "2/6 Format check: dotnet csharpier check ."
dotnet tool restore
dotnet csharpier check .

step "3/6 Project check: unity projects verify (editor $editor_version) and conflict markers"
unity projects verify --strict --expect-editor "$editor_version" --format human
bash scripts/check-conflict-markers.sh

step "4/6 Compile with analyzers and EditMode tests"
run_tests EditMode Logs/editmode.xml

step "5/6 PlayMode tests"
run_tests PlayMode Logs/playmode.xml

if [[ "$build" == true ]]; then
    step "6/6 Windows x64 build"
    build_args=(--target StandaloneWindows64 --output Builds/Windows/Revoxel.exe --non-interactive)
    if [[ -n "$(git status --porcelain)" ]]; then
        echo "gate: working tree is dirty; building with --allow-dirty-build (not WP evidence)"
        build_args+=(--allow-dirty-build)
    fi
    unity build "${build_args[@]}"
fi

echo
echo "gate: all steps passed"
