# gws: UI foundation, project groups and commits view

Date: 2026-09-25
Scope: `pwsh/GitWorkspace` (the `gws` terminal UI) and `pwsh/GitWorkspace.Tests`

## Intent

Make `gws` nicer to use day to day. Three outcomes:

1. The UI is clearer and less modal: context-aware keys, compact status, popups
   instead of full-screen pages, and unambiguous diffs.
2. Project groups are quick to switch and create. Members are picked from a tree
   that includes repositories discovered on disk; no path is ever typed.
3. The commits of the current branch/worktree can be browsed and inspected from the
   main screen, down to a single file's diff.

### Success criteria

- Switching groups takes one keystroke or one click.
- A group containing a not-yet-registered repository can be created without typing a path.
- From a worktree row, a single file's diff in any commit is reachable without leaving the main screen.
- No mouse handling depends on hard-coded x coordinates.
- Every diff states which side is old and which is new.

### Constraints (unchanged behaviour)

- No NuGet packages. Windows Terminal and conhost keyboard + mouse support stays; keyboard fallback elsewhere.
- Existing configuration files stay valid (`Version: 1`); new fields are optional.
- All safety rules stay: previews before actions, stale-state checks, no force-push,
  typed `DISCARD` confirmation, non-atomic batches reported per target, Retry semantics.
- `gws --demo`, `gws --snapshot`, `gws status [--json]` keep working.
- A file with staged and unstaged edits remains **two rows** with independent checkboxes.
- Tests stay in the dependency-free integration runner and are extended, not replaced.

## Delivery

One spec, three phases. Each phase ships and is reviewed on its own. Phases 2 and 3 build on phase 1.

1. UI foundation
2. Groups
3. Commits view

---

## Phase 1: UI foundation

### Panel layer (`Ui/`)

- **`Rect` / `Layout`**: compute regions from terminal size each frame: title bar,
  group tab strip, view bar (Changes · Worktrees · Commits), list, preview, context
  line, footer. The preview split activates from **100 columns** (was 118). `<` / `>`
  resize the split between 35% and 65%; the ratio is saved in `.view.json` (`SplitRatio`).
- **`HitMap`**: while rendering, panels register clickable regions with a target
  (tab N, row N, row N checkbox, disclosure arrow, footer action, view-bar item).
  Mouse events are resolved against the hit map of the last drawn frame. All existing
  hard-coded coordinate checks in `App.Handle` are removed.
- **`IPanel`**: `Render(Canvas, Rect, HitMap)` and `bool Handle(Input)`. `App` keeps a
  focus stack (main screen at the bottom, popups on top). Input goes to the top panel;
  unhandled keys fall through to global handling.
- **`Popup`**: centred, bordered box over a dimmed copy of the last frame. The public
  `Dialogs` methods (`Choose`, `Check`, `Text`, `Review`, `Message`, `Busy`) keep their
  signatures and render as popups, so existing callers are unchanged. Large content
  (diffs, commit reviews) uses a large popup of about 90% of the screen.
- **`Terminal` input** reports which mouse button was pressed (left or right) and
  double-clicks (console event flag `2`). The `Input` record gains `Button` and
  `DoubleClick`. Today only left single clicks are reported.
- **Key precedence**: a panel's own keys take priority over global keys while that panel
  has focus. The footer always lists the effective keys, so an overridden global action
  is never invisible (see Commits view actions).
- **`Canvas`** gains `Box`, `Dim` and additional inks: diff added/removed backgrounds,
  hunk header, line-number gutter, ref labels.

### Rows

File rows use a fixed two-character Git status column: left character = staged side,
right character = unstaged side (`M `, ` M`, `A `, `D `, `R `, `??`, `UU`). Staged side
is green, unstaged yellow, conflicts red. Rows for the same file in both areas remain
separate rows.

### Diff presentation (all diffs: preview pane, review popups, commit files)

- **Header stating the sides**, by context:
  - Unstaged: `− old: staged (index)   + new: working tree`
  - Staged: `− old: HEAD   + new: staged (index)`
  - Untracked: `+ new file (not in Git)`
  - Commit: `− old: parent <short>   + new: <short> "<subject>"` (the chosen parent for merges)
  - Rename: `old path → new path`
- **Two line-number gutters** labelled `old` and `new`. Removed lines show only an old
  number, added lines only a new number, context lines show both.
