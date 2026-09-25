using System.Text;

namespace GitWorkspace;

public static class Ignore
{
    // Anchor selected paths at this worktree's root and quote wildcard characters.
    public static string Literal(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Any(c => c is '\r' or '\n' or '\0') || Path.IsPathFullyQualified(path) ||
            path.Split('/').Any(p => p is "." or "..")) throw new InvalidOperationException("Choose a file inside this worktree.");
        var pattern = new StringBuilder("/");
        foreach (char c in path) {
            if (c is '\\' or '*' or '?' or '[' or ']' or ' ' or '#' or '!') pattern.Append('\\');
            pattern.Append(c);
        }
        return pattern.ToString();
    }
    static byte[] Read(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The .gitignore is a symbolic link. Open its intended source in your editor instead.");
        return File.Exists(path) ? File.ReadAllBytes(path) : [];
    }
    public static Plan Files(Worktree tree, IEnumerable<Change> files)
    {
        var selected = files.ToList();
        if (selected.Count == 0 || selected.Any(c => c.Area != ChangeArea.Untracked))
            throw new InvalidOperationException("Select untracked files to ignore. Already tracked files stay tracked; adding an ignore rule does not remove them from Git.");
        return Patterns(tree, selected.Select(c => Literal(c.Path)), selected.Select(c => c.Path).ToList());
    }
    public static Plan Patterns(Worktree tree, IEnumerable<string> patterns, List<string>? selectedPaths = null)
    {
        Git.LoadStatus(tree);
        if (!tree.Usable) throw new InvalidOperationException("Choose an available worktree.");
        if (tree.Changes.Any(c => c.Path == ".gitignore" && c.Area == ChangeArea.Conflict))
            throw new InvalidOperationException("Resolve the .gitignore conflict before adding rules.");
        if (selectedPaths is not null && selectedPaths.Any(p => !tree.Changes.Any(c => c.Path == p && c.Area == ChangeArea.Untracked)))
            throw new InvalidOperationException("A selected file is no longer untracked. Refresh and review the selection again.");
        var rules = patterns.Distinct(StringComparer.Ordinal).ToList();
        if (rules.Count == 0 || rules.Any(p => string.IsNullOrWhiteSpace(p) || p.Any(c => c is '\r' or '\n' or '\0') || p.StartsWith('#')))
            throw new InvalidOperationException("Enter one Git ignore pattern per entry. Escape a leading # as \\# to match it literally.");
        string path = Path.Combine(tree.Path, ".gitignore"); bool existed = File.Exists(path); var original = Read(path);
        if (original.Contains((byte)0)) throw new InvalidOperationException("The .gitignore contains NUL bytes. Convert it to UTF-8 in your editor first.");
        string text = Encoding.UTF8.GetString(original);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // Do not deduplicate against earlier rules: a later negation can override
        // them. An identical final block, however, is already in the right place.
        var existingRules = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n').Where(s => s.Length > 0 && !s.StartsWith('#')).ToList();
        bool alreadyAdded = existingRules.TakeLast(rules.Count).SequenceEqual(rules);
        var suffix = (original.Length > 0 && original[^1] != (byte)'\n' ? newline : "") + string.Join(newline, rules) + newline;
        byte[] addition = Encoding.UTF8.GetBytes(suffix);
        string State() => Git.Head(tree) + "\0" + Git.Fingerprint(tree) + "\0" + File.Exists(path) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Read(path)));
        return new() {
            Tree = tree, Action = "Add to .gitignore", State = State, Expected = State(),
            Preview = $"File: {path}\n\n{(alreadyAdded ? "These rules are already at the end of this file:" : "Append rules:")}\n" + string.Join('\n', rules.Select(r => "+ " + r)) +
                "\n\nThis edits the selected worktree's .gitignore. Existing contents and staged changes are preserved. Review and commit the .gitignore change as usual. Already tracked files stay tracked.",
            Execute = () => {
                if (File.Exists(path) != existed || !Read(path).SequenceEqual(original)) return new(1, "", ".gitignore changed after the preview. Review it again.");
                if (alreadyAdded) return new(0, "Rules already present at the end of .gitignore.", "");
                using (var stream = new FileStream(path, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read)) {
                    using var copy = new MemoryStream(); stream.CopyTo(copy);
                    if (!copy.ToArray().SequenceEqual(original)) return new(1, "", ".gitignore changed after the preview. Review it again.");
                    stream.Write(addition); stream.Flush();
                }
                string result = $"Added {rules.Count} rule(s) to {path}. The .gitignore change is unstaged.";
                if (selectedPaths is { Count: > 0 }) {
                    var check = Git.RunInput(tree.Path, ["check-ignore", "--stdin", "-z"], string.Join('\0', selectedPaths) + '\0');
                    var ignored = check.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                    var remaining = selectedPaths.Where(p => !ignored.Contains(p)).ToList();
                    if (remaining.Count > 0) result += "\n\nThese paths are still not ignored; check nested .gitignore overrides:\n" + string.Join('\n', remaining);
                }
                return new(0, result, "");
            }
        };
    }
}
