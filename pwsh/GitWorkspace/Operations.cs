using System.Security.Cryptography;
using System.Text;

namespace GitWorkspace;

public sealed class Plan
{
    public required Worktree Tree { get; init; }
    public required string Action { get; init; }
    public required string Preview { get; init; }
    public required Func<GitResult> Execute { get; init; }
    public Func<string>? State { get; init; }
    public string Expected { get; init; } = "";
    public OperationResult Run()
    {
        try
        {
            if (State is not null && State() != Expected)
                throw new InvalidOperationException("The checkout changed after this action was prepared. Refresh and review it again.");
            var result = Execute();
            return new(Tree.Title, Action, result.Ok, result.Detail.Length > 0 ? result.Detail : "Completed", DateTime.Now);
        }
        catch (Exception e) { return new(Tree.Title, Action, false, e.Message, DateTime.Now); }
    }
}

public static class Operations
{
    public static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string ChangeStamp(Worktree tree, Change change)
    {
        if (change.Area == ChangeArea.Untracked)
        {
            try { using var stream = File.OpenRead(System.IO.Path.Combine(tree.Path, change.Path)); return Convert.ToHexString(SHA256.HashData(stream)); }
            catch (IOException e) { return e.Message; }
        }
        return Digest(Git.Diff(tree, change));
    }
    static string CheckoutStamp(Worktree tree) => Git.Head(tree) + "\0" + Git.Fingerprint(tree);
    static void Ready(Worktree tree, bool clean = false)
    {
        if (!tree.Usable) throw new InvalidOperationException(tree.Error.Length > 0 ? tree.Error : "Worktree is unavailable.");
        if (tree.Operation.Length > 0) throw new InvalidOperationException($"{tree.Operation}. Continue or abort it first.");
        if (clean && tree.Changes.Count > 0) throw new InvalidOperationException("This action requires a clean worktree. Commit or stash its changes first.");
        if (tree.Changes.Any(c => c.Area == ChangeArea.Conflict)) throw new InvalidOperationException("Resolve conflicts before starting this action.");
    }
    static Plan Guarded(Worktree tree, string action, string preview, Func<GitResult> execute, Func<string>? state = null)
    {
        state ??= () => CheckoutStamp(tree);
        return new() { Tree = tree, Action = action, Preview = preview, Execute = execute, State = state, Expected = state() };
    }
    public static Plan Files(Worktree tree, string action, IEnumerable<Change> selection)
    {
        if (!tree.Usable) throw new InvalidOperationException(tree.Error);
        var changes = selection.ToList();
        if (changes.Count == 0) throw new InvalidOperationException("Select at least one applicable file.");
        if (changes.Any(c => c.Submodule.StartsWith('S')))
            throw new InvalidOperationException("Submodule changes need to be managed inside the submodule. Register it as a project to inspect it separately.");
        var paths = changes.SelectMany(c => c.Paths).Distinct().ToArray();
        var pathInput = string.Join('\0', paths) + '\0';
        var tail = new[] { "--pathspec-from-file=-", "--pathspec-file-nul" };
        string State() => CheckoutStamp(tree) + string.Join('\0', changes.Select(c => ChangeStamp(tree, c)));
        return Guarded(tree, action, string.Join('\n', changes.Select(c => $"{c.Label}: {c.Path}")), () => {
            switch (action)
            {
                case "Stage": return Git.RunInput(tree.Path, new[] { "add" }.Concat(tail), pathInput);
                case "Unstage":
                    return Git.Head(tree).Length > 0
                        ? Git.RunInput(tree.Path, new[] { "restore", "--staged" }.Concat(tail), pathInput)
                        : Git.RunInput(tree.Path, new[] { "rm", "--cached", "-r", "--ignore-unmatch" }.Concat(tail), pathInput);
                case "Discard unstaged":
                    if (changes.Any(c => c.Area != ChangeArea.Unstaged)) return new(1, "", "Discard only accepts tracked unstaged changes. Unstage first or use Delete untracked.");
                    return Git.RunInput(tree.Path, new[] { "restore", "--worktree" }.Concat(tail), pathInput);
                case "Delete untracked":
                    if (changes.Any(c => c.Area != ChangeArea.Untracked)) return new(1, "", "Only untracked files can be deleted here.");
                    var root = System.IO.Path.GetFullPath(tree.Path).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                    // Literal files only. Never recursively delete a directory or follow a directory link.
                    foreach (var relative in paths)
                    {
                        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(tree.Path, relative));
                        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Directory.Exists(path))
                            return new(1, "", "Refusing a path outside the worktree or a directory: " + relative);
                        var parent = System.IO.Path.GetDirectoryName(path);
                        while (parent is not null && parent.Length >= root.Length)
                        {
                            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return new(1, "", "Refusing a linked parent directory: " + relative);
                            parent = System.IO.Path.GetDirectoryName(parent);
                        }
                    }
                    foreach (var relative in paths) File.Delete(System.IO.Path.Combine(tree.Path, relative));
                    return new(0, $"Deleted {paths.Length} untracked files.", "");
                default: return new(1, "", "Unknown file action.");
            }
        }, State);
    }
    public static Plan Commit(Worktree tree, string message)
    {
        Ready(tree);
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Enter a commit message.");
        var staged = tree.Changes.Where(c => c.Area == ChangeArea.Staged).ToList();
        if (staged.Count == 0) throw new InvalidOperationException("No staged changes.");
        string State() => CheckoutStamp(tree) + Git.IndexTree(tree);
        return Guarded(tree, "Commit", $"Message: {message}\n\nALL staged files in this worktree:\n" + string.Join('\n', staged.Select(c => c.Path)),
            () => Git.Run(tree.Path, "commit", "-m", message), State);
    }
    public static Plan Branch(Worktree tree, string action, string branch, string? start = null, string? destination = null)
    {
        Ready(tree, true);
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-')) throw new InvalidOperationException("Enter a branch name, without a leading dash.");
        Git.Run(tree.Path, "check-ref-format", "--branch", branch).Require();
        if (action != "Checkout" && Git.Run(tree.Path, "show-ref", "--verify", "--quiet", "refs/heads/" + branch).Ok)
            throw new InvalidOperationException("That local branch already exists.");
        if (action == "Checkout")
        {
            var registered = Git.ParseWorktrees(Git.Run(tree.CommonDir, "worktree", "list", "--porcelain", "-z").Require().Output, new(tree.Project, tree.ProjectRoot), tree.CommonDir);
            var occupied = registered.FirstOrDefault(w => w.Branch == branch && !string.Equals(w.Id, tree.Id, StringComparison.OrdinalIgnoreCase));
            if (occupied is not null) throw new InvalidOperationException($"Branch already checked out at {occupied.Path}. Select that worktree in the list.");
            // Exact local names or Git's unambiguous remote-tracking inference, never detached checkout.
            return Guarded(tree, action, "Switch to " + branch, () => Git.Run(tree.Path, "switch", branch));
        }
        var baseCommit = ResolveCommit(tree, string.IsNullOrWhiteSpace(start) ? "HEAD" : start);
        if (action == "New worktree")
        {
            if (string.IsNullOrWhiteSpace(destination) || !System.IO.Path.IsPathFullyQualified(destination)) throw new InvalidOperationException("Choose an absolute path for the new worktree.");
            if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidOperationException("The destination already exists. Choose a new directory.");
            return Guarded(tree, action, $"Branch: {branch}\nBase: {start ?? "HEAD"} ({baseCommit[..7]})\nDirectory: {destination}",
                () => Git.Run(tree.Path, "worktree", "add", "-b", branch, destination, baseCommit));
        }
        return Guarded(tree, "Create and checkout branch", $"Branch: {branch}\nBase: {start ?? "HEAD"} ({baseCommit[..7]})", () => Git.Run(tree.Path, "switch", "-c", branch, baseCommit));
    }
    public static string ResolveCommit(Worktree tree, string revision)
    {
        if (revision.StartsWith('-')) throw new InvalidOperationException("A revision cannot begin with '-'.");
        return Git.Run(tree.Path, "rev-parse", "--verify", "--end-of-options", revision + "^{commit}").Require().Output.Trim();
    }
    public static Plan ExistingWorktree(BranchInfo branch, Project project, string destination)
    {
        if (!branch.Ref.StartsWith("refs/heads/", StringComparison.Ordinal)) throw new InvalidOperationException("Choose a local branch.");
        if (!System.IO.Path.IsPathFullyQualified(destination)) throw new InvalidOperationException("Choose an absolute directory path.");
        destination = System.IO.Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidOperationException("The destination already exists. Choose a new directory.");
        string State() => History.Resolve(branch.CommonDir, branch.Ref) + "\0" + Git.Run(branch.CommonDir, "worktree", "list", "--porcelain", "-z").Require().Output;
        var tree = new Worktree { Project = project.Name, ProjectRoot = project.Path, Path = destination, CommonDir = branch.CommonDir, Branch = branch.Name };
        return Guarded(tree, "Create worktree for existing branch", $"Branch: {branch.Name}\nDirectory: {destination}\nThe existing branch will be checked out here.", () => {
            if (File.Exists(destination) || Directory.Exists(destination)) return new(1, "", "Destination appeared after preview; choose a new directory.");
            return Git.Run(branch.CommonDir, "worktree", "add", "--", destination, branch.Name);
        }, State);
    }
    public static Plan Sync(Worktree tree, string action, string? remote = null)
    {
        if (action == "Fetch") return new() { Tree = tree, Action = action, Preview = "Fetch all configured remotes once for this repository.", Execute = () => Git.Run(tree.CommonDir, "fetch", "--all") };
        Ready(tree, action.StartsWith("Pull"));
        if (tree.Branch == "(detached)") throw new InvalidOperationException("Create or checkout a branch first.");
        if (action.StartsWith("Pull"))
        {
            if (tree.Upstream is null || tree.UpstreamGone) throw new InvalidOperationException("Configure an upstream (Push can set one) before pulling.");
            string mode = action == "Pull with rebase" ? "--rebase" : action == "Pull with merge" ? "--no-rebase" : "--ff-only";
            return Guarded(tree, action, $"{tree.Upstream} → {tree.Branch}\nMode: {mode}", () => Git.Run(tree.Path, "pull", mode));
        }
        var configuredRemote = Git.Run(tree.Path, "config", "--get", "branch." + tree.Branch + ".remote").Output.Trim();
        var mergeRef = Git.Run(tree.Path, "config", "--get", "branch." + tree.Branch + ".merge").Output.Trim();
        var preferredRemote = Git.Run(tree.Path, "config", "--get", "branch." + tree.Branch + ".pushRemote").Output.Trim();
        if (preferredRemote.Length == 0) preferredRemote = Git.Run(tree.Path, "config", "--get", "remote.pushDefault").Output.Trim();
        var setUpstream = remote is not null || configuredRemote.Length == 0 || mergeRef.Length == 0;
        remote ??= preferredRemote.Length > 0 ? preferredRemote : configuredRemote;
        if (string.IsNullOrWhiteSpace(remote) || remote.StartsWith('-')) throw new InvalidOperationException("Choose a configured remote for the first push.");
        if (!Git.Run(tree.Path, "remote").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(remote))
            throw new InvalidOperationException("Choose a configured remote name.");
        var target = setUpstream || remote != configuredRemote ? "refs/heads/" + tree.Branch : mergeRef;
        if (!target.StartsWith("refs/heads/")) throw new InvalidOperationException("Push destination must be a branch.");
        var url = Git.Run(tree.Path, "remote", "get-url", "--push", remote).Require().Output.Trim();
        List<string> args = ["push"];
        if (setUpstream) args.Add("--set-upstream");
        args.Add(remote); args.Add($"refs/heads/{tree.Branch}:{target}");
        return Guarded(tree, "Push", $"{tree.Branch} → {remote}/{target[11..]}\nDestination: {url}\nRegular push (no force).", () => Git.RunInput(tree.Path, args));
    }
    public static Plan Rebase(Worktree tree, string onto)
    {
        Ready(tree, true);
        if (tree.Branch == "(detached)") throw new InvalidOperationException("Checkout a branch first.");
        var commit = ResolveCommit(tree, onto);
        var commits = Git.Run(tree.Path, "log", "--oneline", commit + "..HEAD").Require().Output;
        return Guarded(tree, "Rebase", $"{tree.Branch} onto {onto} ({commit[..7]})\nCandidate commits:\n{commits}\nRebase rewrites commit IDs. A later regular push can be rejected for an already-published branch.", () => Git.Run(tree.Path, "rebase", commit));
    }
    public static Plan Revert(Worktree tree, string revision)
    {
        Ready(tree, true);
        var commit = ResolveCommit(tree, revision);
        var parents = Git.Run(tree.Path, "rev-list", "--parents", "-n", "1", commit).Require().Output.Trim().Split(' ');
        if (parents.Length > 2) throw new InvalidOperationException("This is a merge commit. Reverting it requires an explicit mainline; use Git in this worktree.");
        return Guarded(tree, "Revert commit", Git.Run(tree.Path, "show", "--stat", "--oneline", commit).Require().Output, () => Git.Run(tree.Path, "revert", "--no-edit", commit));
    }
    public static Plan ContinueOrAbort(Worktree tree, bool abort)
    {
        string command = tree.Operation.Split(' ')[0].ToLowerInvariant();
        if (command is not ("rebase" or "merge" or "revert" or "cherry-pick")) throw new InvalidOperationException("No supported operation is in progress.");
        if (!abort && tree.Changes.Any(c => c.Area == ChangeArea.Conflict)) throw new InvalidOperationException("Resolve and stage conflicts before continuing.");
        return Guarded(tree, abort ? "Abort" : "Continue", tree.Operation, () => Git.Run(tree.Path, command, abort ? "--abort" : "--continue"));
    }
    public static Plan Stash(Worktree tree, string message)
    {
        Ready(tree);
        return Guarded(tree, "Stash", "Stash tracked and untracked changes. Ignored files are excluded.\n" + message,
            () => Git.Run(tree.Path, "stash", "push", "--include-untracked", "-m", message));
    }
    public static Plan ApplyStash(Worktree tree, string stash)
    {
        Ready(tree, true);
        var commit = ResolveCommit(tree, stash);
        return Guarded(tree, "Apply stash", "Apply " + stash + "; keep the stash entry.", () => Git.Run(tree.Path, "stash", "apply", "--index", commit));
    }
    public static Plan RemoveWorktree(Worktree tree)
    {
        Ready(tree, true);
        if (tree.Locked is not null) throw new InvalidOperationException("Worktree is locked: " + tree.Locked);
        return Guarded(tree, "Remove worktree", $"Remove clean worktree {tree.Path}. Keep its branch.", () => Git.Run(tree.CommonDir, "worktree", "remove", tree.Path));
    }
}
