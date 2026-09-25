namespace GitWorkspace;

public sealed partial class App
{
    void IgnoreMenu()
    {
        var action = dialog.Choose("Project .gitignore", ["Ignore selected / highlighted untracked files", "Add a file path or pattern…"]);
        if (action is null || !CanWrite()) return;
        if (action == 0) {
            var files = view.SelectedFiles();
            if (files.Count == 0 && view.Current is { Tree: not null, Change: not null } row) files = [(row.Tree, row.Change)];
            if (files.Count == 0) { dialog.Message("Ignore files", "Select untracked files first. Press T to show them if hidden."); return; }
            Execute(files.GroupBy(f => f.Tree).Select(g => Ignore.Files(g.Key, g.Select(f => f.Change))).ToList());
        }
        else {
            var targets = PickTargets("Add .gitignore rule"); if (targets is null) return;
            string initial = view.Current?.Change is { } change ? Ignore.Literal(change.Path) : "";
            var pattern = dialog.Text("Git ignore pattern", "One Git pattern, applied to each chosen worktree's root .gitignore. Examples: *.log, build/, /local.json. Use / for directories. Already tracked files stay tracked.", initial);
            if (string.IsNullOrWhiteSpace(pattern)) return;
            Execute(targets.Select(t => Ignore.Patterns(t, [pattern])).ToList());
        }
    }
}
