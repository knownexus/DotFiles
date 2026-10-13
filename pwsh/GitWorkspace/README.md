# Git Workspace

A Windows terminal Git manager launched by `gws` from your PowerShell profile.
Projects have separate sections and checkboxes, with each registered worktree
listed beneath its project. No change to the shell's current directory is needed.

## Start

The existing `pwsh/Profile.ps1` loader picks up `13-git-workspace.ps1` automatically.
Open a new terminal, or load just that file into an existing session:

```powershell
. C:\repos\DotFiles\pwsh\profile.d\13-git-workspace.ps1
gws
```

Git and the .NET 10 SDK are required. The first launch builds the application;
subsequent launches use the compiled executable until its source changes. Builds
use a separate cache directory for each source version, so an open UI does not lock
an update. Existing windows continue their current version; a new launch uses the
latest one. After updating the PowerShell launcher itself, open a new terminal or
dot-source the launcher again using the command above.
No NuGet packages are required. Profile startup does not build or scan repositories.

```powershell
gws status                # Read-only summary across all registered worktrees
gws status --json         # Structured output, including errors
gws --demo                # Interactive sample; writes disabled
gws add AccessControl C:\repos\AccessControl
gws build                # Explicit rebuild
gws test                 # Integration checks using temporary Git repositories
```

`repos-status` now uses the same discovery. Pass a root to scan its immediate
project folders, e.g. `repos-status C:\repos`. Normal repositories, `.git-main`,
`.bare`, linked worktrees and registered worktrees outside those folders work.
A missing worktree is reported, never silently labelled clean.

## Working in the UI

| Action | Key |
|---|---|
| Move focus / select checkbox | Arrow keys / Space; mouse supported on Windows |
| Open diff on demand | Enter / D; or click Open diff |
| Collapse a project or worktree | Left / Right, or click the disclosure arrow |
| Changes / worktree selection | Tab, or click Changes / Worktrees in the view bar |
| Select all visible / clear selection | A / X |
| Resize the preview split | < / > |
| Search / filter | / / F |
| Show / hide untracked files | T, click Untracked in the toolbar, or F → Show/hide untracked |
| Add files or patterns to the project's .gitignore | G, or M → Add files or patterns to .gitignore |
| Switch group tabs / scroll the tab strip | 1–9, click, or ‹ › |
| New group / edit, rename, duplicate or delete the current tab (or right-click it) / list groups | Ctrl+N / E / W |
| Open focused repository; choose another in Repo View | P |
| Cycle groups (or repositories in Repo View) / go back | [ and ] / Backspace |
| Review history of the focused branch or worktree | H |
| Interactive rebase / manage a paused rebase | I |
| Stage / unstage | S / U |
| Commit all staged changes in chosen worktrees | C |
| Create branch, checkout, new worktree, rebase, revert, continue, abort | B |
| Fetch, push, pull | Y |
| Hunk staging, discard, stash, tools, activity, registration | M |
| Open focused file in editor | O |
| Refresh local state / quit | R / Q |
| Keys for the focused row | footer; ? lists every key |
| Help | ? |

Checkboxes mean **selected**, not **staged**. A file with staged and unstaged
changes appears twice, with independent diffs and action scopes. Parent checkboxes
select the matching visible children; collapsed/filtered selections remain selected
and are counted in the footer. Actions involving hidden selected changes have a
preview. `A` selects only rows currently visible in the list.

`T` shows or hides untracked files across groups and Repo View. The toolbar shows
`Untracked: on/off`, and the preference is remembered between launches. Staged new
files remain visible. Existing selections are retained when hidden, counted in the
footer and disclosed before an action. Worktree status and Git operations still
account for untracked files, including checks that require a clean checkout.

`G` adds selected (or highlighted) untracked files to `.gitignore`, or lets you enter
a pattern such as `*.log`, `build/` or `/local.json`. File selections become literal,
root-anchored rules, so names containing brackets or wildcard characters do not hide
unrelated files. Custom patterns use normal Git syntax. Both actions open a destination
checklist with project separators, branch names and worktree paths. Choose one or more
destinations, including worktrees outside the current group. Selected file paths can
target another worktree in the same repository; the source files stay in place.
For a local branch that is not checked out, choose a new worktree directory. The review
includes worktree creation and ignore rules before either is written. Each chosen
worktree gets an edit to its own root `.gitignore`.
Existing contents and staged changes are preserved. Review and commit the resulting
`.gitignore` edit normally. This does not remove already tracked files from Git.

