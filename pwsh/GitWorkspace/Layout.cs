namespace GitWorkspace;

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Empty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => !Empty && x >= X && x < Right && y >= Y && y < Bottom;
}

// Rows: 1 title · 2 group tabs · 3 view bar · 4 rule · body · rule · context · footer.
public sealed record Layout(Rect Title, Rect Tabs, Rect ViewBar, Rect List, Rect Preview, Rect Context, Rect Footer, bool Split)
{
    public const int MinWidth = 66, MinHeight = 16, SplitFrom = 100;
    public static Layout For(int width, int height, double ratio)
    {
        int body = Math.Max(0, height - 9);
        bool split = width >= SplitFrom && body > 0;
        int left = split ? Math.Clamp((int)Math.Round(width * Math.Clamp(ratio, 0.35, 0.65)), 40, width - 30) : width;
        return new(new(0, 1, width, 1), new(0, 2, width, 1), new(0, 3, width, 1), new(0, 5, left, body),
            split ? new(left + 1, 5, width - left - 1, body) : new(0, 0, 0, 0),
            new(0, Math.Max(0, height - 3), width, 1), new(0, Math.Max(0, height - 2), width, 1), split);
    }
}
