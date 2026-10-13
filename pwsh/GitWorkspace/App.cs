using System.Diagnostics;
using System.Runtime.CompilerServices;

// Task 13': the tab strip's private navigation (SelectTab, EditGroupTab) and dialog wiring
// have no public API, so the pinning tests reach them through this internal seam rather than
// driving a real console. See task-13a-report.md for the rationale.
[assembly: InternalsVisibleTo("GitWorkspace.Tests")]

namespace GitWorkspace;

public sealed partial class App(Settings settings, string configPath, bool demo = false)
{
    readonly WorkspaceView view = new(settings, Config.ReadOr(configPath + ".view.json", new ViewState()));
    readonly List<OperationResult> activity = Config.ReadOr(configPath + ".activity.json", new List<OperationResult>());
    readonly List<Plan> failed = [];
    readonly HitMap hits = new();
    List<StyledLine> previewLines = [];
    Terminal terminal = null!;
    Dialogs dialog = null!;
    string notice = "Checkboxes select; Staged labels show commit contents.";
    string preview = "", previewKey = "";
    int previewScroll;
    bool quit;
    Task<Snapshot>? scan;
    DateTime nextScan;
    string activeTabId = "";
    // Kept when Tab leaves Commits, so returning restores the position; cleared by any group or
    // Repo View change (AfterScopeChange), so the next Commits visit starts in the new scope.
    CommitsModel? commits;
    // Test-only seam (Task 13'): lets TabTests exercise the private tab-menu/write-guard
    // logic without a real console. Never used by Program.cs.
    internal WorkspaceView TestView => view;
    internal void TestAttachSurface(ISurface surface) => dialog = new Dialogs(surface);
    internal CommitsModel? TestCommits => commits;
    internal void TestSetMode(ViewMode mode) => SetMode(mode);

