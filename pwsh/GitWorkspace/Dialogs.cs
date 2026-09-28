namespace GitWorkspace;

public sealed partial class Dialogs(ISurface terminal)
{
    // Full-screen panel (group editor, large reviews on small terminals).
    internal Canvas FullPage(string title, string hint)
    {
        var c = new Canvas(terminal.Width, terminal.Height);
        c.Text(2, 1, title, Ink.Heading, c.Width - 4); c.Line(3);
        c.Line(c.Height - 3); c.Text(2, c.Height - 2, hint, Ink.Accent, c.Width - 4);
        return c;
    }
    Canvas Backdrop()
    {
        var last = terminal.LastFrame;
        var c = last is not null && last.Width == terminal.Width && last.Height == terminal.Height ? last.Clone() : new Canvas(terminal.Width, terminal.Height);
        c.Dim(); return c;
    }
    (Canvas Canvas, Rect Inner) Frame(Canvas backdrop, string title, string hint, int width, int height)
    {
        var c = backdrop.Clone();
        int w = Math.Max(2, Math.Min(terminal.Width - 2, Math.Max(30, width))), h = Math.Max(2, Math.Min(terminal.Height - 2, Math.Max(6, height)));
        var box = new Rect(Math.Max(0, (terminal.Width - w) / 2), Math.Max(0, (terminal.Height - h) / 2), w, h);
        c.Box(box, title);
        c.Text(box.X + 2, box.Bottom - 1, " " + hint + " ", Ink.Accent, Math.Max(0, box.Width - 4));
        return (c, new Rect(box.X + 2, box.Y + 1, Math.Max(0, box.Width - 4), Math.Max(0, box.Height - 2)));
    }
    static bool Outside(Input key, Rect inner) => key.Mouse && key.Wheel == 0 && !new Rect(inner.X - 2, inner.Y - 1, inner.Width + 4, inner.Height + 2).Contains(key.X, key.Y);
    static bool Heading(string option) => option.StartsWith("## ", StringComparison.Ordinal);

