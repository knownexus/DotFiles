namespace GitWorkspace;

public enum CommitLevel { Commits, Files, FileDiff }

public sealed class CommitsModel(HistoryTarget target, Func<string, CommitFilter, int, int, List<GraphRow>> read,
    Func<CommitInfo, int, CommitDetail> detail, Func<CommitInfo, int, CommitFile, string> diff, Func<string, string> resolve, int batch = 200)
{
    readonly Dictionary<string, Task<CommitDetail>> details = [];
    readonly Dictionary<string, Task<string>> diffs = [];
    public HistoryTarget Target { get; } = target;
    public string? Tip { get; private set; }
    public string? Empty { get; private set; }
    public CommitFilter Filter { get; private set; } = new();
    public List<GraphRow> Rows { get; private set; } = [];
    public bool Exhausted { get; private set; }
    public int Focus { get; set; }
    public int Scroll { get; set; }
    public CommitLevel Level { get; private set; }
    public int FileFocus { get; set; }
    public int Parent { get; private set; }
    public GraphRow? Current => Rows.ElementAtOrDefault(Focus);
    public CommitInfo? Commit => Current?.Commit;

    public static CommitsModel ForGit(HistoryTarget target) => new(target,
        (tip, filter, skip, count) => CommitGraph.Read(target.Directory, tip, filter, target.Upstream, skip, count),
        (commit, parent) => CommitGraph.Detail(target.Directory, commit, parent),
        (commit, parent, file) => CommitGraph.FileDiff(target.Directory, commit, parent, file),
        revision => History.Resolve(target.Directory, revision));

    // The tip is resolved once, so commits landing while browsing do not shift the list.
    public void Load()
    {
        Rows = []; Exhausted = false; Focus = Scroll = 0; Level = CommitLevel.Commits; Parent = 0; Empty = null;
        try { Tip = resolve(Target.Revision); }
        catch (InvalidOperationException) { Tip = null; Exhausted = true; Empty = "No commits yet on this branch."; return; }
        LoadMore();
        Focus = Math.Max(0, Rows.FindIndex(r => r.Commit is not null));
        if (!Rows.Any(r => r.Commit is not null)) Empty = "No commits match this search or filter.";
    }
    void LoadMore()
    {
        if (Tip is null || Exhausted) return;
        int loaded = Rows.Count(r => r.Commit is not null);
        var more = read(Tip, Filter, loaded, batch);
        if (more.Count(r => r.Commit is not null) < batch) Exhausted = true;
        Rows.AddRange(more);
    }
    public void EnsureLoaded(int visibleRows) { while (!Exhausted && Focus >= Rows.Count - Math.Max(1, visibleRows)) { int before = Rows.Count; LoadMore(); if (Rows.Count == before) break; } }
    public void Move(int delta)
    {
        if (Level != CommitLevel.Commits) {
            int last = DetailTask() is { IsCompletedSuccessfully: true } d ? d.Result.Files.Count - 1 : 0;
            FileFocus = Math.Clamp(FileFocus + delta, 0, Math.Max(0, last)); return;
        }
        if (Rows.Count == 0) return;
        int step = Math.Sign(delta), remaining = Math.Abs(delta), index = Focus;
        while (remaining > 0) {
            int next = index + step;
            while (next >= 0 && next < Rows.Count && Rows[next].Commit is null) next += step;
            if (next < 0 || next >= Rows.Count) { if (step > 0 && !Exhausted) { LoadMore(); continue; } break; }
            index = next; remaining--;
        }
        Focus = index; Parent = 0;
    }
    public void SetFilter(CommitFilter filter) { Filter = filter; Load(); }
    public void Enter()
    {
        if (Commit is null) return;
        if (Level == CommitLevel.Commits) { Level = CommitLevel.Files; FileFocus = 0; }
        else if (Level == CommitLevel.Files && DetailTask() is { IsCompletedSuccessfully: true } d && d.Result.Files.Count > 0) {
            FileFocus = Math.Clamp(FileFocus, 0, d.Result.Files.Count - 1); Level = CommitLevel.FileDiff;
        }
    }
    public bool Back()
    {
        if (Level == CommitLevel.Commits) return false;
        Level = Level == CommitLevel.FileDiff ? CommitLevel.Files : CommitLevel.Commits; return true;
    }
    public void NextParent() { if (Commit is { Parents.Count: > 1 } c) { Parent = (Parent + 1) % c.Parents.Count; FileFocus = 0; if (Level == CommitLevel.FileDiff) Level = CommitLevel.Files; } }
    public Task<CommitDetail>? DetailTask()
    {
        if (Commit is not { } commit) return null;
        string key = commit.Hash + ":" + Parent; int parent = Parent;
        if (details.TryGetValue(key, out var old) && old.IsFaulted) details.Remove(key);
        if (!details.TryGetValue(key, out var task)) details[key] = task = Task.Run(() => detail(commit, parent));
        return task;
    }
    public CommitFile? File => DetailTask() is { IsCompletedSuccessfully: true } d ? d.Result.Files.ElementAtOrDefault(FileFocus) : null;
    public Task<string>? DiffTask()
    {
        if (Commit is not { } commit || File is not { } file) return null;
        string key = commit.Hash + ":" + Parent + ":" + file.Path; int parent = Parent;
        if (diffs.TryGetValue(key, out var old) && old.IsFaulted) diffs.Remove(key);
        if (!diffs.TryGetValue(key, out var task)) diffs[key] = task = Task.Run(() => diff(commit, parent, file));
        return task;
    }
}

