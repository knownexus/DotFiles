namespace GitWorkspace;

public sealed record DiscoveredRepo(string Name, string Path, string CommonDir, string? RegisteredAs);

public static class Discovery
{
    static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Unset means "use the default"; an explicitly empty list means "no roots".
    public static List<string> Roots(Settings settings) => settings.Roots is null
        ? (Directory.Exists("C:/repos") ? ["C:/repos"] : [])
        : settings.Roots.Select(Path.GetFullPath).Distinct(PathComparer).ToList();

    // Only folders that carry their own repository markers count; otherwise every
    // subfolder of a checkout would resolve to that checkout's repository.
    public static bool IsRepositoryRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")) ||
        Directory.Exists(Path.Combine(directory, ".git-main")) || Directory.Exists(Path.Combine(directory, ".bare")) ||
        File.Exists(Path.Combine(directory, "HEAD")) && Directory.Exists(Path.Combine(directory, "objects"));

    public static List<DiscoveredRepo> Scan(Settings settings, IEnumerable<string> roots)
    {
        var registered = new Dictionary<string, string>(PathComparer);
        foreach (var p in settings.Projects) { try { registered.TryAdd(Git.ResolveRepository(p.Path), p.Name); } catch (InvalidOperationException) { } }
        var seen = new HashSet<string>(PathComparer);
        List<DiscoveredRepo> found = [];
        foreach (var root in roots) found.AddRange(ScanRoot(root, registered, seen));
        return found;
    }

    // Missing, unreadable or non-directory roots are skipped silently: the caller may be
    // scanning several configured folders and one being gone should not fail the others.
    static List<DiscoveredRepo> ScanRoot(string root, Dictionary<string, string> registered, HashSet<string> seen)
    {
        List<string> directories;
        try
        {
            var full = Path.GetFullPath(root);
            if (!Directory.Exists(full)) return [];
            directories = Directory.EnumerateDirectories(full).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return []; }
        List<DiscoveredRepo> found = [];
        foreach (var directory in directories.Where(IsRepositoryRoot))
        {
            string common;
            try { common = Git.ResolveRepository(directory); }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { continue; }
            if (!seen.Add(common)) continue;
            found.Add(new(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Path.GetFullPath(directory).Replace('\\', '/'), common, registered.GetValueOrDefault(common)));
        }
        return found;
    }

    public static Settings SettingsForRoot(string root, Settings known)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        var found = Scan(new() { Projects = known.Projects }, [full]);
        // Registered names are kept as-is (they are already unique); an unregistered
        // folder whose name collides with one, case-insensitively, gets a numeric suffix.
        var used = new HashSet<string>(found.Where(r => r.RegisteredAs is not null).Select(r => r.RegisteredAs!), StringComparer.OrdinalIgnoreCase);
        List<Project> projects = [];
        foreach (var r in found)
        {
            string name = r.RegisteredAs ?? r.Name;
            if (r.RegisteredAs is null) { var suffix = 2; while (!used.Add(name)) name = $"{r.Name} {suffix++}"; }
            projects.Add(new Project(name, r.Path));
        }
        return new() { Projects = projects, RefreshSeconds = known.RefreshSeconds, Editor = known.Editor, EditorArguments = [.. known.EditorArguments] };
    }

    public static bool SameRepository(string leftCommon, string rightCommon) =>
        string.Equals(Normalize(leftCommon), Normalize(rightCommon), PathComparison);

    static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
