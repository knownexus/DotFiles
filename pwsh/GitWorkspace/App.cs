using System.Diagnostics;

namespace GitWorkspace;

public sealed partial class App(Settings settings, string configPath, bool demo = false)
{
    readonly WorkspaceView view = new(settings, Config.ReadOr(configPath + ".view.json", new ViewState()));
    readonly List<OperationResult> activity = Config.ReadOr(configPath + ".activity.json", new List<OperationResult>());
    readonly List<Plan> failed = [];
    Terminal terminal = null!;
    Dialogs dialog = null!;
    string notice = "Checkboxes select; Staged labels show commit contents.";
    string preview = "", previewKey = "";
    int previewScroll;
    bool quit;
    Task<Snapshot>? scan;
    DateTime nextScan;

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
                terminal.Draw(Screen.Render(view, terminal.Width, terminal.Height, preview, previewScroll, notice, scan is not null));
                var key = terminal.Poll(120);
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
            preview = $"{row.Tree!.Title}\n{row.Change.Area} · {row.Change.Path}\n\n" + (demo ? Demo.Diff : Git.Diff(row.Tree, row.Change));
            if (!demo && view.SelectionStamps.TryGetValue(row.Id, out var stamp) && stamp != Operations.ChangeStamp(row.Tree, row.Change))
                preview = "UPDATED SINCE SELECTION · uncheck and review before acting\n\n" + preview;
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
    }
    void Handle(Input key)
    {
        if (key.Mouse)
        {
            if (key.Wheel != 0) {
                if (terminal.Width >= 118 && key.X > Math.Clamp(terminal.Width / 2, 55, 90)) previewScroll = Math.Max(0, previewScroll + key.Wheel);
                else view.Focus = Math.Clamp(view.Focus + key.Wheel, 0, Math.Max(0, view.Rows.Count - 1));
                return;
            }
            if (key.Y == 3 && key.X < 28) { view.WorktreeMode = !view.WorktreeMode; view.Rebuild(); return; }
            if (key.Y == 3 && key.X >= 30 && key.X < 36) { RepoView(); return; }
            if (key.Y == 3 && key.X >= 38 && key.X < 47) { BrowseHistory(); return; }
            if (key.Y == 3 && key.X >= 49 && key.X < 65) { ToggleUntracked(); return; }
            if (key.Y == terminal.Height - 3) {
                if (view.WorktreeMode && key.X >= 43) { RebaseInteractive(); return; }
                int actionX = key.X + (view.WorktreeMode ? 22 : 0);
                char command = actionX < 12 ? 's' : actionX < 24 ? 'u' : actionX < 36 ? 'c' : actionX < 48 ? 'b' : actionX < 58 ? 'y' : 'm';
                Handle(new(Character: command)); return;
            }
            int index = view.Scroll + key.Y - 5;
            if (key.Y >= 5 && key.Y < terminal.Height - 5 && index < view.Rows.Count && (terminal.Width < 118 || key.X < Math.Clamp(terminal.Width / 2, 55, 90))) {
                view.Focus = index;
                var row = view.Current!;
                int checkStart = row.Kind == "project" ? 3 : row.Kind == "worktree" ? 5 : 7;
                if (key.X >= checkStart && key.X <= checkStart + 2) view.Toggle(row);
                else if (row.Kind != "file" && key.X < checkStart) Collapse();
            }
            return;
        }
        if (key.Control && key.Key == ConsoleKey.C) { quit = true; return; }
        if (key.Key == ConsoleKey.UpArrow) { view.Focus = Math.Max(0, view.Focus - 1); return; }
        if (key.Key == ConsoleKey.DownArrow) { view.Focus = Math.Min(Math.Max(0, view.Rows.Count - 1), view.Focus + 1); return; }
        if (key.Key == ConsoleKey.PageUp) { previewScroll = Math.Max(0, previewScroll - 10); return; }
        if (key.Key == ConsoleKey.PageDown) { previewScroll = Math.Max(0, Math.Min(preview.Split('\n').Length - 1, previewScroll + 10)); return; }
        if (key.Key == ConsoleKey.Spacebar) { view.Toggle(view.Current); return; }
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow) { Collapse(); return; }
        if (key.Key == ConsoleKey.Tab) { view.WorktreeMode = !view.WorktreeMode; view.Rebuild(); return; }
        if (key.Key == ConsoleKey.Enter) { if (view.Current?.Kind == "branch" || view.RepoMode && view.Current?.Kind == "worktree") BrowseHistory(); else dialog.Message("Preview · " + (view.Current?.Text ?? "Workspace"), preview); return; }
        if (key.Key == ConsoleKey.Backspace) { view.Back(); previewKey = ""; return; }
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
            case 'w': Workspace(); break;
            case 'p': RepoView(); break;
            case 'h': BrowseHistory(); break;
            case 'i': RebaseInteractive(); break;
            case '[': view.Cycle(-1); previewKey = ""; break;
            case ']': view.Cycle(1); previewKey = ""; break;
            case 'r': Refresh(); break;
            case 's': FileAction("Stage"); break;
            case 'u': FileAction("Unstage"); break;
            case 'c': Commit(); break;
            case 'b': BranchMenu(); break;
            case 'y': SyncMenu(); break;
            case 'm': More(); break;
            case 'o': Editor(); break;
            case '?': Help(); break;
        }
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
    void Execute(List<Plan> plans, bool review = true)
    {
        if (plans.Count == 0) return;
        string title = plans[0].Action + $" · {plans.Count} target(s)";
        var content = string.Join("\n\n────────────────────────\n\n", plans.Select(p => p.Tree.Title + "\n" + p.Preview));
        if (review && !dialog.Review(title, content)) return;
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
            var details = string.Join("\n\n", files.Select(x => x.Tree.Title + "\n" + Git.Diff(x.Tree, x.Change)));
            if (!dialog.Review(action, explanation + "\n\n" + details, "Review file list")) return;
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
        string[] options = ["Stage blocks of the highlighted file…", "Unstage blocks of the highlighted file…", "Discard selected unstaged edits…", "Delete selected untracked files…",
            "Open highlighted file in editor", "Open configured diff tool", "Open configured merge tool", "Stash worktree changes…", "Apply a stash (keep entry)…",
            "Activity and results", "Retry failed actions from last batch…", "Add project…", "Remove project from workspace…", "Remove saved group…", "Diagnostics", "Help", "Add files or patterns to .gitignore…"];
        var pick = dialog.Choose("More actions", options); if (pick is null) return;
        switch (pick)
        {
            case 0: HunkAction(false); break;
            case 1: HunkAction(true); break;
            case 2: FileAction("Discard unstaged"); break;
            case 3: FileAction("Delete untracked"); break;
            case 4: Editor(); break;
            case 5: ExternalGit(false); break;
            case 6: ExternalGit(true); break;
            case 7:
                if (!CanWrite()) return;
                var stashTargets = PickTargets("Stash changes"); if (stashTargets is null) return;
                var message = dialog.Text("Stash changes", "Message (tracked and untracked files are included).", "Workspace " + DateTime.Now.ToString("g"));
                if (message is not null) Execute(stashTargets.Select(t => Operations.Stash(t, message)).ToList()); break;
            case 8:
                if (!CanWrite()) return;
                var applyTargets = PickTargets("Apply stash"); if (applyTargets is null) return; List<Plan> plans = [];
                foreach (var t in applyTargets) {
                    var stashes = Git.Run(t.Path, "stash", "list", "--format=%gd %s").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var s = dialog.Choose("Apply stash · " + t.Project + " (stashes are shared by its worktrees)", stashes);
                    if (s is null) return; plans.Add(Operations.ApplyStash(t, stashes[s.Value].Split(' ')[0]));
                }
                Execute(plans); break;
            case 9: dialog.Message("Activity · most recent first", activity.Count == 0 ? "No actions recorded yet." : string.Join("\n\n", activity.AsEnumerable().Reverse().Select(a => $"{(a.Success ? "✓" : "!")} {a.Time:g} · {a.Action}\n{a.Target}\n{a.Detail}"))); break;
            case 10: if (CanWrite()) { if (failed.Count == 0) dialog.Message("Retry", "No failed actions in this session."); else Execute(failed.ToList()); } break;
            case 11: AddProject(); break;
            case 12:
                if (!CanWrite()) return;
                var remove = dialog.Choose("Remove registration (keep all repository files)", settings.Projects.Select(p => p.Name).ToList());
                if (remove.HasValue && dialog.Review("Remove project registration", "Remove " + settings.Projects[remove.Value].Name + " from this workspace configuration. All files and Git data stay in place.")) {
                    settings.Projects.RemoveAt(remove.Value); Config.Save(configPath, settings); view.Navigate(null, "All projects"); Refresh();
                } break;
            case 13:
                RemoveGroup(); break;
            case 14: dialog.Message("Diagnostics", $"Config: {configPath}\nLocal status refreshed: {view.Snapshot.TakenAt:g}\n\n" +
                string.Join('\n', view.Snapshot.Errors) + "\n\n" + string.Join("\n\n", view.Snapshot.Worktrees.Select(t => $"{t.Title}\n{t.Path}\n{t.Error}\n{t.Prunable}\n{t.Locked}"))); break;
            case 15: Help(); break;
            case 16: IgnoreMenu(); break;
        }
    }
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
        settings.Projects.Add(new(name, path)); Config.Save(configPath, settings); view.Navigate(null, "All projects"); Refresh();
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
        "↑ ↓   Move focus                  Space / click checkbox   Select\nEnter  Review diff / history       ← →   Collapse section\nTab    Changes / Worktrees          A     Select all visible\nX      Clear selections             /     Search paths and branches\nF      Filters                      W     Switch / manage project groups\nP      Repo View                    H     Commit history\n[ / ]  Cycle group or repo          Backspace   Previous view\nI      Interactive rebase / manage paused rebase\nS      Stage selected               U     Unstage selected\nC      Commit staged files          B     Branch / rebase / revert\nY      Fetch / push / pull          M     More actions and activity\nO      Editor                      R     Refresh local status\nPgUp / PgDn / mouse wheel in preview  Scroll diff\nEsc    Clear search and filter      Q     Quit\n\n" +
        "T toggles untracked files (also available under F and by clicking Untracked in the toolbar). This preference is saved between launches and applies across groups and Repo View. Staged new files remain visible.\n\n" +
        "G adds selected untracked files, or a file path / pattern, to the chosen worktree's root .gitignore. The preview lists the rules and destination. Existing contents are preserved; review and commit the .gitignore change normally.\n\n" +
        "Click a row to preview; click its checkbox to select. Project and worktree checkboxes select visible children. Hidden selections are counted in the footer and disclosed before an action.\n\n" +
        "Branch, Sync and Commit open an explicit worktree checklist, including clean worktrees. A commit includes ALL staged files in each chosen worktree.\n\n" +
        "Each target gets its own result. Multi-repository operations are not atomic. Retry never repeats successful targets, and refuses to act if the checkout changed since the original preview.\n\n" +
        "Stage/unstage individual blocks via More. Discard unstaged restores tracked files from the index; Delete untracked is a separate action. Neither runs git clean.\n\n" +
        "W creates, edits, renames, duplicates and deletes groups. Mix whole repos, fixed worktree paths and local branch refs. Branch entries follow their checkout; inactive branches still offer history. P shows the entire repo regardless of group membership.\n\n" +
        "History: Enter opens the commit patch, N/P page, B chooses another branch, M selects a merge parent. I includes the highlighted commit and its descendants in an interactive rebase, opening Git's real todo editor. A clean worktree and local branch are required. Use I again to continue, edit the remaining todo, amend an edit stop or abort.\n\n" +
        "Settings and saved groups are local to this machine. Local status refreshes automatically. Remote state only refreshes after an explicit Fetch/Pull. No background network polling.");
}
