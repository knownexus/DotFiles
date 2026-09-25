namespace GitWorkspace;

public sealed record GroupMember(string Kind, string Project, string Value, string Label)
{
    public string Id => Kind + "\0" + Project + "\0" + Value;
}
public sealed class GroupDraft
{
    public List<GroupMember> Members { get; } = [];
    public HashSet<string> Checked { get; } = [];
    public GroupDraft(Settings settings, Snapshot snapshot, WorkspaceGroup? existing = null)
    {
        foreach (var project in settings.Projects)
        {
            Members.Add(new("repo", project.Name, project.Name, project.Name + " · entire repository"));
            foreach (var t in snapshot.Worktrees.Where(t => t.Project == project.Name))
                Members.Add(new("worktree", project.Name, t.Id, "  Worktree · " + t.Name + " → " + t.BranchLabel + (t.Error.Length > 0 ? " (unavailable)" : "")));
            foreach (var b in snapshot.Branches.Where(b => b.Project == project.Name))
                Members.Add(new("branch", project.Name, b.Ref, "  Branch   · " + b.Name));
        }
        // Retain unavailable entries when editing, so a temporary missing disk/ref
        // does not silently remove group membership.
        if (existing is null) return;
        foreach (var p in existing.Projects) SelectOrAdd(new("repo", p, p, p + " · entire repository (unavailable)"));
        foreach (var path in existing.Worktrees) {
            var known = Members.FirstOrDefault(m => m.Kind == "worktree" && m.Value == path);
            SelectOrAdd(known ?? new("worktree", "Unavailable", path, "  Worktree · " + path + " (unavailable)"));
        }
        foreach (var b in existing.Branches) SelectOrAdd(new("branch", b.Project, b.Ref, "  Branch · " + b.Project + " / " + b.Ref + " (unavailable)"));
    }
    void SelectOrAdd(GroupMember member)
    {
        if (!Members.Any(m => m.Id == member.Id)) Members.Add(member);
        Checked.Add(member.Id);
    }
    public bool Inherited(GroupMember member) => member.Kind != "repo" && Checked.Contains(new GroupMember("repo", member.Project, member.Project, "").Id);
    public void Toggle(GroupMember member)
    {
        if (Inherited(member)) throw new InvalidOperationException("This is included by the whole repository. Uncheck the repository to select individual entries.");
        if (!Checked.Remove(member.Id)) Checked.Add(member.Id);
    }
    public WorkspaceGroup Build(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Equals("All projects", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a group name other than All projects.");
        var selected = Members.Where(m => Checked.Contains(m.Id)).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Select at least one repository, worktree or branch.");
        return new(name, selected.Where(m => m.Kind == "repo").Select(m => m.Value).Distinct().ToList(),
            selected.Where(m => m.Kind == "worktree").Select(m => m.Value).Distinct().ToList()) {
                Branches = selected.Where(m => m.Kind == "branch").Select(m => new BranchSelection(m.Project, m.Value)).Distinct().ToList()
            };
    }
}
