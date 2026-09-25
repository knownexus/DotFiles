using System.Diagnostics;
using System.Text;

namespace GitWorkspace;

public static class Git
{
    public static ProcessStartInfo StartInfo(string path, IEnumerable<string> args, bool capture = true)
    {
        var arguments = args.ToArray();
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false,
            RedirectStandardOutput = capture, RedirectStandardError = capture, RedirectStandardInput = capture,
            CreateNoWindow = capture };
        if (capture) { start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8; start.StandardInputEncoding = new UTF8Encoding(false); }
        // A target always means this checkout, even if the launching shell inherited Git internals.
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_PREFIX", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES" })
            start.Environment.Remove(name);
        if (capture) { start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GIT_EDITOR"] = "true"; start.Environment["GIT_SEQUENCE_EDITOR"] = "true"; }
        start.ArgumentList.Add("--no-pager");
        start.ArgumentList.Add("--no-optional-locks");
        // Apply literal path handling only to commands that consume selected files.
        // A global literal flag also reaches stash's internal clean and prevents it
        // removing saved untracked files on Git for Windows 2.53.
        if (arguments.FirstOrDefault() is "add" or "restore" or "rm" or "diff" or "difftool" or "mergetool")
            start.ArgumentList.Add("--literal-pathspecs");
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        return start;
    }
    public static GitResult Run(string path, params string[] args) => RunInput(path, args);
    public static GitResult RunInput(string path, IEnumerable<string> args, string? input = null, int timeoutSeconds = 120)
    {
        try
        {
            using var process = Process.Start(StartInfo(path, args))!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            var write = Task.Run(async () => {
                try { if (input is not null) await process.StandardInput.WriteAsync(input); }
                catch (IOException) { }
                finally { process.StandardInput.Close(); }
            });
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                process.Kill(true); process.WaitForExit();
                Task.WhenAll(output, error, write).GetAwaiter().GetResult();
                return new(-1, output.Result, "Operation timed out. Inspect refreshed state before retrying; a remote may already have accepted changes.\n" + error.Result);
            }
            Task.WhenAll(output, error, write).GetAwaiter().GetResult();
            return new(process.ExitCode, output.Result, error.Result);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return new(-1, "", e.Message); }
    }
    public static string ResolveRepository(string projectPath)
    {
        foreach (var candidate in new[] { projectPath, System.IO.Path.Combine(projectPath, ".git-main"), System.IO.Path.Combine(projectPath, ".bare") })
        {
            if (!Directory.Exists(candidate)) continue;
            var result = Run(candidate, "rev-parse", "--path-format=absolute", "--git-common-dir");
            if (result.Ok) return result.Output.TrimEnd('\r', '\n');
        }
        throw new InvalidOperationException($"No accessible repository at {projectPath} (including .git-main and .bare).");
    }
    public static Snapshot Scan(Settings settings)
    {
        var trees = new List<Worktree>(); var branches = new List<BranchInfo>(); var errors = new List<string>(); var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var project in settings.Projects)
        {
            try
            {
                var common = ResolveRepository(project.Path);
                if (!seen.Add(common)) { errors.Add($"{project.Name}: repository already registered; remove the duplicate project entry."); continue; }
                var result = Run(common, "worktree", "list", "--porcelain", "-z").Require();
                foreach (var tree in ParseWorktrees(result.Output, project, common))
                {
                    LoadStatus(tree); trees.Add(tree);
                }
                branches.AddRange(History.Branches(project.Name, common));
            }
            catch (Exception e) { errors.Add($"{project.Name}: {e.Message}"); }
        }
        return new(trees, errors, DateTime.Now) { Branches = branches };
    }
    public static List<Worktree> ParseWorktrees(string output, Project project, string common)
    {
        List<Worktree> trees = []; Worktree? current = null; bool bare = false;
        foreach (var field in output.Split('\0'))
        {
            if (field.StartsWith("worktree ")) { current = new() { Project = project.Name, ProjectRoot = project.Path, CommonDir = common, Path = field[9..] }; bare = false; }
            else if (current is not null)
            {
                if (field == "bare") bare = true;
                else if (field.StartsWith("branch ")) current.Branch = field[7..].Replace("refs/heads/", "");
                else if (field.StartsWith("HEAD ")) current.Head = field[5..];
                else if (field.StartsWith("locked")) current.Locked = field.Length > 7 ? field[7..] : "Locked";
                else if (field.StartsWith("prunable")) current.Prunable = field.Length > 9 ? field[9..] : "Stale registration";
                else if (field.Length == 0) { if (!bare) trees.Add(current); current = null; }
            }
        }
        if (current is not null && !bare) trees.Add(current);
        return trees;
    }
    public static void LoadStatus(Worktree tree)
    {
        if (!Directory.Exists(tree.Path)) { tree.Error = "Missing worktree" + (tree.Prunable is null ? "" : " · stale registration"); return; }
        var result = Run(tree.Path, "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all");
        if (!result.Ok) { tree.Error = result.Detail; return; }
        ParseStatus(result.Output, tree);
        if (tree.Upstream is not null) tree.UpstreamGone = !Run(tree.Path, "rev-parse", "--verify", "--quiet", "@{upstream}").Ok;
        var metadata = Run(tree.Path, "rev-parse", "--path-format=absolute", "--git-dir");
        if (metadata.Ok)
        {
            string dir = metadata.Output.TrimEnd('\r', '\n');
            tree.Operation = Directory.Exists(System.IO.Path.Combine(dir, "rebase-merge")) || Directory.Exists(System.IO.Path.Combine(dir, "rebase-apply")) ? "Rebase in progress"
                : File.Exists(System.IO.Path.Combine(dir, "MERGE_HEAD")) ? "Merge in progress"
                : File.Exists(System.IO.Path.Combine(dir, "REVERT_HEAD")) ? "Revert in progress"
                : File.Exists(System.IO.Path.Combine(dir, "CHERRY_PICK_HEAD")) ? "Cherry-pick in progress" : "";
            tree.RebasingBranch = null;
            if (tree.Operation.StartsWith("Rebase", StringComparison.Ordinal)) {
                var headName = new[] { "rebase-merge", "rebase-apply" }.Select(folder => System.IO.Path.Combine(dir, folder, "head-name")).FirstOrDefault(File.Exists);
                if (headName is not null) { var name = File.ReadAllText(headName).Trim(); if (name.StartsWith("refs/heads/", StringComparison.Ordinal)) tree.RebasingBranch = name; }
            }
        }
        var fetch = System.IO.Path.Combine(tree.CommonDir, "FETCH_HEAD");
        if (File.Exists(fetch)) tree.FetchedAt = File.GetLastWriteTime(fetch);
    }
    public static void ParseStatus(string output, Worktree tree)
    {
        tree.Changes.Clear(); tree.Upstream = null; tree.Ahead = tree.Behind = 0;
        var records = output.Split('\0');
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.StartsWith("# branch.head ")) tree.Branch = record[14..];
            else if (record.StartsWith("# branch.oid ")) tree.Head = record[13..];
            else if (record.StartsWith("# branch.upstream ")) tree.Upstream = record[18..];
            else if (record.StartsWith("# branch.ab ")) {
                var counts = record[12..].Split(' '); tree.Ahead = int.Parse(counts[0][1..]); tree.Behind = int.Parse(counts[1][1..]);
            }
            else if (record.StartsWith("? ")) tree.Changes.Add(new(record[2..], null, "??", ChangeArea.Untracked));
            else if (record.StartsWith("1 ") || record.StartsWith("2 ") || record.StartsWith("u "))
            {
                var parts = record.Split(' ', record[0] == '1' ? 9 : record[0] == '2' ? 10 : 11);
                var path = parts[^1]; var xy = parts[1]; var original = record[0] == '2' ? records[++i] : null;
                if (record[0] == 'u') tree.Changes.Add(new(path, original, xy, ChangeArea.Conflict, parts[2]));
                else {
                    if (xy[0] != '.') tree.Changes.Add(new(path, original, xy, ChangeArea.Staged, parts[2]));
                    if (xy[1] != '.' || (parts[2].StartsWith('S') && parts[2] != "S...")) tree.Changes.Add(new(path, original, xy, ChangeArea.Unstaged, parts[2]));
                }
            }
        }
    }
    public static string Diff(Worktree tree, Change change)
    {
        if (change.Area == ChangeArea.Untracked)
        {
            var path = System.IO.Path.Combine(tree.Path, change.Path);
            try {
                if (new FileInfo(path).Length > 512_000) return "Untracked file exceeds preview limit. Open in editor to review.";
                var bytes = File.ReadAllBytes(path);
                if (bytes.Contains((byte)0)) return "Binary untracked file.";
                return "Untracked · " + change.Path + "\n\n" + Encoding.UTF8.GetString(bytes);
            } catch (IOException e) { return e.Message; }
        }
        List<string> args = ["diff", "--no-ext-diff", "--no-textconv", "--no-color"];
        if (change.Area == ChangeArea.Staged) args.Add("--cached");
        args.Add("--"); args.AddRange(change.Paths);
        var result = RunInput(tree.Path, args);
        return result.Ok ? result.Output.Length == 0 ? "No textual diff (mode change, submodule or recently updated file)." : result.Output : result.Detail;
    }
    public static string Fingerprint(Worktree tree) => Run(tree.Path, "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all").Require().Output;
    public static string IndexTree(Worktree tree) => Run(tree.Path, "write-tree").Require().Output.Trim();
    public static string Head(Worktree tree) => Run(tree.Path, "rev-parse", "--verify", "HEAD").Output.Trim();
}
