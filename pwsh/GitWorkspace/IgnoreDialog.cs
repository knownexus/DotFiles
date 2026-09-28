namespace GitWorkspace;

public sealed partial class Dialogs
{
    public List<IgnoreDestination>? IgnoreTargets(IReadOnlyList<IgnoreDestination> choices, IEnumerable<string> initial)
    {
        if (choices.Count == 0) { Message(".gitignore destination", "No available worktrees or local branches."); return null; }
        var selected = initial.Where(id => choices.Any(c => c.Id == id)).ToHashSet();
        var backdrop = Backdrop(); int focus = 0, scroll = 0; string search = "";
        while (true) {
            var visible = choices.Where(c => (c.Project + " " + c.Label + " " + c.Detail).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            focus = Math.Clamp(focus, 0, Math.Max(0, visible.Count - 1));
            var lines = new List<(string Project, IgnoreDestination? Choice, int Index)>();
            foreach (var group in visible.GroupBy(c => c.Project)) {
                lines.Add((group.Key, null, -1));
                foreach (var entry in group) lines.Add((group.Key, entry, visible.IndexOf(entry)));
            }
            var (c, inner) = Frame(backdrop, ".gitignore · destination branches / worktrees",
                "Space select · / search · Enter review · Esc cancel", terminal.Width * 9 / 10, terminal.Height * 9 / 10);
            c.Text(inner.X, inner.Y, $"{selected.Count} selected · all worktrees, including outside this group", Ink.Accent, inner.Width);
            int top = inner.Y + 2, rows = Math.Max(1, inner.Height - 4);
            int focusLine = Math.Max(0, lines.FindIndex(l => l.Index == focus));
            if (focusLine < scroll) scroll = focusLine;
            if (focusLine >= scroll + rows) scroll = focusLine - rows + 1;
            scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - rows));
            var hits = new HitMap();
            for (int i = scroll; i < Math.Min(scroll + rows, lines.Count); i++) {
                var line = lines[i]; int y = top + i - scroll;
                if (line.Choice is null) { c.Line(y, inner.X, inner.Width); c.Text(inner.X + 1, y, " " + line.Project + " ", Ink.Heading, inner.Width - 2); }
                else {
                    bool active = line.Index == focus;
                    if (active) c.Fill(inner.X, y, inner.Width);
                    c.Text(inner.X + 1, y, (selected.Contains(line.Choice.Id) ? "☑ " : "☐ ") + line.Choice.Label, Ink.Text, inner.Width - 2, active);
                    hits.Add(inner.X, y, inner.Width, new Hit("destination", line.Index));
                }
            }
            if (visible.ElementAtOrDefault(focus) is { } current) c.Text(inner.X, inner.Bottom - 1, current.Detail, Ink.Muted, inner.Width);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.Enter) return choices.Where(c => selected.Contains(c.Id)).ToList();
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.Home) focus = 0;
            if (key.Key == ConsoleKey.End) focus = Math.Max(0, visible.Count - 1);
            if (key.Key == ConsoleKey.PageUp) focus = Math.Max(0, focus - rows);
            if (key.Key == ConsoleKey.PageDown) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + rows);
            if (key.Character == '/') { search = Text("Find ignore destination", "Search projects, branches and paths.", search) ?? search; focus = scroll = 0; }
            bool toggle = key.Key == ConsoleKey.Spacebar;
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, visible.Count - 1));
            else if (key.Mouse && key.Button != MouseButton.Right && hits.At(key.X, key.Y) is { Kind: "destination" } hit) { focus = hit.Index; toggle = true; }
            if (toggle && visible.ElementAtOrDefault(focus) is { } choice) { if (!selected.Remove(choice.Id)) selected.Add(choice.Id); }
        }
    }
}
