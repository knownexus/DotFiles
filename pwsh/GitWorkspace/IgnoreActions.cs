namespace GitWorkspace;

public sealed partial class App
{
    void IgnoreMenu()
    {
        var action = dialog.Choose("Project .gitignore", ["Ignore selected / highlighted untracked files", "Add a file path or pattern…"]);
        if (action is null || !CanWrite()) return;
        List<Plan> plans = [];
        if (action == 0) {
            var files = view.SelectedFiles();
            if (files.Count == 0 && view.Current is { Tree: not null, Change: not null } row) files = [(row.Tree, row.Change)];
            if (files.Count == 0) { dialog.Message("Ignore files", "Select untracked files first. Press T to show them if hidden."); return; }
            foreach (var group in files.GroupBy(f => f.Tree.CommonDir)) {
                var selected = group.ToList();
                var rules = dialog.Busy("Checking selected ignore paths", () => Ignore.FileRules(selected));
                var destinations = dialog.IgnoreTargets(IgnoreDestinations.Available(view.Snapshot, group.Key), selected.Select(f => f.Tree.Id));
                if (destinations is null || destinations.Count == 0) return;
                foreach (var target in destinations) {
                    var plan = PrepareIgnore(target, rules, selected); if (plan is null) return;
                    plans.Add(plan);
                }
            }
        }
        else {
            string initial = view.Current?.Change is { } change ? Ignore.Literal(change.Path) : "";
            var pattern = dialog.Text("Git ignore pattern", "One Git pattern. Choose the destination branches/worktrees next. Examples: *.log, build/, /local.json. Already tracked files stay tracked.", initial);
            if (string.IsNullOrWhiteSpace(pattern)) return;
            var defaults = view.Targets.ToHashSet();
            if (defaults.Count == 0 && view.Current?.Tree is { } current) defaults.Add(current.Id);
            if (defaults.Count == 0 && view.Current?.Branch is { } branch) defaults.Add(branch.Id);
            var destinations = dialog.IgnoreTargets(IgnoreDestinations.Available(view.Snapshot), defaults);
            if (destinations is null || destinations.Count == 0) return;
            foreach (var target in destinations) {
                var plan = PrepareIgnore(target, [pattern]); if (plan is null) return;
                plans.Add(plan);
            }
        }
        Execute(plans);
    }
    Plan? PrepareIgnore(IgnoreDestination target, List<string> rules, List<(Worktree Tree, Change Change)>? files = null)
    {
        if (target.Tree is { } tree) return dialog.Busy("Preparing .gitignore · " + tree.BranchLabel,
            () => files is null ? Ignore.Patterns(tree, rules) : Ignore.FilesTo(tree, files));
        var branch = target.Branch!; var project = settings.Projects.First(p => p.Name == target.Project);
        string suggested = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(project.Path).TrimEnd(Path.DirectorySeparatorChar))!,
            Path.GetFileName(project.Path.TrimEnd(Path.DirectorySeparatorChar)) + "-" + branch.Name.Replace('/', '-'));
        var directory = dialog.Text("New worktree · " + branch.Name, "This branch is not checked out. Choose a new absolute directory. The worktree and .gitignore edit will both appear in the review before anything is written.", suggested);
        return string.IsNullOrWhiteSpace(directory) ? null : dialog.Busy("Preparing .gitignore · " + branch.Name, () => Ignore.NewWorktree(branch, project, directory, rules));
    }
}
