namespace GitWorkspace;

public sealed record CommitFilter(string Search = "", bool UnpushedOnly = false, string? Base = null);
public sealed record GraphRow(string Graph, CommitInfo? Commit, List<string> Refs, bool Unpushed);

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
}
