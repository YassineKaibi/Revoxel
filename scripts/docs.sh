#!/usr/bin/env bash
# Project documents (CLAUDE.md, docs/, .claude/) live in a separate private
# repository whose work tree is this repo's root (D-037, D-042). The game repo
# ignores them; this wrapper versions them through a bare clone in .docs.git/.
#
#   bash scripts/docs.sh clone <url>    once per machine, in a fresh game clone
#   bash scripts/docs.sh pull           get teammates' document changes
#   bash scripts/docs.sh status         what changed locally (tracked files only)
#   bash scripts/docs.sh add <path>...  stage documents (forced past .gitignore)
#   bash scripts/docs.sh commit -m "docs(docs): ..."
#   bash scripts/docs.sh push
# Any other arguments are passed to git unchanged.
set -euo pipefail
cd "$(dirname "$0")/.."

docs_git_dir=".docs.git"
docs_git() { git --git-dir="$docs_git_dir" --work-tree=. "$@"; }

if [[ $# -eq 0 ]]; then
    sed -n '2,13p' "$0"
    exit 2
fi

case "$1" in
    clone)
        if [[ $# -ne 2 ]]; then
            echo "usage: bash scripts/docs.sh clone <url>" >&2
            exit 2
        fi
        if [[ -e "$docs_git_dir" ]]; then
            echo "docs: $docs_git_dir already exists" >&2
            exit 1
        fi
        git clone --bare "$2" "$docs_git_dir"
        docs_git config status.showUntrackedFiles no
        docs_git config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
        docs_git fetch origin
        docs_git checkout
        branch="$(docs_git symbolic-ref --short HEAD)"
        docs_git branch --set-upstream-to="origin/$branch" "$branch"
        echo "docs: checked out $(docs_git ls-files | wc -l) files from $2"
        ;;
    add)
        shift
        docs_git add -f "$@"
        ;;
    *)
        docs_git "$@"
        ;;
esac
