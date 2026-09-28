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
        if (Level != CommitLevel.Commits) { FileFocus = Math.Max(0, FileFocus + delta); return; }
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
        if (!details.TryGetValue(key, out var task)) details[key] = task = Task.Run(() => detail(commit, parent));
        return task;
    }
    public CommitFile? File => DetailTask() is { IsCompletedSuccessfully: true } d ? d.Result.Files.ElementAtOrDefault(FileFocus) : null;
    public Task<string>? DiffTask()
    {
        if (Commit is not { } commit || File is not { } file) return null;
        string key = commit.Hash + ":" + Parent + ":" + file.Path; int parent = Parent;
        if (!diffs.TryGetValue(key, out var task)) diffs[key] = task = Task.Run(() => diff(commit, parent, file));
        return task;
    }
}
