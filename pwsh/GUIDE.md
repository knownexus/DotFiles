# pwsh config guide

Everything added/changed in this config, mirroring the zsh side (see its
own GUIDE.md for the original). Read this with `view GUIDE.md` (renders
via `glow -s pink`, if installed) or your usual pager. For a live,
in-shell version of the alias tables, run `cheat` any time.

> **Nothing here has been tested on a live Windows/pwsh install.** This
> was all written from a Linux machine with no `pwsh`/`dotnet` available
> to run or compile against — try it and see, and treat anything odd as
> a bug report waiting to happen rather than an intentional design.

**Contents:** shell settings · prompt · safety changes · new commands ·
git additions · optional tool integrations · sig-predictor · not ported

---

## Shell behavior settings

| Setting | What it does |
|---|---|
| `AUTO_PUSHD`-ish | `cd` now calls `Push-Location`; `Pop-Location` undoes it |
| `AUTO_CD`-ish | A bare directory name (no `cd`) changes into it |
| `BellStyle None` | No audible beep on tab-complete misses |

> **Directory stack caveat:** PowerShell's location stack is a plain
> LIFO — there's no numbered `cd -N` the way zsh has it, just the
> single-step `Pop-Location`. `cd -` still does the old plain `$OLDPWD`
> toggle, unchanged.
>
> **No `CORRECT` equivalent was ported.** zsh's typo-suggestion setting
> would need a `CommandNotFoundAction` handler doing fuzzy string
> matching against every known command — that hook affects *every*
> command you type, and this couldn't be tested at all, so it was left
> out rather than risk something that broken silently and constantly.
> Ask if you still want it built.
>
> **Completion caching wasn't ported either** — PowerShell's argument
> completers don't have an equivalent caching concept the way zsh's
> compsys does.

## Prompt

| Segment | Shows |
|---|---|
| Exit code | A red `✗` when the last command's exit status was non-zero |
| Background jobs | `[N]` when you have N running jobs |
| Path (outside a repo) | Truncates to `...\c\d\e` past 3 directory levels |
| Long commands | A toast notification when one crosses `$global:ReportTime` (10s) |

> The exit-code and job-count segments, the path truncation, and the
> notification hook are all new — none of it existed on the pwsh side
> before. The git branch/status segment itself is unchanged.
>
> **Exit-code capture:** `prompt` runs several commands internally
> (`Set-WindowTitle`, git status, etc.) before building the final
> string — those would silently overwrite `$?`/`$LASTEXITCODE` before
> the indicator ever read them. Fixed by capturing both as the very
> first statement in `prompt`, mirroring a bug found and fixed the same
> way on the zsh side.

## Safety changes to familiar commands

| Command | Before | Now |
|---|---|---|
| `cp`, `mv` | Silent overwrite | `-Confirm` — prompts before every operation |
| `rm`, `rmr` | Deleted permanently | Sends to the Recycle Bin — see below |
| `grh` (`reset --hard`) | Ran immediately | Shows what's discarded, asks `y/n` |
| `gclean` (`clean -fd`) | Ran immediately | Shows a preview, asks `y/n` |
| `gpf`/`gitfp`/`gitpf` (force-push) | Ran immediately | Shows the upstream, asks `y/n` |

> **`cp`/`mv` differ from the zsh side:** PowerShell's `-Confirm`
> prompts for *every* operation, not just overwrites — there's no
> built-in "only if the destination exists" mode the way POSIX `cp -i`
> has. Same safety intent, more talkative in practice.
>
> **The trash workflow uses the real Windows Recycle Bin** (via
> `Microsoft.VisualBasic.FileIO.FileSystem`), not a manual folder like
> the zsh side — so anything `rm`/`rmr` sends there is also restorable
> from Explorer, not just from `trash-restore`. `trash-empty` calls the
> built-in `Clear-RecycleBin` (confirms first). Internal config cleanup
> elsewhere still uses `Remove-Item` directly, so temp files delete for
> real.

## New commands

