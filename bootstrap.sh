#!/bin/sh
# Deploys the zsh side of this dotfiles monorepo on Linux/Mac.
#
# What it does:
#   1. Symlinks ~/.resources -> <this repo>/zsh
#   2. Makes sure ~/.zshrc sources ~/.resources/rc
#   3. Installs git-subtree-adjacent required tooling (yq — needed at
#      runtime by `aliases`/`gitaliases`/`cheat` to read shared/commands.yaml)
#   4. Reports which optional integrations (bat, glow, fzf, zoxide, direnv,
#      notify-send, clipboard tools) are already available, same list
#      `zsh-doctor` checks once the shell is up.
#
# Safe to re-run — every step is idempotent.

set -eu

REPO_DIR=$(cd "$(dirname "$0")" && pwd)
RESOURCES_LINK="$HOME/.resources"
ZSHRC="$HOME/.zshrc"

echo "==> Deploying zsh config from $REPO_DIR/zsh"

# --- 1. ~/.resources symlink -------------------------------------------------
if [ -L "$RESOURCES_LINK" ] && [ "$(readlink -f "$RESOURCES_LINK")" = "$(cd "$REPO_DIR/zsh" && pwd)" ]; then
    echo "    ~/.resources already points here — skipping"
elif [ -e "$RESOURCES_LINK" ]; then
    backup="$RESOURCES_LINK.bak.$$"
    echo "    ~/.resources already exists and points elsewhere — moving it to $backup"
    mv "$RESOURCES_LINK" "$backup"
    ln -s "$REPO_DIR/zsh" "$RESOURCES_LINK"
else
    ln -s "$REPO_DIR/zsh" "$RESOURCES_LINK"
fi
echo "    ~/.resources -> $REPO_DIR/zsh"

# --- 2. ~/.zshrc sourcing line -----------------------------------------------
SOURCE_LINE='source ~/.resources/rc'
OLD_SOURCE_LINE='source ~/.resources/zsh/rc'
if [ -f "$ZSHRC" ] && grep -qF "$SOURCE_LINE" "$ZSHRC"; then
    echo "    ~/.zshrc already sources ~/.resources/rc — skipping"
elif [ -f "$ZSHRC" ] && grep -qF "$OLD_SOURCE_LINE" "$ZSHRC"; then
    # Pre-monorepo layout had an extra zsh/ segment (~/.resources pointed at
    # the repo root, which itself had a nested zsh/ dir) — that path no
    # longer exists now that ~/.resources points straight at zsh/, so the
    # old line has to be rewritten, not left alongside the new one.
    sed -i.bak "s#$OLD_SOURCE_LINE#$SOURCE_LINE#" "$ZSHRC"
    echo "    Rewrote the old '$OLD_SOURCE_LINE' line in ~/.zshrc to '$SOURCE_LINE' (backup: ~/.zshrc.bak)"
else
    echo "" >> "$ZSHRC"
    echo "$SOURCE_LINE" >> "$ZSHRC"
    echo "    Added '$SOURCE_LINE' to ~/.zshrc"
fi

# --- 3. Required tooling ------------------------------------------------------
install_pkg() {
    pkg="$1"
    if command -v dnf >/dev/null 2>&1; then sudo dnf install -y "$pkg"
    elif command -v apt-get >/dev/null 2>&1; then sudo apt-get install -y "$pkg"
    elif command -v brew >/dev/null 2>&1; then brew install "$pkg"
    else
        echo "    Don't know this system's package manager — install '$pkg' yourself"
        return 1
    fi
}

if ! command -v yq >/dev/null 2>&1; then
    echo "==> yq not found (required — aliases/gitaliases/cheat read shared/commands.yaml through it)"
    install_pkg yq || true
else
    echo "    yq: found"
fi

if ! command -v git >/dev/null 2>&1; then
    echo "==> git not found"
    install_pkg git || true
else
    echo "    git: found"
fi

# --- 4. Optional integrations report -----------------------------------------
echo ""
echo "==> Optional integrations (inert until installed, activate automatically):"
for tool in bat glow fzf zoxide direnv notify-send wl-copy xclip xsel; do
    if command -v "$tool" >/dev/null 2>&1; then
        echo "    [x] $tool"
    else
        echo "    [ ] $tool"
    fi
done

echo ""
echo "==> Done. Open a new shell (or 'sb' in an existing one) to load everything."
