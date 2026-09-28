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
    }
}