| Command | What it does |
|---|---|
| `cheat` | Full alias/command cheatsheet (alias for `allaliases`) |
| `aliases` / `gitaliases` | Just the general or just the git half of `cheat` |
| `doctor` | Reports which optional integrations are active right now |
| `copy` | Pipes input to the system clipboard (`Set-Clipboard`) |
| `view <file>` | Read-only viewer — see below |
| `repos-status [-Root <path>]` | Scans `C:\repos\*` for uncommitted/unpushed work |
| `trash-restore <name>` | Restores an item from the Recycle Bin by name |
| `trash-empty` | Empties the Recycle Bin (confirms first) |

> `view` picks its renderer by file type: markdown gets properly
> *rendered* via `glow -s pink`; everything else is syntax-*highlighted*
> via `bat`; falls back to plain `Get-Content` if neither is installed.

## Git — new/changed commands

Nothing here is actually new — `gsl`, `localignore`, `pushdr-f`, the
`grev` family, `wt-setup`'s salvage logic, and `wt-workspace` all
already existed in this repo (it was the *source* for the zsh port).
Two real fixes came back the other way:

> **`grev-clean`'s empty-amend message** — if commit B undoes
> *everything* commit A did, stripping it leaves nothing to amend and
> `git commit --amend --no-edit` fails. It used to just say "Amend
> failed — run rebase --abort." Now it detects that specific case and
> gives the real recovery options: `--allow-empty`, `rebase --skip`, or
> `--abort`. Found and fixed on the zsh side first, ported back here.
>
> **`wt-go`/`wt-list`/`wt-feature`/`wt-fix`/`wt-c`/`wt-done`/`wt-done-f`/
> `wt!`/`wt-prune` all failed with "not a git repository" when run from
> a `wt-setup`/`wt-workspace` root** (a plain folder containing
> `.git-main`/`.bare` plus the worktree subdirs) — exactly the natural
> place to run them from, since you're not yet inside any specific
> worktree. `git worktree list/add/remove/prune` only work from inside
> an actual working tree or the bare repo itself. Added
> `Get-WorktreeGitDir`, which resolves to `.git-main`/`.bare` when
> sitting at a workspace root, and threaded it through every worktree
> command via `--git-dir`. This bug actually originated *here* — the
> zsh side inherited it from this file, and it was found and fixed
> there first via a real bug report, then ported back.

## Optional tool integrations (gated on being installed)

Same dormant-until-installed pattern as the graceful `dircolors`/`svn`
checks already in this config — inert until the tool exists, then
activates automatically. Run `doctor` any time to see what's live.

| Tool | Enables | Install |
|---|---|---|
| `bat` | Non-markdown files in `view` | — |
| `glow` | Rendered markdown in `view` | — |
| PSFzf (module) | Ctrl+T/Ctrl+R, `wt-gof`, `gitcf` | `Install-Module PSFzf -Scope CurrentUser` |
| `zoxide` | `z <partial-name>` | — |
| `direnv` | Per-project `.envrc` environment variables | — |
| BurntToast (module) | Desktop notification for long commands | `Install-Module BurntToast -Scope CurrentUser` |

## Inline signature hints (sig-predictor)

`sig-predictor/SignatureHintPredictor.cs` has all the new commands
added to its argument-hint table (`copy`, `view`, `cheat`, `doctor`,
`repos-status`, `trash-restore`, `gsl`, `wt-gof`, `gitcf`, etc.).

> **This needs a rebuild to take effect** — run `build-predictor` (or
> `dotnet build sig-predictor -c Release`) and reload the profile. Like
> everything else here, the C# changes couldn't be compiled or checked
> from where this was written; double-check it builds cleanly before
> relying on it.

## Not ported, and why

- **`CORRECT` typo-suggestion equivalent** — see the callout under
  Shell behavior settings above.
- **Completion caching** — no equivalent concept in PowerShell's
  argument-completer system.
- **A numbered `cd -N`** — PowerShell's location stack doesn't support
  indexed access the way zsh's dirstack does; `Pop-Location` (single
  step back) is the closest built-in equivalent.