- **Removed lines**: `−` marker and red background. **Added lines**: `+` marker and green
  background. Context lines are plain. The side remains identifiable without colour.
- Git's `a/`/`b/` prefixes and `---`/`+++` lines are replaced by the header. Hunk
  headers are shown in the hunk ink.
- A single `DiffView` component parses unified diff text and renders it; every diff
  surface uses it.

### Footer and menus

- The footer shows only keys valid for the focused row kind (file, worktree, project,
  inactive branch, commit, commit file). `?` shows the full reference.
- The `More` menu is regrouped under section headings: **Files** (hunks, discard,
  delete untracked, editor), **Stash**, **Tools** (difftool, mergetool), **Repos &
  groups** (add project, remove project, search folders), **Admin** (activity, retry,
  diagnostics, help). Section headings are not selectable.

### Phase 1 testing

- `Layout` regions at 66×16, 80×24, 120×32 and 160×40.
- `HitMap` resolution: nested regions, the last-registered region wins, clicks outside every region are ignored.
- `DiffView` parsing: unstaged, staged, untracked, rename, binary notice, multiple hunks; line-number assignment.
- `--snapshot` renders at 80, 120 and 160 columns, including a popup snapshot.

---

## Phase 2: Groups

### Tab strip

- Tabs sit below the title: `All projects │ <group> … │ +`, each with its change count
  (e.g. `Secure QR 3`). Overflow scrolls, with `‹` / `›` markers.
- `1`–`9` jump to a tab. `[` / `]` cycle. Clicking a tab switches to it; clicking `+` creates a group.
- `Ctrl+N` creates a group. `E` (or right-clicking a tab) opens a popup for **that tab**:
  Edit members · Rename · Duplicate · Delete. There is no secondary "pick a group" step.
  `All projects` has no edit popup.
- Repo View (`P`) appears as a temporary tab `⌂ <repo> ×`. Backspace or clicking `×` closes it.
- `W` remains as an alias that opens a popup list of groups plus the actions above.

### Discovery (`Discovery.cs`)

- New optional setting `Settings.Roots: List<string>`. When absent, it defaults to
  `["C:/repos"]` if that directory exists, otherwise to an empty list.
- The scan looks one level deep under each root using `Git.ResolveRepository`
  (normal repositories, `.git-main`, `.bare`). Each result records its path, suggested
  name (folder name), common Git directory and whether it is already registered.
  Registration is matched on the common Git directory, not the name.
- The scan runs in the background when the group editor opens; `R` rescans.
- Roots can be edited from `More → Repos & groups → Search folders…`.
- `gws status --root <path>` is reimplemented on `Discovery`.

### Group editor (full-screen panel)

- **Name** field at the top; `Tab` switches between the name and the tree. A new
  group starts as `New group`. Names must be unique and cannot be `All projects`,
  which is checked when saving.
- **Tree**: registered repositories first, then discovered ones marked
  `★ not registered`. Worktrees are listed under each repository.
  - `Space` ticks. A repo's box is ☐ / ◩ / ☑ according to how many of its children are ticked.
  - `W` switches a repo between **whole repo** (includes future worktrees) and
    **individual entries**.
  - `B` loads that repo's local branches under it for branch membership.
  - `/` filters; `←` / `→` fold and unfold.
- Missing members from an existing group are retained and shown as `(unavailable)`.
- **Save**:
  1. Register each ticked unregistered repo under its folder name. If the name is taken,
     append ` 2`, ` 3` and so on.
  2. Build the group.
  3. Persist registrations and the group in a single `Config.Save`.

  If any registration fails (for example the folder is no longer a repository), nothing
  is saved, and the failing row is highlighted with the error.
- `GroupDraft` keeps its current rules and gains a discovered-member kind and an
  explicit whole-repo flag per repo.

### Phase 2 testing

- Discovery on a temporary root containing a normal repo, a `.git-main` layout, a
  `.bare` layout, a plain folder and an already-registered repo (registered under a
  different name).
- `GroupDraft`: automatic registration on save, name collision suffixes, a failed
  registration saves nothing, unavailable entries retained, whole repo ↔ individual toggle.
- Tab strip layout and hit targets with 0, 3 and 20 groups.
- `Settings` with and without `Roots` round-trips; the old example config still loads.

---

## Phase 3: Commits view

### Entry and scope

