namespace GitWorkspace;

public sealed record HistoryChoice(string Action, CommitInfo? Commit = null);

public sealed partial class Dialogs
{
    public HistoryChoice? Commits(string title, string tip, Func<int, List<CommitInfo>> read, Func<CommitInfo, int, string> detail)
    {
        const int pageSize = 80;
        int page = 0, focus = 0, scroll = 0, previewScroll = 0, parent = 0;
        var commits = Busy("Reading commit history", () => read(0));
        string preview = "", previewId = "";
        while (true)
        {
            var selected = commits.ElementAtOrDefault(focus);
            string id = selected?.Hash + ":" + parent;
            if (id != previewId) { preview = selected is null ? "No commits on this branch." : detail(selected, parent); previewId = id; previewScroll = 0; }
            bool split = terminal.Width >= 118;
            int left = split ? Math.Clamp(terminal.Width / 2, 55, 90) : terminal.Width;
            var c = FullPage("History · " + title, terminal.Width < 100 ? "↑↓ · Enter patch · N/P pages · B branch · I rebase · Esc back" : "↑↓ select · Enter patch · N/P pages · B branch · I rebase from commit · Esc back");
            c.Text(2, 2, "Tip " + tip[..Math.Min(12, tip.Length)] + " · page " + (page + 1) + " · R refresh · M merge parent · PgUp/PgDn scroll diff", Ink.Muted, c.Width - 4);
            int rows = Math.Max(1, c.Height - 9);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, commits.Count); i++) {
                var commit = commits[i]; int y = 5 + i - scroll;
                if (i == focus) c.Fill(1, y, left - 2);
                c.Text(2, y, commit.ShortHash + "  " + commit.Date + "  " + (commit.Parents.Count > 1 ? "⑂ " : "") + commit.Subject, Ink.Text, left - 4, i == focus);
            }
            if (selected is not null) c.Text(2, c.Height - 4, selected.Author + " · " + selected.Subject, Ink.Muted, c.Width - 4);
            if (split) {
                for (int y = 4; y < c.Height - 4; y++) c.Text(left, y, "│", Ink.Muted);
                var lines = preview.Replace("\r", "").Split('\n');
                for (int i = 0; i < rows && previewScroll + i < lines.Length; i++) {
                    var line = lines[previewScroll + i]; c.Text(left + 2, 5 + i, line, line.StartsWith('+') ? Ink.Good : line.StartsWith('-') ? Ink.Bad : Ink.Text, c.Width - left - 3);
                }
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || key.Key == ConsoleKey.Backspace) return null;
            int oldFocus = focus;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, commits.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.Home) focus = 0;
            if (key.Key == ConsoleKey.End) focus = Math.Max(0, commits.Count - 1);
            if (key.Key == ConsoleKey.PageUp) previewScroll = Math.Max(0, previewScroll - rows);
            if (key.Key == ConsoleKey.PageDown) previewScroll = Math.Min(Math.Max(0, preview.Split('\n').Length - rows), previewScroll + rows);
            if (key.Key == ConsoleKey.Enter) Message("Commit · " + selected?.ShortHash, preview);
            if (key.Mouse && key.Wheel != 0) {
                if (split && key.X > left) previewScroll = Math.Clamp(previewScroll + key.Wheel, 0, Math.Max(0, preview.Split('\n').Length - rows));
                else focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, commits.Count - 1));
            }
            else if (key.Mouse && key.X < left && key.Y >= 5 && key.Y < 5 + rows && scroll + key.Y - 5 < commits.Count) focus = scroll + key.Y - 5;
            if (oldFocus != focus) parent = 0;
            char command = char.ToLowerInvariant(key.Character);
            if (command is 'b' or 'r') return new(command == 'b' ? "branch" : "refresh");
            if (command == 'i' && selected is not null) return new("rebase", selected);
            if (command == 'm' && selected?.Parents.Count > 1) { var pick = Choose("Compare merge against parent", selected.Parents.Select((p, i) => $"Parent {i + 1} · {p}").ToArray(), parent); if (pick.HasValue) parent = pick.Value; }
            if (command == 'n' && commits.Count == pageSize || command == 'p' && page > 0) {
                int next = command == 'n' ? page + 1 : page - 1;
                var loaded = Busy("Reading commit history", () => read(next * pageSize));
                if (loaded.Count > 0) { commits = loaded; page = next; focus = scroll = parent = 0; previewId = ""; }
            }
        }
    }
}
