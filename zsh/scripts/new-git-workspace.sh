#!/usr/bin/env bash
# Creates a workspace directory containing multiple git repos, each with worktrees
# for the specified branches.
#
# For each repo URL, clones a bare repo into <workspace>/<repo-name>/.bare/, creates
# a .git pointer file so tooling recognises the directory, then adds a worktree for
# each requested branch.
#
# Resulting layout:
#   <path>/
#     <repo-name>/
#       .bare/        <- bare clone
#       .git          <- file: "gitdir: ./.bare"
#       <branch>/     <- worktree per branch
#       ...
#
# Usage: new-git-workspace.sh -p <path> -r <repo-url>[,<repo-url>...] -b <branch>[,<branch>...]

set -euo pipefail

path="" repos_str="" branches_str=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--path)     shift; path="$1" ;;
        -r|--repos)    shift; repos_str="$1" ;;
        -b|--branches) shift; branches_str="$1" ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
    shift
done

if [[ -z "$path" || -z "$repos_str" || -z "$branches_str" ]]; then
    echo "Usage: new-git-workspace.sh -p <path> -r <repo-url,...> -b <branch,...>" >&2
    exit 1
fi

mkdir -p "$path"
path="$(cd "$path" && pwd)"

IFS=',' read -ra repos <<< "$repos_str"
IFS=',' read -ra branches <<< "$branches_str"

echo "Workspace: $path"

for repo_url in "${repos[@]}"; do
    repo_name="${repo_url##*/}"
    repo_name="${repo_name%.git}"
    repo_dir="$path/$repo_name"
    bare_dir="$repo_dir/.bare"

    echo ""
    echo "Cloning $repo_name..."
    mkdir -p "$repo_dir"

    if ! git clone --bare "$repo_url" "$bare_dir"; then
        echo "Failed to clone $repo_url - skipping." >&2
        continue
    fi

    # Point the repo directory at the bare clone so git tooling (IDEs, scripts)
    # works from <repo_dir> without needing to cd into .bare.
    echo 'gitdir: ./.bare' > "$repo_dir/.git"

    # Bare clones don't configure fetch refspecs by default; set it so
    # git fetch pulls down remote-tracking branches (needed for worktree add).
    git -C "$bare_dir" config remote.origin.fetch '+refs/heads/*:refs/remotes/origin/*'
    git -C "$bare_dir" fetch --all --quiet

    for branch in "${branches[@]}"; do
        worktree_path="$repo_dir/$branch"

        if git -C "$bare_dir" rev-parse --verify "origin/$branch" &>/dev/null; then
            echo "  + worktree: $branch (remote)"
            # DWIM: git auto-creates a local tracking branch when the remote ref exists.
            git -C "$bare_dir" worktree add "$worktree_path" "$branch" \
                || echo "  Could not add worktree for '$branch' in $repo_name." >&2
        else
            echo "  + worktree: $branch (new branch)"
            git -C "$bare_dir" worktree add -b "$branch" "$worktree_path" \
                || echo "  Could not add worktree for '$branch' in $repo_name." >&2
        fi
    done
done

echo ""
echo "Done. Workspace ready at: $path"
