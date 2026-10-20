# zsh config guide

Everything added/changed in this config beyond the original setup.
Read this with `view GUIDE.md` (renders properly via `glow`, if installed)
or `less`. For a live, in-shell version of the alias tables, run `cheat`
any time.

**Contents:** shell settings · `ls` rendering · prompt · safety changes ·
new commands · git additions · key bindings · optional tool integrations ·
signature hints · bugs fixed

---

## `ls` rendering

Ported from the pwsh side's custom renderer, which does a noticeably
nicer job than plain `ls` + dircolors.

| Command | Layout |
|---|---|
| `ls [path]` | Colored, name-only 3-column grid (top-to-bottom fill) |
| `l [path]` | Plain wide name-only listing |
| `ll [path]` | One-per-line long listing, mode/date/size + dir/file count summary |
| `la [path]` | Same as `ls`, including dotfiles |
| `lla [path]` | Same as `ll`, including dotfiles |

All colored by type: directories blue, executables green, archives
yellow, images cyan, hidden entries gray.

> Only takes an optional path — actual flag usage (`ls -la`, `ll -h`,
> etc.) falls back to real `ls` rather than trying to parse GNU-style
> flags itself, so habitual flag usage still works normally.

## Shell behavior settings

These changed the shell's default behavior, not just added a command.

| Setting | What it does |
|---|---|
| `AUTO_PUSHD` | `cd` pushes onto the directory stack automatically |
| `AUTO_CD` | A bare directory name (no `cd`) changes into it |
| `CORRECT` | Misspelled command names get a "did you mean...?" suggestion |
| `unsetopt BEEP` | No audible beep on tab-complete misses |
| Completion caching | Cached under `~/.zsh_cache`, speeds up slow generators |
| `menu select` + `Shift+Tab` | Arrow-key-navigable completion menu on demand |
| `list-colors` | Completion menu color-codes entries like `ls` does |

