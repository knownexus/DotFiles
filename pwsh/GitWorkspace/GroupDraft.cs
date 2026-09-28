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
    public HashSet<string> BranchesShown { get; } = [];
    public void AddDiscovered(IEnumerable<DiscoveredRepo> repos)
    {
        foreach (var repo in repos.Where(r => r.RegisteredAs is null))
            if (!Members.Any(m => m.Kind == "new-repo" && m.Value == repo.Path)) Members.Add(new("new-repo", repo.Path, repo.Path, repo.Name));
    }
    public RepoState State(string project)
    {
        if (Checked.Contains(new GroupMember("repo", project, project, "").Id) || Checked.Contains(new GroupMember("new-repo", project, project, "").Id)) return RepoState.Whole;
        return Members.Any(m => m.Project == project && m.Kind is "worktree" or "branch" && Checked.Contains(m.Id)) ? RepoState.Some : RepoState.None;
    }
    public void ToggleWhole(string project)
    {
        var repo = Members.FirstOrDefault(m => m.Kind == "repo" && m.Project == project); if (repo is null) return;
        var children = Members.Where(m => m.Project == project && m.Kind is "worktree" or "branch").ToList();
        if (Checked.Remove(repo.Id)) { foreach (var tree in children.Where(m => m.Kind == "worktree")) Checked.Add(tree.Id); }
        else { foreach (var child in children) Checked.Remove(child.Id); Checked.Add(repo.Id); }
    }
    public List<GroupMember> Rows(string search, ISet<string> folded)
    {
        List<GroupMember> rows = [];
        foreach (var head in Members.Where(m => m.Kind == "repo").Concat(Members.Where(m => m.Kind == "new-repo")))
        {
            var children = head.Kind == "repo" ? Members.Where(m => m.Project == head.Project && (m.Kind == "worktree" ||
                m.Kind == "branch" && (BranchesShown.Contains(head.Project) || Checked.Contains(m.Id)))).ToList() : [];
            bool match(GroupMember m) => search.Length == 0 || (head.Label + " " + m.Label).Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!match(head) && !children.Any(match)) continue;
            rows.Add(head);
            if (!folded.Contains(head.Project)) rows.AddRange(children.Where(match));
        }
        // Saved entries whose repository is no longer registered stay visible, so they can be unticked.
        rows.AddRange(Members.Where(m => m.Kind is "worktree" or "branch" && Checked.Contains(m.Id) && !Members.Any(h => h.Kind == "repo" && h.Project == m.Project)));
        return rows;
    }
    public static string UniqueName(string folder, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(folder)) return folder;
        for (int n = 2; ; n++) if (!used.Contains(folder + " " + n)) return folder + " " + n;
    }
    public WorkspaceGroup Commit(Settings settings, string name, WorkspaceGroup? existing, Func<string, string> resolve)
    {
        name = name.Trim();
        if (settings.Groups.Any(g => g != existing && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A group with that name already exists.");
        List<Project> added = [];
        foreach (var member in Members.Where(m => m.Kind == "new-repo" && Checked.Contains(m.Id)))
        {
            try { resolve(member.Value); }
            catch (InvalidOperationException e) { throw new GroupSaveException(member.Id, member.Label + ": " + e.Message); }
            added.Add(new(UniqueName(member.Label, settings.Projects.Select(p => p.Name).Concat(added.Select(p => p.Name))), member.Value));
        }
        // Translate ticked new repos into whole-repo entries under their registered names.
        var build = new GroupDraft(); build.Members.AddRange(Members.Where(m => m.Kind != "new-repo")); build.Checked.UnionWith(Checked);
        foreach (var (member, project) in Members.Where(m => m.Kind == "new-repo" && Checked.Contains(m.Id)).Zip(added)) {
            var repo = new GroupMember("repo", project.Name, project.Name, project.Name); build.Members.Add(repo); build.Checked.Add(repo.Id);
        }
        var group = build.Build(name);
        settings.Projects.AddRange(added);
        if (existing is not null && settings.Groups.IndexOf(existing) is var at && at >= 0) settings.Groups[at] = group; else settings.Groups.Add(group);
        return group;
    }
    GroupDraft() { }
}
public enum RepoState { None, Some, Whole }
public sealed class GroupSaveException(string memberId, string message) : InvalidOperationException(message)
{
    public string MemberId { get; } = memberId;
}
