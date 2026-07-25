# zsh config guide

Everything added/changed in this config beyond the original setup. Read this
with `view GUIDE.md` (syntax-highlighted, if `bat` is installed) or `less`.
For a live, in-shell version of the alias tables, run `cheat` any time.

- [Shell behavior settings](#shell-behavior-settings)
- [Prompt](#prompt)
- [Safety changes to familiar commands](#safety-changes-to-familiar-commands)
- [New commands](#new-commands)
- [Git — new/changed commands](#git--newchanged-commands)
- [Key bindings](#key-bindings)
- [Optional tool integrations (gated on being installed)](#optional-tool-integrations-gated-on-being-installed)
- [Notable bugs fixed](#notable-bugs-fixed)

---

## Shell behavior settings

These changed the shell's default behavior, not just added a command.

| Setting | What it does |
|---|---|
| `AUTO_PUSHD` | `cd` automatically pushes onto the directory stack. Combined with `PUSHD_IGNORE_DUPS` and `PUSHD_SILENT`, `cd -N` / `cd -<Tab>` hops back through the last 20 (`DIRSTACKSIZE=20`) directories you were in. |
| `AUTO_CD` | Typing a bare directory name (no `cd`) changes into it. |
| `CORRECT` | Misspelled command names get a "did you mean...?" suggestion. |
| `unsetopt BEEP` | No audible beep on tab-complete misses etc. |
| Completion caching | `zstyle ':completion:*' use-cache on`, cached under `~/.zsh_cache`. Speeds up completions that shell out (branch lists, etc.). |
| `zstyle ':completion:*' menu select` | A second consecutive Tab press turns the completion list into an arrow-key-navigable menu (composes with the pre-existing `no_auto_menu`, which governs the *first* Tab press). |
| `zstyle ':completion:*' list-colors "$LS_COLORS"` | The completion menu color-codes entries the same way `ls` does. |

## Prompt

| Change | Detail |
|---|---|
| Git branch/status now actually shows | `_find_git` was gated on a hardcoded `/usr/lib/git-core/git-name-rev` path that doesn't exist on this system (uses `/usr/libexec/git-core`) — so the VCS segment silently never rendered. Fixed; `RPS1` now shows `#/reponame[branch:sha,status]` inside any git repo. |
| Exit-code indicator | A red `✗` appears in `PS1` when the last command's exit status was non-zero. (Fixed once already — see [Notable bugs fixed](#notable-bugs-fixed).) |
| Background job count | `PS1` shows `[N]` when you have N background jobs. |
| Path truncation outside git repos | `RPS1` shows `.../c/d/e` instead of the full path once you're more than 3 levels deep and not in a VCS-tracked directory. |
| Long-command desktop notification | Any command that runs longer than `REPORTTIME` (10s) fires a `notify-send` with the command and duration when it finishes — useful for a build/test you've tabbed away from. No-ops if `notify-send` isn't installed. |

## Safety changes to familiar commands

These change behavior you might type from muscle memory — worth knowing about explicitly.

| Command | Old behavior | New behavior |
|---|---|---|
| `cp`, `mv` | Silent overwrite | `-i` — confirms before overwriting a file |
| `rm`, `rmr` | Deletes immediately | Moves to `~/.trash` instead. Restore with `trash-restore <name>`, permanently clear with `trash-empty` (which also confirms). Internal config cleanup uses `command rm` to bypass this, so temp files still delete for real. |
| `grh` (`git reset --hard`) | Ran immediately | Shows what would be discarded, asks `y/n` first |
| `gclean` (`git clean -fd`) | Ran immediately | Shows what would be deleted (`git clean -fdn` preview), asks `y/n` first |
| `gpf` / `gitfp` / `gitpf` (`git push --force`) | Ran immediately | Shows which upstream would be overwritten, asks `y/n` first |

## New commands

| Command | What it does |
|---|---|
| `cheat` (alias for `allaliases`) | Prints the full alias/command cheatsheet — general + git. Also shown once as a tip at the start of each shell session. |
| `aliases` / `gitaliases` / `allaliases` | Same cheatsheet, split by category if you only want one part. |
| `zsh-doctor` | Reports which optional, tool-gated integrations (below) are actually active right now. |
| `copy` | Pipes stdin to the system clipboard (`wl-copy`/`xclip`/`xsel`, whichever is found). |
| `view <file>` | Syntax-highlighted, paged file viewer via `bat` (falls back to `less`). `cat` itself is untouched. |
| `repos-status [root]` | Scans `~/repos/*` (or a given root) and reports which repos have uncommitted changes and/or unpushed commits. |
| `trash-restore <name> [name...]` | Brings something back out of `~/.trash`. Run with no args to list what's in there. |
| `trash-empty` | Permanently clears `~/.trash` (confirms first). |
| `mcd <dir>` | *(pre-existing, listed for completeness)* `mkdir -p && cd` in one step. |

## Git — new/changed commands

| Command | What it does |
|---|---|
| `gsl [commit]` | `git show`, paged through `less` with color. |
| `localignore <pattern> [pattern...]` | Ignore files locally via `.git/info/exclude`, without touching the committed `.gitignore`. |
| `pushdr-f <remote-branch>` | Force-push to a differently-named remote branch (`pushdr` is the non-force version). |
| `grev <commitA> <commitB>` | Shows lines commit A added/removed that commit B later undid. |
| `grev-clean <commitA> <commitB> [--dry-run]` | Rewrites commit A via interactive rebase to strip the changes B later reverted. **Rewrites history** — read the preview carefully; `--dry-run` shows what would change without touching anything. If A's *entire* diff turns out to be canceled by B, it'll tell you the exact recovery commands (`--allow-empty`, `rebase --skip`, or `--abort`) instead of just failing. |
| `grev-scan [range]` | Scans a commit range (defaults to since `develop`, or last 20 commits) for any changes later reverted, not just between two named commits. |
| `wt-setup -b <branches> [-n name] [-u url] [-p path]` | Bare-clone + branch-worktree setup for a single repo. If cloning into a directory that already has content, salvages it into a subfolder first instead of clobbering it. |
| `wt-workspace <path> <repo-urls> <branches>` | Same idea across multiple repos at once — clones each bare, adds the requested worktrees for each. |
| `wt-gof` *(needs fzf)* | Fuzzy-pick a worktree and `cd` to it. |
| `gitcf` *(needs fzf)* | Fuzzy-pick a local/remote branch and check it out. |

For the full list of existing git aliases (there are ~90), run `gitaliases`.

## Key bindings

| Key | Action |
|---|---|
| `Ctrl+Right` / `Ctrl+Left` | Word-forward / word-backward *(pre-existing)* |
| `Up` / `Down` | Prefix-aware history search — type part of a command first, then Up/Down cycles only through matching history, not the full unfiltered list |
| `End` / `Ctrl+E` | Accept the current inline signature hint into the buffer (falls through to normal end-of-line if no hint is showing) |
| `Alt+H` | Jump to the man page for whatever command is on the line (`run-help`) |
| `Ctrl+R` / `Ctrl+T` / `Alt+C` *(needs fzf)* | Fuzzy history search / fuzzy file-insert / fuzzy cd — these are fzf's own bindings, loaded automatically once fzf is installed |

## Optional tool integrations (gated on being installed)

Several things in this config are inert until their underlying tool exists, then activate automatically with no config changes needed — same pattern the original config already used for `dircolors`. Run `zsh-doctor` any time to see which of these are actually live.

| Tool | What it enables |
|---|---|
| `bat` | Colorized man pages (`MANPAGER`), the `view` command |
| `fzf` | Ctrl+R/Ctrl+T/Alt+C, `wt-gof`, `gitcf` |
| `zoxide` | `z <partial-name>` — jump to a frecently-visited directory |
| `direnv` | Per-project environment variables auto-loaded from a project's `.envrc` |
| `notify-send` | Desktop notification when a long-running command finishes |
| `wl-copy` / `xclip` / `xsel` | The `copy` command |

## Inline signature hints

As you type a known command, a dim "ghost" suggestion shows the remaining
expected arguments (e.g. typing `grev` shows ` <commitA> <commitB>`), or —
if you've typed a unique partial prefix of a known command — the rest of
that command name. This is a zsh-native equivalent of the old PowerShell
predictor; press **End** or **Ctrl+E** to accept it into the buffer.

## Notable bugs fixed

Found and fixed while building/reviewing the above — worth knowing about
since they mean things behave differently now than they appeared to before:

- **Git branch/status in the prompt was silently disabled** on this machine the whole time — a hardcoded binary path check for a decade-obsolete git layout. Now fixed; see [Prompt](#prompt).
- **The exit-code `✗` indicator never actually fired** — `precmd`'s own internal commands were clobbering `$?` before the prompt ever read it. Fixed by capturing the exit code as `precmd`'s very first action.
- **`trash-empty` could report success while leaving dotfiles behind** — zsh's `*` glob skips dotfiles by default; fixed with the `(D)` glob qualifier.
- **`g1`/`g2` search aliases silently ignored their exclude-dir lists** — an argument was landing in the wrong parameter slot.
- **`fix-fetch-refspecs` printed a stray blank line between repos** — a zsh quirk where re-declaring an already-`local` variable inside a repeated loop echoes it; same root cause was also present in `grev-scan` before a fix.
