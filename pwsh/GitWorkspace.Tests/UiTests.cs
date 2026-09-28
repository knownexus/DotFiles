using GitWorkspace;

static partial class Tests
{
    static void UiTests()
    {
        Test("layout regions split from 100 columns and clamp the ratio", () => {
            var wide = Layout.For(120, 32, 0.5);
            Check(wide.Split, "Split at 120"); Equal(60, wide.List.Width, "List width"); Equal(61, wide.Preview.X, "Preview x");
            Equal(59, wide.Preview.Width, "Preview width"); Equal(5, wide.List.Y, "Body top"); Equal(23, wide.List.Height, "Body height");
            Equal(30, wide.Footer.Y, "Footer y"); Equal(29, wide.Context.Y, "Context y");
            var narrow = Layout.For(80, 24, 0.5); Check(!narrow.Split && narrow.Preview.Empty, "No split below 100"); Equal(80, narrow.List.Width, "Full list");
            Equal(104, Layout.For(160, 40, 0.9).List.Width, "Ratio clamps to 0.65");
            Equal(40, Layout.For(100, 20, 0.35).List.Width, "List keeps 40 columns");
            var tiny = Layout.For(10, 3, 0.5); Check(tiny.List.Height == 0 && !tiny.Split, "Tiny terminal has an empty body");
        });
        Test("hit map returns the last registered region and ignores empty areas", () => {
            var hits = new HitMap();
            hits.Add(new Rect(0, 5, 60, 1), new Hit("row", 0));
            hits.Add(3, 5, 3, new Hit("check", 0));
            hits.Add(new Rect(10, 10, 0, 1), new Hit("action", Id: "never"));
            Equal("check", hits.At(4, 5)?.Kind, "Checkbox wins over row");
            Equal("row", hits.At(20, 5)?.Kind, "Row");
            Check(hits.At(20, 6) is null, "Outside every region");
            Equal(2, hits.Count, "Empty region skipped");
            hits.Clear(); Check(hits.At(4, 5) is null, "Cleared");
        });
        Test("mouse decoder reports left, right, double click and wheel once per press", () => {
            uint previous = 0;
            var left = MouseDecoder.Decode(1, 0, 4, 6, ref previous); Equal(MouseButton.Left, left?.Button, "Left press"); Equal(4, left?.X, "X");
            Check(MouseDecoder.Decode(1, 1, 5, 6, ref previous) is null, "Held button while moving is not a new click");
            Check(MouseDecoder.Decode(0, 0, 5, 6, ref previous) is null, "Release");
            Equal(MouseButton.Right, MouseDecoder.Decode(2, 0, 1, 1, ref previous)?.Button, "Right press");
            MouseDecoder.Decode(0, 0, 1, 1, ref previous);
            Check(MouseDecoder.Decode(1, 2, 1, 1, ref previous)?.DoubleClick == true, "Double click");
            Equal(-3, MouseDecoder.Decode(unchecked((uint)(120 << 16)), 4, 1, 1, ref previous)?.Wheel, "Wheel up");
        });
        Test("canvas boxes clip safely, dim a copy and styled diff lines fill their row", () => {
            var c = new Canvas(30, 8); c.Text(0, 0, "BACKDROP");
            var copy = c.Clone(); copy.Dim(); Equal(Ink.Gutter, copy.InkAt(0, 0), "Dimmed copy"); Equal(Ink.Text, c.InkAt(0, 0), "Original untouched");
            copy.Box(new Rect(2, 1, 12, 4), "Title");
            var lines = copy.Plain().Split(Environment.NewLine);
            Check(lines[1].StartsWith("  ┌─ Title ──┐"), "Top border: " + lines[1]); Check(lines[4].StartsWith("  └──────────┘"), "Bottom border");
            Check(lines[2].Substring(3, 10).Trim().Length == 0, "Interior cleared");
            new Canvas(5, 2).Box(new Rect(-3, -3, 40, 40), "x"); new Canvas(5, 2).Box(new Rect(0, 0, 1, 1)); // no exception
            var styled = new Canvas(20, 3); Styled.Draw(styled, new Rect(0, 0, 20, 3), [new("+ a", Ink.Added), new("plain", Ink.Text)], 0);
            Equal(Ink.Added, styled.InkAt(19, 0), "Added background spans the row");
            Equal(3, Styled.FromText("a\r\nb\nc").Count, "Text lines");
            Equal(3, Styled.Fit(Styled.FromText("abcdefghijklmnopqrstuvwxyz"), 10).Count, "Prose wraps");
            Equal("klmnopqrst", Styled.Fit([new("abcdefghijklmnopqrstuvwxyz", Ink.Text)], 10).Single().Text[10..20], "Diff-like lines stay intact");
        });
        Test("diff view numbers old and new lines and states each side", () => {
            string diff = "diff --git a/src/A.cs b/src/A.cs\r\nindex 1..2 100644\r\n--- a/src/A.cs\r\n+++ b/src/A.cs\r\n@@ -10,3 +10,4 @@ class A\r\n keep\r\n-old line\r\n+new line\r\n+added\r\n tail\r\n\\ No newline at end of file\r\n";
            var lines = DiffView.Parse(diff);
            var removed = lines.Single(l => l.Kind == DiffKind.Removed); Equal(11, removed.Old, "Removed old number"); Check(removed.New is null, "Removed has no new number");
            var added = lines.Where(l => l.Kind == DiffKind.Added).ToList(); Equal(11, added[0].New, "Added new number"); Equal(12, added[1].New, "Second added");
            var tail = lines.Last(l => l.Kind == DiffKind.Context); Equal(12, tail.Old, "Context old"); Equal(13, tail.New, "Context new");
            Check(lines.Any(l => l.Kind == DiffKind.Note), "No-newline note"); Check(lines.All(l => !l.Text.Contains('\r')), "CR stripped");
            Equal("src/A.cs", lines.First(l => l.Kind == DiffKind.Header).Text, "File header");
            var styled = DiffView.Render(lines, DiffSides.ForChange(new("src/A.cs", null, ".M", ChangeArea.Unstaged)));
            Equal("− old: staged (index)   + new: working tree", styled[0].Text, "Legend");
            Check(styled.Any(s => s.Ink == Ink.Removed && s.Text.Contains(" 11 ") && s.Text.Contains("− old line")), "Removed rendering");
            Check(styled.Any(s => s.Ink == Ink.Added && s.Text.Contains("+ new line")), "Added rendering");
            Check(!styled.Any(s => s.Text.StartsWith("+++") || s.Text.StartsWith("---")), "Raw file markers hidden");
            Equal("− old: HEAD   + new: staged (index)", DiffSides.ForChange(new("a", null, "M.", ChangeArea.Staged)).Legend, "Staged legend");
            Equal("+ new file (not in Git)", DiffSides.ForChange(new("a", null, "??", ChangeArea.Untracked)).Legend, "Untracked legend");
            Equal("− old: parent 8175ff1   + new: a928430 \"Add QR\"", DiffSides.ForCommit("8175ff1", "a928430", "Add QR").Legend, "Commit legend");
            var rename = DiffView.Parse("diff --git a/old.txt b/new.txt\nsimilarity index 90%\nrename from old.txt\nrename to new.txt\n--- a/old.txt\n+++ b/new.txt\n");
            Equal("old.txt → new.txt", rename.First(l => l.Kind == DiffKind.Header).Text, "Rename header");
            var combined = DiffView.Parse("diff --cc c.txt\n@@@ -1,1 -1,1 +1,3 @@@\n++<<<<<<< ours\n +mine\n- theirs\n");
            Check(combined.Count(l => l.Kind == DiffKind.Added) == 2 && combined.Any(l => l.Kind == DiffKind.Removed), "Combined conflict diff");
        });
        Test("untracked and tracked diffs parse with real line numbers", () => {
            var repo = Repo(); Write(repo, "new.txt", "one\r\ntwo\n"); Write(repo, "file.txt", original.Replace("line 2\n", "line two\n"));
            var tree = Tree(repo);
            var untracked = DiffView.Parse(Git.Diff(tree, tree.Changes.Single(c => c.Area == ChangeArea.Untracked)));
            Equal(2, untracked.Count(l => l.Kind == DiffKind.Added), "Untracked lines"); Equal(2, untracked.Last(l => l.Kind == DiffKind.Added).New, "Untracked numbering");
            Equal("new.txt", untracked.First(l => l.Kind == DiffKind.Header).Text, "Untracked header");
            var tracked = DiffView.Parse(Git.Diff(tree, tree.Changes.Single(c => c.Area == ChangeArea.Unstaged)));
            Equal(2, tracked.Single(l => l.Kind == DiffKind.Removed).Old, "Tracked old number"); Equal(2, tracked.Single(l => l.Kind == DiffKind.Added).New, "Tracked new number");
        });
        Test("popups draw over the dimmed screen, skip headings and survive tiny terminals", () => {
            var backdrop = new Canvas(80, 24); backdrop.Text(0, 0, "BACKDROP");
            var surface = new ScriptedSurface(80, 24, new Input(ConsoleKey.DownArrow), new Input(ConsoleKey.Enter)); surface.SetBackdrop(backdrop);
            var pick = new Dialogs(surface).Choose("More actions", ["## Files", "Stage blocks", "## Stash", "Stash changes"]);
            Equal(3, pick, "Down skips the heading");
            Check(surface.Frames[0].StartsWith("BACKDROP"), "Backdrop visible behind popup"); Check(surface.Frames[0].Contains("┌─ More actions"), "Popup border");
            var outside = new ScriptedSurface(80, 24, new Input(X: 0, Y: 0, Button: MouseButton.Left));
            Check(new Dialogs(outside).Choose("Pick", ["a", "b"]) is null, "Click outside cancels");
            Check(new Dialogs(new ScriptedSurface(20, 6, new Input(ConsoleKey.Escape))).Choose("Tiny", ["a"]) is null, "Tiny terminal");
            Check(new Dialogs(new ScriptedSurface(20, 6, new Input(ConsoleKey.Escape))).Text("Tiny", "A long prompt that will not fit anywhere") is null, "Tiny text prompt");
            var review = new ScriptedSurface(100, 30, new Input(ConsoleKey.Enter));
            Check(new Dialogs(review).Review("Diff", "diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-x\n+y\n"), "Review accepts");
            Check(review.Frames[0].Contains("− old: before   + new: after") && review.Frames[0].Contains("+ y") && review.Frames[0].Contains("− x"), "Review renders diff sides");
            var mixed = new ScriptedSurface(80, 24, new Input(ConsoleKey.Enter));
            Check(new Dialogs(mixed).Review("Mixed", "Preview this very long sentence before the patch so it wraps inside the popup instead of being clipped away from view.\n\ndiff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-x\n+y\n"), "Mixed review accepts");
            Check(mixed.Frames[0].Contains("− old: before   + new: after"), "Mixed patch has side legend");
            Check(mixed.Frames[0].Contains("instead of being clipped"), "Long prose wraps");
            var longLine = new ScriptedSurface(42, 14, new Input(ConsoleKey.RightArrow), new Input(ConsoleKey.RightArrow), new Input(ConsoleKey.RightArrow), new Input(ConsoleKey.Enter));
            Check(!new Dialogs(longLine).Review("Long diff", "diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-old\n+012345678901234567890123456789TAIL\n", confirm: false), "Long diff closes");
            Check(longLine.Frames.Any(f => f.Contains("TAIL")), "Horizontal scroll reveals long diff text");
            var reviewOutside = new ScriptedSurface(80, 24, new Input(X: 0, Y: 0, Button: MouseButton.Left));
            Check(!new Dialogs(reviewOutside).Review("Diff", "diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-x\n+y\n"), "Click outside closes Review");
        });
        Test("main screen uses status letters and registers row, checkbox and view hits", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            var hits = new HitMap(); var layout = Layout.For(120, 32, 0.5);
            string text = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(text.Contains("[All projects") && text.Contains(" Secure QR") && text.Contains(" Maintenance") && text.Contains(" + "), "Group tabs render: " + text);
            Equal("tab", hits.At(3, layout.Tabs.Y)?.Kind, "All projects tab hit");
            Equal("new", Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).First(h => h?.Id == "new")?.Id, "New group tab hit");
            Check(text.Contains(" M  src/EntryService.cs"), "Unstaged status letter"); Check(text.Contains("M   Controllers/QrCode.cs"), "Staged status letter");
            Check(!text.Contains("Modified"), "Word labels removed");
            int fileRow = view.Rows.FindIndex(r => r.Change?.Path == "src/EntryService.cs"); int y = layout.List.Y + fileRow;
            var check = Enumerable.Range(0, layout.List.Width).Select(x => hits.At(x, y)).First(h => h?.Kind == "check");
            Equal(fileRow, check!.Index, "Checkbox hit index"); Equal("row", hits.At(layout.List.Width - 3, y)?.Kind, "Row hit");
            Equal("view", hits.At(3, layout.ViewBar.Y)?.Kind, "View bar hit"); Equal("preview", hits.At(layout.Preview.X + 5, layout.Preview.Y + 3)?.Kind, "Preview hit");
            Equal("??", Screen.StatusCode(new("x", null, "??", ChangeArea.Untracked)), "Untracked code");
            Equal("UU", Screen.StatusCode(new("x", null, "UU", ChangeArea.Conflict)), "Conflict code");
            Equal("R ", Screen.StatusCode(new("x", "y", "R.", ChangeArea.Staged)), "Staged rename");
            Equal(" D", Screen.StatusCode(new("x", null, ".D", ChangeArea.Unstaged)), "Unstaged delete");
            view.Navigate("AccessControl"); hits.Clear();
            string repoText = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(repoText.Contains("⌂ AccessControl") && repoText.Contains("×"), "Repo View tab renders");
            Equal("repo-close", Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).First(h => h?.Id == "repo-close")?.Id, "Repo close hit");
        });
        Test("group tab overflow exposes scroll controls and keeps the active tab visible", () => {
            var settings = Demo.Settings();
            settings.Groups.Clear();
            for (int i = 1; i <= 14; i++) settings.Groups.Add(new("Group " + i.ToString("00"), ["AccessControl"], []));
            var state = new ViewState();
            var view = new WorkspaceView(settings, state); view.Update(Demo.Snapshot());
            var layout = Layout.For(80, 24, 0.5); var hits = new HitMap();
            string first = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(first.Contains("[All projects") && first.Contains("›") && !first.Contains("Group 14"), "Initial overflow: " + first);
            var right = Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).First(h => h?.Id == "tab-right");
            Equal("tab-right", right?.Id, "Right scroll hit");
            state.TabOffset = right!.Index; hits.Clear();
            string middle = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(middle.Contains("‹") && middle.Contains("Group 02"), "Scrolled tabs: " + middle);
            Equal("tab-left", Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).First(h => h?.Id == "tab-left")?.Id, "Left scroll hit");
            state.Group = "Group 14"; state.TabOffset = 0; hits.Clear();
            string active = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(active.Contains("[Group 14") && state.TabOffset > 0, "Active tab forced visible: " + active);
        });
        Test("footer keys follow the focused row", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            string Keys() => string.Join(",", Footer.For(view, null).Select(k => k.Key));
            view.Focus = view.Rows.FindIndex(r => r.Kind == "file"); Check(Keys().StartsWith(" ,d,s,u,c,o"), "File keys: " + Keys());
            view.Focus = view.Rows.FindIndex(r => r.Kind == "worktree"); Check(Keys().Contains("h") && !Keys().Contains(",s,"), "Worktree keys: " + Keys());
            view.WorktreeMode = true; view.Rebuild(); view.Focus = view.Rows.FindIndex(r => r.Kind == "worktree"); Check(Keys().Contains("i"), "Worktree mode rebase");
            view.Navigate("AccessControl"); view.Focus = view.Rows.FindIndex(r => r.Kind == "branch"); Equal("\r,b,p,?,q", Keys(), "Branch row keys");
            Check(Footer.For(view, null).All(k => k.Key.Length == 1), "Single-character ids");
        });
        Test("change previews carry a side legend and stale-selection warning", () => {
            var repo = Repo(); Write(repo, "file.txt", "changed\n"); Git.Run(repo, "add", ".").Require(); Write(repo, "file.txt", "changed again\n");
            var tree = Tree(repo); var staged = tree.Changes.Single(c => c.Area == ChangeArea.Staged);
            var lines = App.PreviewFor(tree, staged, Git.Diff(tree, staged), false);
            Check(lines.Any(l => l.Text == "− old: HEAD   + new: staged (index)"), "Staged legend");
            Check(lines.Any(l => l.Ink == Ink.Added && l.Text.EndsWith("+ changed")), "Staged addition");
            var warned = App.PreviewFor(tree, staged, Git.Diff(tree, staged), true); Equal(Ink.Warning, warned[0].Ink, "Stale warning first");
        });
    }
}
