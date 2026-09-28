namespace GitWorkspace;

public sealed partial class App
{
    void InactiveBranch(BranchInfo branch)
    {
        var action = dialog.Choose(branch.Project + " / " + branch.Name, ["Review commit history", "Checkout branch in an existing worktree…", "Create a worktree for this branch…"]);
        if (action is null) return;
        if (action == 0) { BrowseHistory(Target(branch)); return; }
        if (!CanWrite()) return;
        if (action == 1) {
            var trees = view.Snapshot.Worktrees.Where(t => t.Project == branch.Project && t.Usable).ToList();
            var pick = dialog.Choose("Choose checkout for " + branch.Name, trees.Select(t => t.Title + " · " + t.Summary).ToList());
            if (pick is null) return;
            Execute([Operations.Branch(trees[pick.Value], "Checkout", branch.Name)]);
        }
        else {
            var project = settings.Projects.First(p => p.Name == branch.Project);
            var destination = dialog.Text("Create worktree · " + branch.Name, "Choose a new absolute directory. The existing branch and its commits are kept.", Path.Combine(project.Path, branch.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrWhiteSpace(destination)) return;
            Execute([Operations.ExistingWorktree(branch, project, destination)]);
        }
        var checkout = view.Snapshot.Worktrees.FirstOrDefault(t => t.Project == branch.Project && t.ActiveBranchRef == branch.Ref);
        if (checkout is not null) { view.Navigate(branch.Project); view.Focus = Math.Max(0, view.Rows.FindIndex(r => r.Tree?.Id == checkout.Id)); }
    }
    void Workspace()
    {
        var groups = new[] { "All projects" }.Concat(settings.Groups.Select(g => g.Name)).ToList();
        var options = groups.Concat(new[] { "+ Create project group…", "Edit group membership…", "Rename group…", "Duplicate group…", "Delete group…", "Repo View…" }).ToList();
        var pick = dialog.Choose("Project groups", options, Math.Max(0, groups.IndexOf(view.State.Group)));
        if (pick is null) return;
        if (pick < groups.Count) { view.Navigate(null, groups[pick.Value]); previewKey = ""; return; }
        switch (pick - groups.Count)
        {
            case 0: EditGroup(); break;
            case 1: var edit = PickGroup("Edit membership"); if (edit is not null) EditGroup(edit); break;
            case 2: var rename = PickGroup("Rename group"); if (rename is not null) CopyGroup(rename, false); break;
            case 3: var copy = PickGroup("Duplicate group"); if (copy is not null) CopyGroup(copy, true); break;
            case 4: RemoveGroup(); break;
            case 5: RepoView(true); break;
        }
    }
    void Tab(Hit hit, bool edit)
    {
        if (hit.Id == "tab-left" || hit.Id == "tab-right")
        {
            view.State.TabOffset = Math.Clamp(hit.Index, 0, Math.Max(0, view.Tabs().Count - 1));
            return;
        }
        if (hit.Id == "new") { EditGroup(); return; }
        if (hit.Id == "repo-close") { view.Back(); previewKey = ""; return; }
        if (edit) { EditGroupTab(hit.Index); return; }
        SelectTab(hit.Index);
    }
    // internal (Task 13' test seam): pinned directly by TabTests via App.TestView, since the
    // tab strip has no other public surface. Only nudges the offset when the tab is hidden to
    // the left of the current scroll window - it must never jump straight to the tab's own
    // index, or "All projects" would land behind ‹ on every single-digit click.
    internal void SelectTab(int index)
    {
        if (index < 0) return;
        var tabs = view.Tabs();
        var tab = tabs.FirstOrDefault(t => t.Index == index) ?? tabs.ElementAtOrDefault(index);
        if (tab is null) return;
        if (tab.Id.StartsWith("repo:", StringComparison.Ordinal)) return;
        if (tab.Index < view.State.TabOffset) view.State.TabOffset = tab.Index;
        if (tab.Index == 0) view.Navigate(null, "All projects");
        else if (tab.Index - 1 < settings.Groups.Count) view.Navigate(null, settings.Groups[tab.Index - 1].Name);
        previewKey = "";
    }
    void EditCurrentTab()
    {
        if (view.RepoMode) { notice = "Repo View is temporary · Backspace closes it."; return; }
        var index = view.State.Group == "All projects" ? 0 : settings.Groups.FindIndex(g => g.Name == view.State.Group) + 1;
        EditGroupTab(index);
    }
    // internal (Task 13' test seam): see SelectTab above.
    internal void EditGroupTab(int index)
    {
        if (index <= 0) { notice = "All projects has no saved membership to edit."; return; }
        if (index - 1 >= settings.Groups.Count) return;
        var group = settings.Groups[index - 1];
        var pick = dialog.Choose(group.Name, ["Edit members", "Rename", "Duplicate", "Delete"]);
        if (pick is null) return;
        if (pick == 0) EditGroup(group);
        if (pick == 1) CopyGroup(group, false);
        if (pick == 2) CopyGroup(group, true);
        // Critical (checkpoint review): --demo runs against the real config path, so a delete
        // here must be guarded like every other write action, or it silently overwrites the
        // user's git-workspaces.json with the demo data minus the deleted group.
        if (pick == 3 && CanWrite() && dialog.Review("Delete group", "Delete the saved membership of " + group.Name + "? Repository files, branches and worktrees stay in place.", "Delete group"))
        {
            settings.Groups.Remove(group); Config.Save(configPath, settings); view.Navigate(null, "All projects"); previewKey = "";
        }
    }
    WorkspaceGroup? PickGroup(string title)
    {
        var pick = dialog.Choose(title, settings.Groups.Select(g => g.Name).ToList(), Math.Max(0, settings.Groups.FindIndex(g => g.Name == view.State.Group)));
        return pick is null ? null : settings.Groups[pick.Value];
    }
    string? GroupName(string title, string initial = "", WorkspaceGroup? replacing = null)
    {
        var name = dialog.Text(title, "A unique name for this project group.", initial)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Equals("All projects", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("All projects is reserved.");
        if (settings.Groups.Any(g => g != replacing && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A group with that name already exists.");
        return name;
    }
    void EditGroup(WorkspaceGroup? existing = null)
    {
        if (!CanWrite()) return;
        var name = existing?.Name ?? GroupName("Create project group"); if (name is null) return;
        var draft = dialog.EditGroup(new(settings, view.Snapshot, existing), name); if (draft is null) return;
        var group = draft.Build(name);
        if (existing is not null) settings.Groups[settings.Groups.IndexOf(existing)] = group; else settings.Groups.Add(group);
        Config.Save(configPath, settings); view.Navigate(null, name); previewKey = "";
        notice = "Saved group · whole repos, fixed worktree paths and branch refs can be mixed.";
    }
    void CopyGroup(WorkspaceGroup original, bool duplicate)
    {
        if (!CanWrite()) return;
        var name = GroupName(duplicate ? "Duplicate group" : "Rename group", duplicate ? original.Name + " copy" : original.Name, duplicate ? null : original);
        if (name is null) return;
        var updated = original with { Name = name, Projects = [.. original.Projects], Worktrees = [.. original.Worktrees], Branches = [.. original.Branches] };
        if (duplicate) settings.Groups.Add(updated); else settings.Groups[settings.Groups.IndexOf(original)] = updated;
        Config.Save(configPath, settings); view.Navigate(null, name); previewKey = "";
    }
    void RemoveGroup()
    {
        if (!CanWrite()) return;
        var group = PickGroup("Delete project group");
        if (group is null || !dialog.Review("Delete group", "Delete the saved membership of " + group.Name + "? Repository files, branches and worktrees stay in place.", "Delete group")) return;
        settings.Groups.Remove(group); Config.Save(configPath, settings); view.Navigate(null, "All projects"); previewKey = "";
    }
    void RepoView(bool choose = false)
    {
        string? project = choose || view.RepoMode ? null : view.Current?.Project;
        if (project is null) {
            var projects = settings.Projects.Select(p => p.Name).ToList();
            var pick = dialog.Choose("Repo View · choose repository", projects, Math.Max(0, projects.IndexOf(view.State.Repo ?? view.Current?.Project ?? "")));
            if (pick is null) return; project = projects[pick.Value];
        }
        view.Navigate(project); previewKey = "";
        notice = "All worktrees and local branches · H history · [ ] next repo · Backspace return";
    }
}
