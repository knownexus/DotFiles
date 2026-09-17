#!/bin/sh
# Deploys the zsh side of this dotfiles monorepo on Linux/Mac.
#
# What it does:
#   1. Symlinks ~/.resources -> <this repo>/zsh
#   2. Makes sure ~/.zshrc sources ~/.resources/rc
#   3. Deploys the shared vim config (~/.vimrc -> shared/vim/vimrc, plus the
#      cheatsheet F1 opens) so vim behaves the same here as on Windows
#   4. Installs everything this config uses so nothing has to be installed
#      by hand afterward: git, yq (read at runtime by `aliases`/`gitaliases`/
#      `cheat` for shared/commands.yaml), vim, less, tree, ripgrep, and the
#      optional integrations (bat, glow, fzf, zoxide, direnv, notify-send,
#      a clipboard tool) that `zsh-doctor` checks once the shell is up.
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

# --- 3. vim config -------------------------------------------------------------
# Symlinked, not copied, so edits to the repo take effect straight away. The
# vimrc finds cheatsheet.txt next to itself through the resolved symlink, so
# there is no second copy to drift.
VIMRC_SRC="$REPO_DIR/shared/vim/vimrc"
VIMRC_LINK="$HOME/.vimrc"

echo "==> Deploying vim config from $VIMRC_SRC"
if [ -L "$VIMRC_LINK" ] && [ "$(readlink -f "$VIMRC_LINK")" = "$VIMRC_SRC" ]; then
    echo "    ~/.vimrc already points here — skipping"
else
    if [ -e "$VIMRC_LINK" ]; then
        backup="$VIMRC_LINK.bak.$$"
        echo "    ~/.vimrc already exists — moving it to $backup"
        mv "$VIMRC_LINK" "$backup"
    fi
    ln -s "$VIMRC_SRC" "$VIMRC_LINK"
fi
echo "    ~/.vimrc -> $VIMRC_SRC"

mkdir -p "$HOME/.vim/undodir"
echo "    cheatsheet read from the repo (F1 or <space>c toggles it)"

# vim 8 packages, cloned into the repo (gitignored) rather than vendored, so
# updating is a pull and the dotfiles stay free of third-party code.
PACK_DIR="$REPO_DIR/shared/vim/pack/plugins/start"
mkdir -p "$PACK_DIR"
if [ -d "$PACK_DIR/vim-visual-multi/.git" ]; then
    echo "    vim-visual-multi: updating"
    git -C "$PACK_DIR/vim-visual-multi" pull --quiet --ff-only || \
        echo "    (update failed — keeping the existing copy)"
else
    echo "    vim-visual-multi: cloning"
    git clone --quiet --depth 1 https://github.com/mg979/vim-visual-multi.git \
        "$PACK_DIR/vim-visual-multi" || \
        echo "    (clone failed — vim works fine without it)"
fi

# --- 3b. less key bindings -----------------------------------------------------
# less 582+ reads the source file directly via $LESSKEYIN (set in
# zsh/rc.d/70-variables), so nothing needs deploying there. Older builds only
# read a compiled ~/.less, so produce one when the lesskey command exists.
if command -v lesskey >/dev/null 2>&1; then
    less_major=$(less --version 2>/dev/null | head -1 | sed -n 's/^less \([0-9]*\).*/\1/p')
    if [ -n "$less_major" ] && [ "$less_major" -lt 582 ] 2>/dev/null; then
        lesskey -o "$HOME/.less" "$REPO_DIR/shared/less/lesskey" && \
            echo "    less < 582 — compiled key bindings to ~/.less"
    else
        echo "    less reads shared/less/lesskey directly (no compile needed)"
    fi
fi

# --- 4. Install everything this config uses -----------------------------------
PKG_MANAGER=""
if command -v dnf >/dev/null 2>&1; then PKG_MANAGER=dnf
elif command -v apt-get >/dev/null 2>&1; then PKG_MANAGER=apt
elif command -v brew >/dev/null 2>&1; then PKG_MANAGER=brew
fi

# $1 = command name (used in messages), $2 = apt pkg, $3 = dnf pkg, $4 = brew pkg
install_pkg() {
    case "$PKG_MANAGER" in
        apt)  sudo apt-get install -y "$2" ;;
        dnf)  sudo dnf install -y "$3" ;;
        brew) brew install "$4" ;;
        *)
            echo "    Don't know this system's package manager — install '$1' yourself"
            return 1
            ;;
    esac
}

# $1 = command to check on PATH, $2..$4 = apt/dnf/brew package names
ensure() {
    if command -v "$1" >/dev/null 2>&1; then
        echo "    $1: found"
    else
        echo "==> $1 not found — installing"
        install_pkg "$1" "$2" "$3" "$4" || true
    fi
}

echo "==> Installing required + optional tooling"
ensure git    git       git          git
ensure yq     yq        yq           yq
ensure vim    vim       vim-enhanced vim
ensure less   less      less         less
ensure tree   tree      tree         tree
ensure rg     ripgrep   ripgrep      ripgrep
ensure fzf    fzf       fzf          fzf
ensure bat    bat       bat          bat
ensure glow   glow      glow         glow
ensure zoxide zoxide    zoxide       zoxide
ensure direnv direnv    direnv       direnv

# Desktop notifications + clipboard tools are Linux-only concepts — macOS
# has Notification Center and pbcopy/pbpaste built in, nothing to install.
if [ "$PKG_MANAGER" = "apt" ] || [ "$PKG_MANAGER" = "dnf" ]; then
    ensure notify-send libnotify-bin libnotify ""
    if ! command -v wl-copy >/dev/null 2>&1 && ! command -v xclip >/dev/null 2>&1 && ! command -v xsel >/dev/null 2>&1; then
        echo "==> No clipboard tool found — installing xclip"
        install_pkg xclip xclip xclip "" || true
    else
        echo "    clipboard tool: found"
    fi
fi

# --- 5. Final report -----------------------------------------------------------
echo ""
echo "==> Tooling status:"
for tool in git yq vim less tree rg fzf bat glow zoxide direnv notify-send wl-copy xclip xsel; do
    if command -v "$tool" >/dev/null 2>&1; then
        echo "    [x] $tool"
    else
        echo "    [ ] $tool"
    fi
done

echo ""
echo "==> Done. Open a new shell (or 'sb' in an existing one) to load everything."
