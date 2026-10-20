# GWS implementation review

Reviewed 25 September 2026 against [the design](C:/repos/DotFiles/docs/superpowers/specs/2026-09-25-gws-ui-groups-commits-design.md) and the current source, including the ignore destination and on-demand diff changes from this task.

**The specification is not finished. Phase 1 is substantially implemented but has gaps. Phase 2 retains the earlier group manager, without the new tabs or discovery workflow. Phase 3 retains the earlier history dialog; the integrated Commits view is a placeholder.**

## Phase 1 — finish the foundation

Implemented:

- Rect/Layout, split from 100 columns, saved SplitRatio and resize keys.
- HitMap routing for the main screen; right-click and double-click input decoding.
- Popup menus, prompts, checklists, reviews and busy indicators over a dimmed frame.
- Canvas boxes, dimming and diff inks; Git status columns and contextual footer choices.
- Shared DiffView with old/new line numbers, side legends and added/removed backgrounds for file reviews.
- More menu section headings and dependency-free UI tests.

Still required:

- [ ] Implement the specified IPanel abstraction and focus stack. Current dialogs have their own blocking input loops; ISurface is a terminal abstraction, not a panel manager. This is an architectural gap even though dialog keys already work locally.
- [x] Finish consistent diff presentation in action reviews. Mixed prose and patch reviews now render patch sections with a generic `− old: before   + new: after` legend when no richer context is available; the snapshot compatibility renderer also uses that default. Context-specific file and commit reviews still pass their exact side labels.
- [x] Make long review content readable. Prose now wraps inside review popups, while diff/code lines keep their original shape and can be scrolled horizontally with the left and right arrow keys.
- [ ] Finish shared layout/input routing for history. It still uses its own 118-column split, fixed coordinates and direct mouse checks, rather than Layout/HitMap throughout.
- [ ] Complete the specified verification: all layout regions at 66×16, 80×24, 120×32 and 160×40; binary and multiple-hunk DiffView cases; explicit popup snapshots and snapshot variants. Current tests cover parts of these, not the complete matrix.
- [ ] Update help/docs after these gaps close. In particular, “every diff” currently overstates the coverage of side legends.

Evidence: [App.cs](C:/repos/DotFiles/pwsh/GitWorkspace/App.cs), [Dialogs.cs](C:/repos/DotFiles/pwsh/GitWorkspace/Dialogs.cs), [Styled.cs](C:/repos/DotFiles/pwsh/GitWorkspace/Styled.cs), [HistoryDialog.cs](C:/repos/DotFiles/pwsh/GitWorkspace/HistoryDialog.cs), [UiTests.cs](C:/repos/DotFiles/pwsh/GitWorkspace.Tests/UiTests.cs).

## Phase 2 — build the new groups workflow

Already usable: saved groups with whole repositories, fixed worktrees and local branch membership; missing members are retained; W offers create/edit/rename/duplicate/delete; brackets cycle; P opens Repo View.

Still required:

- [x] Render group tabs with change counts, overflow controls and a + button.
- [x] Add digit shortcuts, Ctrl+N, E and right-click editing of the actual selected tab, without another group picker.
- [x] Show Repo View as a closable temporary tab.
- [x] Finish tab overflow scrolling controls for very large group sets.
- [ ] Add optional Settings.Roots and a shared Discovery service, with common-Git-directory deduplication and the specified default root.
- [ ] Run discovery in the group editor in the background, support R rescan, expose Search folders, and reuse Discovery for status --root.
- [ ] Replace the existing flat membership list with the specified foldable tree, editable name field, partial check states, W whole-repository toggle and B branch expansion.
- [ ] Include unregistered discovered repositories. Register selected entries, resolve name collisions, validate all registrations and save the registrations plus group atomically.
- [ ] Add the discovery, atomic-save, tab-overflow, Roots compatibility and new group-editor tests.

The current GroupDraft is populated only from registered projects. The current CLI root scan is a separate inline loop, so it does not supply discovery to group creation.

Evidence: [Navigation.cs](C:/repos/DotFiles/pwsh/GitWorkspace/Navigation.cs), [GroupEditor.cs](C:/repos/DotFiles/pwsh/GitWorkspace/GroupEditor.cs), [GroupDialog.cs](C:/repos/DotFiles/pwsh/GitWorkspace/GroupDialog.cs), [Models.cs](C:/repos/DotFiles/pwsh/GitWorkspace/Models.cs), [Program.cs](C:/repos/DotFiles/pwsh/GitWorkspace/Program.cs).

## Phase 3 — replace the history dialog with integrated Commits

