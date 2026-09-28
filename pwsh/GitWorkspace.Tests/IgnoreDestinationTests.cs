using GitWorkspace;

static partial class Tests
{
    static void IgnoreDestinationTests()
    {
        Test("ignore file rules can target another worktree outside the active group", () => {
            var repo = Repo(); var linked = Path.Combine(root, "ignore destination");
            Git.Run(repo, "worktree", "add", "-b", "destination", linked).Require();
            Git.Run(repo, "branch", "inactive").Require();
            Write(repo, "literal[2].log", "source content\n");
            var settings = new Settings { Projects = [new("Test", repo)], Groups = [new("Source only", [], [Path.GetFullPath(repo).Replace('\\', '/')])] };
            var snapshot = Git.Scan(settings); var source = snapshot.Worktrees.Single(t => t.Branch == "main"); var target = snapshot.Worktrees.Single(t => t.Branch == "destination");
            var view = new WorkspaceView(settings, new() { Group = "Source only" }); view.Update(snapshot);
            Equal(1, view.InGroup().Count(), "Fixture group scope");
            var choices = IgnoreDestinations.Available(snapshot, source.CommonDir);
            Check(choices.Any(c => c.Tree?.Id == target.Id), "Destination outside group omitted");
            Check(choices.Any(c => c.Branch?.Name == "inactive"), "Inactive branch omitted");
            Check(!choices.Any(c => c.Branch?.Name == "destination"), "Checked-out branch duplicated");
            var file = source.Changes.Single(c => c.Path == "literal[2].log"); var plan = Ignore.FilesTo(target, [(source, file)]);
            Check(plan.Preview.Replace('\\', '/').Contains(Path.Combine(linked, ".gitignore").Replace('\\', '/')), "Destination not in review"); Success(plan);
            Equal("/literal\\[2\\].log\n", File.ReadAllText(Path.Combine(linked, ".gitignore")), "Rule in wrong destination");
            Check(!File.Exists(Path.Combine(repo, ".gitignore")), "Source worktree was edited");
            Equal("source content\n", File.ReadAllText(Path.Combine(repo, file.Path)), "Source file changed");
            Equal("", Git.Run(linked, "diff", "--cached").Require().Output, "Destination was staged");
            var unrelated = Tree(Repo()); Throws(() => Ignore.FilesTo(unrelated, [(source, file)]), "Cross-repository file destination accepted");
        });
        Test("ignore can create worktrees for multiple inactive branches in one reviewed batch", () => {
            var repo = Repo(); Git.Run(repo, "branch", "first").Require(); Git.Run(repo, "branch", "second").Require();
            var snapshot = Git.Scan(new() { Projects = [new("Test", repo)] }); string head = Git.Head(snapshot.Worktrees.Single());
            var first = Path.Combine(root, "ignore first branch"); var second = Path.Combine(root, "ignore second branch");
            var plans = new[] {
                Ignore.NewWorktree(snapshot.Branches.Single(b => b.Name == "first"), new("Test", repo), first, ["*.log"]),
                Ignore.NewWorktree(snapshot.Branches.Single(b => b.Name == "second"), new("Test", repo), second, ["/build/"])
            };
            Check(!Directory.Exists(first) && !Directory.Exists(second), "Worktree created before review");
            foreach (var plan in plans) Success(plan);
            Equal("*.log\n", File.ReadAllText(Path.Combine(first, ".gitignore")), "First branch rule");
            Equal("/build/\n", File.ReadAllText(Path.Combine(second, ".gitignore")), "Second branch rule");
            Equal("first", Tree(first).Branch, "Wrong branch checked out"); Equal("second", Tree(second).Branch, "Wrong second branch");
            Equal("main", Tree(repo).Branch, "Original worktree switched"); Equal(head, Git.Head(Tree(repo)), "Commit changed");
            Check(!File.Exists(Path.Combine(repo, ".gitignore")), "Original worktree edited");
            Check(!plans[0].Run().Success, "Duplicate worktree execution accepted");
        });
        Test("ignore refuses a destination whose branch changed after review", () => {
            var repo = Repo(); var tree = Tree(repo); var plan = Ignore.Patterns(tree, ["*.tmp"]);
            Git.Run(repo, "switch", "-c", "other-at-same-commit").Require();
            Check(!plan.Run().Success, "Ignore wrote to a different branch");
            Check(!File.Exists(Path.Combine(repo, ".gitignore")), "Stale plan wrote file");
            Git.Run(repo, "branch", "not-checked-out").Require();
            var branch = History.Branches("Test", tree.CommonDir).Single(b => b.Name == "not-checked-out");
            var destination = Path.Combine(root, "stale ignore branch"); var create = Ignore.NewWorktree(branch, new("Test", repo), destination, ["*.log"]);
            Write(repo, "file.txt", "new commit"); Git.Run(repo, "add", "file.txt").Require(); Git.Run(repo, "commit", "-m", "Advance").Require();
            Git.Run(repo, "branch", "-f", branch.Name, "HEAD").Require();
            Check(!create.Run().Success && !Directory.Exists(destination), "Moved inactive branch accepted");
        });
        Test("history navigation never loads a patch until explicitly requested", () => {
            int details = 0, diffs = 0;
            var model = new CommitsModel(new("Test", "C:/demo", "HEAD", "Test"), (tip, filter, skip, count) => skip == 0 ? Demo.Graph() : [],
                (c, p) => { Interlocked.Increment(ref details); return new CommitDetail("commit " + c.Hash, [new("M", "src/EntryService.cs", null, 4, 1)]); },
                (c, p, f) => { Interlocked.Increment(ref diffs); return Demo.Diff; }, _ => "d3a721f904ed");
            model.Load(); model.Move(1); model.Move(1); model.Move(-1);
            // A narrow frame at the commit level draws only the list, so nothing reads a commit.
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot()); view.Mode = ViewMode.Commits;
            Screen.Render(view, Layout.For(80, 32, 0.5), new HitMap(), [], 0, "", false, (c, l, h) => CommitsRender.Body(c, l, h, model, 0), model);
            Equal(0, details, "Arrow navigation loaded commit details");
            CommitsRender.Preview(model); model.DetailTask()!.Wait(); CommitsRender.Preview(model);
            Equal(1, details, "Detail load count for the shown commit"); Equal(0, diffs, "Showing a commit loaded a file diff");
            model.Enter(); model.Enter(); CommitsRender.Preview(model); model.DiffTask()!.Wait(); var lines = CommitsRender.Preview(model);
            Equal(1, details, "Drill-down reloaded the detail"); Equal(1, diffs, "Explicit diff load count");
            Check(lines.Any(l => l.Text.Contains("old: parent 8175ff1") && l.Text.Contains("new: a928430")), "Commit sides missing");
        });
        Test("ignore picker keeps project separators and accepts only checked destinations", () => {
            var a = Demo.Snapshot().Worktrees[0]; var b = Demo.Snapshot().Worktrees[1];
            var surface = new ScriptedSurface(120, 32, new(ConsoleKey.Spacebar), new(ConsoleKey.DownArrow), new(ConsoleKey.Spacebar), new(ConsoleKey.Enter));
            var selected = new Dialogs(surface).IgnoreTargets([new(a.Project, Tree: a), new(b.Project, Tree: b)], [a.Id]);
            Equal(b.Id, selected!.Single().Id, "Wrong checked destination");
            Check(surface.Frames[0].Contains("AccessControl") && surface.Frames[0].Contains("Joiners"), "Project separators missing");
        });
    }
}
