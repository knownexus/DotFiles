using System.Diagnostics;

namespace GitWorkspace;

public sealed record InteractivePlan(Worktree Tree, string Action, string Preview, ProcessStartInfo Start, string Expected)
{
    public static string State(Worktree tree) => Git.Head(tree) + "\0" + Git.Fingerprint(tree) + "\0" +
        Git.Run(tree.Path, "diff", "--cached", "--no-ext-diff", "--no-textconv", "--binary").Require().Output;
    public void Validate()
    {
        if (Expected != State(Tree)) throw new InvalidOperationException("The checkout changed after this preview. Refresh and prepare the action again.");
    }
}

public static class InteractiveRebase
{
    public static InteractivePlan Prepare(Worktree tree, string? baseRevision, bool root = false)
    {
        RequireCheckout(tree);
        if (tree.Operation.Length > 0 || tree.Changes.Count > 0) throw new InvalidOperationException("Interactive rebase needs a clean checkout with no operation in progress. Commit or stash changes first.");
        if (tree.Branch == "(detached)") throw new InvalidOperationException("Checkout a local branch first.");
        if (tree.Head == "(initial)") throw new InvalidOperationException("This branch has no commits yet.");
        string? baseCommit = root ? null : History.Resolve(tree.Path, baseRevision ?? throw new ArgumentException("Choose a base commit."));
        var commits = Git.Run(tree.Path, "log", "--reverse", "--oneline", root ? "HEAD" : baseCommit + "..HEAD", "--").Require().Output;
        if (string.IsNullOrWhiteSpace(commits)) throw new InvalidOperationException("There are no commits after that base. Choose an earlier commit.");
        List<string> args = ["rebase", "--interactive", "--rebase-merges", "--no-update-refs", "--no-autostash"];
        if (root) args.Add("--root"); else args.Add(baseCommit!);
        return new(tree, "Interactive rebase",
            $"{tree.Title}\n\nRange: {(root ? "including the root commit" : "after " + baseRevision)}\n\n{commits}\n" +
            "Git will open its todo list in your configured editor. Reorder entries or use pick, reword, edit, squash, fixup and drop. Merge structure is preserved.\n\n" +
            "This rewrites this branch's commit IDs. Other local branch refs will not be updated. No push is performed. Close the editor to proceed; the UI returns when Git completes or pauses.",
            Git.StartInfo(tree.Path, args, false), InteractivePlan.State(tree));
    }
    public static InteractivePlan Continue(Worktree tree)
    {
        RequireCheckout(tree);
        if (!tree.Operation.StartsWith("Rebase", StringComparison.Ordinal)) throw new InvalidOperationException("No rebase is in progress.");
        if (tree.Changes.Any(c => c.Area == ChangeArea.Conflict)) throw new InvalidOperationException("Resolve and stage conflicts before continuing.");
        return new(tree, "Continue rebase", tree.Title + "\n\nContinue the paused rebase. Git may reopen the commit-message editor.",
            Git.StartInfo(tree.Path, ["rebase", "--continue"], false), InteractivePlan.State(tree));
    }
    public static InteractivePlan Amend(Worktree tree)
    {
        RequireCheckout(tree);
        string marker = Git.Run(tree.Path, "rev-parse", "--path-format=absolute", "--git-path", "rebase-merge/amend").Require().Output.Trim();
        if (!tree.Operation.StartsWith("Rebase", StringComparison.Ordinal) || tree.Changes.Any(c => c.Area == ChangeArea.Conflict) || !File.Exists(marker))
            throw new InvalidOperationException("Amend is available when an interactive rebase stops at an edit command. For a resolved conflict, use Continue instead.");
        return new(tree, "Amend paused commit", tree.Title + "\n\nAmend HEAD using all staged changes and open the commit-message editor.\n" +
            string.Join('\n', tree.Changes.Where(c => c.Area == ChangeArea.Staged).Select(c => c.Path)),
            Git.StartInfo(tree.Path, ["commit", "--amend"], false), InteractivePlan.State(tree));
    }
    public static InteractivePlan EditTodo(Worktree tree)
    {
        RequireCheckout(tree);
        if (!tree.Operation.StartsWith("Rebase", StringComparison.Ordinal)) throw new InvalidOperationException("No rebase is in progress.");
        return new(tree, "Edit rebase todo", tree.Title + "\n\nOpen the remaining rebase commands in Git's configured sequence editor.",
            Git.StartInfo(tree.Path, ["rebase", "--edit-todo"], false), InteractivePlan.State(tree));
    }
    static void RequireCheckout(Worktree tree)
    {
        Git.LoadStatus(tree);
        if (!tree.Usable) throw new InvalidOperationException(tree.Error.Length > 0 ? tree.Error : "Worktree is unavailable.");
    }
}
