namespace GitWorkspace;

public sealed partial class Dialogs
{
    public GroupDraft? EditGroup(GroupDraft draft, string name)
    {
        int focus = 0, scroll = 0; string search = "";
        while (true)
        {
            var visible = draft.Members.Where(m => (m.Project + " " + m.Label).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            focus = Math.Clamp(focus, 0, Math.Max(0, visible.Count - 1));
            var c = FullPage("Project group · " + name, "Space select · / search · Enter save membership · Esc cancel");
            c.Text(2, 4, "Mix entire repos, worktree paths and branches. Whole repos include future worktrees.", Ink.Muted, c.Width - 4);
            c.Text(2, 5, $"{draft.Checked.Count} explicit entries" + (search.Length > 0 ? " · / " + search : ""), Ink.Accent, c.Width - 4);
            int rows = Math.Max(1, c.Height - 11);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, visible.Count); i++) {
                var m = visible[i]; bool inherited = draft.Inherited(m); bool check = inherited || draft.Checked.Contains(m.Id);
                if (m.Kind == "repo") c.Line(7 + i - scroll, 1, c.Width - 2);
                if (i == focus) c.Fill(1, 7 + i - scroll, c.Width - 2);
                c.Text(2, 7 + i - scroll, (check ? "☑ " : "☐ ") + m.Label + (inherited ? " (included by repo)" : ""),
                    inherited ? Ink.Muted : m.Kind == "repo" ? Ink.Heading : Ink.Text, c.Width - 4, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.Enter) return draft;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.PageDown) focus = Math.Min(Math.Max(0, visible.Count - 1), focus + rows);
            if (key.Key == ConsoleKey.PageUp) focus = Math.Max(0, focus - rows);
            if (key.Character == '/') { search = Text("Filter group entries", "Search repositories, branches and worktree names.", search) ?? search; focus = scroll = 0; }
            bool toggle = key.Key == ConsoleKey.Spacebar;
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, visible.Count - 1));
            else if (key.Mouse && key.Y >= 7 && key.Y < 7 + rows && scroll + key.Y - 7 < visible.Count) { focus = scroll + key.Y - 7; toggle = true; }
            if (toggle && visible.Count > 0) { try { draft.Toggle(visible[focus]); } catch (InvalidOperationException e) { Message("Group membership", e.Message); } }
        }
    }
}
