using System.Text.RegularExpressions;

namespace GitWorkspace;

public enum DiffKind { Header, Meta, Hunk, Context, Added, Removed, Note }
public sealed record DiffLine(DiffKind Kind, int? Old, int? New, string Text);

public sealed record DiffSides(string Old, string New)
{
    public static DiffSides Generic { get; } = new("before", "after");
    public static DiffSides ForChange(Change change) => change.Area switch {
        ChangeArea.Staged => new("HEAD", "staged (index)"),
        ChangeArea.Untracked => new("", "new file (not in Git)"),
        ChangeArea.Conflict => new("each side of the conflict", "working tree (with conflict markers)"),
        _ => new("staged (index)", "working tree")
    };
    public static DiffSides ForCommit(string parentShort, string commitShort, string subject) =>
        new(parentShort.Length == 0 ? "" : "parent " + parentShort, $"{commitShort} \"{subject}\"");
    public string Legend => Old.Length == 0 ? "+ " + New : $"− old: {Old}   + new: {New}";
}

public static partial class DiffView
{
    [GeneratedRegex(@"^@@+ -(\d+)(?:,\d+)? (?:-\d+(?:,\d+)? )*\+(\d+)")] private static partial Regex HunkStart();

    public static bool LooksLikeDiff(string text) => text.StartsWith("diff --", StringComparison.Ordinal) || text.StartsWith("--- ", StringComparison.Ordinal) ||
        text.Contains("\ndiff --", StringComparison.Ordinal) || text.Contains("\n@@ ", StringComparison.Ordinal);

    public static List<DiffLine> Parse(string diff)
    {
        List<DiffLine> lines = []; int oldNo = 0, newNo = 0; bool inHunk = false, combined = false; string? from = null, renameFrom = null;
        foreach (var raw in diff.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --", StringComparison.Ordinal)) { inHunk = false; from = renameFrom = null; continue; }
            if (!inHunk || line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (line.StartsWith("index ", StringComparison.Ordinal) || line.StartsWith("similarity index", StringComparison.Ordinal)) continue;
                if (line.StartsWith("rename from ", StringComparison.Ordinal)) { renameFrom = line[12..]; continue; }
                if (line.StartsWith("rename to ", StringComparison.Ordinal)) { lines.Add(new(DiffKind.Header, null, null, renameFrom + " → " + line[10..])); continue; }
                if (line.StartsWith("--- ", StringComparison.Ordinal)) { from = Strip(line[4..], "a/"); continue; }
                if (line.StartsWith("+++ ", StringComparison.Ordinal)) {
                    if (renameFrom is not null) continue;
                    var to = Strip(line[4..], "b/");
                    lines.Add(new(DiffKind.Header, null, null, to == "/dev/null" ? from + " (deleted)" : from is null || from == "/dev/null" || from == to ? to : from + " → " + to));
                    continue;
                }
                var hunk = HunkStart().Match(line);
                if (hunk.Success) {
                    oldNo = int.Parse(hunk.Groups[1].Value); newNo = int.Parse(hunk.Groups[2].Value); inHunk = true; combined = line.StartsWith("@@@", StringComparison.Ordinal);
                    lines.Add(new(DiffKind.Hunk, null, null, line)); continue;
                }
                if (!inHunk) { lines.Add(new(DiffKind.Meta, null, null, line)); continue; }
            }
            if (line.StartsWith('\\')) { lines.Add(new(DiffKind.Note, null, null, line.TrimStart('\\', ' '))); continue; }
            if (combined) {
                // Combined (conflict) diffs have one marker column per parent; numbers are only meaningful for the result.
                var marks = line.Length >= 2 ? line[..2] : line.PadRight(2); var text = line.Length > 2 ? line[2..] : "";
                if (marks.Contains('-')) lines.Add(new(DiffKind.Removed, null, null, text));
                else lines.Add(new(marks.Contains('+') ? DiffKind.Added : DiffKind.Context, null, newNo++, text));
                continue;
            }
            char mark = line.Length > 0 ? line[0] : ' '; string body = line.Length > 0 ? line[1..] : "";
            if (mark == '+') lines.Add(new(DiffKind.Added, null, newNo++, body));
            else if (mark == '-') lines.Add(new(DiffKind.Removed, oldNo++, null, body));
            else if (mark == ' ') lines.Add(new(DiffKind.Context, oldNo++, newNo++, body));
            else lines.Add(new(DiffKind.Meta, null, null, line));
        }
        return lines;
    }
    static string Strip(string path, string prefix) => path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;

    public static List<StyledLine> Render(IReadOnlyList<DiffLine> lines, DiffSides? sides)
    {
        int digits = Math.Max(3, lines.Select(l => Math.Max(l.Old ?? 0, l.New ?? 0)).DefaultIfEmpty(0).Max().ToString().Length);
        string blank = new(' ', digits);
        List<StyledLine> output = [];
        if (sides is not null) { output.Add(new(sides.Legend, Ink.Heading)); output.Add(new("old".PadLeft(digits) + " " + "new".PadLeft(digits), Ink.Gutter)); }
        foreach (var l in lines)
        {
            string text = l.Text.Replace("\t", "    ");
            string old = l.Old?.ToString().PadLeft(digits) ?? blank, @new = l.New?.ToString().PadLeft(digits) ?? blank;
            output.Add(l.Kind switch {
                DiffKind.Header => new("━ " + text, Ink.Heading),
                DiffKind.Hunk => new(blank + " " + blank + " ┊ " + text, Ink.Hunk),
                DiffKind.Added => new(old + " " + @new + " + " + text, Ink.Added),
                DiffKind.Removed => new(old + " " + @new + " − " + text, Ink.Removed),
                DiffKind.Context => new(old + " " + @new + "   " + text, Ink.Text),
                DiffKind.Note => new(blank + " " + blank + "   (" + text + ")", Ink.Muted),
                _ => new(text, Ink.Muted)
            });
        }
        return output;
    }
}
