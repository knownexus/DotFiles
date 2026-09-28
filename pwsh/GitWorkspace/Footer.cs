namespace GitWorkspace;

public static class Footer
{
    public static List<(string Key, string Label)> For(WorkspaceView view, object? commits)
    {
        List<(string, string)> keys = view.Current?.Kind switch {
            "file" when !view.WorktreeMode => [(" ", "Space select"), ("d", "Diff"), ("s", "Stage"), ("u", "Unstage"), ("c", "Commit…"), ("o", "Open"), ("m", "More…")],
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
        static string Text(string key, string label) => key == " " || key == "\r" ? label : key.ToUpperInvariant() + " " + label;
        var pinned = keys.Where(k => k.Key is "?" or "q").ToList();
        var rest = keys.Where(k => k.Key is not ("?" or "q")).ToList();
        int pinnedWidth = pinned.Sum(k => Text(k.Key, k.Label).Length) + Math.Max(0, pinned.Count - 1) * 3;
        int pinnedX = area.Right - 2 - pinnedWidth;
        int x = pinnedX;
        foreach (var (key, label) in pinned) {
            string text = Text(key, label);
            c.Text(x, area.Y, text, Ink.Accent); hits.Add(x, area.Y, text.Length, new Hit("action", Id: key)); x += text.Length + 3;
        }
        x = area.X + 2;
        foreach (var (key, label) in rest) {
            string text = Text(key, label);
            if (x + text.Length > pinnedX - 3) break;
            c.Text(x, area.Y, text, Ink.Accent); hits.Add(x, area.Y, text.Length, new Hit("action", Id: key)); x += text.Length + 3;
        }
    }
}
