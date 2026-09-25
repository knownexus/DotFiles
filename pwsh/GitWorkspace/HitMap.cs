namespace GitWorkspace;

public sealed record Hit(string Kind, int Index = -1, string? Id = null);

// Filled while a frame renders; mouse input is resolved against the last drawn frame.
public sealed class HitMap
{
    readonly List<(Rect Area, Hit Hit)> regions = [];
    public int Count => regions.Count;
    public void Add(Rect area, Hit hit) { if (!area.Empty) regions.Add((area, hit)); }
    public void Add(int x, int y, int width, Hit hit) => Add(new Rect(x, y, width, 1), hit);
    public Hit? At(int x, int y)
    {
        for (int i = regions.Count - 1; i >= 0; i--) if (regions[i].Area.Contains(x, y)) return regions[i].Hit;
        return null;
    }
    public void Clear() => regions.Clear();
}
