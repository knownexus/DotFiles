namespace GitWorkspace;

public sealed record IgnoreDestination(string Project, Worktree? Tree = null, BranchInfo? Branch = null)
{
    public string Id => Tree?.Id ?? Branch!.Id;
    public string Label => Tree is { } tree ? tree.BranchLabel + " · " + tree.Name : Branch!.Name + " · new worktree";
    public string Detail => Tree is { } tree ? Path.Combine(tree.Path, ".gitignore") : "Not checked out · choose a new worktree directory next";
}

public static class IgnoreDestinations
{
    public static bool SameRepository(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Use the complete snapshot, including worktrees outside the current group.
    public static List<IgnoreDestination> Available(Snapshot snapshot, string? common = null)
    {
        var choices = snapshot.Worktrees.Where(t => t.Usable && (common is null || SameRepository(t.CommonDir, common)))
            .Select(t => new IgnoreDestination(t.Project, Tree: t)).ToList();
        choices.AddRange(snapshot.Branches.Where(b => (common is null || SameRepository(b.CommonDir, common)) &&
            !snapshot.Worktrees.Any(t => SameRepository(t.CommonDir, b.CommonDir) && t.ActiveBranchRef == b.Ref))
            .Select(b => new IgnoreDestination(b.Project, Branch: b)));
        return choices.OrderBy(c => c.Project).ThenBy(c => c.Tree is null).ThenBy(c => c.Label).ToList();
    }
}