Diffs load only on demand: press `Enter` / `D`, or click **Open diff**. Moving through
files never loads their contents or runs a diff. Terminals of 100 columns or more show
basic details beside the list; `<` / `>` resize the split (remembered between launches).
The requested diff opens in a large popup, including on narrow terminals.
Minimum size is 66 columns by 16 rows. Menus and prompts open as popups over the main
screen; Esc or a click outside closes them.

File rows use Git's two-letter status: the left letter is the staged side, the right
letter the unstaged side (` M`, `M `, `A `, `D `, `R `, `??`, `UU`). Every diff starts
with a legend naming its sides, for example `− old: staged (index)   + new: working tree`,
and shows separate `old` / `new` line numbers. Removed lines have a red background and
`−`; added lines a green background and `+`.

Branch, Sync and Commit open a separate worktree checklist. This includes clean
worktrees that still have commits to push. File checkboxes never implicitly decide
the final targets for those operations. Commit preview lists **all** staged files
in each selected worktree; messages can be shared or customised per target.

Pull defaults to fast-forward only. Rebase and merge modes are explicit choices.
Push names the remote and exact destination ref; it never force-pushes. An initial
push lets you choose the remote and establishes the upstream. Checkout offers to
focus an existing worktree if it already holds the requested branch.

Local status refreshes automatically without clearing selections. Remote state
is updated only through Fetch/Pull; the worktree preview shows the last FETCH_HEAD
timestamp. Repositories with errors stay visible in Diagnostics. Per-repository
Git commands within a batch run sequentially. The UI does not lock out external
Git processes, but actions refuse stale prepared state when detected.

## Destructive actions and recovery

Discard unstaged restores selected tracked files **from the index**, preserving
staged edits. Deleting selected untracked files is a separate action. Both require
a diff review and explicit confirmation; no recursive `git clean` is used.
Revert commit creates a new reversing commit. Merge-commit revert is left to Git
because a mainline parent must be chosen deliberately.

Stage/unstage blocks is available for modified text files. Binary files, renames,
mode changes, new/deleted files and submodule entries are handled as whole files
or in their own repository. Register a submodule separately to manage its contents.

Create/checkout/rebase/pull require clean worktrees; Stash includes tracked and
untracked files and Apply keeps the stash entry. Stashes are shared by a repository's
worktrees. Rebase conflicts appear with Continue and Abort actions; use the configured
merge tool, then stage resolutions. Removing a worktree keeps its branch and relies
on Git's dirty/locked-worktree protections. Stale registrations are diagnosed, not
automatically pruned.

## Groups, Repo View and history

Groups appear as tabs under the title: `All projects`, `Secure QR 3`, … and `+`. Press
`1`–`9` or click a tab to switch, `‹` / `›` scroll the strip when it overflows, `[` / `]`
cycle group or repo, `Ctrl+N` or `+` create a group, and `E` (or right-click a tab) opens
that tab's popup to edit members, rename, duplicate or delete it. `W` opens the same tabs,
plus New group…, Edit current group… and Repo View…, as a list.

The group editor has a **Name** field and a tree of repositories. It lists registered
repositories and every repository found one level below your search folders (`Roots` in
the config, default `C:/repos`; change them with More → Search folders…, and an empty
value turns off discovery, so the editor shows registered repositories only). Ticking a
repository marked `★ not registered` registers it under its folder name when you save.
`Space` ticks, `W` switches a repository between **whole repo** (includes future
worktrees) and **individual entries**, `B` shows its branches, `/` filters, `←` / `→` fold
a repository's entries and `R` rescans the search folders. Registrations and the group are
saved together, or not at all. A repository's entries are one of three kinds:

- **Entire repository:** includes every registered worktree, including future ones.
- **Worktree:** follows that exact checkout path, whichever branch it checks out.
- **Branch:** follows a named local branch as it moves between worktrees. When it
  is not checked out, a branch row remains available for reviewing history.

Missing paths or refs are retained when editing, so temporary unavailability does not
silently change a saved group.