    public int Run()
    {
        view.WorktreeMode = view.RepoMode;
        if (demo) { view.Stamp = (_, _) => "demo"; view.Selectable = _ => true; }
        using var screen = new Terminal(); terminal = screen; dialog = new(screen);
        Refresh();
        try
        {
            while (!quit)
            {
                if (scan?.IsCompleted == true) { view.Update(scan.GetAwaiter().GetResult()); scan = null; previewKey = ""; nextScan = DateTime.Now.AddSeconds(Math.Max(2, settings.RefreshSeconds)); }
                if (!demo && scan is null && DateTime.Now >= nextScan) { scan = Task.Run(() => Git.Scan(settings)); nextScan = DateTime.Now.AddSeconds(Math.Max(2, settings.RefreshSeconds)); }
                UpdatePreview();
                var layout = Layout.For(terminal.Width, terminal.Height, view.State.SplitRatio);
                // Keep the active tab reachable only when it actually changed - never on every
                // frame, or a manual ‹/› scroll would be fought back on the very next redraw.
                string tabId = view.Tabs().FirstOrDefault(t => t.Active)?.Id ?? "";
                if (tabId != activeTabId) { view.State.TabOffset = Screen.EnsureTabVisible(view.Tabs(), layout.Tabs.Width, view.State.TabOffset); activeTabId = tabId; }
                terminal.Draw(view.Mode == ViewMode.Commits && commits is { } model
                    ? Screen.Render(view, layout, hits, [], 0, notice, scan is not null, (c, l, h) => CommitsRender.Body(c, l, h, model, previewScroll), model)
                    : Screen.Render(view, layout, hits, previewLines, previewScroll, notice, scan is not null));
                var key = terminal.Poll(30);
                if (key is null) continue;
                try { Handle(key); } catch (Exception e) { dialog.Message("Unable to complete action", e.Message); notice = "Action stopped; open Activity for completed operations."; }
            }
        }
        finally { if (!demo) { Config.Save(configPath + ".view.json", view.State); SaveActivity(); } }
        return 0;
    }
    void Refresh()
    {
        if (scan is not null) { dialog.Busy("Refreshing worktrees", () => scan.GetAwaiter().GetResult()); scan = null; }
        view.Update(demo ? Demo.Snapshot() : dialog.Busy("Reading projects and worktrees", () => Git.Scan(settings)));
        previewKey = ""; nextScan = DateTime.Now.AddSeconds(Math.Max(2, settings.RefreshSeconds));
        if (view.Snapshot.Errors.Count > 0) notice = $"{view.Snapshot.Errors.Count} project errors · M → Diagnostics";
    }
    void UpdatePreview()
    {
        var row = view.Current; string key = (row?.Id ?? "") + view.Snapshot.TakenAt.Ticks;
        if (key == previewKey) return;
        bool moved = !previewKey.StartsWith(row?.Id ?? "\0", StringComparison.Ordinal);
        previewKey = key; if (moved) previewScroll = 0;
        if (row?.Change is not null)
        {
            // Navigation reads only the existing snapshot; a diff is loaded by
            // ShowPreview after an explicit key press or click.
            preview = $"{row.Tree!.Title}\n{row.Change.Area} · {row.Change.Path}\n\nEnter / D: open diff\n\nDiffs load only when requested.";
        }
        else if (row?.Branch is { } branch)
            preview = $"{branch.Project} / {branch.Name}\n\nNot checked out in a worktree.\n{branch.Head}\n{branch.Subject}\n\nH / Enter: review commit history\nP: open this repository\n\nA branch group entry follows this branch wherever it is checked out.";
        else if (row?.Tree is not null)
        {
            var t = row.Tree;
            preview = $"{t.Title}\n\n{t.Summary}\n\nPath: {t.Path}\nUpstream: {t.Upstream ?? "None"}\nRemote refreshed: {(t.FetchedAt?.ToString("g") ?? "Unknown; Fetch to refresh")}\n\n{t.Changes.Count(c => c.Area == ChangeArea.Conflict)} conflicts\n{t.Locked}\n{t.Prunable}\n\nTab switches between file and worktree selection.\nBranch and Sync include clean worktrees.";
        }
        else preview = string.Join("\n\n", view.InGroup().Where(t => row?.Project is null || t.Project == row.Project).Select(t => t.Title + "\n" + t.Summary)) +
            (view.Snapshot.Errors.Count > 0 ? "\n\nProject errors:\n" + string.Join('\n', view.Snapshot.Errors) : "");
        previewLines = Styled.FromText(preview);
    }
    void ShowPreview()
    {
        if (view.Current?.Kind == "branch" || view.RepoMode && view.Current?.Kind == "worktree") { BrowseHistory(); return; }
        UpdatePreview();
        var lines = previewLines;
        if (view.Current is { Tree: { } tree, Change: { } change } row) {
            view.SelectionStamps.TryGetValue(row.Id, out var selectedStamp);
            lines = dialog.Busy("Loading file diff", () => {
                string diff = demo ? Demo.Diff : Git.Diff(tree, change);
                // Reuse the diff when checking a tracked selection, instead of
                // launching a second identical Git process.
                bool stale = !demo && selectedStamp is not null && selectedStamp !=
                    (change.Area == ChangeArea.Untracked ? Operations.ChangeStamp(tree, change) : Operations.Digest(diff));
                return PreviewFor(tree, change, diff, stale);
            });
        }
        dialog.Review("Preview · " + (view.Current?.Text ?? "Workspace"), lines, confirm: false);
    }
    public static List<StyledLine> PreviewFor(Worktree tree, Change change, string diff, bool changedSinceSelection)
    {
        List<StyledLine> lines = [];
        if (changedSinceSelection) { lines.Add(new("UPDATED SINCE SELECTION · uncheck and review before acting", Ink.Warning)); lines.Add(new("", Ink.Text)); }
        lines.Add(new(tree.Title, Ink.Accent));
        if (DiffView.LooksLikeDiff(diff)) lines.AddRange(DiffView.Render(DiffView.Parse(diff), DiffSides.ForChange(change)));
        else { lines.Add(new(DiffSides.ForChange(change).Legend, Ink.Heading)); lines.AddRange(Styled.FromText(diff)); }
        return lines;
    }
    void Handle(Input key)
    {
        if (key.Mouse)
        {
            var hit = hits.At(key.X, key.Y);
            if (view.Mode == ViewMode.Commits && commits is { } open)
            {
                if (key.Wheel != 0) { if (hit?.Kind == "preview") previewScroll = Math.Max(0, previewScroll + key.Wheel); else open.Move(key.Wheel); return; }
                if (hit?.Kind == "row") {
                    open.Select(hit.Index); previewScroll = 0;
                    if (key.DoubleClick) dialog.Review(open.Target.Label, CommitsRender.Preview(open), confirm: false);
                    return;
                }
                if (hit?.Kind == "subject") { PickCommitSubject(); return; }
            }
            if (key.Wheel != 0) {
                if (hit?.Kind == "preview") previewScroll = Math.Max(0, previewScroll + key.Wheel);
                else view.Focus = Math.Clamp(view.Focus + key.Wheel, 0, Math.Max(0, view.Rows.Count - 1));
                return;
            }
            if (key.Button == MouseButton.Right && hit?.Kind != "tab") return;
            switch (hit?.Kind)
            {
                case "tab": Tab(hit, key.Button == MouseButton.Right); break;
                case "view": SetMode((ViewMode)hit.Index); break;
                case "action": HandleAction(hit.Id!); break;
                case "check": view.Focus = hit.Index; view.Toggle(view.Current); break;
                case "fold": view.Focus = hit.Index; Collapse(); break;
                case "row": view.Focus = hit.Index; if (key.DoubleClick) Handle(new(ConsoleKey.Enter)); break;
            }
            return;
        }
        if (key.Control && key.Key == ConsoleKey.C) { quit = true; return; }
        if (key.Control && key.Key == ConsoleKey.N) { EditGroup(); return; }
        if (view.Mode == ViewMode.Commits && commits is not null && HandleCommits(key)) return;
        if (key.Character >= '1' && key.Character <= '9') { SelectTab(key.Character - '1'); return; }
        if (key.Key == ConsoleKey.UpArrow) { view.Focus = Math.Max(0, view.Focus - 1); return; }
        if (key.Key == ConsoleKey.DownArrow) { view.Focus = Math.Min(Math.Max(0, view.Rows.Count - 1), view.Focus + 1); return; }
        if (key.Key == ConsoleKey.PageUp) { previewScroll = Math.Max(0, previewScroll - 10); return; }
        if (key.Key == ConsoleKey.PageDown) { previewScroll = Math.Max(0, Math.Min(preview.Split('\n').Length - 1, previewScroll + 10)); return; }
        if (key.Key == ConsoleKey.Spacebar) { view.Toggle(view.Current); return; }
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow) { Collapse(); return; }
        if (key.Key == ConsoleKey.Tab) { SetMode(view.Mode switch { ViewMode.Changes => ViewMode.Worktrees, ViewMode.Worktrees => ViewMode.Commits, _ => ViewMode.Changes }); return; }
        if (key.Key == ConsoleKey.Enter) { ShowPreview(); return; }
        if (key.Key == ConsoleKey.Backspace) { view.Back(); previewKey = ""; AfterScopeChange(); return; }
        if (key.Key == ConsoleKey.Escape) { view.Search = ""; view.Filter = "All changes"; view.Rebuild(); return; }
        switch (char.ToLowerInvariant(key.Character))
        {
            case 'q': quit = true; break;
            case 'a': view.SelectVisible(); break;
            case 'x': view.Selected.Clear(); view.Targets.Clear(); view.SelectionStamps.Clear(); view.Rebuild(); break;
            case '/': var search = dialog.Text("Search", "Find a filename, project, worktree or branch. Empty text clears the search.", view.Search); if (search is not null) { view.Search = search; view.Rebuild(); } break;
            case 'f': FilterChanges(); break;
            case 't': ToggleUntracked(); break;
            case 'g': IgnoreMenu(); break;
            case 'd': ShowPreview(); break;
            case 'e': EditCurrentTab(); break;
            case 'w': Workspace(); break;
            case 'p': RepoView(); break;
            case 'h': BrowseHistory(); break;
            case 'i': RebaseInteractive(); break;
            case '[': view.Cycle(-1); previewKey = ""; AfterScopeChange(); break;
            case ']': view.Cycle(1); previewKey = ""; AfterScopeChange(); break;
            case 'r': Refresh(); break;
            case 's': FileAction("Stage"); break;
            case 'u': FileAction("Unstage"); break;
            case 'c': Commit(); break;
            case 'b': BranchMenu(); break;
            case 'y': SyncMenu(); break;
            case 'm': More(); break;
            case 'o': Editor(); break;
            case '?': Help(); break;
            case '<': view.State.SplitRatio = Math.Clamp(view.State.SplitRatio - 0.05, 0.35, 0.65); break;
            case '>': view.State.SplitRatio = Math.Clamp(view.State.SplitRatio + 0.05, 0.35, 0.65); break;
        }
    }
    void SetMode(ViewMode mode)
    {
        if (mode == ViewMode.Commits && commits is null) {
            var target = view.Current?.Tree is { } tree ? Target(tree) : view.Current?.Branch is { } branch ? Target(branch) :
                view.InGroup().FirstOrDefault(t => t.Usable) is { } first ? Target(first) : null;
            if (target is null) { notice = "No worktree in this view to show commits for."; return; }
            OpenCommits(target); return;
        }
        view.Mode = mode; view.Rebuild(); previewKey = ""; previewScroll = 0;
    }
    void OpenCommits(HistoryTarget target)
    {
        commits = demo ? Demo.CommitsModel(target) : CommitsModel.ForGit(target); commits.Load();
        view.Mode = ViewMode.Commits; previewScroll = 0;
        notice = "Commits · Enter files · / search (@author :path) · B another branch · Tab back to changes";
    }
    // A new group or Repo View changes what the Commits subject may be, so the model is always
    // dropped; in Commits it restarts at once on the new scope (or drops back to Changes when
    // there is nothing to show), elsewhere the next Tab into Commits opens it fresh.
    void AfterScopeChange()
    {
        commits = null;
        if (view.Mode != ViewMode.Commits) return;
        SetMode(ViewMode.Commits);
        if (commits is null) SetMode(ViewMode.Changes);
    }
    void HandleAction(string id)
    {
        if (id == " ") Handle(new(ConsoleKey.Spacebar));
        else if (id == "\r") Handle(new(ConsoleKey.Enter));
        else if (id == "\u001b") Handle(new(ConsoleKey.Escape));
        else Handle(new(Character: id[0]));
    }
    bool HandleCommits(Input key)
    {
        var m = commits!;
        if (key.Key == ConsoleKey.UpArrow) { m.Move(-1); previewScroll = 0; return true; }
        if (key.Key == ConsoleKey.DownArrow) { m.Move(1); previewScroll = 0; return true; }
        if (key.Key == ConsoleKey.Home && m.Level == CommitLevel.Commits) { m.Select(0); previewScroll = 0; return true; }
        if (key.Key == ConsoleKey.PageUp) { previewScroll = Math.Max(0, previewScroll - 10); return true; }
        if (key.Key == ConsoleKey.PageDown) { previewScroll = Math.Max(0, Math.Min(CommitsRender.Preview(m).Count - 1, previewScroll + 10)); return true; }
        if (key.Key == ConsoleKey.Enter) { m.Enter(); previewScroll = 0; return true; }
        if (key.Key is ConsoleKey.Escape or ConsoleKey.Backspace && m.Back()) { previewScroll = 0; return true; }
        // Selection and folding act on the hidden Changes rows, so they are inert here.
        if (key.Key is ConsoleKey.Spacebar or ConsoleKey.LeftArrow or ConsoleKey.RightArrow) return true;
        if (key.Character is 'Y' or 'M') { if (key.Character == 'Y') SyncMenu(); else More(); return true; }
        switch (char.ToLowerInvariant(key.Character))
        {
            case '/': var s = dialog.Text("Search commits", "Words match the message, @name the author, :path a file. All matching is literal and case-insensitive. Empty clears.", m.Filter.Search);
                if (s is not null) { m.SetFilter(m.Filter with { Search = s }); previewScroll = 0; } return true;
            case 'f': CommitFilterMenu(m); previewScroll = 0; return true;
            case 'b' or 'h': PickCommitSubject(); return true;
            case 'r': m.Load(); previewScroll = 0; return true;
            case 'm': m.NextParent(); previewScroll = 0; return true;
            case 'z': dialog.Review(m.Target.Label, CommitsRender.Preview(m), confirm: false); return true;
            case 'y': if (m.Commit is { } c) CopyHash(c.Hash); return true;
            case 'i': if (m.Commit is { } first) RebaseFromCommit(m, first); return true;
            case 'v': if (m.Commit is { } revert) RevertFromCommits(m, revert); return true;
            case 'd': if (m.Level != CommitLevel.Commits && m.Commit is { } dc && m.File is { } file && CanWrite()) {
                    List<string> args = ["difftool", "--no-prompt", CommitGraph.Base(m.Target.Directory, dc, m.Parent), dc.Hash, "--"]; if (file.OldPath is not null) args.Add(file.OldPath); args.Add(file.Path);
                    RunExternal(Git.StartInfo(m.Target.Directory, args, false)); }
                return true;
            case 's' or 'u' or 'c' or 'a' or 'x' or 't' or 'g' or 'o': notice = "Switch to Changes (Tab) to select, stage or commit files."; return true;
        }
        return false;
    }
    void CommitFilterMenu(CommitsModel m)
    {
        var pick = dialog.Choose("Filter commits", ["All commits", "Unpushed only" + (m.Target.Upstream is null ? " (no upstream)" : ""), "Since a base (branch, tag or commit)…"]);
        if (pick == 0) m.SetFilter(m.Filter with { UnpushedOnly = false, Base = null });
        if (pick == 1) m.SetFilter(m.Filter with { UnpushedOnly = true, Base = null });
        if (pick == 2 && dialog.Text("Since base", "Show commits after this base, e.g. main or origin/main.", m.Filter.Base ?? "main") is { Length: > 0 } b) m.SetFilter(m.Filter with { UnpushedOnly = false, Base = b });
    }
    void PickCommitSubject()
    {
        var targets = view.InGroup().Where(t => t.Head != "(initial)").Select(Target).Concat(view.InactiveBranches().Select(Target)).ToList();
        var pick = dialog.Choose("Commits · choose branch or worktree", targets.Select(t => t.Label + (t.WorktreeId is null ? " · not checked out" : "")).ToList());
        if (pick is not null) OpenCommits(targets[pick.Value]);
    }
    void CopyHash(string hash)
    {
        try {
            var start = new ProcessStartInfo("clip.exe") { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
            using var process = Process.Start(start)!; process.StandardInput.Write(hash); process.StandardInput.Close(); process.WaitForExit(5000);
            notice = "Copied " + hash;
        } catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { notice = "Clipboard unavailable · " + hash; }
    }
    void RevertFromCommits(CommitsModel m, CommitInfo commit)
    {
        if (!CanWrite()) return;
        var tree = view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == m.Target.WorktreeId && t.Usable);
        if (tree is null) { dialog.Message("Revert commit", "Revert needs this branch checked out in a worktree. Use B → Checkout branch first."); return; }
        if (Execute([Operations.Revert(tree, commit.Hash)])) m.Load();
    }
    void ToggleUntracked()
    {
        view.State.ShowUntracked = !view.State.ShowUntracked;
        view.Rebuild(); previewKey = "";
        notice = view.State.ShowUntracked ? "Untracked files shown." : "Untracked files hidden · T to show";
    }
    void FilterChanges()
    {
        var filters = WorkspaceView.Filters;
        var options = filters.Append(view.State.ShowUntracked ? "Hide untracked files (T)" : "Show untracked files (T)").ToArray();
        var choice = dialog.Choose("Filter changes", options, Array.IndexOf(filters, view.Filter));
        if (choice == filters.Length) ToggleUntracked();
        else if (choice.HasValue) { view.Filter = filters[choice.Value]; view.Rebuild(); }
    }
    void Collapse()
    {
        if (view.Current is not { Kind: "project" or "worktree" } row) return;
        if (!view.State.Collapsed.Remove(row.Id)) view.State.Collapsed.Add(row.Id); view.Rebuild();
    }
    bool CanWrite()
    {
        if (!demo) return true;
        dialog.Message("Demo", "This preview uses sample data. Open gws normally to operate on registered repositories."); return false;
    }
    List<Worktree>? PickTargets(string action, bool stagedOnly = false, bool allowMissing = false)
    {
        var all = view.InGroup().Where(t => (allowMissing || t.Usable) && (!stagedOnly || t.Changes.Any(c => c.Area == ChangeArea.Staged))).ToList();
        var ids = view.Targets.Count > 0 ? view.Targets : view.SelectedFiles().Select(x => x.Tree.Id).ToHashSet();
        if (ids.Count == 0 && view.Current?.Tree is { } current) ids = [current.Id];
        var picked = dialog.Check(action + " · select worktrees", all.Select(t => t.Title + "  · " + t.Summary).ToArray(),
            Enumerable.Range(0, all.Count).Where(i => ids.Contains(all[i].Id)));
        if (picked is null || picked.Count == 0) return null;
        return picked.Select(i => all[i]).ToList();
    }
    // Returns false when nothing ran (no plans, or the review was cancelled).
    bool Execute(List<Plan> plans, bool review = true)
    {
        if (plans.Count == 0) return false;
        string title = plans[0].Action + $" · {plans.Count} target(s)";
        var content = string.Join("\n\n────────────────────────\n\n", plans.Select(p => p.Tree.Title + "\n" + p.Preview));
        if (review && !dialog.Review(title, content)) return false;
        failed.Clear(); List<OperationResult> results = [];
        for (int i = 0; i < plans.Count; i++) {
            var p = plans[i];
            var result = dialog.Busy($"{p.Action} · {i + 1}/{plans.Count} · {p.Tree.Project}", p.Run);
            results.Add(result); activity.Add(result); if (!result.Success) failed.Add(p);
            SaveActivity();
        }
        Refresh();
        notice = $"{results.Count(r => r.Success)} completed · {failed.Count} failed · M → Activity";
        if (review || failed.Count > 0) dialog.Message(title + " · results", string.Join("\n\n", results.Select(r => (r.Success ? "✓ " : "! ") + r.Target + "\n" + r.Detail)));
        return true;
    }
    void SaveActivity() { if (!demo) Config.Save(configPath + ".activity.json", activity.TakeLast(200).ToList()); }
    void FileAction(string action)
    {
        if (view.WorktreeMode) { dialog.Message(action, "Switch to Changes (Tab), then select the files to act on."); return; }
        if (!CanWrite()) return;
        var files = view.SelectedFiles();
        if (files.Count == 0 && view.Current is { Tree: not null, Change: not null } row) files = [(row.Tree, row.Change)];
        files = files.Where(x => action switch {
            "Stage" => x.Change.Area != ChangeArea.Staged, "Unstage" => x.Change.Area == ChangeArea.Staged,
            "Discard unstaged" => x.Change.Area == ChangeArea.Unstaged, "Delete untracked" => x.Change.Area == ChangeArea.Untracked, _ => false
        }).ToList();
        if (files.Count == 0) { dialog.Message(action, "Select applicable files with Space first."); return; }
        foreach (var (tree, change) in files)
            if (view.SelectionStamps.TryGetValue(tree.FileId(change), out var stamp) && stamp != Operations.ChangeStamp(tree, change))
                throw new InvalidOperationException($"{tree.Project}: {change.Path} changed since you selected it. Uncheck it, review the updated diff, then select it again.");
        var plans = files.GroupBy(x => x.Tree).Select(g => Operations.Files(g.Key, action, g.Select(x => x.Change))).ToList();
        bool destructive = action is "Discard unstaged" or "Delete untracked";
        if (destructive)
        {
            string explanation = action == "Discard unstaged" ? "Restore tracked files to their staged versions. Discard the selected unstaged edits." : "Permanently delete these untracked files. They are not recoverable from Git.";
            List<StyledLine> details = [.. Styled.FromText(explanation), new("", Ink.Text)];
            foreach (var (tree, change) in files) { details.AddRange(PreviewFor(tree, change, Git.Diff(tree, change), false)); details.Add(new("", Ink.Text)); }
            if (!dialog.Review(action, details, "Review file list")) return;
            if (dialog.Text(action, "Type DISCARD to confirm the reviewed files.") != "DISCARD") return;
        }
        Execute(plans, destructive || files.Any(x => !view.Rows.Any(r => r.Id == x.Tree.FileId(x.Change))));
    }
    void Commit()
    {
        if (!CanWrite()) return;
        var targets = PickTargets("Commit ALL staged changes", stagedOnly: true); if (targets is null) return;
        var message = dialog.Text("Commit message", "One message for the selected worktrees. Each gets a separate commit.");
        if (string.IsNullOrWhiteSpace(message)) return;
        var body = dialog.Text("Commit description", "Optional description (leave empty for a subject-only commit)."); if (body is null) return;
        if (body.Length > 0) message += "\n\n" + body;
        bool individual = false;
        if (targets.Count > 1) {
            var mode = dialog.Choose("Commit messages", ["Use this message for every commit", "Customise each message"]);
            if (mode is null) return; individual = mode == 1;
        }
        List<Plan> plans = [];
        foreach (var t in targets) {
            var text = individual ? dialog.Text("Message · " + t.Project, t.Title, message) : message;
            if (text is null) return; plans.Add(Operations.Commit(t, text));
        }
        Execute(plans);
    }
    void BranchMenu()
    {
        if (view.Current?.Branch is { } inactive) { InactiveBranch(inactive); return; }
        string[] actions = ["Create and checkout branch", "Checkout branch", "Create branch in new worktree", "Rebase onto…", "Revert a commit…", "Continue operation", "Abort operation", "Remove clean worktree", "Interactive rebase…", "Amend paused rebase commit…"];
        var choice = dialog.Choose("Branches and worktrees", actions); if (!choice.HasValue || !CanWrite()) return;
        if (choice == 8) { RebaseInteractive(); return; }
        var targets = PickTargets(actions[choice.Value]); if (targets is null) return;
        List<Plan> plans = [];
        if (choice < 3)
        {
            if (choice is 0 or 2 && targets.GroupBy(t => t.CommonDir).Any(g => g.Count() > 1)) throw new InvalidOperationException("Select one source worktree per repository when creating the same new branch.");
            string? branch;
            if (choice == 1 && targets.Count == 1)
            {
                var branches = Git.Run(targets[0].Path, "for-each-ref", "--format=%(refname:short)", "refs/heads/", "refs/remotes/").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(b => !b.EndsWith("/HEAD")).ToList();
                branches.Insert(0, "Enter branch name…"); var b = dialog.Choose("Checkout branch · " + targets[0].Project, branches); if (b is null) return;
                branch = b == 0 ? dialog.Text("Checkout", "Local branch name or an unambiguous remote branch name.") : branches[b.Value];
                // Remote selections create a local tracking branch through switch's DWIM.
                if (branch is not null && !Git.Run(targets[0].Path, "show-ref", "--verify", "--quiet", "refs/heads/" + branch).Ok)
                {
                    var remotes = Git.Run(targets[0].Path, "remote").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var remote = remotes.FirstOrDefault(r => branch.StartsWith(r + "/"));
                    if (remote is not null) branch = branch[(remote.Length + 1)..];
                }
            }
            else branch = dialog.Text(actions[choice.Value], "Branch name (the same name in each selected repository).");
            if (string.IsNullOrWhiteSpace(branch)) return;
            foreach (var t in targets)
            {
                string? start = choice == 1 ? null : dialog.Text("Starting point · " + t.Project, "Branch, tag or commit to start from.", "HEAD");
                if (choice != 1 && string.IsNullOrWhiteSpace(start)) return;
                string? path = choice == 2 ? dialog.Text("New worktree · " + t.Project, "New absolute directory path.", Path.Combine(t.ProjectRoot, branch.Replace('/', Path.DirectorySeparatorChar))) : null;
                if (choice == 2 && string.IsNullOrWhiteSpace(path)) return;
                try { plans.Add(Operations.Branch(t, choice == 1 ? "Checkout" : choice == 2 ? "New worktree" : "New branch", branch, start, path)); }
                catch (InvalidOperationException e) when (choice == 1 && e.Message.Contains("already checked out"))
                {
                    var occupied = view.Snapshot.Worktrees.First(w => w.CommonDir == t.CommonDir && w.Branch == branch && w.Id != t.Id);
                    if (dialog.Choose("Branch already checked out", ["Select " + occupied.Title, "Cancel"]) == 0) {
                        view.Navigate(occupied.Project);
                        view.Focus = Math.Max(0, view.Rows.FindIndex(r => r.Tree?.Id == occupied.Id)); view.Targets.Clear(); view.Targets.Add(occupied.Id);
                    }
                    return;
                }
            }
        }
        else foreach (var t in targets)
        {
            if (choice == 3) { var onto = dialog.Text("Rebase · " + t.Project, "Base branch, tag or commit. Your worktree must be clean.", t.Upstream ?? ""); if (string.IsNullOrWhiteSpace(onto)) return; plans.Add(Operations.Rebase(t, onto)); }
            if (choice == 4) {
                var log = Git.Run(t.Path, "log", "-30", "--format=%h %s").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var pick = dialog.Choose("Revert commit · " + t.Project, log); if (pick is null) return; plans.Add(Operations.Revert(t, log[pick.Value].Split(' ')[0]));
            }
            if (choice == 5 && t.Operation.StartsWith("Rebase", StringComparison.Ordinal)) RunInteractive(InteractiveRebase.Continue(t));
            else if (choice is 5 or 6) plans.Add(Operations.ContinueOrAbort(t, choice == 6));
            if (choice == 7) plans.Add(Operations.RemoveWorktree(t));
            if (choice == 9) RunInteractive(InteractiveRebase.Amend(t));
        }
        Execute(plans);
    }
    void SyncMenu()
    {
        string[] actions = ["Fetch", "Push", "Pull (fast-forward only)", "Pull with rebase", "Pull with merge"];
        var action = dialog.Choose("Sync · choose operation", actions); if (action is null || !CanWrite()) return;
        var targets = PickTargets(actions[action.Value]); if (targets is null) return;
        if (action == 0) targets = targets.DistinctBy(t => t.CommonDir).ToList();
        List<Plan> plans = [];
        foreach (var t in targets)
        {
            string? remote = null;
            if (action == 1 && (t.Upstream is null || t.UpstreamGone)) {
                var remotes = Git.Run(t.Path, "remote").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var r = dialog.Choose("Push destination · " + t.Project, remotes); if (r is null) return; remote = remotes[r.Value];
            }
            plans.Add(Operations.Sync(t, action == 2 ? "Pull" : actions[action.Value], remote));
        }
        Execute(plans);
    }
    void More()
    {
        (string Label, Action? Run)[] items = [
            ("## Files", null),
            ("Stage blocks of the highlighted file…", () => HunkAction(false)), ("Unstage blocks of the highlighted file…", () => HunkAction(true)),
            ("Discard selected unstaged edits…", () => FileAction("Discard unstaged")), ("Delete selected untracked files…", () => FileAction("Delete untracked")),
            ("Open highlighted file in editor", Editor), ("Add files or patterns to .gitignore…", IgnoreMenu),
            ("## Stash", null),
            ("Stash worktree changes…", StashChanges), ("Apply a stash (keep entry)…", ApplyStash),
            ("## Tools", null),
            ("Open configured diff tool", () => ExternalGit(false)), ("Open configured merge tool", () => ExternalGit(true)),
            ("## Repos & groups", null),
            ("Add project…", AddProject), ("Remove project from workspace…", RemoveProject), ("Search folders…", SearchFolders), ("Remove saved group…", RemoveGroup),
            ("## Admin", null),
            ("Activity and results", ShowActivity), ("Retry failed actions from last batch…", RetryFailed), ("Diagnostics", Diagnostics), ("Help", Help)
        ];
        var pick = dialog.Choose("More actions", items.Select(i => i.Label).ToList(), 1);
        if (pick is not null) items[pick.Value].Run?.Invoke();
    }
    void StashChanges()
    {
        if (!CanWrite()) return;
        var targets = PickTargets("Stash changes"); if (targets is null) return;
        var message = dialog.Text("Stash changes", "Message (tracked and untracked files are included).", "Workspace " + DateTime.Now.ToString("g"));
        if (message is not null) Execute(targets.Select(t => Operations.Stash(t, message)).ToList());
    }
    void ApplyStash()
    {
        if (!CanWrite()) return;
        var targets = PickTargets("Apply stash"); if (targets is null) return; List<Plan> plans = [];
        foreach (var t in targets) {
            var stashes = Git.Run(t.Path, "stash", "list", "--format=%gd %s").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var s = dialog.Choose("Apply stash · " + t.Project + " (stashes are shared by its worktrees)", stashes);
            if (s is null) return; plans.Add(Operations.ApplyStash(t, stashes[s.Value].Split(' ')[0]));
        }
        Execute(plans);
    }
    void RemoveProject()
    {
        if (!CanWrite()) return;
        var remove = dialog.Choose("Remove registration (keep all repository files)", settings.Projects.Select(p => p.Name).ToList());
        if (remove.HasValue && dialog.Review("Remove project registration", "Remove " + settings.Projects[remove.Value].Name + " from this workspace configuration. All files and Git data stay in place.")) {
            settings.Projects.RemoveAt(remove.Value); Config.Save(configPath, settings); view.Navigate(null, "All projects"); Refresh(); AfterScopeChange();
        }
    }
    void ShowActivity() => dialog.Message("Activity · most recent first", activity.Count == 0 ? "No actions recorded yet." :
        string.Join("\n\n", activity.AsEnumerable().Reverse().Select(a => $"{(a.Success ? "✓" : "!")} {a.Time:g} · {a.Action}\n{a.Target}\n{a.Detail}")));
    void RetryFailed() { if (CanWrite()) { if (failed.Count == 0) dialog.Message("Retry", "No failed actions in this session."); else Execute(failed.ToList()); } }
    void Diagnostics() => dialog.Message("Diagnostics", $"Config: {configPath}\nLocal status refreshed: {view.Snapshot.TakenAt:g}\n\n" +
        string.Join('\n', view.Snapshot.Errors) + "\n\n" + string.Join("\n\n", view.Snapshot.Worktrees.Select(t => $"{t.Title}\n{t.Path}\n{t.Error}\n{t.Prunable}\n{t.Locked}")));
    void AddProject()
    {
        if (!CanWrite()) return;
        var path = dialog.Text("Add existing project", "Absolute path to a checkout or a folder containing .git-main / .bare."); if (string.IsNullOrWhiteSpace(path)) return;
        path = Path.GetFullPath(path); var common = Git.ResolveRepository(path);
        foreach (var p in settings.Projects) {
            string? existing = null; try { existing = Git.ResolveRepository(p.Path); } catch (InvalidOperationException) { }
            if (string.Equals(existing, common, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("That repository is already registered as " + p.Name);
        }
        var name = dialog.Text("Project name", "A unique name for this project.", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (settings.Projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Project name already exists.");
        settings.Projects.Add(new(name, path)); Config.Save(configPath, settings); view.Navigate(null, "All projects"); Refresh(); AfterScopeChange();
    }
    public static List<string> ParseRoots(string text)
    {
        var roots = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (roots.FirstOrDefault(r => !Path.IsPathFullyQualified(r)) is { } bad) throw new InvalidOperationException("Use absolute folders: " + bad);
        return roots.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
    }
    void SearchFolders()
    {
        if (!CanWrite()) return;
        var current = string.Join("; ", Discovery.Roots(settings));
        var text = dialog.Text("Search folders", "Folders whose immediate subfolders are offered as repositories in the group editor. Separate with ; . Leave empty to turn off discovery - the editor then shows registered repositories only.", current);
        if (text is null) return;
        var roots = ParseRoots(text);
        settings.Roots = roots; Config.Save(configPath, settings);
        if (roots.Count == 0) { notice = "No search folders · group editor shows registered repos only"; return; }
        var missing = roots.Where(r => !Directory.Exists(r)).ToList();
        notice = "Saved " + roots.Count + " search folder" + (roots.Count == 1 ? "" : "s") + (missing.Count == 0 ? "" : " · not found: " + string.Join(", ", missing));
    }
    void Editor()
    {
        if (!CanWrite() || view.Current is not { Tree: not null, Change: not null } row) return;
        var start = new ProcessStartInfo(settings.Editor) { WorkingDirectory = row.Tree.Path, UseShellExecute = false };
        foreach (var arg in settings.EditorArguments) start.ArgumentList.Add(arg);
        start.ArgumentList.Add(Path.Combine(row.Tree.Path, row.Change.Path));
        RunExternal(start);
    }
    void ExternalGit(bool merge)
    {
        if (!CanWrite() || view.Current?.Tree is not { } t) return;
        List<string> args = merge ? ["mergetool"] : ["difftool", "--no-prompt", "--trust-exit-code"];
        if (!merge && view.Current.Change?.Area == ChangeArea.Staged) args.Add("--cached");
        if (view.Current.Change is { } c) { args.Add("--"); args.AddRange(c.Paths); }
        RunExternal(Git.StartInfo(t.Path, args, false));
    }
    void RunExternal(ProcessStartInfo start)
    {
        terminal.Suspend();
        try { using var process = Process.Start(start); process?.WaitForExit(); }
        finally { terminal.Resume(); Refresh(); }
    }
    void HunkAction(bool unstage)
    {
        if (!CanWrite() || view.Current is not { Tree: not null, Change: not null } row) return;
        if (row.Change.Area != (unstage ? ChangeArea.Staged : ChangeArea.Unstaged)) throw new InvalidOperationException("Highlight a " + (unstage ? "staged" : "tracked unstaged") + " file first.");
        var patch = Patches.Read(row.Tree, row.Change);
        var labels = patch.Hunks.Select(h => h.Split('\n')[0] + "  " + string.Join(" ", h.Split('\n').Where(s => s.StartsWith('+') || s.StartsWith('-')).Take(2))).ToArray();
        var selected = dialog.Check(unstage ? "Unstage blocks" : "Stage blocks", labels); if (selected is null || selected.Count == 0) return;
        Execute([Patches.Plan(row.Tree, row.Change, patch, selected, unstage)]);
    }
    void Help() => dialog.Message("Git Workspace · help", "Checkboxes select items for an action. They do not stage files. Staged rows show exactly what is in Git's index.\n\n" +
        "↑ ↓   Move focus                  Space / click checkbox   Select\nEnter/D  Open diff / history       ← →   Collapse section\nTab    Changes / Worktrees / Commits  A   Select all visible\n<  >  Resize the preview split\nX      Clear selections             /     Search paths and branches\nF      Filters                      W     Switch / manage project groups\n1-9    Jump group tab               Ctrl+N  New project group\nE      Edit current group tab       P     Repo View\nH      Commits view                 [ / ]  Cycle group or repo\nBackspace   Previous view / close Repo View\nI      Interactive rebase / manage paused rebase\nS      Stage selected               U     Unstage selected\nC      Commit staged files          B     Branch / rebase / revert\nY      Fetch / push / pull          M     More actions and activity\nO      Editor                      R     Refresh local status\nPgUp / PgDn / mouse wheel in preview  Scroll diff\nEsc    Clear search and filter      Q     Quit\n\n" +
        "Commits (H or Tab): Enter files, Enter diff, Esc back. / search (@author, :path), F filter, B branch, M merge parent, D difftool (files/diff levels), I rebase from commit, V revert, Y copy hash, Z zoom, PgUp/PgDn scroll preview. Shift+Y / Shift+M reach Sync and More.\n\n" +
        "Diff legend: the first line names the old (−) and new (+) sides. Two gutters show old and new line numbers; removed lines have a red background, added lines green.\n\n" +
        "T toggles untracked files (also available under F and by clicking Untracked in the toolbar). This preference is saved between launches and applies across groups and Repo View. Staged new files remain visible.\n\n" +
        "G opens a destination checklist for selected untracked files or a pattern. Choose branches/worktrees, including outside this group. Inactive branches ask for a new worktree directory. The review lists rules and destinations before writing. Existing contents are preserved; review and commit the .gitignore change normally.\n\n" +
        "Click a row for details; Enter / D or Open diff loads its diff on demand. Click its checkbox to select. Project and worktree checkboxes select visible children. Hidden selections are counted in the footer and disclosed before an action.\n\n" +
        "Branch, Sync and Commit open an explicit worktree checklist, including clean worktrees. A commit includes ALL staged files in each chosen worktree.\n\n" +
        "Each target gets its own result. Multi-repository operations are not atomic. Retry never repeats successful targets, and refuses to act if the checkout changed since the original preview.\n\n" +
        "Stage/unstage individual blocks via More. Discard unstaged restores tracked files from the index; Delete untracked is a separate action. Neither runs git clean.\n\n" +
        "Groups are tabs: 1–9 or click to switch, [ ] cycle, Ctrl+N new, E edit/rename/duplicate/delete, W list. The editor ticks registered and discovered repositories (More → Search folders); new ones are registered on save.\n\n" +
        "Commits (Tab or H): the list shows the branch graph with ↑ on unpushed commits. Enter drills into a commit's files, then a file's diff; Esc goes back. / searches (words, @author, :path), F filters (unpushed, since a base), B chooses another branch or worktree, lowercase M selects a merge parent, D opens the difftool, V reviews a revert, Y copies the hash, Z zooms the preview. I includes the highlighted commit and its descendants in an interactive rebase, opening Git's real todo editor. A clean worktree and local branch are required. Use I again to continue, edit the remaining todo, amend an edit stop or abort.\n\n" +
        "Settings and saved groups are local to this machine. Local status refreshes automatically. Remote state only refreshes after an explicit Fetch/Pull. No background network polling.");
}
