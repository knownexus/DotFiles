namespace GitWorkspace;

public static class Footer
{
    public static List<(string Key, string Label)> For(WorkspaceView view, object? commits)
    {
        List<(string, string)> keys = view.Current?.Kind switch {
            "file" when !view.WorktreeMode => [(" ", "Space select"), ("s", "Stage"), ("u", "Unstage"), ("c", "Commit…"), ("o", "Open"), ("\r", "Enter review"), ("m", "More…")],
            "branch" => [("\r", "Enter history"), ("b", "Checkout…"), ("p", "Repo")],
            "worktree" or "project" when view.WorktreeMode => [(" ", "Space select"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("h", "History"), ("p", "Repo"), ("i", "Rebase…"), ("m", "More…")],
            "worktree" or "project" => [(" ", "Space select"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("h", "History"), ("p", "Repo"), ("m", "More…")],
            _ => [("w", "Groups"), ("m", "More…")]
        };
        keys.Add(("?", "keys")); keys.Add(("q", "quit"));
        return keys;
    }
    public static void Render(Canvas c, Rect area, HitMap hits, List<(string Key, string Label)> keys)
    {
        int x = area.X + 2;
        foreach (var (key, label) in keys) {
            string text = key == " " || key == "\r" ? label : key.ToUpperInvariant() + " " + label;
            if (x + text.Length > area.Right - 1) break;
            c.Text(x, area.Y, text, Ink.Accent); hits.Add(x, area.Y, text.Length, new Hit("action", Id: key)); x += text.Length + 3;
        }
    }
}
