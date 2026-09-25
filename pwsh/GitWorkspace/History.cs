namespace GitWorkspace;

public static class History
{
    public static List<BranchInfo> Branches(string project, string common, bool includeRemote = false)
    {
        List<string> args = ["for-each-ref", "--sort=refname", "--format=%(refname)%00%(objectname)%00%(subject)%00%(upstream:short)", "refs/heads/"];
        if (includeRemote) args.Add("refs/remotes/");
        var output = Git.RunInput(common, args).Require().Output;
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\0'))
            .Where(p => p.Length == 4 && !p[0].EndsWith("/HEAD", StringComparison.Ordinal))
            .Select(p => new BranchInfo(project, common, p[0], p[1], p[2], p[3].Length == 0 ? null : p[3])).ToList();
    }
    // Resolve once to a commit ID so paging stays stable while another worktree advances the branch.
    public static string Resolve(string directory, string revision) => Git.Run(directory, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}").Require().Output.Trim();
    public static List<CommitInfo> Read(string directory, string tip, int skip = 0, int count = 80)
    {
        var oid = Resolve(directory, tip);
        string output = Git.Run(directory, "log", "--date=short", "--topo-order", $"--skip={skip}", $"--max-count={count}",
            "--format=%H%x00%h%x00%an%x00%ad%x00%s%x00%P", oid, "--").Require().Output;
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\0'))
            .Where(p => p.Length == 6).Select(p => new CommitInfo(p[0], p[1], p[2], p[3], p[4], p[5].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList())).ToList();
    }
    public static string Detail(string directory, CommitInfo commit, int parent = 0)
    {
        string header = Git.Run(directory, "show", "--no-patch", "--format=fuller", commit.Hash, "--").Require().Output;
        // Always show a merge against the explicitly selected parent, rather than
        // Git's combined diff which can hide files resolved unchanged from one side.
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--stat", "--patch", "--find-renames" };
        GitResult patch;
        if (commit.Parents.Count == 0) patch = Git.Run(directory, "show", "--format=", "--root", "--no-ext-diff", "--no-textconv", "--no-color", "--stat", "--patch", commit.Hash, "--");
        else {
            if (parent < 0 || parent >= commit.Parents.Count) throw new ArgumentOutOfRangeException(nameof(parent));
            args.Add(commit.Parents[parent]); args.Add(commit.Hash); args.Add("--"); patch = Git.RunInput(directory, args);
        }
        return header + (commit.Parents.Count > 1 ? $"\nMerge diff against parent {parent + 1}: {commit.Parents[parent]}\n" : "\n") + patch.Require().Output;
    }
}
