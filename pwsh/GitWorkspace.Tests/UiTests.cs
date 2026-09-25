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
    }
}
