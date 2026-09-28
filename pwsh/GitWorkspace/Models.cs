using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitWorkspace;

public sealed record Project(string Name, string Path);
public sealed record BranchSelection(string Project, string Ref);
public sealed record WorkspaceGroup(string Name, List<string> Projects, List<string> Worktrees)
{
    public List<BranchSelection> Branches { get; init; } = [];
    public bool Includes(Worktree tree) => Projects.Contains(tree.Project) || Worktrees.Contains(tree.Id) ||
        Branches.Any(b => b.Project == tree.Project && b.Ref == tree.ActiveBranchRef);
    public bool Includes(BranchInfo branch) => Projects.Contains(branch.Project) || Branches.Any(b => b.Project == branch.Project && b.Ref == branch.Ref);
}
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public List<Project> Projects { get; set; } = [];
    public List<WorkspaceGroup> Groups { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Roots { get; set; }
    public int RefreshSeconds { get; set; } = 5;
    public string Editor { get; set; } = "vim";
    public List<string> EditorArguments { get; set; } = [];
}

public sealed class ViewState
{
    public string Group { get; set; } = "All projects";
    public string? Repo { get; set; }
    public bool ShowUntracked { get; set; } = true;
    public HashSet<string> Collapsed { get; set; } = [];
    public double SplitRatio { get; set; } = 0.5;
    public int TabOffset { get; set; }
}

public static class Config
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string DefaultPath => Environment.GetEnvironmentVariable("GWS_CONFIG")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "git-workspaces.json");
    public static Settings Read(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty workspace configuration.");
        if (settings.Version != 1) throw new InvalidDataException($"Unsupported configuration version: {settings.Version}");
        if (settings.Projects.Any(p => string.IsNullOrWhiteSpace(p.Name) || !System.IO.Path.IsPathFullyQualified(p.Path)))
            throw new InvalidDataException("Each project needs a name and an absolute path.");
        if (settings.Projects.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Projects.Count)
            throw new InvalidDataException("Project names must be unique.");
        return settings;
    }
    public static T ReadOr<T>(string path, T fallback)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return fallback; }
    }
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public enum ChangeArea { Unstaged, Staged, Conflict, Untracked }
public sealed record Change(string Path, string? OriginalPath, string XY, ChangeArea Area, string Submodule = "N...")
{
    public string Id => $"{Area}\0{Path}";
    public string Label => Area switch {
        ChangeArea.Staged => "Staged", ChangeArea.Conflict => "Conflict", ChangeArea.Untracked => "Untracked",
        _ => XY.Contains('D') ? "Deleted" : XY.Contains('R') ? "Renamed" : "Modified"
    };
    public string[] Paths => OriginalPath is null ? [Path] : [Path, OriginalPath];
}

public sealed class Worktree
{
    public required string Project { get; init; }
    public required string ProjectRoot { get; init; }
    public required string Path { get; init; }
    public required string CommonDir { get; init; }
    public string Branch { get; set; } = "(detached)";
    public string? RebasingBranch { get; set; }
    public string? ActiveBranchRef => RebasingBranch ?? (Branch == "(detached)" ? null : "refs/heads/" + Branch);
    public string BranchLabel => RebasingBranch is null ? Branch : RebasingBranch[11..] + " (rebasing)";
    public string Head { get; set; } = "";
    public string? Upstream { get; set; }
    public bool UpstreamGone { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public string Operation { get; set; } = "";
    public string Error { get; set; } = "";
    public string? Locked { get; set; }
    public string? Prunable { get; set; }
    public DateTime? FetchedAt { get; set; }
    public List<Change> Changes { get; set; } = [];
    public string Id => System.IO.Path.GetFullPath(Path).Replace('\\', '/');
    public string Name => System.IO.Path.GetRelativePath(ProjectRoot, Path).Replace('\\', '/') is "." ? "checkout" : System.IO.Path.GetRelativePath(ProjectRoot, Path).Replace('\\', '/');
    public string Title => $"{Project} / {Name} → {BranchLabel}";
    public bool Usable => Error.Length == 0 && Directory.Exists(Path);
    public string Summary => Error.Length > 0 ? Error : $"{Changes.Select(c => c.Path).Distinct().Count()} changed · {Changes.Count(c => c.Area == ChangeArea.Staged)} staged · ↑{Ahead} ↓{Behind}" +
        (Upstream is null ? " · no upstream" : UpstreamGone ? " · upstream missing" : "") + (Operation.Length > 0 ? $" · {Operation}" : "");
    public string FileId(Change change) => Id + "\0" + change.Id;
}

public sealed record BranchInfo(string Project, string CommonDir, string Ref, string Head, string Subject, string? Upstream = null)
{
    public string Name => Ref.StartsWith("refs/heads/") ? Ref[11..] : Ref.StartsWith("refs/remotes/") ? Ref[13..] : Ref;
    public string Id => "b:" + Project + ":" + Ref;
}
public sealed record CommitInfo(string Hash, string ShortHash, string Author, string Date, string Subject, List<string> Parents);
public sealed record Snapshot(List<Worktree> Worktrees, List<string> Errors, DateTime TakenAt)
{
    public List<BranchInfo> Branches { get; init; } = [];
}
public sealed record GitResult(int Code, string Output, string Error)
{
    public bool Ok => Code == 0;
    public string Detail => string.Join('\n', new[] { Output.Trim(), Error.Trim() }.Where(s => s.Length > 0));
    public GitResult Require() { if (!Ok) throw new InvalidOperationException(Detail); return this; }
}
public sealed record OperationResult(string Target, string Action, bool Success, string Detail, DateTime Time);
