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
    }
}
