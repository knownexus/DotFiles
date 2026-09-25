namespace GitWorkspace;

public sealed record StyledLine(string Text, Ink Ink);

public static class Styled
{
    public static void Draw(Canvas c, Rect area, IReadOnlyList<StyledLine> lines, int scroll)
    {
        for (int i = 0; i < area.Height && scroll + i < lines.Count; i++) {
            var line = lines[scroll + i];
            // Added/removed backgrounds span the row so the side is visible without reading the marker.
            string text = line.Ink is Ink.Added or Ink.Removed ? line.Text.PadRight(area.Width) : line.Text;
            c.Text(area.X, area.Y + i, text, line.Ink, area.Width);
        }
    }
    public static List<StyledLine> FromText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => new StyledLine(l.Replace("\t", "    "), Ink.Text)).ToList();
}
