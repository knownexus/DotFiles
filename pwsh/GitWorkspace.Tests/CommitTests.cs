using GitWorkspace;

static partial class Tests
{
    static string MergeRepo()
    {
        var repo = Repo(); string initial = History.Resolve(repo, "HEAD"); CommitFile(repo, "Main");
        Git.Run(repo, "switch", "-c", "side", initial).Require(); CommitFile(repo, "Side");
        Git.Run(repo, "switch", "main").Require(); Git.Run(repo, "merge", "--no-ff", "-m", "Merge side", "side").Require();
        Git.Run(repo, "tag", "v1").Require(); return repo;
    }
    static void CommitTests()
    {
        Test("commit graph keeps connector lines, refs and root parents", () => {
            var repo = MergeRepo(); var rows = CommitGraph.Read(repo, History.Resolve(repo, "HEAD"), new(), null);
            var commits = rows.Where(r => r.Commit is not null).ToList();
            Equal(4, commits.Count, "Commits"); Check(rows.Count > commits.Count, "Connector rows kept");
            Check(commits[0].Refs.Contains("tag: v1") && commits[0].Refs.Any(r => r.Contains("main")), "Refs: " + string.Join(",", commits[0].Refs));
            Check(commits[0].Graph.Contains('*'), "Graph prefix"); Equal(2, commits[0].Commit!.Parents.Count, "Merge parents");
            Equal(0, commits[^1].Commit!.Parents.Count, "Root commit");
        });
        Test("commit search is literal, case-insensitive and combines author and path", () => {
            Equal("--author=phil|--grep=fix(|-i|--fixed-strings|--all-match|--|:(literal)-odd [x].txt",
                string.Join("|", CommitGraph.SearchArguments("@phil fix( \":-odd [x].txt\"")), "Arguments");
            var repo = MergeRepo(); Git.Run(repo, "commit", "--allow-empty", "-m", "Fix [wip] (scanner)").Require();
            string tip = History.Resolve(repo, "HEAD");
            Equal("Fix [wip] (scanner)", CommitGraph.Read(repo, tip, new("[wip] (scan"), null).Single(r => r.Commit is not null).Commit!.Subject, "Metacharacters literal");
            Equal(5, CommitGraph.Read(repo, tip, new("\"@workspace test\""), null).Count(r => r.Commit is not null), "Quoted author, case-insensitive");
            var byPath = CommitGraph.Read(repo, tip, new(":Side.txt"), null).Where(r => r.Commit is not null).Select(r => r.Commit!.Subject).ToList();
            Check(byPath.Contains("Side") && !byPath.Contains("Main"), "Path filter: " + string.Join(",", byPath));
            Equal(0, CommitGraph.Read(repo, tip, new("no such message"), null).Count, "No match is empty, not an error");
        });
        Test("unpushed markers and filters use the upstream range", () => {
            var repo = Repo(); var remote = Path.Combine(root, "remote " + ++sequence + ".git"); Git.Run(root, "init", "--bare", remote).Require();
            Git.Run(repo, "remote", "add", "origin", remote).Require(); Git.Run(repo, "push", "-u", "origin", "main").Require();
            CommitFile(repo, "Local"); string tip = History.Resolve(repo, "HEAD");
            var rows = CommitGraph.Read(repo, tip, new(), "origin/main").Where(r => r.Commit is not null).ToList();
            Check(rows[0].Unpushed && !rows[1].Unpushed, "Only the local commit is unpushed");
            Equal(1, CommitGraph.Read(repo, tip, new(UnpushedOnly: true), "origin/main").Count(r => r.Commit is not null), "Unpushed only");
            Equal(1, CommitGraph.Read(repo, tip, new(Base: "origin/main"), null).Count(r => r.Commit is not null), "Since base");
            Equal(2, CommitGraph.Read(repo, tip, new(UnpushedOnly: true), null).Count(r => r.Commit is not null), "No upstream shows everything");
        });
        Test("commit files list renames, root additions and each merge parent", () => {
            var repo = MergeRepo(); string tip = History.Resolve(repo, "HEAD");
            var commits = CommitGraph.Read(repo, tip, new(), null).Where(r => r.Commit is not null).Select(r => r.Commit!).ToList();
            Equal("Side.txt", CommitGraph.Files(repo, commits[0], 0).Single().Path, "First parent adds Side");
            Equal("Main.txt", CommitGraph.Files(repo, commits[0], 1).Single().Path, "Second parent adds Main");
            var root = CommitGraph.Files(repo, commits[^1]); Equal("A", root.Single().Status, "Root adds"); Equal(40, root.Single().Added, "Root line count");
            Git.Run(repo, "mv", "file.txt", "renamed file.txt").Require(); Git.Run(repo, "commit", "-m", "Rename").Require();
            var rename = CommitGraph.Read(repo, History.Resolve(repo, "HEAD"), new(), null).First(r => r.Commit is not null).Commit!;
            var file = CommitGraph.Files(repo, rename).Single(); Equal("R", file.Status, "Rename status"); Equal("file.txt", file.OldPath, "Old path"); Equal("renamed file.txt", file.Path, "New path");
            var parsed = DiffView.Parse(CommitGraph.FileDiff(repo, rename, 0, file));
            Equal("file.txt → renamed file.txt", parsed.First(l => l.Kind == DiffKind.Header).Text, "Rename diff header");
            var rootDiff = DiffView.Parse(CommitGraph.FileDiff(repo, commits[^1], 0, root.Single())); Equal(40, rootDiff.Count(l => l.Kind == DiffKind.Added), "Root diff all added");
            var detail = CommitGraph.Detail(repo, commits[0]); Check(detail.Header.Contains("Merge side") && detail.Header.Contains("Workspace Test"), "Header");
        });
        Test("commits model skips connector rows, loads more and drills into files", () => {
            var repo = MergeRepo(); for (int i = 0; i < 5; i++) CommitFile(repo, "Extra" + i);
            var model = new CommitsModel(new("Test", repo, "HEAD", "Test"), (tip, filter, skip, count) => CommitGraph.Read(repo, tip, filter, null, skip, count),
                (c, p) => CommitGraph.Detail(repo, c, p), (c, p, f) => CommitGraph.FileDiff(repo, c, p, f), rev => History.Resolve(repo, rev), batch: 4);
            model.Load(); Check(model.Commit is not null, "Focus on a commit");
            for (int i = 0; i < 12; i++) { model.Move(1); model.EnsureLoaded(3); Check(model.Current?.Commit is not null, "Never focus a connector"); }
            Check(model.Rows.Count(r => r.Commit is not null) > 4, "Loaded a second batch"); Equal(9, model.Rows.Count(r => r.Commit is not null), "All commits once");
            CommitFile(repo, "Later"); model.Move(1); model.EnsureLoaded(3); Equal(9, model.Rows.Count(r => r.Commit is not null), "Pinned tip");
            model.Focus = model.Rows.FindIndex(r => r.Commit?.Subject == "Merge side"); model.Enter(); Equal(CommitLevel.Files, model.Level, "Files level");
            Equal("Side.txt", model.DetailTask()!.GetAwaiter().GetResult().Files.Single().Path, "Detail loaded");
            model.NextParent(); Equal(1, model.Parent, "Second parent"); Equal("Main.txt", model.DetailTask()!.GetAwaiter().GetResult().Files.Single().Path, "Parent switched");
            model.Enter(); Equal(CommitLevel.FileDiff, model.Level, "Diff level"); Check(model.DiffTask()!.GetAwaiter().GetResult().Contains("+Main"), "File diff");
            Check(model.Back() && model.Level == CommitLevel.Files, "Back to files"); Check(model.Back() && model.Level == CommitLevel.Commits, "Back to commits"); Check(!model.Back(), "Already at top");
        });
        Test("commits model retries after a faulted detail task instead of caching the failure", () => {
            var repo = MergeRepo(); int attempts = 0;
            var model = new CommitsModel(new("Test", repo, "HEAD", "Test"), (tip, filter, skip, count) => CommitGraph.Read(repo, tip, filter, null, skip, count),
                (c, p) => { attempts++; if (attempts == 1) throw new InvalidOperationException("transient failure"); return CommitGraph.Detail(repo, c, p); },
                (c, p, f) => CommitGraph.FileDiff(repo, c, p, f), rev => History.Resolve(repo, rev));
            model.Load(); model.Focus = model.Rows.FindIndex(r => r.Commit?.Subject == "Merge side");
            var first = model.DetailTask()!; Throws(() => first.GetAwaiter().GetResult(), "First attempt should fault");
            Check(first.IsFaulted, "First task recorded as faulted");
            var second = model.DetailTask()!; Check(!ReferenceEquals(first, second), "Faulted task not reused");
            Equal("Side.txt", second.GetAwaiter().GetResult().Files.Single().Path, "Second attempt succeeds");
            model.Enter(); Equal(CommitLevel.Files, model.Level, "Files level"); model.Enter(); Equal(CommitLevel.FileDiff, model.Level, "Diff level");
            model.NextParent(); Equal(CommitLevel.Files, model.Level, "Next parent drops out of file diff");
        });
        Test("commits model reports an unborn branch and an empty search without throwing", () => {
            var empty = Repo(initial: false); var model = CommitsModel.ForGit(new("Empty", empty, "HEAD", "Empty")); model.Load();
            Equal("No commits yet on this branch.", model.Empty, "Unborn"); Check(model.Commit is null && model.Rows.Count == 0, "No rows");
            model.Move(1); model.Enter(); Equal(CommitLevel.Commits, model.Level, "Enter does nothing");
            var repo = Repo(); var searched = CommitsModel.ForGit(new("Test", repo, "HEAD", "Test")); searched.Load(); searched.SetFilter(new("zzz-not-found"));
            Equal("No commits match this search or filter.", searched.Empty, "Empty search");
        });
        Test("commits view survives a base that git cannot read and renders the error", () => {
            var repo = MergeRepo(); var model = CommitsModel.ForGit(new("Test", repo, "HEAD", "Test")); model.Load();
            model.SetFilter(new(Base: "nope")); Check(model.Empty?.StartsWith("Unable to read commits:") == true, "Empty: " + model.Empty); Check(model.Exhausted, "Exhausted");
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot()); view.Mode = ViewMode.Commits;
            string text = Screen.Render(view, Layout.For(120, 32, 0.5), new HitMap(), [], 0, "", false, (c, l, h) => CommitsRender.Body(c, l, h, model, 0)).Plain();
            Check(text.Contains("Unable to read commits"), "Error rendered");
            // A later page failing from the render path stops paging instead of throwing out of the frame.
            int calls = 0;
            var paging = new CommitsModel(new("Test", repo, "HEAD", "Test"), (tip, filter, skip, count) => ++calls == 1 ? CommitGraph.Read(repo, tip, filter, null, skip, count) : throw new InvalidOperationException("fatal: bad range"),
                (c, p) => CommitGraph.Detail(repo, c, p), (c, p, f) => CommitGraph.FileDiff(repo, c, p, f), rev => History.Resolve(repo, rev), batch: 2);
            paging.Load(); Check(paging.Empty is null, "First page fine");
            CommitsRender.List(new Canvas(120, 40), new Rect(0, 0, 120, 30), new HitMap(), paging);
            Equal("Unable to read commits: fatal: bad range", paging.Empty, "Later page error"); Check(paging.Exhausted, "Paging stopped");
        });
        Test("commits detail loads wait for the focus to settle and run one at a time", () => {
            var repo = MergeRepo(); for (int i = 0; i < 6; i++) CommitFile(repo, "Extra" + i);
            int started = 0; using var gate = new ManualResetEventSlim(false); var now = new DateTime(2026, 1, 1);
            var model = new CommitsModel(new("Test", repo, "HEAD", "Test"), (tip, filter, skip, count) => CommitGraph.Read(repo, tip, filter, null, skip, count),
                (c, p) => { Interlocked.Increment(ref started); gate.Wait(); return new CommitDetail("commit " + c.Hash, []); },
                (c, p, f) => "", rev => History.Resolve(repo, rev)) { Clock = () => now };
            model.Load();
            for (int i = 0; i < 8; i++) { CommitsRender.Preview(model); model.Move(1); now = now.AddMilliseconds(20); }
            CommitsRender.Preview(model); Equal(0, started, "No load while moving");
            now = now.AddMilliseconds(200); CommitsRender.Preview(model); SpinWait.SpinUntil(() => Volatile.Read(ref started) == 1, 2000); Equal(1, started, "One load once settled");
            var first = model.DetailTask()!;
            model.Move(-1); CommitsRender.Preview(model); now = now.AddMilliseconds(200); CommitsRender.Preview(model);
            Thread.Sleep(50); Equal(1, started, "Still one while the first runs");
            gate.Set(); first.Wait();
            CommitsRender.Preview(model); SpinWait.SpinUntil(() => Volatile.Read(ref started) == 2, 2000); Equal(2, started, "Latest focus loads next");
            model.DetailTask()!.Wait(); var ready = CommitsRender.Preview(model);
            Check(ready.Any(l => l.Text.StartsWith("commit ")), "Detail shown");
            Check(ReferenceEquals(ready, CommitsRender.Preview(model)), "Preview lines reused between frames");
        });
        Test("since base defaults to the upstream, then main, then master, else empty", () => {
            var repo = Repo(); Equal("origin/x", History.DefaultBase(repo, "origin/x"), "Upstream"); Equal("main", History.DefaultBase(repo, null), "main");
            Git.Run(repo, "branch", "-m", "main", "master").Require(); Equal("master", History.DefaultBase(repo, null), "master only");
            Git.Run(repo, "branch", "-m", "master", "trunk").Require(); Equal("", History.DefaultBase(repo, null), "Neither");
        });
        Test("branches drop an upstream whose remote-tracking ref is gone", () => {
            var repo = Repo(); var remote = Path.Combine(root, "remote " + ++sequence + ".git"); Git.Run(root, "init", "--bare", remote).Require();
            Git.Run(repo, "remote", "add", "origin", remote).Require(); Git.Run(repo, "push", "-u", "origin", "main").Require();
            string common = Git.ResolveRepository(repo);
            Equal("origin/main", History.Branches("Test", common).Single(b => b.Name == "main").Upstream, "Upstream present");
            Git.Run(repo, "update-ref", "-d", "refs/remotes/origin/main").Require();
            Equal(null, History.Branches("Test", common).Single(b => b.Name == "main").Upstream, "Gone upstream dropped");
        });
        Test("commits select after a merge parent switch resets the parent and file focus", () => {
            var repo = MergeRepo();
            var model = CommitsModel.ForGit(new("Test", repo, "HEAD", "Test")); model.Load();
            model.Focus = model.Rows.FindIndex(r => r.Commit?.Subject == "Merge side"); model.NextParent(); model.Enter(); model.FileFocus = 3;
            Equal(1, model.Parent, "Second parent chosen");
            int single = model.Rows.FindIndex(r => r.Commit is { Parents.Count: 1 }); model.Select(single);
            Equal(0, model.Parent, "Parent reset"); Equal(0, model.FileFocus, "File focus reset"); Equal(CommitLevel.Commits, model.Level, "Back at the commit level");
            var detail = model.DetailTask()!; detail.Wait(); Check(detail.IsCompletedSuccessfully, "Detail after select");
            var lines = CommitsRender.Preview(model); Check(!lines.Any(l => l.Ink == Ink.Bad), "Preview error: " + string.Join(" | ", lines.Select(l => l.Text)));
            model.Select(0); Check(model.Commit is not null, "Home lands on a commit row");
        });
        Test("changing group outside commits resets the commits subject for the next visit", () => {
            var settings = Demo.Settings(); var app = new App(settings, Path.Combine(root, "scope-" + Guid.NewGuid().ToString("N") + ".json"), demo: true);
            app.TestView.Update(Demo.Snapshot());
            void FocusTree(string project) => app.TestView.Focus = app.TestView.Rows.FindIndex(r => r.Kind == "worktree" && r.Project == project);
            FocusTree("AccessControl"); app.TestSetMode(ViewMode.Commits); Equal("AccessControl", app.TestCommits?.Target.Project, "First subject");
            app.TestSetMode(ViewMode.Changes);
            app.SelectTab(settings.Groups.FindIndex(g => g.Name == "Maintenance") + 1); Check(app.TestCommits is null, "Group change dropped the old model");
            FocusTree("mobileApp"); app.TestSetMode(ViewMode.Commits);
            Equal("mobileApp", app.TestCommits?.Target.Project, "Subject follows the new group"); Equal(ViewMode.Commits, app.TestView.Mode, "In Commits");
        });
        Test("commits view renders graph rows, refs and the drill-down preview at every width", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot()); view.Mode = ViewMode.Commits;
            var model = Demo.CommitsModel(); model.Load(); model.DetailTask()!.Wait();
            foreach (var width in new[] { 80, 120, 160 }) {
                var hits = new HitMap(); var layout = Layout.For(width, 32, 0.5);
                string text = Screen.Render(view, layout, hits, [], 0, "", false, (c, l, h) => CommitsRender.Body(c, l, h, model, 0)).Plain();
                Check(text.Contains("[Commits]") && text.Contains("d3a721f") && text.Contains("Validate signed QR"), $"List at {width}");
                if (width >= 100) Check(text.Contains("Author") || text.Contains("Phillip"), $"Preview at {width}");
                Equal("row", hits.At(4, layout.List.Y)?.Kind, $"Row hit at {width}");
            }
            Check(CommitsRender.Preview(model).Any(l => l.Text.Contains("src/EntryService.cs")), "Files in preview");
            model.Enter(); model.Enter(); model.DiffTask()!.Wait(); var diff = CommitsRender.Preview(model);
            Check(diff.Any(l => l.Text.StartsWith("− old: parent a928430")), "Commit legend names the parent");
            var keys = string.Join(",", Footer.For(view, model).Select(k => k.Key)); Check(keys.Contains("d") && !keys.Contains("s"), "Diff-level keys: " + keys);
            model.Back(); model.Back(); keys = string.Join(",", Footer.For(view, model).Select(k => k.Key)); Check(keys.StartsWith("\r,/,f,b,i,v,y,z,Y,M"), "Commit keys: " + keys);
        });
    }
}
