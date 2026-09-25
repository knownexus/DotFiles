using System.Diagnostics;
using System.Text.Json;
using GitWorkspace;

static partial class Tests
{
    static void CommitFile(string repo, string name)
    {
        Write(repo, name + ".txt", name + "\n"); Git.Run(repo, "add", "--", name + ".txt").Require(); Git.Run(repo, "commit", "-m", name).Require();
    }
    static int SequenceEditor(string[] args)
    {
        string mode = args[1], path = args[^1]; var lines = File.ReadAllLines(path).ToList();
        var picks = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith("pick ")).ToList();
        if (mode == "edit") lines[picks[0]] = "edit " + lines[picks[0]][5..];
        else if (mode == "fixup-drop") { lines[picks[1]] = "fixup " + lines[picks[1]][5..]; lines[picks[2]] = "drop " + lines[picks[2]][5..]; }
        else if (mode == "reverse") { var reversed = picks.Select(i => lines[i]).Reverse().ToList(); for (int i = 0; i < picks.Count; i++) lines[picks[i]] = reversed[i]; }
        File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false)); return 0;
    }
    static GitResult Native(InteractivePlan plan, string mode = "keep")
    {
        plan.Validate();
        string assembly = typeof(Tests).Assembly.Location.Replace('\\', '/');
        plan.Start.Environment["GIT_SEQUENCE_EDITOR"] = "dotnet '" + assembly.Replace("'", "'\\''") + "' --sequence-editor " + mode;
        plan.Start.Environment["GIT_EDITOR"] = "true";
        plan.Start.RedirectStandardOutput = plan.Start.RedirectStandardError = plan.Start.RedirectStandardInput = true;
        using var process = Process.Start(plan.Start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.StandardInput.Close();
        if (!process.WaitForExit(60000)) { process.Kill(true); throw new Exception("Rebase timed out"); }
        return new(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
    static void GroupHistoryTests()
    {
        Test("mixed groups round-trip and older groups stay compatible", () => {
            var settings = Demo.Settings(); var snapshot = Demo.Snapshot();
            var old = JsonSerializer.Deserialize<WorkspaceGroup>("{\"Name\":\"Older\",\"Projects\":[\"Joiners\"],\"Worktrees\":[]}", Config.Json)!;
            Equal(0, old.Branches.Count, "Old schema");
            var draft = new GroupDraft(settings, snapshot);
            draft.Toggle(draft.Members.First(m => m.Kind == "repo" && m.Project == "Joiners"));
            var inherited = draft.Members.First(m => m.Kind == "branch" && m.Project == "Joiners");
            Check(draft.Inherited(inherited), "Repo children not included"); Throws(() => draft.Toggle(inherited), "Inherited branch toggled");
            draft.Toggle(draft.Members.First(m => m.Kind == "worktree" && m.Project == "mobileApp"));
            draft.Toggle(draft.Members.First(m => m.Kind == "branch" && m.Value == "refs/heads/main"));
            var group = draft.Build(" Mixed "); settings.Groups.Add(group);
            string config = Path.Combine(root, "groups.json"); Config.Save(config, settings);
            var saved = Config.Read(config).Groups.Last(); Equal("Mixed", saved.Name, "Trimmed name"); Equal(1, saved.Projects.Count, "Repos"); Equal(1, saved.Worktrees.Count, "Worktrees"); Equal(1, saved.Branches.Count, "Branches");
            var unavailable = saved with { Worktrees = ["C:/missing/checkout"], Branches = [new("AccessControl", "refs/heads/missing")] };
            var edited = new GroupDraft(settings, snapshot, unavailable).Build("Keep unavailable");
            Equal(unavailable.Worktrees[0], edited.Worktrees[0], "Unavailable path lost"); Equal(unavailable.Branches[0], edited.Branches[0], "Unavailable branch lost");
        });
        Test("branch groups follow refs, fixed worktrees follow paths and Repo View restores navigation", () => {
            var repo = Repo(); var linked = Path.Combine(root, "group linked"); Git.Run(repo, "worktree", "add", "-b", "feature", linked).Require();
            Git.Run(repo, "branch", "inactive").Require();
            var settings = new Settings { Projects = [new("Test", repo)], Groups = [new("Branch", [], []) { Branches = [new("Test", "refs/heads/feature"), new("Test", "refs/heads/inactive")] }, new("Path", [], [Path.GetFullPath(linked).Replace('\\', '/')])] };
            var view = new WorkspaceView(settings, new() { Group = "Branch" }); view.Update(Git.Scan(settings));
            Equal("feature", view.InGroup().Single().Branch, "Branch selection"); Check(view.Rows.Any(r => r.Branch?.Name == "inactive"), "Inactive branch hidden");
            Git.Run(linked, "switch", "-c", "different").Require(); Git.Run(repo, "switch", "feature").Require(); view.Update(Git.Scan(settings));
            Equal(Path.GetFullPath(repo).Replace('\\', '/'), view.InGroup().Single().Id, "Branch did not follow checkout");
            view.Focus = view.Rows.FindIndex(r => r.Kind == "branch"); string focused = view.Current!.Id;
            view.Navigate("Test"); Equal(2, view.InGroup().Count(), "Repo restricted by group"); Check(view.Rows.Any(r => r.Branch?.Name == "main"), "Inactive repo branch missing");
            view.Back(); Equal("Branch", view.State.Group, "Back scope"); Equal(focused, view.Current!.Id, "Back focus");
            view.Cycle(1); Equal("Path", view.State.Group, "Group cycle"); Equal("different", view.InGroup().Single().Branch, "Path did not follow checkout");
        });
        Test("history reads an inactive branch, paginates stable tips and reviews root and merge parents", () => {
            var repo = Repo(); string initial = History.Resolve(repo, "HEAD"); CommitFile(repo, "Main"); Git.Run(repo, "switch", "-c", "side", initial).Require(); CommitFile(repo, "Side");
            Git.Run(repo, "switch", "main").Require(); Git.Run(repo, "merge", "--no-ff", "side", "-m", "Merge side").Require();
            string before = Git.Fingerprint(Tree(repo)); string tip = History.Resolve(repo, "main");
            var commits = History.Read(repo, tip); Equal(4, commits.Count, "History count"); Equal(2, commits[0].Parents.Count, "Merge parents");
            Check(History.Detail(repo, commits[0], 0).Contains("Side.txt"), "First parent diff"); Check(History.Detail(repo, commits[0], 1).Contains("Main.txt"), "Second parent diff");
            Check(History.Detail(repo, commits.Last()).Contains("+line 1"), "Root diff");
            Equal("Side", History.Read(repo, "side")[0].Subject, "Inactive history");
            Equal(commits[2].Hash, History.Read(repo, tip, 2, 1).Single().Hash, "Paging");
            Equal(before, Git.Fingerprint(Tree(repo)), "History changed checkout");
            CommitFile(repo, "Later"); Equal(4, History.Read(repo, tip).Count, "Pinned tip shifted");
        });
        Test("existing branch opens in a new worktree without rewriting its ref", () => {
            var repo = Repo(); Git.Run(repo, "branch", "topic").Require(); var branch = History.Branches("Test", Git.ResolveRepository(repo)).Single(b => b.Name == "topic");
            var destination = Path.Combine(root, "existing topic"); Success(Operations.ExistingWorktree(branch, new("Test", repo), destination));
            Equal("topic", Tree(destination).Branch, "Attached branch"); Equal(branch.Head, History.Resolve(repo, "topic"), "Branch changed");
        });
        Test("interactive rebase invokes the sequence editor for fixup and drop without updating other refs", () => {
            var repo = Repo(); CommitFile(repo, "One"); CommitFile(repo, "Two"); CommitFile(repo, "Three"); Git.Run(repo, "branch", "keep-original").Require();
            string other = History.Resolve(repo, "keep-original"); var plan = InteractiveRebase.Prepare(Tree(repo), "HEAD~3");
            Check(!plan.Start.RedirectStandardInput && (!plan.Start.Environment.TryGetValue("GIT_SEQUENCE_EDITOR", out var editor) || editor != "true"), "Native editor disabled");
            Native(plan, "fixup-drop").Require();
            var commits = History.Read(repo, "HEAD"); Equal(2, commits.Count, "Fixup/drop commit count"); Equal("One", commits[0].Subject, "Fixup subject");
            Check(File.Exists(Path.Combine(repo, "Two.txt")) && !File.Exists(Path.Combine(repo, "Three.txt")), "Fixup/drop contents");
            Equal(other, History.Resolve(repo, "keep-original"), "Other ref updated");
        });
        Test("interactive root rebase can reorder commits", () => {
            var repo = Repo(); CommitFile(repo, "One"); CommitFile(repo, "Two"); var before = History.Read(repo, "HEAD").Select(c => c.Subject).ToArray();
            Native(InteractiveRebase.Prepare(Tree(repo), null, true), "reverse").Require();
            Equal(string.Join('|', before.Reverse()), string.Join('|', History.Read(repo, "HEAD").Select(c => c.Subject)), "Root reordering");
        });
        Test("interactive edit pauses preserve branch groups and support amend continue and abort", () => {
            var repo = Repo(); CommitFile(repo, "One"); CommitFile(repo, "Two"); var plan = InteractiveRebase.Prepare(Tree(repo), "HEAD~2"); Native(plan, "edit").Require();
            var paused = Tree(repo); Check(paused.Operation.StartsWith("Rebase"), "Edit did not pause"); Equal("refs/heads/main", paused.ActiveBranchRef, "Rebase branch name");
            Check(new WorkspaceGroup("Feature", [], []) { Branches = [new("Test", "refs/heads/main")] }.Includes(paused), "Paused checkout disappeared from group");
            Write(repo, "amended.txt", "change"); Success(Operations.Files(Tree(repo), "Stage", Tree(repo).Changes)); Native(InteractiveRebase.Amend(Tree(repo))).Require();
            Native(InteractiveRebase.EditTodo(Tree(repo))).Require(); Native(InteractiveRebase.Continue(Tree(repo))).Require();
            Check(Tree(repo).Operation.Length == 0 && File.Exists(Path.Combine(repo, "amended.txt")), "Continue failed");
            string tip = History.Resolve(repo, "HEAD"); Native(InteractiveRebase.Prepare(Tree(repo), "HEAD~2"), "edit").Require();
            Success(Operations.ContinueOrAbort(Tree(repo), true)); Equal(tip, History.Resolve(repo, "HEAD"), "Abort changed history");
        });
        Test("interactive rebase refuses dirty detached and stale prepared checkouts", () => {
            var repo = Repo(); CommitFile(repo, "One"); var plan = InteractiveRebase.Prepare(Tree(repo), "HEAD~1"); Write(repo, "dirty.txt", "new");
            Throws(plan.Validate, "Stale plan accepted"); Throws(() => InteractiveRebase.Prepare(Tree(repo), "HEAD~1"), "Dirty checkout accepted");
            Git.Run(repo, "add", ".").Require(); Git.Run(repo, "commit", "-m", "Dirty saved").Require(); Git.Run(repo, "switch", "--detach").Require();
            Throws(() => InteractiveRebase.Prepare(Tree(repo), "HEAD~1"), "Detached checkout accepted");
        });
    }
}
