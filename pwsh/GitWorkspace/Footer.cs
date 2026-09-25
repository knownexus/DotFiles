namespace GitWorkspace;

public static class Footer
{
    public static List<(string Key, string Label)> For(WorkspaceView view, object? commits) =>
        view.WorktreeMode ? [("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("m", "More…"), ("i", "Rebase…"), ("?", "keys"), ("q", "quit")]
            : [(" ", "Space select"), ("s", "Stage"), ("u", "Unstage"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("m", "More…"), ("?", "keys"), ("q", "quit")];
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
