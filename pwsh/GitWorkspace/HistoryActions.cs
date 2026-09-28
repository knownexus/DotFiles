using System.Diagnostics;

namespace GitWorkspace;

public sealed record HistoryTarget(string Project, string Directory, string Revision, string Label, string? WorktreeId = null, string? BranchRef = null, string? Upstream = null);

public sealed partial class App
{
    HistoryTarget Target(Worktree tree) => new(tree.Project, tree.Usable ? tree.Path : tree.CommonDir,
        tree.Usable ? "HEAD" : tree.Head, tree.Title, tree.Id, tree.ActiveBranchRef, tree.UpstreamGone ? null : tree.Upstream);
    HistoryTarget Target(BranchInfo branch) => new(branch.Project, branch.CommonDir, branch.Ref, branch.Project + " / " + branch.Name,
        view.Snapshot.Worktrees.FirstOrDefault(t => t.Project == branch.Project && t.ActiveBranchRef == branch.Ref)?.Id, branch.Ref, branch.Upstream);
    HistoryTarget? PickHistory(string? project)
    {
        if (project is null) {
            var pick = dialog.Choose("History · choose repository", settings.Projects.Select(p => p.Name).ToList());
            if (pick is null) return null; project = settings.Projects[pick.Value].Name;
        }
        var targets = view.Snapshot.Worktrees.Where(t => t.Project == project && t.Head != "(initial)").Select(Target).ToList();
        targets.AddRange(view.Snapshot.Branches.Where(b => b.Project == project && !view.Snapshot.Worktrees.Any(t => t.Project == b.Project && t.ActiveBranchRef == b.Ref)).Select(Target));
        var choice = dialog.Choose("History · " + project, targets.Select(t => t.Label + (t.WorktreeId is null ? " · not checked out" : "")).ToList());
        return choice is null ? null : targets[choice.Value];
    }
    void BrowseHistory(HistoryTarget? target = null)
    {
        target ??= view.Current?.Tree is { } tree ? Target(tree) : view.Current?.Branch is { } branch ? Target(branch) : PickHistory(view.Current?.Project);
        if (target is not null) OpenCommits(target);
    }
    // The list was read at a pinned tip; rebasing is only offered while the worktree still sits
    // on that exact commit and branch, so the highlighted commit is the one Git will rewrite.
    void RebaseFromCommit(CommitsModel m, CommitInfo commit)
    {
        if (!CanWrite()) return;
        Refresh();
        var checkout = view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == m.Target.WorktreeId && t.Usable);
        if (checkout is null) { dialog.Message("Interactive rebase", "This branch needs an available worktree before rebasing. Use B → Checkout branch, or create a worktree for it, then open its commits."); return; }
        if (Git.Head(checkout) != m.Tip || checkout.ActiveBranchRef != m.Target.BranchRef) { dialog.Message("History changed", "This worktree moved after its commits were listed. The list has been refreshed; select the intended commit again."); OpenCommits(Target(checkout)); return; }
        RebaseInteractive(checkout, commit);
        OpenCommits(Target(view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == checkout.Id) ?? checkout));
    }
    Worktree? FocusedCheckout(string title)
    {
        if (view.Current?.Tree is { } current) return current;
        if (view.Current?.Branch is not null) {
            dialog.Message(title, "This branch is not checked out. Use B → Checkout branch to select a worktree for it."); return null;
        }
        var choices = view.InGroup().Where(t => t.Usable && (view.Current?.Project is null || t.Project == view.Current.Project)).ToList();
        var pick = dialog.Choose(title + " · choose one worktree", choices.Select(t => t.Title).ToList());
        return pick is null ? null : choices[pick.Value];
    }
    void RebaseInteractive(Worktree? tree = null, CommitInfo? first = null)
    {
        if (!CanWrite()) return;
        tree ??= FocusedCheckout("Interactive rebase"); if (tree is null) return;
        Git.LoadStatus(tree);
        if (tree.Operation.StartsWith("Rebase", StringComparison.Ordinal)) {
            var action = dialog.Choose("Paused rebase · " + tree.Title, ["Continue rebase", "Edit remaining todo list", "Amend paused edit commit", "Abort rebase"]);
            if (action == 0) RunInteractive(InteractiveRebase.Continue(tree));
            if (action == 1) RunInteractive(InteractiveRebase.EditTodo(tree));
            if (action == 2) RunInteractive(InteractiveRebase.Amend(tree));
            if (action == 3) Execute([Operations.ContinueOrAbort(tree, true)]);
            return;
        }
        string? baseRevision; bool root = false;
        if (first is not null) {
            if (!Git.Run(tree.Path, "merge-base", "--is-ancestor", first.Hash, "HEAD").Ok) throw new InvalidOperationException("That commit is no longer an ancestor of this worktree's HEAD. Refresh history first.");
            root = first.Parents.Count == 0; baseRevision = first.Parents.FirstOrDefault();
        }
        else {
            var mode = dialog.Choose("Interactive rebase · " + tree.Title, ["Choose the first commit in history…", "Rebase commits after a branch / tag / revision…", "Include all commits, starting at the root…"]);
            if (mode is null) return;
            if (mode == 0) { BrowseHistory(Target(tree)); return; }
            root = mode == 2;
            baseRevision = root ? null : dialog.Text("Interactive rebase base", "Rebase commits AFTER this base. Examples: main, origin/main, HEAD~3. The worktree must be clean.", tree.Upstream ?? "HEAD~1");
            if (!root && string.IsNullOrWhiteSpace(baseRevision)) return;
        }
        RunInteractive(InteractiveRebase.Prepare(tree, baseRevision, root));
    }
    void RunInteractive(InteractivePlan plan)
    {
        if (!dialog.Review(plan.Action, plan.Preview, "Open Git editor")) return;
        plan.Validate();
        int code = -1;
        terminal.Suspend();
        try {
            using var process = Process.Start(plan.Start) ?? throw new InvalidOperationException("Unable to start Git.");
            process.WaitForExit(); code = process.ExitCode;
            Console.WriteLine("\nGit exited with code " + code + ". Press any key to return to Git Workspace.");
            Console.ReadKey(true);
        }
        finally { terminal.Resume(); Refresh(); }
        var current = view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == plan.Tree.Id);
        var detail = $"Git exited with code {code}. " + (current?.Operation.Length > 0 ? current.Operation + ". Use I to continue, edit the todo list, amend an edit stop or abort." : "Git editor session ended.");
        activity.Add(new(plan.Tree.Title, plan.Action, code == 0, detail, DateTime.Now)); SaveActivity();
        notice = detail;
    }
}