- `Tab` cycles **Changes → Worktrees → Commits**; the view bar is clickable.
- `H` jumps to Commits for the focused row: worktree, file (its worktree), inactive
  branch, or, on a project heading, a branch/worktree picker.
- The view bar shows the subject being viewed, e.g. `AccessControl / master → feature/secure-qr ▾`.
  `B` or clicking it chooses another worktree or branch within the current group or Repo View tab.
- Changing group tab resets the subject to the first worktree in that group.

### List (left pane)

- The graph comes from `git log --graph --topo-order` with a delimited format. Each line
  is split into the graph prefix and the commit fields; connector-only lines are kept.
  The graph is muted and the commit dot uses the accent colour.
- Row: `graph  short-hash  relative-date  subject  refs`. Refs are coloured labels:
  `(HEAD)`, `(origin/…)`, `(tag: …)`.
- Commits in `upstream..HEAD` get an `↑` marker. Upstream-only commits get `↓` when that filter is active.
- Commits load 200 at a time as the list scrolls near its end. The tip is resolved once
  when the view opens (existing `History.Resolve`), so new commits do not shift the
  list; `R` reloads. The `N` / `P` paging keys are removed.
- Filters (`F`): all · unpushed only · since base (`main` or an entered ref).
- Search (`/`): plain text → `--grep -i`; `@name` → `--author`; `:path` → `-- <path>`.
  Terms combine. Arguments are passed literally, and paths after `--`.

### Preview (right pane)

- Commit header: full hash, author, author date (plus commit date when different), refs,
  parents, full message.
- Changed files with status letter and `+/−` counts, plus totals.
- Drill-down: `Enter` focuses the file list, `↑` / `↓` pick a file, `Enter` shows that file's
  diff using `DiffView`, and `Esc` goes back one level.
- `M` on a merge commit selects the parent to compare against; the diff header names it.
- The root commit shows every file as a new file.
- `Z` or double-click opens the preview full-screen. On narrow terminals (no split),
  `Enter` on a commit opens it full-screen with the same drill-down levels.

### Actions

- `I`: interactive rebase from the highlighted commit (existing `InteractiveRebase`).
- `V`: revert the highlighted commit (existing `Operations.Revert`, same review and checks).
- `Y`: copy the full hash to the clipboard via `clip.exe`; a notice confirms it.
- `D`: external difftool for the focused file between the parent and the commit.
- Nothing else in the view checks out or moves refs.
- While Commits has focus, `B`, `F`, `M`, `Y`, `V`, `D` and `I` are its own keys (above).
  Global Sync and More stay reachable by clicking them in the footer, or with
  `Shift+Y` / `Shift+M`. Global handling currently lower-cases the character, so it must
  distinguish shifted keys. `S`, `U`, `C` do nothing in Commits mode, and the footer omits them.

### Code

- `History.cs` adds:
  - `Graph(dir, tip, skip, count, CommitFilter) → List<GraphRow(Graph, CommitInfo?, Refs, Unpushed)>`
  - `Files(dir, commit, parent) → List<CommitFile(Status, Path, OldPath?, Added, Removed)>`
  - `FileDiff(dir, commit, parent, path) → string`
- Commit detail and file lists are cached per `hash:parent`. Git calls for the preview
  run in the background so scrolling stays responsive; the preview shows `Loading…` until the call completes.
- `HistoryDialog.cs` is removed once Commits mode covers its behaviour. `HistoryActions.cs`
  keeps its entry points and routes them to the Commits view.

### Phase 3 testing

- Graph parsing on a temporary repo with a merge, a side branch, a tag and a root commit.
- Search argument mapping, including a path with a leading dash and brackets.
- Unpushed markers against a local bare remote.
- `Files` / `FileDiff` for a rename, the root commit, and each parent of a merge.
- Incremental loading remains stable after a new commit lands on the branch.
- `--snapshot` of Commits mode at 80, 120 and 160 columns.

---

## Documentation

`README.md` and the in-app help are updated in each phase to match its keys and
behaviour. The key table changes most in phase 1 (footer), phase 2 (tabs, `E`, `Ctrl+N`,
digits) and phase 3 (Commits mode, `H`, and the removal of `N` / `P` paging).

## Out of scope

- Side-by-side (two-column) diff rendering.
- Checking out or resetting to a commit from the Commits view.
- Remote-only branches as group members.
- Recursive discovery below one level.
- Any third-party TUI library.