    public int? Choose(string title, IReadOnlyList<string> options, int initial = 0)
    {
        if (options.Count(o => !Heading(o)) == 0) { Message(title, "No available choices."); return null; }
        var backdrop = Backdrop(); string search = "";
        int focus = Math.Clamp(initial, 0, options.Count - 1), scroll = 0;
        while (true)
        {
            var visible = Enumerable.Range(0, options.Count).Where(i => search.Length == 0 ? true : !Heading(options[i]) && options[i].Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            int width = options.Select(o => o.Length).DefaultIfEmpty(0).Max() + 8, height = visible.Count + 3 + (search.Length > 0 ? 1 : 0);
            var (c, inner) = Frame(backdrop, title, "↑↓ · / search · Enter select · Esc cancel", Math.Max(width, title.Length + 8), height);
            int top = inner.Y + (search.Length > 0 ? 1 : 0), rows = Math.Max(1, inner.Bottom - top);
            if (search.Length > 0) c.Text(inner.X, inner.Y, "/ " + search, Ink.Muted, inner.Width);
            focus = NextSelectable(visible, options, Math.Clamp(focus, 0, Math.Max(0, visible.Count - 1)), 1);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, visible.Count); i++) {
                var option = options[visible[i]]; int y = top + i - scroll;
                if (Heading(option)) { c.Text(inner.X, y, option[3..].ToUpperInvariant(), Ink.Heading, inner.Width); continue; }
                if (i == focus) c.Fill(inner.X - 1, y, inner.Width + 2);
                c.Text(inner.X + 1, y, option, Ink.Text, inner.Width - 1, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = NextSelectable(visible, options, Math.Max(0, focus - 1), -1);
            if (key.Key == ConsoleKey.DownArrow) focus = NextSelectable(visible, options, Math.Min(visible.Count - 1, focus + 1), 1);
            if (key.Key == ConsoleKey.Home) focus = NextSelectable(visible, options, 0, 1);
            if (key.Key == ConsoleKey.End) focus = NextSelectable(visible, options, visible.Count - 1, -1);
            if (key.Key == ConsoleKey.PageDown) focus = NextSelectable(visible, options, Math.Min(visible.Count - 1, focus + rows), 1);
            if (key.Key == ConsoleKey.PageUp) focus = NextSelectable(visible, options, Math.Max(0, focus - rows), -1);
            if (key.Character == '/') { search = Text("Search choices", "Filter the available choices. Empty text shows all.", search) ?? search; focus = scroll = 0; }
            if (key.Key == ConsoleKey.Enter && visible.Count > 0 && !Heading(options[visible[focus]])) return visible[focus];
            if (key.Mouse && key.Wheel != 0) focus = NextSelectable(visible, options, Math.Clamp(focus + key.Wheel, 0, Math.Max(0, visible.Count - 1)), Math.Sign(key.Wheel));
            else if (key.Mouse && key.Y >= top && key.Y < top + rows && scroll + key.Y - top < visible.Count && !Heading(options[visible[scroll + key.Y - top]])) return visible[scroll + key.Y - top];
        }
    }
    static int NextSelectable(List<int> visible, IReadOnlyList<string> options, int from, int direction)
    {
        for (int i = from; i >= 0 && i < visible.Count; i += direction) if (!Heading(options[visible[i]])) return i;
        for (int i = from; i >= 0 && i < visible.Count; i -= direction) if (!Heading(options[visible[i]])) return i;
        return 0;
    }
    public List<int>? Check(string title, IReadOnlyList<string> options, IEnumerable<int>? initial = null)
    {
        if (options.Count == 0) { Message(title, "No available worktrees."); return null; }
        var backdrop = Backdrop(); var selected = (initial ?? []).ToHashSet(); int focus = 0, scroll = 0;
        while (true)
        {
            var (c, inner) = Frame(backdrop, title, "Space checkbox · A all · Enter accept · Esc cancel", options.Max(o => o.Length) + 12, options.Count + 4);
            c.Text(inner.X, inner.Y, $"{selected.Count} selected", Ink.Muted, inner.Width);
            int top = inner.Y + 1, rows = Math.Max(1, inner.Height - 1);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, options.Count); i++) {
                int y = top + i - scroll;
                if (i == focus) c.Fill(inner.X - 1, y, inner.Width + 2);
                c.Text(inner.X + 1, y, (selected.Contains(i) ? "☑  " : "☐  ") + options[i], Ink.Text, inner.Width - 1, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(options.Count - 1, focus + 1);
            if (key.Key == ConsoleKey.Spacebar) { if (!selected.Remove(focus)) selected.Add(focus); }
            if (key.Key == ConsoleKey.A) { if (selected.Count == options.Count) selected.Clear(); else selected.UnionWith(Enumerable.Range(0, options.Count)); }
            if (key.Key == ConsoleKey.Enter) return selected.Order().ToList();
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, options.Count - 1);
            else if (key.Mouse && key.Y >= top && key.Y < top + rows && scroll + key.Y - top < options.Count) {
                focus = scroll + key.Y - top; if (!selected.Remove(focus)) selected.Add(focus);
            }
        }
    }
    public string? Text(string title, string prompt, string initial = "")
    {
        var backdrop = Backdrop(); string value = initial; int cursor = value.Length;
        while (true)
        {
            int width = Math.Min(terminal.Width - 2, 76);
            var wrapped = Wrap(prompt, Math.Max(10, width - 6)).ToList();
            var (c, inner) = Frame(backdrop, title, "Enter accept · Esc cancel · Ctrl+U clear", width, wrapped.Count + 5);
            foreach (var (line, index) in wrapped.Take(Math.Max(0, inner.Height - 2)).Select((s, i) => (s, i))) c.Text(inner.X, inner.Y + index, line, Ink.Muted, inner.Width);
            int y = inner.Bottom - 1; int offset = Math.Max(0, cursor - inner.Width + 3);
            c.Fill(inner.X, y, inner.Width); c.Text(inner.X, y, value[offset..], Ink.Text, inner.Width, true);
            c.Text(inner.X + cursor - offset, y, cursor < value.Length ? value[cursor].ToString() : "▏", Ink.Heading, 1, true);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.Enter) return value;
            if (key.Control && key.Key == ConsoleKey.U) { value = ""; cursor = 0; }
            else if (key.Key == ConsoleKey.Backspace && cursor > 0) value = value.Remove(--cursor, 1);
            else if (key.Key == ConsoleKey.Delete && cursor < value.Length) value = value.Remove(cursor, 1);
            else if (key.Key == ConsoleKey.LeftArrow) cursor = Math.Max(0, cursor - 1);
            else if (key.Key == ConsoleKey.RightArrow) cursor = Math.Min(value.Length, cursor + 1);
            else if (key.Key == ConsoleKey.Home) cursor = 0;
            else if (key.Key == ConsoleKey.End) cursor = value.Length;
            else if (!key.Mouse && !key.Control && !char.IsControl(key.Character) && key.Character != '\0') { value = value.Insert(cursor, key.Character.ToString()); cursor++; }
        }
    }
    public bool Review(string title, string text, string accept = "Execute", bool confirm = true) =>
        Review(title, DiffView.LooksLikeDiff(text) ? Mixed(text) : Styled.FromText(text), accept, confirm);
    // Plan previews mix prose with patches; only the patch part gets diff styling.
    static List<StyledLine> Mixed(string text)
    {
        int start = text.StartsWith("diff --", StringComparison.Ordinal) ? 0 : text.IndexOf("\ndiff --", StringComparison.Ordinal) + 1;
        if (start <= 0 && !text.StartsWith("diff --", StringComparison.Ordinal)) start = Math.Max(0, text.IndexOf("\n@@ ", StringComparison.Ordinal) + 1);
        return [.. Styled.FromText(text[..start].TrimEnd('\n')).Where(_ => start > 0), .. DiffView.Render(DiffView.Parse(text[start..]), DiffSides.Generic)];
    }
    public bool Review(string title, IReadOnlyList<StyledLine> lines, string accept = "Execute", bool confirm = true)
    {
        var backdrop = Backdrop(); int scroll = 0, horizontal = 0;
        while (true)
        {
            var (c, inner) = Frame(backdrop, title, confirm ? $"↑↓ PgUp PgDn scroll · Enter {accept} · Esc cancel" : "↑↓ PgUp PgDn scroll · Enter / Esc close",
                (int)(terminal.Width * 0.9), (int)(terminal.Height * 0.9));
            var visible = Styled.Fit(lines, inner.Width);
            int count = Math.Max(1, inner.Height);
            scroll = Math.Clamp(scroll, 0, Math.Max(0, visible.Count - count));
            horizontal = Math.Clamp(horizontal, 0, Math.Max(0, Styled.MaxWidth(visible) - inner.Width));
            Styled.Draw(c, inner, visible, scroll, horizontal);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return false;
            if (key.Key == ConsoleKey.Enter) return confirm;
            if (key.Key == ConsoleKey.UpArrow) scroll--;
            if (key.Key == ConsoleKey.DownArrow) scroll++;
            if (key.Key == ConsoleKey.PageUp) scroll -= count;
            if (key.Key == ConsoleKey.PageDown) scroll += count;
            if (key.Key == ConsoleKey.Home) scroll = 0;
            if (key.Key == ConsoleKey.End) scroll = visible.Count - count;
            if (key.Key == ConsoleKey.LeftArrow) horizontal -= Math.Max(4, inner.Width / 4);
            if (key.Key == ConsoleKey.RightArrow) horizontal += Math.Max(4, inner.Width / 4);
            if (key.Mouse) scroll += key.Wheel;
        }
    }
    public void Message(string title, string text) => Review(title, text, confirm: false);
    public T Busy<T>(string title, Func<T> work)
    {
        var backdrop = Backdrop(); var task = Task.Run(work); int frame = 0;
        while (!task.IsCompleted) {
            var (c, inner) = Frame(backdrop, title, "Working…", 44, 5);
            c.Text(inner.X, inner.Y + 1, new[] { "◐", "◓", "◑", "◒" }[frame++ % 4] + "  Please wait", Ink.Accent, inner.Width);
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
