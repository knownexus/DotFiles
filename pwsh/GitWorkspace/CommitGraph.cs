namespace GitWorkspace;

// Base is a resolved commit ID; BaseLabel keeps the name the user typed for display.
public sealed record CommitFilter(string Search = "", bool UnpushedOnly = false, string? Base = null, string? BaseLabel = null);
public sealed record GraphRow(string Graph, CommitInfo? Commit, List<string> Refs, bool Unpushed);
public sealed record CommitFile(string Status, string Path, string? OldPath, int Added, int Removed);
public sealed record CommitDetail(string Header, List<CommitFile> Files);

public static partial class CommitGraph
{
    const char Record = '\u001e', Field = '\u001f';
    static IEnumerable<string> Tokens(string text)
    {
        var current = new System.Text.StringBuilder(); bool quoted = false;
        foreach (char ch in text) {
            if (ch == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(ch) && !quoted) { if (current.Length > 0) { yield return current.ToString(); current.Clear(); } continue; }
            current.Append(ch);
        }
        if (current.Length > 0) yield return current.ToString();
    }
    // Plain words match the message, @name the author, :path a file. Every pattern is a fixed string.
    public static List<string> SearchArguments(string search)
    {
        List<string> options = [], words = [], paths = [];
        foreach (var token in Tokens(search)) {
            if (token.StartsWith('@') && token.Length > 1) options.Add("--author=" + token[1..]);
            else if (token.StartsWith(':') && token.Length > 1) paths.Add(":(literal)" + token[1..]);
            else words.Add(token);
        }
        if (words.Count > 0) options.Add("--grep=" + string.Join(' ', words));
        if (options.Count > 0) options.AddRange(["-i", "--fixed-strings", "--all-match"]);
        return [.. options, "--", .. paths];
    }
    public static List<string> LogArguments(string tip, CommitFilter filter, string? upstream, int skip, int count)
    {
        var search = SearchArguments(filter.Search); int split = search.IndexOf("--");
        string range = filter.Base is { Length: > 0 } b ? b + ".." + tip : filter.UnpushedOnly && upstream is not null ? upstream + ".." + tip : tip;
        return ["log", "--graph", "--topo-order", "--date=relative", "--decorate=short",
            "--format=%x1e%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1f%P%x1f%D", $"--skip={skip}", $"--max-count={count}",
            .. search.Take(split), range, .. search.Skip(split)];
    }
    public static List<GraphRow> Read(string directory, string tip, CommitFilter filter, string? upstream, int skip = 0, int count = 200)
    {
        HashSet<string> unpushed = upstream is null ? [] : Git.Run(directory, "rev-list", upstream + ".." + tip, "--").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var result = Git.RunInput(directory, LogArguments(tip, filter, upstream, skip, count));
        if (!result.Ok) throw new InvalidOperationException(result.Detail);
        List<GraphRow> rows = [];
        foreach (var line in result.Output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = line.IndexOf(Record);
            if (at < 0) { rows.Add(new(line, null, [], false)); continue; }
            var f = line[(at + 1)..].Split(Field);
            if (f.Length < 7) { rows.Add(new(line[..at], null, [], false)); continue; }
            var commit = new CommitInfo(f[0], f[1], f[2], f[3], f[4], f[5].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList());
            var refs = f[6].Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(r => r.Replace("HEAD -> ", "HEAD → ")).ToList();
            rows.Add(new(line[..at], commit, refs, unpushed.Contains(f[0])));
        }
        return rows;
    }
    public static string Base(string directory, CommitInfo commit, int parent)
    {
        if (commit.Parents.Count == 0) return Git.RunInput(directory, ["hash-object", "-t", "tree", "--stdin"], "").Require().Output.Trim();
        if (parent < 0 || parent >= commit.Parents.Count) throw new ArgumentOutOfRangeException(nameof(parent));
        return commit.Parents[parent];
    }
    public static List<CommitFile> Files(string directory, CommitInfo commit, int parent = 0)
    {
        string from = Base(directory, commit, parent);
        var status = Git.Run(directory, "diff", "--no-ext-diff", "--no-color", "--find-renames", "-z", "--name-status", from, commit.Hash, "--").Require().Output.Split('\0');
        var counts = Git.Run(directory, "diff", "--no-ext-diff", "--no-color", "--find-renames", "-z", "--numstat", from, commit.Hash, "--").Require().Output.Split('\0');
        List<CommitFile> files = [];
        for (int s = 0, n = 0; s < status.Length && status[s].Length > 0; s++)
        {
            string code = status[s]; bool renamed = code[0] is 'R' or 'C';
            string? old = renamed ? status[++s] : null; string path = status[++s];
            var numbers = counts[n].Split('\t'); n += numbers.Length == 3 && numbers[2].Length == 0 ? 3 : 1;
            int Number(string v) => int.TryParse(v, out var value) ? value : -1;
            files.Add(new(code[..1], path, old, Number(numbers[0]), numbers.Length > 1 ? Number(numbers[1]) : -1));
        }
        return files;
    }
    public static CommitDetail Detail(string directory, CommitInfo commit, int parent = 0) =>
        new(Git.Run(directory, "show", "--no-patch", "--format=fuller", "--decorate=short", commit.Hash, "--").Require().Output.TrimEnd(), Files(directory, commit, parent));
    public static string FileDiff(string directory, CommitInfo commit, int parent, CommitFile file)
    {
        List<string> args = ["diff", "--no-ext-diff", "--no-textconv", "--no-color", "--find-renames", Base(directory, commit, parent), commit.Hash, "--"];
        if (file.OldPath is not null) args.Add(file.OldPath);
        args.Add(file.Path);
        var output = Git.RunInput(directory, args).Require().Output;
        return output.Length == 0 ? "No textual diff (mode change or binary file)." : output;
    }
}