public static class CommitsRender
{
    const int SubjectX = 40; // after the " Changes  Worktrees  [Commits]" labels drawn by Screen
    public static void Body(Canvas c, Layout layout, HitMap hits, CommitsModel model, int previewScroll)
    {
        var subject = model.Target.Label + " ▾" + (model.Filter.Search.Length > 0 ? "   / " + model.Filter.Search : "") + (model.Filter.UnpushedOnly ? "   · unpushed" : model.Filter.Base is { } b ? "   · since " + b : "");
        int room = Math.Max(0, layout.ViewBar.Width - SubjectX - 2);
        c.Text(SubjectX, layout.ViewBar.Y, subject, Ink.Accent, room); hits.Add(SubjectX, layout.ViewBar.Y, Math.Min(subject.Length, room), new Hit("subject"));
        // Narrow terminals show deeper levels in place of the list.
        if (!layout.Split && model.Level != CommitLevel.Commits) { Styled.Draw(c, new Rect(2, layout.List.Y, layout.List.Width - 3, layout.List.Height), Preview(model), previewScroll); hits.Add(layout.List, new Hit("preview")); return; }
        List(c, layout.List, hits, model);
        if (layout.Split) Styled.Draw(c, new Rect(layout.Preview.X + 1, layout.Preview.Y, layout.Preview.Width - 2, layout.Preview.Height), Preview(model), previewScroll);
    }
    public static void List(Canvas c, Rect area, HitMap hits, CommitsModel model)
    {
        if (model.Empty is not null) { c.Text(3, area.Y + 1, model.Empty, Ink.Muted, area.Width - 6); return; }
        model.EnsureLoaded(area.Height);
        if (model.Focus < model.Scroll) model.Scroll = model.Focus;
        if (model.Focus >= model.Scroll + area.Height) model.Scroll = model.Focus - area.Height + 1;
        for (int y = 0; y < area.Height && model.Scroll + y < model.Rows.Count; y++)
        {
            int index = model.Scroll + y; var r = model.Rows[index]; int row = area.Y + y; bool focus = index == model.Focus && model.Level == CommitLevel.Commits;
            if (focus) c.Fill(1, row, area.Width - 2);
            c.Text(2, row, r.Graph, Ink.Gutter, Math.Min(r.Graph.Length, area.Width - 4), focus);
            if (r.Commit is not { } commit) continue;
            hits.Add(0, row, area.Width, new Hit("row", index));
            int x = 2 + r.Graph.Length; int star = r.Graph.IndexOf('*'); if (star >= 0) c.Text(2 + star, row, "●", Ink.Accent, 1, focus);
            c.Text(x, row, (r.Unpushed ? "↑" : " ") + commit.ShortHash, r.Unpushed ? Ink.Good : Ink.Muted, 9, focus); x += 10;
            c.Text(x, row, Screen.Shorten(commit.Date, 14), Ink.Muted, 14, focus); x += 15;
            // The subject keeps its start (truncated at the edge); refs follow it only when they fit.
            int room = Math.Max(0, area.Width - x - 2);
            c.Text(x, row, commit.Subject, Ink.Text, room, focus);
            string refs = r.Refs.Count == 0 ? "" : "(" + string.Join(", ", r.Refs) + ")";
            int rx = x + commit.Subject.Length + 1;
            if (refs.Length > 0 && rx + refs.Length <= area.Width - 2) c.Text(rx, row, refs, Ink.Ref, refs.Length, focus);
        }
    }
    public static List<StyledLine> Preview(CommitsModel model)
    {
        if (model.Commit is not { } commit) return [new(model.Empty ?? "", Ink.Muted)];
        var detailTask = model.DetailTask();
        if (detailTask is not { IsCompleted: true }) return [new("Loading…", Ink.Muted)];
        if (!detailTask.IsCompletedSuccessfully) return [new(detailTask.Exception?.GetBaseException().Message ?? "Unable to read commit.", Ink.Bad)];
        var detail = detailTask.Result;
        string parentShort = commit.Parents.Count == 0 ? "" : commit.Parents[model.Parent][..Math.Min(7, commit.Parents[model.Parent].Length)];
        if (model.Level == CommitLevel.FileDiff)
        {
            var diffTask = model.DiffTask();
            if (diffTask is not { IsCompleted: true }) return [new("Loading…", Ink.Muted)];
            if (!diffTask.IsCompletedSuccessfully) return [new(diffTask.Exception?.GetBaseException().Message ?? "Unable to read diff.", Ink.Bad)];
            return DiffView.Render(DiffView.Parse(diffTask.Result), DiffSides.ForCommit(parentShort, commit.ShortHash, commit.Subject));
        }
        List<StyledLine> lines = [.. Styled.FromText(detail.Header).Select(l => l with { Ink = l.Text.StartsWith("commit ") ? Ink.Accent : Ink.Text })];
        if (commit.Parents.Count > 1) lines.Add(new($"Comparing with parent {model.Parent + 1} of {commit.Parents.Count} ({parentShort}) · M switches", Ink.Warning));
        int added = detail.Files.Where(f => f.Added > 0).Sum(f => f.Added), removed = detail.Files.Where(f => f.Removed > 0).Sum(f => f.Removed);
        lines.Add(new("", Ink.Text)); lines.Add(new($"{detail.Files.Count} files  +{added} −{removed}" + (model.Level == CommitLevel.Files ? "   ↑↓ file · Enter diff · Esc back" : "   Enter to browse files"), Ink.Heading));
        for (int i = 0; i < detail.Files.Count; i++) {
            var f = detail.Files[i]; bool on = model.Level == CommitLevel.Files && i == model.FileFocus;
            string counts = f.Added < 0 ? "binary" : $"+{f.Added} −{f.Removed}";
            lines.Add(new((on ? "▸ " : "  ") + f.Status + " " + (f.OldPath is null ? f.Path : f.OldPath + " → " + f.Path) + "   " + counts, on ? Ink.Accent : Ink.Text));
        }
        return lines;
    }
}
