namespace GitWorkspace;

public sealed record EditorError(string Message, string? MemberId = null);

public sealed partial class Dialogs
{
    public string? EditGroup(GroupDraft draft, string name, Task<List<DiscoveredRepo>>? discovery, Func<string, EditorError?>? validate = null,
        Func<Task<List<DiscoveredRepo>>>? rescan = null)
    {
        int focus = 0, scroll = 0; string search = ""; bool editingName = name == "New group"; int cursor = name.Length;
        var folded = new HashSet<string>(); string status = "";
        while (true)
        {
            if (discovery is { IsCompleted: true }) {
                status = discovery.IsCompletedSuccessfully ? "" : "Repository search failed: " + discovery.Exception?.GetBaseException().Message;
                if (discovery.IsCompletedSuccessfully) draft.AddDiscovered(discovery.Result); discovery = null;
            }
            var rows = draft.Rows(search, folded); focus = Math.Clamp(focus, 0, Math.Max(0, rows.Count - 1));
            var c = FullPage("Project group", editingName ? "Type a name · Tab to members · Enter save · Esc cancel"
                : "Space tick · W whole ↔ entries · B branches · ←→ fold · / filter · R rescan · Tab name · Enter save · Esc cancel");
            c.Text(2, 2, "Name: ", Ink.Muted); c.Fill(8, 2, Math.Min(40, c.Width - 10), editingName); c.Text(8, 2, name, Ink.Text, Math.Min(40, c.Width - 10), editingName);
            if (editingName) c.Text(8 + Math.Min(cursor, 39), 2, cursor < name.Length ? name[cursor].ToString() : "▏", Ink.Heading, 1, true);
            c.Text(2, 4, discovery is null ? status.Length > 0 ? status : (search.Length > 0 ? "/ " + search : $"{draft.Checked.Count} ticked") : "Searching for repositories…",
                status.Length > 0 ? Ink.Warning : Ink.Muted, c.Width - 4);
            int top = 5, height = Math.Max(1, c.Height - 3 - top);
            if (focus < scroll) scroll = focus; if (focus >= scroll + height) scroll = focus - height + 1;
            for (int i = scroll; i < Math.Min(scroll + height, rows.Count); i++) {
                var m = rows[i]; int y = top + i - scroll; bool on = i == focus && !editingName;
                if (on) c.Fill(1, y, c.Width - 2);
                c.Text(2, y, Label(draft, m, folded), m.Kind is "repo" or "new-repo" ? Ink.Heading : draft.Inherited(m) ? Ink.Muted : Ink.Text, c.Width - 4, on);
            }
            terminal.Draw(c);
            var key = discovery is null ? terminal.Read() : terminal.Poll(100); if (key is null) continue;
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.Tab) { editingName = !editingName; cursor = name.Length; continue; }
            if (key.Key == ConsoleKey.Enter) {
                var error = name.Trim().Length == 0 || name.Trim().Equals("All projects", StringComparison.OrdinalIgnoreCase)
                    ? new EditorError("Choose a group name other than All projects.") : validate?.Invoke(name.Trim());
                if (error is null) return name.Trim();
                status = error.Message;
                if (error.MemberId is not null && draft.Rows("", new HashSet<string>()).FindIndex(r => r.Id == error.MemberId) is var at && at >= 0) {
                    search = ""; folded.Clear(); focus = at; editingName = false;
                }
                continue;
            }
            if (editingName) {
                if (key.Control && key.Key == ConsoleKey.U) { name = ""; cursor = 0; }
                else if (key.Key == ConsoleKey.Backspace && cursor > 0) name = name.Remove(--cursor, 1);
                else if (key.Key == ConsoleKey.LeftArrow) cursor = Math.Max(0, cursor - 1);
                else if (key.Key == ConsoleKey.RightArrow) cursor = Math.Min(name.Length, cursor + 1);
                else if (!key.Mouse && !key.Control && !char.IsControl(key.Character) && key.Character != '\0') name = name.Insert(cursor++, key.Character.ToString());
                continue;
            }
            var current = rows.ElementAtOrDefault(focus);
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, rows.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.Home) focus = 0;
            if (key.Key == ConsoleKey.End) focus = Math.Max(0, rows.Count - 1);
            if (key.Key == ConsoleKey.PageDown) focus = Math.Min(Math.Max(0, rows.Count - 1), focus + height);
            if (key.Key == ConsoleKey.PageUp) focus = Math.Max(0, focus - height);
            if (current is not null && key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow && current.Kind == "repo") { if (!folded.Remove(current.Project)) folded.Add(current.Project); }
            char command = char.ToLowerInvariant(key.Character);
            if (command == '/') { search = Text("Filter group entries", "Search repositories, branches and worktree names.", search) ?? search; focus = scroll = 0; }
            if (command == 'r' && rescan is not null && discovery is null) { discovery = rescan(); status = ""; }
            if (current is not null && command == 'w') draft.ToggleWhole(current.Project);
            if (current is not null && command == 'b') {
                if (current.Kind == "new-repo") status = "Branches are available once the repository is registered; tick it to include the whole repo.";
                else if (!draft.BranchesShown.Remove(current.Project)) draft.BranchesShown.Add(current.Project);
            }
            bool toggle = key.Key == ConsoleKey.Spacebar;
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, rows.Count - 1));
            else if (key.Mouse && key.Y == 2) { editingName = true; cursor = name.Length; }
            else if (key.Mouse && key.Y >= top && key.Y < top + height && scroll + key.Y - top < rows.Count) { focus = scroll + key.Y - top; toggle = true; editingName = false; }
            if (toggle && rows.ElementAtOrDefault(focus) is { } target) { try { draft.Toggle(target); } catch (InvalidOperationException e) { status = e.Message; } }
        }
    }
    static string Label(GroupDraft draft, GroupMember m, ISet<string> folded)
    {
        string box(bool on) => on ? "☑ " : "☐ ";
        return m.Kind switch {
            "repo" => (folded.Contains(m.Project) ? "▸ " : "▾ ") + draft.State(m.Project) switch { RepoState.Whole => "☑ ", RepoState.Some => "◩ ", _ => "☐ " } + m.Project +
                (draft.State(m.Project) == RepoState.Whole ? "   · whole repo (includes future worktrees)" : "") + (m.Label.EndsWith("(unavailable)") ? "  (unavailable)" : ""),
            "new-repo" => "  " + box(draft.Checked.Contains(m.Id)) + m.Label + "   ★ not registered · " + m.Value,
            _ => "      " + (draft.Inherited(m) ? "☑ " : box(draft.Checked.Contains(m.Id))) + m.Label.Trim() + (draft.Inherited(m) ? "  (included by repo)" : "")
        };
    }
}
