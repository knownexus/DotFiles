namespace GitWorkspace;

public sealed record Patch(string Header, List<string> Hunks, string Original);
public static class Patches
{
    public static Patch Read(Worktree tree, Change change)
    {
        List<string> args = ["diff", "--no-ext-diff", "--no-textconv", "--no-color", "--src-prefix=a/", "--dst-prefix=b/", "--unified=3", "--full-index"];
        if (change.Area == ChangeArea.Staged) args.Add("--cached");
        args.Add("--"); args.Add(change.Path);
        string diff = Git.RunInput(tree.Path, args).Require().Output;
        if (change.OriginalPath is not null || change.Submodule.StartsWith('S') || diff.Contains("Binary files") || diff.Contains("new file mode") || diff.Contains("deleted file mode") || diff.Contains("old mode"))
            throw new InvalidOperationException("Stage or unstage this file as a whole (binary, rename, mode change, new/deleted file or submodule).");
        int first = diff.IndexOf("\n@@ ", StringComparison.Ordinal);
        if (first < 0) throw new InvalidOperationException("No text blocks available.");
        string header = diff[..(first + 1)]; var hunks = new List<string>(); int start = first + 1;
        while (start < diff.Length) {
            int next = diff.IndexOf("\n@@ ", start, StringComparison.Ordinal);
            if (next < 0) { hunks.Add(diff[start..]); break; }
            hunks.Add(diff[start..(next + 1)]); start = next + 1;
        }
        return new(header, hunks, diff);
    }
    public static Plan Plan(Worktree tree, Change change, Patch patch, List<int> indices, bool reverse)
    {
        string selected = patch.Header + string.Concat(indices.Order().Select(i => patch.Hunks[i]));
        string State() => Git.Head(tree) + Read(tree, change).Original;
        string expected = Git.Head(tree) + patch.Original;
        return new() { Tree = tree, Action = reverse ? "Unstage blocks" : "Stage blocks", Preview = selected, State = State, Expected = expected,
            Execute = () => {
                List<string> args = ["apply", "--cached"];
                if (reverse) args.Add("--reverse");
                var check = Git.RunInput(tree.Path, args.Append("--check"), selected); if (!check.Ok) return check;
                return Git.RunInput(tree.Path, args, selected);
            }
        };
    }
}
