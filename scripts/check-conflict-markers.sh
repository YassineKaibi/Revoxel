#!/usr/bin/env bash
# Fails if a tracked or untracked (not ignored) file under Assets/, Packages/ or
# ProjectSettings/ contains a git conflict marker line (D-040). Covers the scene,
# prefab and asset files that `unity projects verify` does not scan.
set -euo pipefail
cd "$(dirname "$0")/.."

code=0
git grep --untracked -nE '^(<{7}|>{7})( |$)' -- Assets Packages ProjectSettings || code=$?
case "$code" in
    0)
        echo "conflict markers: FOUND (lines above). Resolve the merge before committing." >&2
        exit 1
        ;;
    1) echo "conflict markers: none" ;;
    *)
        echo "conflict markers: git grep failed (exit $code)" >&2
        exit "$code"
        ;;
esac