> **Directory stack:** with `AUTO_PUSHD` + `PUSHD_IGNORE_DUPS` +
> `PUSHD_SILENT`, `cd -N` or `cd -<Tab>` hops back through the last 20
> (`DIRSTACKSIZE=20`) directories you've visited.
>
> **Menu select needs `zsh/complist` loaded** to do anything at all —
> that's what provides the `menu-select` widget and the arrow-key
> bindings, and it was missing for a while (found via a real bug
> report). It also needs its own key: `AUTO_MENU` — the option that
> makes a *second* Tab press auto-enter the menu — has been off in this
> config since long before any of this (`no_auto_menu`, "turn off
> annoying vim style tab completion"), so repeated Tab was never going
> to trigger it no matter what. Rather than override that long-standing
> preference, **Shift+Tab** enters the menu on demand instead — plain
> Tab's behavior is completely unchanged.

## Prompt

| Segment | Shows |
|---|---|
| Git branch/status | `#/reponame[branch:sha,status]` inside any git repo |
| Exit code | A red `✗` when the last command's exit status was non-zero |
| Background jobs | `[N]` when you have N background jobs |
| Path (outside a repo) | Truncates to `.../c/d/e` past 3 directory levels |
| Long commands | A desktop notification when one crosses `REPORTTIME` (10s) |

> Two of these needed real fixes to actually work — see **Bugs fixed**
> at the bottom. The git branch/status segment, in particular, was
> silently disabled on this machine the whole session.

## Safety changes to familiar commands

These change behavior you might type from muscle memory — worth
knowing about explicitly.

| Command | Before | Now |
|---|---|---|
| `cp`, `mv` | Silent overwrite | `-i` — confirms first |
| `rm`, `rmr` | Deletes immediately | Moves to `~/.trash` — see below |
| `grh` (`reset --hard`) | Ran immediately | Shows what's discarded, asks `y/n` |
| `gclean` (`clean -fd`) | Ran immediately | Shows a preview, asks `y/n` |
| `gpf`/`gitfp`/`gitpf` (force-push) | Ran immediately | Shows the upstream, asks `y/n` |

> **The trash workflow:** `rm`/`rmr` move to `~/.trash` instead of
> deleting. Bring something back with `trash-restore <name>`; clear it
> for good with `trash-empty` (which also confirms). Internal cleanup
> elsewhere in this config uses `command rm` to bypass this, so temp
> files still delete for real.

## New commands

| Command | What it does |
|---|---|
| `cheat` | Full alias/command cheatsheet, paged through `less` |
| `aliases` / `gitaliases` / `keys` | Just one of the three tables, unpaged |
| `allaliases` | All three tables at once, unpaged |
| `zsh-doctor` | Reports which optional integrations are active right now |
| `copy` | Pipes stdin to the system clipboard |
| `view <file>` | Read-only viewer — see below |
| `repos-status [root]` | Scans `~/repos/*` for uncommitted/unpushed work |
| `trash-restore <name>` | Brings something back out of `~/.trash` |
| `trash-empty` | Permanently clears `~/.trash` (confirms first) |

> `cheat` is also shown once as a tip at the start of each session.
>
> **`view`** picks its renderer by file type: markdown gets properly
> *rendered* (headers, bold, tables) via `glow`; everything else is
> syntax-*highlighted* via `bat`; falls back to `less` if neither is
> installed. Plain `cat` is untouched either way.

## Git — new/changed commands

| Command | What it does |
|---|---|
| `gsl [commit]` | `git show`, paged through `less` with color |
| `localignore <pattern>` | Ignore files locally via `.git/info/exclude` |
| `pushdr-f <remote-branch>` | Force-push to a differently-named remote branch |
| `grev <A> <B>` | Shows what A changed that B later undid |
| `grev-clean <A> <B>` | Rewrites A to strip what B reverted — see below |
| `grev-scan [range]` | Same idea, scanned across a whole commit range |
| `wt-setup -b <branches>` / `wt-init` | Bare-clone + branch-worktree setup, one repo |
| `wt-workspace <path> <urls> <branches>` | Same, across multiple repos |
| `wt-gof` *(fzf)* | Fuzzy-pick a worktree and `cd` to it |
| `gitcf` *(fzf)* | Fuzzy-pick a branch and check it out |
| `wt-repair [--all [root]]` | Fix stale worktree links — see below |
| `grmi` / `gromi` | Interactive rebase onto origin/main\|master, as-is / freshly fetched |
| `grsi [branch]` | Interactive rebase of this branch's own commits — see below |

> **`main` vs `master`** — wherever one of those two is named, whichever
> the repo actually has is used instead: ask for `main` in a `master`
> repo (or `master` in a `main` one) and you get the branch that exists.
> Applies to `wt-setup`/`wt-init` branch lists, `wt-feature`/`wt-fix`
> bases, `wt-c`, and `wt-default-base`. Local branches are checked before
> `origin/`, the name you typed wins if both exist, and it's passed
> through untouched if neither does yet.

> **Interactive rebase shortcuts** — `grmi` rebases onto `origin/main`
> or `origin/master`, whichever the repo has (the local branch is only
> used if origin has no copy); `gromi` does the same after a fetch. `grsi
> [branch]` rewrites just this branch's own commits from where it forked,
> without moving the base. With no argument it works out the source from
> the branch's reflog (`Created from X`, or the checkout it was made in),
> then falls back to `wt.default-base`, `develop`, then main/master. It
> checks both the local and `origin/` copy of the source and uses the
> nearer fork point, so a stale local `develop` can't pull extra commits
> into the todo list. Extra args go to `git rebase` (`grsi --autosquash`).

> **`wt-repair`** fixes the "`prunable gitdir file points to non-existent
> location`" situation that comes from copying/syncing a `wt-setup`/
> `wt-workspace` workspace to a different machine or path (e.g.
> Windows → Linux — this is exactly how it was first found: a workspace
> created via pwsh's `wt-setup` on Windows, then copied over, left every
> worktree pointing at the old `C:\...` paths). With no args, repairs the
> workspace at the current directory; `--all [root]` scans a directory of
> repos (default `~/repos`) for any workspaces with stale links and
> repairs all of them. Only rewrites the internal `.git`/`gitdir` pointer
> files — no commits or working-tree files are touched.

There are ~90 git aliases in total — run `gitaliases` for the full list.

> **`grev-clean` rewrites history** via interactive rebase. Read its
> preview carefully; pass `--dry-run` to see what would change without
> touching anything. If A's *entire* diff turns out to be canceled by
> B, it tells you the exact recovery commands (`--allow-empty`,
> `rebase --skip`, or `--abort`) instead of just failing partway.

## Key bindings

| Key | Action |
|---|---|
| `Ctrl+→` / `Ctrl+←` | Word-forward / word-backward |
| `↑` / `↓` | Prefix-aware history search |
| `End` / `Ctrl+E` | Accept the current inline hint (else: end-of-line) |
| `Alt+H` | Jump to the man page for the command on the line |
| `Tab` | Complete the current word (no auto-menu) |
| `Shift+Tab` | Enter the arrow-key-navigable completion menu |
| `cd ` + `Tab` | Folder browser, one level at a time (below) |
| `cd path/` + `Shift+Tab` | Drop the last folder off the path |
| `Ctrl+D` | Delete the char under the cursor; else `popd`; else exit |
| `Ctrl+R` / `Ctrl+T` / `Alt+C` *(fzf)* | Fuzzy history / file-insert / cd |

> **↑/↓** only filter by what you've already typed — type `git` then
> press `↑` to cycle just `git ...` history, not the full unfiltered
> list.

> This table is also live in-shell: run `keys` (or `keybindings`) for
> just it, or `cheat` for everything.

### cd folder browser

`Tab` on a `cd` / `pushd` line opens a one-column list of the folders at
the path typed so far (folders only, no files), and the command line
follows along as you move:

| Key | Action |
|---|---|
| `↑` / `↓`, `PgUp` / `PgDn` | Move the highlight |
| `→` | Go into the highlighted folder |
| `←` (or `Backspace` with no filter) | Go up a level |
| typing | Filter the current level by prefix (`.` shows dot-folders) |
| `Enter` / `Tab` | Put the highlighted folder on the line and close |
| `Esc` / `Ctrl+C` | Close and put the line back as it was |

The top row below the starting level is `.` (this folder), so `→` then
`Enter` picks the folder you just went into. A half-typed name with only
one match goes straight in, and `cd -<Tab>` still offers the directory
stack. It lives in `rc.d/88-cd-browser`, after `83-fzf`, so every other
line falls through to whatever Tab was bound to (`fzf-completion` when
fzf's completion script is loaded). It doesn't use the `menuselect`
keymap: that's shared by every completion menu, so taking `←`/`→` there
would break the grid menus elsewhere.

## Optional tool integrations (gated on being installed)

Several things here are inert until their underlying tool exists, then
activate automatically with no config changes needed — the same
pattern the original config already used for `dircolors`. Run
`zsh-doctor` any time to see what's actually live.

| Tool | Enables |
|---|---|
| `bat` | Colorized man pages, non-markdown files in `view` |
| `glow` | Properly rendered markdown in `view` |
| `fzf` | Ctrl+R / Ctrl+T / Alt+C, `wt-gof`, `gitcf` |
| `zoxide` | `z <partial-name>` — jump to a frecent directory |
| `direnv` | Per-project `.envrc` environment variables |
| `notify-send` | Desktop notification for long-running commands |
| `wl-copy` / `xclip` / `xsel` | The `copy` command |

## Inline signature hints

As you type a known command, a dim "ghost" suggestion shows the
remaining expected arguments — typing `grev` shows ` <commitA>
<commitB>` — or, for a unique partial prefix, the rest of that command
name. Press **End** or **Ctrl+E** to accept it into the buffer. This is
a zsh-native equivalent of the old PowerShell predictor, built on
`$POSTDISPLAY` instead of an `ICommandPredictor` plugin.

## Bugs fixed

Found while building/reviewing the above — worth knowing about since
they mean things behave differently now than they appeared to before.

| Bug | Root cause |
|---|---|
| Git branch/status never shown in the prompt | Hardcoded, decade-obsolete git binary path |
| Exit-code `✗` never actually fired | `precmd` clobbered `$?` before the prompt read it |
| `trash-empty` silently left dotfiles behind | zsh's `*` glob skips dotfiles by default |
| `g1`/`g2` ignored their exclude-dir lists | An argument in the wrong parameter slot |
| Stray blank line between repos in `fix-fetch-refspecs` | A `local` re-declared inside a loop |
