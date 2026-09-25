using System.Text;
using GitWorkspace;

static partial class Tests
{
    static void IgnoreTests()
    {
        Test("ignore selected files escapes literal names and preserves existing bytes and index", () => {
            var repo = Repo(); string ignorePath = Path.Combine(repo, ".gitignore");
            File.WriteAllBytes(ignorePath, new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("# existing\r\nold-cache/\r\n")).ToArray());
            Git.Run(repo, "add", ".gitignore").Require(); string index = Git.Run(repo, "show", ":.gitignore").Require().Output;
            File.AppendAllText(ignorePath, "# local edit without newline", new UTF8Encoding(false)); var before = File.ReadAllBytes(ignorePath);
            string[] names = ["literal[1].log", "space ü.txt", "#note.txt", "!keep.txt"];
            foreach (var name in names) Write(repo, name, "keep contents\n");
            Write(repo, "literal1.log", "also keep\n"); Directory.CreateDirectory(Path.Combine(repo, "sub")); Write(repo, "sub/literal[1].log", "nested\n");
            var tree = Tree(repo); Success(Ignore.Files(tree, tree.Changes.Where(c => names.Contains(c.Path))));
            Check(File.ReadAllBytes(ignorePath).Take(before.Length).SequenceEqual(before), "Existing ignore bytes changed");
            Check(File.ReadAllText(ignorePath).Contains("\r\n/literal\\[1\\].log\r\n"), "Line endings changed");
            Equal(index, Git.Run(repo, "show", ":.gitignore").Require().Output, "Staged ignore changed");
            foreach (var name in names) { Check(Git.Run(repo, "check-ignore", "--", name).Ok, "Not ignored: " + name); Check(File.Exists(Path.Combine(repo, name)), "Ignored file deleted"); }
            Check(!Git.Run(repo, "check-ignore", "--", "literal1.log", "sub/literal[1].log").Ok, "Literal rule hid unrelated file");
        });
        Test("custom ignore rules respect selected worktree, last-rule precedence and tracked files", () => {
            var repo = Repo(); Write(repo, "tracked.log", "tracked\n"); Git.Run(repo, "add", "tracked.log").Require(); Git.Run(repo, "commit", "-m", "Tracked log").Require();
            var linked = Path.Combine(root, "ignore other worktree"); Git.Run(repo, "worktree", "add", "-b", "other", linked).Require();
            Write(repo, ".gitignore", "*.log\n!keep.log\n"); Write(repo, "keep.log", "untracked\n"); Write(repo, "tracked.log", "changed\n");
            var tree = Tree(repo); string head = Git.Head(tree); Success(Ignore.Patterns(tree, ["*.log"]));
            Check(Git.Run(repo, "check-ignore", "--", "keep.log").Ok, "Earlier negation still won");
            Check(Tree(repo).Changes.Any(c => c.Path == "tracked.log" && c.Area == ChangeArea.Unstaged), "Tracked file hidden");
            Check(!File.Exists(Path.Combine(linked, ".gitignore")), "Other worktree edited"); Equal(head, Git.Head(Tree(repo)), "Commit changed");
            string contents = File.ReadAllText(Path.Combine(repo, ".gitignore")); Success(Ignore.Patterns(Tree(repo), ["*.log"])); Equal(contents, File.ReadAllText(Path.Combine(repo, ".gitignore")), "Duplicate final rule appended");
            Throws(() => Ignore.Files(Tree(repo), [Tree(repo).Changes.Single(c => c.Path == "tracked.log")]), "Tracked file accepted as untracked");
        });
        Test("ignore plans reject stale edits and report nested overrides", () => {
            var repo = Repo(); Write(repo, ".gitignore", "# before\n"); var plan = Ignore.Patterns(Tree(repo), ["*.tmp"]);
            Write(repo, ".gitignore", "# external edit\n"); Check(!plan.Run().Success, "Concurrent edit overwritten"); Equal("# external edit\n", File.ReadAllText(Path.Combine(repo, ".gitignore")), "Lost external edit");
            Directory.CreateDirectory(Path.Combine(repo, "nested")); Write(repo, "nested/.gitignore", "!keep.log\n"); Write(repo, "nested/keep.log", "keep\n");
            var tree = Tree(repo); var result = Ignore.Files(tree, tree.Changes.Where(c => c.Path == "nested/keep.log")).Run();
            Check(result.Success && result.Detail.Contains("still not ignored") && result.Detail.Contains("nested/keep.log"), "Nested override not reported");
            Throws(() => Ignore.Patterns(Tree(repo), ["*.log\n*.tmp"]), "Multiline pattern accepted");
        });
    }
}
