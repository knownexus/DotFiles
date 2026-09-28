namespace GitWorkspace;

public sealed record DiscoveredRepository(string Path, string Name, string CommonDir, string? RegisteredName)
{
    public bool Registered => RegisteredName is not null;
}

public static class Discovery
{
    static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static List<string> Roots(Settings settings) => settings.Roots is { Count: > 0 }
        ? settings.Roots.Select(Path.GetFullPath).Distinct(PathComparer).ToList()
        : DefaultRoots();

    public static List<string> DefaultRoots()
    {
        const string windowsRepos = "C:/repos";
        return Directory.Exists(windowsRepos) ? [Path.GetFullPath(windowsRepos)] : [];
    }

    public static List<DiscoveredRepository> Scan(Settings settings) =>
        Roots(settings).SelectMany(root => ScanRoot(root, settings.Projects)).ToList();

    public static List<DiscoveredRepository> ScanRoot(string root, IReadOnlyList<Project>? registered = null)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var registrations = RegisteredCommons(registered ?? []);
        var seen = new HashSet<string>(PathComparer);
        List<DiscoveredRepository> repositories = [];
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var common = Git.ResolveRepository(directory);
                if (!seen.Add(common)) continue;
                registrations.TryGetValue(common, out var registeredName);
                repositories.Add(new(Path.GetFullPath(directory), Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), common, registeredName));
            }
            catch (InvalidOperationException) { }
        }
        return repositories;
    }

    public static Settings SettingsForRoot(string root, Settings known)
    {
        var projects = ScanRoot(root, known.Projects)
            .Select(r => new Project(r.RegisteredName ?? r.Name, r.Path))
            .ToList();
        return new() { Projects = projects, RefreshSeconds = known.RefreshSeconds, Editor = known.Editor, EditorArguments = [.. known.EditorArguments] };
    }

    static Dictionary<string, string> RegisteredCommons(IReadOnlyList<Project> projects)
    {
        var registered = new Dictionary<string, string>(PathComparer);
        foreach (var project in projects)
        {
            try
            {
                var common = Git.ResolveRepository(project.Path);
                registered.TryAdd(common, project.Name);
            }
            catch (InvalidOperationException) { }
        }
        return registered;
    }

    public static bool SameRepository(string leftCommon, string rightCommon) =>
        string.Equals(Normalize(leftCommon), Normalize(rightCommon), PathComparison);

    static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