`P` opens **Repo View** for the highlighted project. This view shows all of its
worktrees and local branches, regardless of group membership. `[` / `]` cycle repos;
outside Repo View they cycle groups. `Backspace` restores the previous view and focus.
Press `P` again to pick a different repo, or `W` to switch to a group. Inactive branch
rows have no operation checkbox; `B` can check them out in an existing worktree or
create a new worktree for the existing branch. The shell stays in its original directory.

`H` opens commit history for the focused branch/worktree; on a project heading it
offers a branch/worktree picker. Repo View also opens history with Enter on those rows.
History shows hash, date, author and subject. `Enter` / `D` (or **Open diff**) loads the
full message, file summary and patch. Moving between commits does not load patches.
Wide terminals show basic commit details beside the list. `M` chooses
which parent to compare for a merge commit, and `PgUp` / `PgDn` scroll the preview.
`N` / `P` page through the full history, `B` selects another branch/worktree, and `R`
refreshes. History pins the tip while browsing, so new commits do not shift pages.
Reviewing an inactive branch does not check it out. `/` searches branch/group pickers.

## Interactive rebase

Highlight a worktree and press `I`, or open its history, highlight the **first commit
to include**, and press `I`. You can also supply a base revision (such as `main` or
`HEAD~3`) or include the root commit. A preview lists the affected commits before Git
opens its real interactive todo editor. Reorder entries or use `reword`, `edit`,
`squash`, `fixup` and `drop`. Merge structure is preserved with `--rebase-merges`;
automatic updating of other branch refs is disabled. The checkout must be clean and
on a local branch. An inactive branch needs a worktree before it can be rebased.

This uses Git's `sequence.editor` / `GIT_SEQUENCE_EDITOR` for the todo list, and
`core.editor` / `GIT_EDITOR` for commit messages, with Git's usual fallbacks. Configure
a blocking editor command (for example `code --wait`) if using a GUI editor. The
workspace `Editor` setting applies to the file editor opened with `O`.

The terminal is restored while Git and the editor run. On completion or a pause,
press any key to return. A paused rebase stays in its branch-based groups even though
Git has detached HEAD. Stage conflict resolutions as usual, then use `I` to continue,
edit the remaining todo list, amend a commit paused by `edit`, or abort. `B` also has
Continue, Abort and Amend actions. Rebase changes commit IDs; it never pushes for you.

Across repositories, commits and network operations are **not atomic**. Every target
gets a result in Activity. Retry repeats only failures from the last batch and checks
that their prepared checkout state still matches. If state changed, prepare the action
again. After a network timeout, inspect the remote state before retrying.

## Local configuration

`$HOME\.config\git-workspaces.json` is machine-specific. Override it with
`GWS_CONFIG` or `gws --config <path>`. The adjacent `.view.json` stores collapsed
sections, the last group and Repo View; `.activity.json` stores the last 200 operation results.
No repository paths, selections or activity logs need to be committed to DotFiles.

See `git-workspaces.example.json`. A group can combine `Projects`, `Worktrees` and
`Branches` (project name plus full local ref, such as `refs/heads/main`). Existing
configurations without `Branches` remain compatible. Worktree membership uses the
actual path; a moved worktree must be reselected. Branch membership follows the ref
instead. Branch names do not have to match across repositories.

`Editor` is an executable name or path; `EditorArguments` is an array of arguments.
For example, `"Editor": "code", "EditorArguments": ["--wait"]` when the editor
executable is available on PATH. Git difftool and mergetool use your existing Git
configuration. The application restores terminal modes while these tools run.

## Development and verification

```powershell
dotnet build .\GitWorkspace.csproj -c Release --configfile .\NuGet.Config
dotnet build ..\GitWorkspace.Tests\GitWorkspace.Tests.csproj -c Release --configfile .\NuGet.Config
dotnet ..\GitWorkspace.Tests\bin\Release\net10.0\GitWorkspace.Tests.dll
```

Tests isolate Git configuration, create unique temporary repositories and local bare
remotes, and leave their fixture paths in the output for inspection. No real project
is committed, pushed, rebased or modified during tests. Source is split into discovery,
operation plans, view state, rendering/input and dialogs; the integration runner has
no test-framework package dependency.
