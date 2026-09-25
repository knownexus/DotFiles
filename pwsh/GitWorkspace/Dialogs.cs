namespace GitWorkspace;

public sealed partial class Dialogs(Terminal terminal)
{
    Canvas Page(string title, string hint)
    {
        var c = new Canvas(terminal.Width, terminal.Height);
        c.Text(2, 1, title, Ink.Heading, c.Width - 4); c.Line(3);
        c.Line(c.Height - 3); c.Text(2, c.Height - 2, hint, Ink.Accent, c.Width - 4);
        return c;
    }
    public int? Choose(string title, IReadOnlyList<string> options, int initial = 0)
    {
        if (options.Count == 0) { Message(title, "No available choices."); return null; }
        int focus = Math.Clamp(initial, 0, options.Count - 1), scroll = 0; string search = "";
        while (true)
        {
            var visible = Enumerable.Range(0, options.Count).Where(i => options[i].Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            focus = Math.Clamp(focus, 0, Math.Max(0, visible.Count - 1));
            var c = Page(title, "↑↓ choose · / search · Enter select · Esc cancel"); int rows = Math.Max(1, c.Height - 8);
            if (search.Length > 0) c.Text(3, 4, "/ " + search, Ink.Muted, c.Width - 6);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, visible.Count); i++) {
                if (i == focus) c.Fill(1, 5 + i - scroll, c.Width - 2);
                c.Text(3, 5 + i - scroll, options[visible[i]], Ink.Text, c.Width - 6, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.Home) focus = 0;
            if (key.Key == ConsoleKey.End) focus = Math.Max(0, visible.Count - 1);
            if (key.Key == ConsoleKey.PageDown) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + rows);
            if (key.Key == ConsoleKey.PageUp) focus = Math.Max(0, focus - rows);
            if (key.Character == '/') { search = Text("Search choices", "Filter the available choices. Empty text shows all.", search) ?? search; focus = scroll = 0; }
            if (key.Key == ConsoleKey.Enter && visible.Count > 0) return visible[focus];
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, visible.Count - 1));
            else if (key.Mouse && key.Y >= 5 && key.Y < 5 + rows && scroll + key.Y - 5 < visible.Count) return visible[scroll + key.Y - 5];
        }
    }
    public List<int>? Check(string title, IReadOnlyList<string> options, IEnumerable<int>? initial = null)
    {
        if (options.Count == 0) { Message(title, "No available worktrees."); return null; }
        var selected = (initial ?? []).ToHashSet(); int focus = 0, scroll = 0;
        while (true)
        {
            var c = Page(title, "Space checkbox · A all · Enter accept · Esc cancel"); int rows = Math.Max(1, c.Height - 9);
            c.Text(2, 4, $"{selected.Count} selected", Ink.Muted);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, options.Count); i++) {
                if (i == focus) c.Fill(1, 6 + i - scroll, c.Width - 2);
                c.Text(3, 6 + i - scroll, (selected.Contains(i) ? "☑  " : "☐  ") + options[i], Ink.Text, c.Width - 6, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(options.Count - 1, focus + 1);
            if (key.Key == ConsoleKey.Spacebar) { if (!selected.Remove(focus)) selected.Add(focus); }
            if (key.Key == ConsoleKey.A) { if (selected.Count == options.Count) selected.Clear(); else selected.UnionWith(Enumerable.Range(0, options.Count)); }
            if (key.Key == ConsoleKey.Enter) return selected.Order().ToList();
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, options.Count - 1);
            else if (key.Mouse && key.Y >= 6 && key.Y < 6 + rows && scroll + key.Y - 6 < options.Count) {
                focus = scroll + key.Y - 6; if (!selected.Remove(focus)) selected.Add(focus);
            }
        }
    }
    public string? Text(string title, string prompt, string initial = "")
    {
        string value = initial; int cursor = value.Length;
        while (true)
        {
            var c = Page(title, "Enter accept · Esc cancel · Ctrl+U clear");
            foreach (var (line, index) in Wrap(prompt, Math.Max(10, c.Width - 6)).Take(Math.Max(1, c.Height - 12)).Select((s, i) => (s, i))) c.Text(3, 5 + index, line, Ink.Muted);
            int y = c.Height - 6; int offset = Math.Max(0, cursor - c.Width + 9);
            c.Fill(2, y, c.Width - 4); c.Text(3, y, value[offset..], Ink.Text, c.Width - 6, true);
            c.Text(3 + cursor - offset, y, cursor < value.Length ? value[cursor].ToString() : "▏", Ink.Heading, 1, true);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.Enter) return value;
            if (key.Control && key.Key == ConsoleKey.U) { value = ""; cursor = 0; }
            else if (key.Key == ConsoleKey.Backspace && cursor > 0) value = value.Remove(--cursor, 1);
            else if (key.Key == ConsoleKey.Delete && cursor < value.Length) value = value.Remove(cursor, 1);
            else if (key.Key == ConsoleKey.LeftArrow) cursor = Math.Max(0, cursor - 1);
            else if (key.Key == ConsoleKey.RightArrow) cursor = Math.Min(value.Length, cursor + 1);
            else if (key.Key == ConsoleKey.Home) cursor = 0;
            else if (key.Key == ConsoleKey.End) cursor = value.Length;
            else if (!key.Control && !char.IsControl(key.Character)) { value = value.Insert(cursor, key.Character.ToString()); cursor++; }
        }
    }
    public bool Review(string title, string text, string accept = "Execute", bool confirm = true)
    {
        int scroll = 0;
        while (true)
        {
            var c = Page(title, confirm ? $"↑↓ / PgUp PgDn scroll · Enter {accept} · Esc cancel" : "↑↓ / PgUp PgDn scroll · Enter / Esc close");
            var lines = Wrap(text, Math.Max(10, c.Width - 6)).ToArray(); int count = Math.Max(1, c.Height - 8);
            scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Length - count));
            for (int i = 0; i < count && scroll + i < lines.Length; i++) {
                var line = lines[scroll + i]; c.Text(3, 5 + i, line, line.StartsWith('+') ? Ink.Good : line.StartsWith('-') ? Ink.Bad : Ink.Text, c.Width - 6);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return false;
            if (key.Key == ConsoleKey.Enter) return true;
            if (key.Key == ConsoleKey.UpArrow) scroll--;
            if (key.Key == ConsoleKey.DownArrow) scroll++;
            if (key.Key == ConsoleKey.PageUp) scroll -= count;
            if (key.Key == ConsoleKey.PageDown) scroll += count;
            if (key.Key == ConsoleKey.Home) scroll = 0;
            if (key.Key == ConsoleKey.End) scroll = lines.Length - count;
            if (key.Mouse) scroll += key.Wheel;
        }
    }
    public void Message(string title, string text) => Review(title, text, confirm: false);
    public T Busy<T>(string title, Func<T> work)
    {
        var task = Task.Run(work); int frame = 0;
        while (!task.IsCompleted) {
            var c = Page(title, "Working… results will appear here."); c.Text(3, 6, new[] { "◐", "◓", "◑", "◒" }[frame++ % 4] + "  Please wait", Ink.Accent);
            terminal.Draw(c); terminal.Poll(90);
        }
        return task.GetAwaiter().GetResult();
    }
    public static bool Cancel(Input key) => key.Key == ConsoleKey.Escape || key.Control && key.Key == ConsoleKey.C;
    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var raw in text.Replace("\r", "").Split('\n')) {
            var line = Canvas.Clean(raw.Replace("\t", "    "));
            if (line.Length == 0) { yield return ""; continue; }
            for (int start = 0; start < line.Length; start += width) yield return line.Substring(start, Math.Min(width, line.Length - start));
        }
    }
}