Reusable existing behavior: history on checked-out or inactive branches, pinned tips, root/merge-parent patches, branch/worktree selection, and real interactive rebases.

Still required:

- [ ] Implement Commits mode in the main screen and the three-way Tab cycle. SetMode currently reports that Commits arrives in a later update.
- [ ] Add a subject selector scoped to the current group/Repo View, resetting appropriately on tab changes.
- [ ] Add graph rows and connector lines, ref labels, relative dates, upstream comparisons and unpushed/upstream-only markers.
- [ ] Replace 80-entry N/P pages with stable incremental loading in batches of 200.
- [ ] Add commit filters and combined text/author/path searches with literal arguments.
- [ ] Add History.Graph, History.Files and History.FileDiff.
- [ ] Add full commit details, changed-file status/counts, totals and per-file drill-down in the main screen, including root and merge-parent handling.
- [ ] Add full-screen drill-down with Z/double-click and narrow-screen behavior.
- [ ] Wire highlighted-commit revert, hash copy and per-file external difftool; make all Commits keys take precedence and retain shifted global Sync/More.
- [ ] Cache commit details/files by hash and parent, loading requested content without blocking navigation.
- [ ] Retire HistoryDialog after the main Commits view covers it.
- [ ] Add the graph, search, upstream marker, file-diff, incremental-loading and Commits snapshot tests.

Evidence: [App.cs](C:/repos/DotFiles/pwsh/GitWorkspace/App.cs), [WorkspaceView.cs](C:/repos/DotFiles/pwsh/GitWorkspace/WorkspaceView.cs), [History.cs](C:/repos/DotFiles/pwsh/GitWorkspace/History.cs), [HistoryActions.cs](C:/repos/DotFiles/pwsh/GitWorkspace/HistoryActions.cs), [HistoryDialog.cs](C:/repos/DotFiles/pwsh/GitWorkspace/HistoryDialog.cs).

## Incorporate the newer requirements into the spec

- Diffs must load only on an explicit action. The user's later request supersedes automatic patch previews on focus changes. Background loading and caching can support an explicit request, but should not cause automatic diff reads while navigating.
- Keep Enter/Open diff for internal inspection. The current history dialog also accepts D; the planned Commits view reserves D for external difftool, so document that context change when implementing it.
- Include the .gitignore destination checklist for file selections and custom patterns, worktrees outside the active group, and creating a worktree for an inactive local branch.

The current task implements those ignore destinations and on-demand file/history diffs. It also removes the duplicate tracked-file diff read used for the selection warning and drains queued console input without per-record delays.

## Recommended order

1. Close the Phase 1 readability, diff-label, panel/input and verification gaps.
2. Ship repository discovery and the new group editor together, then the tab strip/navigation.
3. Build the integrated Commits model and file drill-down, then its graph, search, actions and tests.

Do not count the visible Commits label or the existing group/history dialogs as completion of Phases 2–3.

## Verification of this task's fixes

- Release build: zero warnings and errors.
- Initial full integration run: 46 passed, one failed assertion. The assertion compared equivalent paths using different Windows slash styles; it was corrected.
- Final focused reruns: eight ignore/hit-map checks, one manual-history-loading check and one footer check passed.
- Live terminal trace: 19 arrow inputs triggered zero diff commands; pressing D triggered one diff command.
- Live destination check: the selected review worktree received /scratch.log in its .gitignore; the source worktree's .gitignore was not created and its file remained intact.
- Tests and UI writes used disposable repositories, not the registered project repositories.
- Continued implementation pass: review popups gained wrapped prose, horizontal patch scrolling and generic side legends for raw mixed patches.
- Full integration runner after the continued pass: 47 passed, 0 failed.
- Launcher build after the continued pass: zero warnings and errors; cached release `bin/releases/275fc9b51ffda69165da/gws.dll`.
- Continued groups pass: main-screen group tabs now show counts, the + target creates a group, 1-9 jump tabs, Ctrl+N creates a group, E and right-click edit the current/specific saved group, and Repo View appears as a temporary closable tab.
- Full integration runner after the groups pass: 47 passed, 0 failed.
- Launcher build after the groups pass: zero warnings and errors; cached release `bin/releases/008247a350bcb120cbf5/gws.dll`.
- Continued tab overflow pass: crowded group tabs now expose clickable `‹` and `›` controls, preserve manual tab browsing, and keep later active tabs visible when selected directly.
- Full integration runner after the tab overflow pass: 48 passed, 0 failed.
- Launcher build after the tab overflow pass: zero warnings and errors; cached release `bin/releases/ceeedd1bb4e6e4e75def/gws.dll`.
