using GitWorkspace;

static partial class Tests
{
    static string root = "";
    static string? filter;
    static int passed, failed, sequence;
    static readonly string original = string.Join('\n', Enumerable.Range(1, 40).Select(i => "line " + i)) + "\n";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Equal<T>(T expected, T actual, string message) { Check(Equals(expected, actual), $"{message}: expected {expected}, got {actual}"); }
    static void Test(string name, Action test)
    {
        if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        try { test(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + "\n" + e); failed++; }
    }
    static string Repo(bool initial = true)
    {
        string path = Path.Combine(root, "repo " + ++sequence); Directory.CreateDirectory(path);
        Git.Run(path, "init", "-b", "main").Require();
        Git.Run(path, "config", "user.name", "Workspace Test").Require();
        Git.Run(path, "config", "user.email", "test@example.invalid").Require();
        Git.Run(path, "config", "core.autocrlf", "false").Require();
        if (initial) { Write(path, "file.txt", original); Git.Run(path, "add", ".").Require(); Git.Run(path, "commit", "-m", "Initial").Require(); }
        return path;
    }
    static void Write(string repo, string file, string contents) => File.WriteAllText(Path.Combine(repo, file), contents);
    static Worktree Tree(string repo) { var scan = Git.Scan(new() { Projects = [new("Test", repo)] }); if (scan.Errors.Count > 0) throw new Exception(string.Join(';', scan.Errors)); return scan.Worktrees.First(t => t.Path.Replace('\\', '/') == repo.Replace('\\', '/')); }
    static void Success(Plan plan) { var r = plan.Run(); Check(r.Success, r.Detail); }
    static void Throws(Action act, string message)
    {
        try { act(); } catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--sequence-editor") return SequenceEditor(args);
        int filterAt = Array.IndexOf(args, "--filter");
        if (filterAt >= 0 && filterAt + 1 < args.Length) filter = args[filterAt + 1];
        root = Path.Combine(Path.GetTempPath(), "gws-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string global = Path.Combine(root, "gitconfig"); File.WriteAllText(global, "");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", global); Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", null);

        if (!args.Contains("--new") && !args.Contains("--ignore")) {
        Test("worktree parser skips bare entries and keeps locked, detached and missing registrations", () => {
            string data = "worktree C:/repos/Test/.git-main\0bare\0\0worktree C:/other/location\0HEAD abc\0detached\0locked keep me\0\0worktree C:/missing\0branch refs/heads/topic\0prunable missing gitdir\0\0";
            var trees = Git.ParseWorktrees(data, new("Test", "C:/repos/Test"), "C:/repos/Test/.git-main");
            Equal(2, trees.Count, "Count"); Equal("(detached)", trees[0].Branch, "Detached"); Equal("keep me", trees[0].Locked, "Lock"); Equal("topic", trees[1].Branch, "Branch");
        });
        Test("status parser separates staged and unstaged sides and consumes rename NUL records", () => {
            var t = new Worktree { Project = "T", ProjectRoot = root, Path = root, CommonDir = root };
            Git.ParseStatus("# branch.head actual-branch\0# branch.oid abc\0# branch.upstream origin/main\0# branch.ab +2 -3\0" +
                "1 MM N... 100644 100644 100644 abc abc space [a].txt\0" +
                "2 R. N... 100644 100644 100644 abc abc R100 new name.txt\0old name.txt\0? unknown.txt\0", t);
            Equal("actual-branch", t.Branch, "Branch"); Equal(2, t.Ahead, "Ahead"); Equal(3, t.Behind, "Behind"); Equal(4, t.Changes.Count, "Rows");
            Equal("old name.txt", t.Changes[2].OriginalPath, "Rename source");
        });
        Test("discovery handles normal repositories, bare layouts, external worktrees and deduplication", () => {
            var source = Repo(); var project = Path.Combine(root, "bare project"); Directory.CreateDirectory(project);
            var bare = Path.Combine(project, ".git-main"); Git.Run(source, "clone", "--bare", source, bare).Require();
            var inside = Path.Combine(project, "master"); Git.Run(bare, "worktree", "add", inside, "main").Require();
            var outside = Path.Combine(root, "external feature"); Git.Run(bare, "worktree", "add", "-b", "topic", outside, "main").Require();
            var scan = Git.Scan(new() { Projects = [new("Bare", project)] }); Equal(2, scan.Worktrees.Count, "Worktrees"); Check(scan.Worktrees.All(t => t.Error.Length == 0), "Status failed");
            Equal("main", scan.Worktrees.First(t => t.Name == "master").Branch, "Folder not branch");
            scan = Git.Scan(new() { Projects = [new("Bare", project), new("Duplicate", outside)] }); Equal(1, scan.Errors.Count, "Duplicate registration");
            var alternate = Path.Combine(root, "bare alternate"); Directory.CreateDirectory(alternate);
            Git.Run(source, "clone", "--bare", source, Path.Combine(alternate, ".bare")).Require();
            Check(Git.ResolveRepository(alternate).Replace('\\', '/').EndsWith("/.bare"), "Alternative layout");
        });
        Test("stale registrations are reported and never treated as clean", () => {
            var path = Repo(); var linked = Path.Combine(root, "stale worktree"); var moved = Path.Combine(root, "moved worktree");
            Git.Run(path, "worktree", "add", "-b", "stale", linked).Require();
            Check(Path.GetFullPath(linked).StartsWith(root + Path.DirectorySeparatorChar) && Path.GetFullPath(moved).StartsWith(root + Path.DirectorySeparatorChar), "Move scope");
            Directory.Move(linked, moved);
            var scan = Git.Scan(new() { Projects = [new("Test", path)] }); Check(scan.Worktrees.Any(t => t.Error.Contains("Missing")), "Missing registration not exposed");
        });
        Test("stage and unstage literal paths with spaces, unicode, brackets and leading dash", () => {
            var repo = Repo(); var names = new[] { "literal[1].txt", "literal1.txt", "-option.txt", "space ü.txt" };
            foreach (var name in names) Write(repo, name, "new\n");
            var tree = Tree(repo); Success(Operations.Files(tree, "Stage", tree.Changes.Where(c => c.Path != "literal1.txt")));
            tree = Tree(repo); Equal(3, tree.Changes.Count(c => c.Area == ChangeArea.Staged), "Staged exact paths");
            Check(tree.Changes.Any(c => c.Path == "literal1.txt" && c.Area == ChangeArea.Untracked), "Wildcard matched another file");
            Success(Operations.Files(tree, "Unstage", tree.Changes.Where(c => c.Area == ChangeArea.Staged)));
            Check(Tree(repo).Changes.All(c => c.Area == ChangeArea.Untracked), "Unstage did not preserve files");
        });
        Test("unborn repository can stage and unstage without a HEAD commit", () => {
            var repo = Repo(false); Write(repo, "first.txt", "first\n"); var t = Tree(repo);
            Success(Operations.Files(t, "Stage", t.Changes)); t = Tree(repo);
            Success(Operations.Files(t, "Unstage", t.Changes)); Check(File.Exists(Path.Combine(repo, "first.txt")), "Unstage deleted working file");
            Equal(ChangeArea.Untracked, Tree(repo).Changes.Single().Area, "Unborn unstage");
        });
        Test("unstaging a rename preserves both working files correctly", () => {
            var repo = Repo(); Git.Run(repo, "mv", "file.txt", "renamed file.txt").Require(); var t = Tree(repo);
            Check(t.Changes.Single().OriginalPath == "file.txt", "Rename not parsed");
            Success(Operations.Files(t, "Unstage", t.Changes));
            Check(File.Exists(Path.Combine(repo, "renamed file.txt")), "Rename destination lost"); Check(!File.Exists(Path.Combine(repo, "file.txt")), "Unstage restored working file unexpectedly");
            Equal("", Git.Run(repo, "diff", "--cached", "--name-only").Require().Output, "Rename still staged");
        });
        Test("discard unstaged edits preserves the staged version", () => {
            var repo = Repo(); Write(repo, "file.txt", "staged\n"); Git.Run(repo, "add", "file.txt").Require(); Write(repo, "file.txt", "unstaged\n");
            var t = Tree(repo); Success(Operations.Files(t, "Discard unstaged", t.Changes.Where(c => c.Area == ChangeArea.Unstaged)));
            Equal("staged\n", File.ReadAllText(Path.Combine(repo, "file.txt")), "Staged content not preserved");
            Equal(1, Tree(repo).Changes.Count, "Staged content disappeared");
        });
        Test("discard rejects staged or untracked rows", () => {
            var repo = Repo(); Write(repo, "new.txt", "new"); var t = Tree(repo); var r = Operations.Files(t, "Discard unstaged", t.Changes).Run();
            Check(!r.Success && File.Exists(Path.Combine(repo, "new.txt")), "Untracked data was discarded");
        });
        Test("delete untracked only deletes selected literal files", () => {
            var repo = Repo(); Write(repo, "delete[1].txt", "one"); Write(repo, "delete1.txt", "two"); var t = Tree(repo);
            Success(Operations.Files(t, "Delete untracked", t.Changes.Where(c => c.Path == "delete[1].txt")));
            Check(!File.Exists(Path.Combine(repo, "delete[1].txt")) && File.Exists(Path.Combine(repo, "delete1.txt")), "Deletion was not literal");
        });
        Test("staging edits after the preview is rejected even if status codes did not change", () => {
            var repo = Repo(); Write(repo, "file.txt", "first edit"); var t = Tree(repo); var plan = Operations.Files(t, "Stage", t.Changes);
            Write(repo, "file.txt", "second edit"); Check(!plan.Run().Success, "Stale preview was accepted");
        });
        Test("partial staging and unstaging keep separate hunks intact", () => {
            var repo = Repo(); Write(repo, "file.txt", original.Replace("line 2\n", "changed 2\n").Replace("line 35\n", "changed 35\n")); var t = Tree(repo); var c = t.Changes.Single();
            var patch = Patches.Read(t, c); Equal(2, patch.Hunks.Count, "Separate hunks"); Success(Patches.Plan(t, c, patch, [0], false));
            var staged = Git.Run(repo, "diff", "--cached").Require().Output; Check(staged.Contains("changed 2") && !staged.Contains("changed 35"), "Wrong hunks staged");
            t = Tree(repo); c = t.Changes.Single(c => c.Area == ChangeArea.Staged); patch = Patches.Read(t, c); Success(Patches.Plan(t, c, patch, [0], true));
            Equal("", Git.Run(repo, "diff", "--cached").Require().Output, "Hunk still staged");
            Check(File.ReadAllText(Path.Combine(repo, "file.txt")).Contains("changed 35"), "Unstage lost edits");
        });
        Test("commit plan includes all staged files and leaves unstaged edits", () => {
            var repo = Repo(); Write(repo, "file.txt", "staged"); Write(repo, "other.txt", "other"); Git.Run(repo, "add", ".").Require(); Write(repo, "file.txt", "later");
            var t = Tree(repo); var plan = Operations.Commit(t, "Two files"); Check(plan.Preview.Contains("other.txt") && plan.Preview.Contains("ALL staged"), "Commit scope not explicit");
            Success(plan); Equal(1, Tree(repo).Changes.Count, "Unstaged edits lost"); Equal("staged", Git.Run(repo, "show", "HEAD:file.txt").Require().Output, "Committed unstaged content");
        });
        Test("batch commits remain separate and a failed hook does not repeat successful commits", () => {
            var a = Repo(); var b = Repo(); foreach (var repo in new[] { a, b }) { Write(repo, "file.txt", "edit"); Git.Run(repo, "add", ".").Require(); }
            Write(b, ".git/hooks/pre-commit", "#!/bin/sh\nexit 1\n");
            var first = Operations.Commit(Tree(a), "Shared feature"); var second = Operations.Commit(Tree(b), "Shared feature"); Success(first);
            Check(!second.Run().Success, "Hook did not fail"); string committed = Git.Head(Tree(a));
            Write(b, ".git/hooks/pre-commit", "#!/bin/sh\nexit 0\n"); Success(second);
            Equal(committed, Git.Head(Tree(a)), "Successful target repeated");
        });
        Test("branch creation, checkout and new worktree with one branch per checkout", () => {
            var repo = Repo(); Success(Operations.Branch(Tree(repo), "New branch", "feature/test", "HEAD")); Equal("feature/test", Tree(repo).Branch, "New branch");
            Success(Operations.Branch(Tree(repo), "Checkout", "main"));
            var newPath = Path.Combine(root, "new feature"); Success(Operations.Branch(Tree(repo), "New worktree", "feature/worktree", "HEAD", newPath));
            Throws(() => Operations.Branch(Tree(repo), "Checkout", "feature/worktree"), "Occupied branch should be refused");
            var snapshot = Git.Scan(new() { Projects = [new("Test", repo)] }); Equal(2, snapshot.Worktrees.Count, "New worktree not discovered");
            var linked = snapshot.Worktrees.Single(t => t.Branch == "feature/worktree"); Success(Operations.RemoveWorktree(linked));
            Check(!Directory.Exists(newPath) && Git.Run(repo, "show-ref", "--verify", "refs/heads/feature/worktree").Ok, "Removal deleted branch or left directory");
        });
        Test("push, fetch and fast-forward pull use local test remotes", () => {
            var repo = Repo(); var remote = Path.Combine(root, "remote " + ++sequence + ".git"); Git.Run(root, "init", "--bare", "-b", "main", remote).Require();
            Git.Run(repo, "remote", "add", "origin", remote).Require(); Success(Operations.Sync(Tree(repo), "Push", "origin"));
            Equal("origin/main", Tree(repo).Upstream, "Push upstream");
            var other = Path.Combine(root, "clone " + ++sequence); Git.Run(root, "clone", remote, other).Require();
            Git.Run(other, "config", "user.name", "Other").Require(); Git.Run(other, "config", "user.email", "other@example.invalid").Require();
            Write(other, "remote.txt", "incoming"); Git.Run(other, "add", ".").Require(); Git.Run(other, "commit", "-m", "Remote change").Require(); Git.Run(other, "push").Require();
            Success(Operations.Sync(Tree(repo), "Fetch")); Equal(1, Tree(repo).Behind, "Behind after fetch");
            Success(Operations.Sync(Tree(repo), "Pull")); Check(File.Exists(Path.Combine(repo, "remote.txt")), "Pull did not update worktree");
            Write(repo, "local.txt", "outgoing"); Git.Run(repo, "add", ".").Require(); Git.Run(repo, "commit", "-m", "Local change").Require();
            Success(Operations.Sync(Tree(repo), "Push")); Equal(0, Tree(repo).Ahead, "Push not reflected");
        });
        Test("rebase conflicts can be aborted or resolved and continued", () => {
            var repo = Repo(); Git.Run(repo, "switch", "-c", "topic").Require(); Write(repo, "file.txt", "topic\n"); Git.Run(repo, "commit", "-am", "Topic").Require();
            string before = Git.Head(Tree(repo)); Git.Run(repo, "switch", "main").Require(); Write(repo, "file.txt", "base\n"); Git.Run(repo, "commit", "-am", "Base").Require(); Git.Run(repo, "switch", "topic").Require();
            Check(!Operations.Rebase(Tree(repo), "main").Run().Success, "Expected conflict");
            var t = Tree(repo); Check(t.Operation.Contains("Rebase") && t.Changes.Any(c => c.Area == ChangeArea.Conflict), "Conflict not represented");
            Success(Operations.ContinueOrAbort(t, true)); Equal(before, Git.Head(Tree(repo)), "Abort changed branch");
            Check(!Operations.Rebase(Tree(repo), "main").Run().Success, "Expected conflict again");
            Write(repo, "file.txt", "resolved\n"); t = Tree(repo); Success(Operations.Files(t, "Stage", t.Changes));
            Success(Operations.ContinueOrAbort(Tree(repo), false)); Check(Tree(repo).Operation.Length == 0 && Tree(repo).Changes.Count == 0, "Rebase did not finish");
        });
        Test("revert creates a new commit and stash apply preserves the stash", () => {
            var repo = Repo(); Write(repo, "extra.txt", "extra"); Git.Run(repo, "add", ".").Require(); Git.Run(repo, "commit", "-m", "Extra").Require();
            var head = Git.Head(Tree(repo)); Success(Operations.Revert(Tree(repo), head)); Check(!File.Exists(Path.Combine(repo, "extra.txt")) && Git.Head(Tree(repo)) != head, "Revert not committed");
            Write(repo, "file.txt", "stash edit"); Write(repo, "untracked.txt", "new"); Success(Operations.Stash(Tree(repo), "Feature work")); Equal(0, Tree(repo).Changes.Count, "Stash not clean");
            Success(Operations.ApplyStash(Tree(repo), "stash@{0}")); Check(File.Exists(Path.Combine(repo, "untracked.txt")) && Git.Run(repo, "stash", "list").Output.Length > 0, "Stash not preserved");
        });
        Test("view filters, mixed checkboxes, hidden selection and refresh preserve focus", () => {
            var repo = Repo(); Write(repo, "file.txt", "edit"); Write(repo, "new.txt", "new");
            var settings = new Settings { Projects = [new("Test", repo)] }; var v = new WorkspaceView(settings, new()); v.Update(Git.Scan(settings));
            v.Toggle(v.Rows.First(r => r.Change?.Path == "file.txt")); Equal("◩  ", v.Check(v.Rows.First()), "Mixed checkbox");
            v.Search = "new.txt"; v.Rebuild(); Equal(1, v.HiddenSelections, "Hidden selection");
            v.SelectVisible(); Equal(2, v.Selected.Count, "Select visible discarded hidden selection");
            v.Focus = v.Rows.FindIndex(r => r.Change is not null); string id = v.Current!.Id; v.Update(Git.Scan(settings)); Equal(id, v.Current!.Id, "Refresh moved focus");
            v.State.Collapsed.Add("p:Test"); v.Rebuild(); Equal(2, v.HiddenSelections, "Collapse lost selection");
        });
        Test("saved feature groups select worktrees without requiring matching branch names", () => {
            var repo = Repo(); var other = Path.Combine(root, "group worktree"); Git.Run(repo, "worktree", "add", "-b", "another-name", other).Require();
            var settings = new Settings { Projects = [new("Test", repo)] }; var snap = Git.Scan(settings); var selected = snap.Worktrees.Single(t => t.Branch == "another-name");
            settings.Groups.Add(new("Feature", [], [selected.Id])); var view = new WorkspaceView(settings, new() { Group = "Feature" }); view.Update(snap);
            Equal(1, view.InGroup().Count(), "Feature scope"); Equal("another-name", view.InGroup().Single().Branch, "Wrong worktree");
        });
        Test("canvas adapts to narrow and wide layouts and sanitizes terminal controls", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            string wide = Screen.Render(view, 140, 32, Demo.Diff, 0, "", false).Plain(); Check(wide.Contains("PREVIEW") && wide.Contains("AccessControl") && wide.Contains("Joiners") && wide.Contains("mobileApp"), "Wide layout");
            string narrow = Screen.Render(view, 85, 24, Demo.Diff, 0, "", false).Plain(); Check(!narrow.Contains("PREVIEW") && narrow.Contains("Space select"), "Narrow layout");
            Equal("bad [31mtext", Canvas.Clean("bad\u001b[31mtext"), "Control sanitization");
        });
        }
        if (!args.Contains("--ignore")) GroupHistoryTests();
        IgnoreTests();
        Console.WriteLine($"\n{passed} passed; {failed} failed. Temporary fixtures: {root}");
        return failed == 0 ? 0 : 1;
    }
}
