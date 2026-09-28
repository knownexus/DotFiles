namespace GitWorkspace;

public sealed record StyledLine(string Text, Ink Ink, bool Wrap = false);

public static class Styled
{
    public static void Draw(Canvas c, Rect area, IReadOnlyList<StyledLine> lines, int scroll)
        => Draw(c, area, lines, scroll, 0);

    public static void Draw(Canvas c, Rect area, IReadOnlyList<StyledLine> lines, int scroll, int horizontal)
    {
        for (int i = 0; i < area.Height && scroll + i < lines.Count; i++) {
            var line = lines[scroll + i];
            // Added/removed backgrounds span the row so the side is visible without reading the marker.
            string text = horizontal > 0 && line.Text.Length > horizontal ? line.Text[horizontal..] : horizontal > 0 ? "" : line.Text;
            if (line.Ink is Ink.Added or Ink.Removed) text = text.PadRight(area.Width);
            c.Text(area.X, area.Y + i, text, line.Ink, area.Width);
        }
    }

    public static List<StyledLine> Fit(IReadOnlyList<StyledLine> lines, int width)
    {
        List<StyledLine> fit = [];
        width = Math.Max(1, width);
        foreach (var line in lines)
        {
            if (!line.Wrap || line.Text.Length <= width) { fit.Add(line); continue; }
            for (int start = 0; start < line.Text.Length; start += width)
                fit.Add(new(line.Text.Substring(start, Math.Min(width, line.Text.Length - start)), line.Ink, Wrap: true));
        }
        return fit;
    }

    public static int MaxWidth(IReadOnlyList<StyledLine> lines) => lines.Select(l => l.Text.Length).DefaultIfEmpty(0).Max();

    public static List<StyledLine> FromText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => new StyledLine(l.Replace("\t", "    "), Ink.Text, Wrap: true)).ToList();
}
