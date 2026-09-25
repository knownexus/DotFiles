namespace GitWorkspace;

public sealed record Row(string Id, string Kind, string Text, Worktree? Tree = null, Change? Change = null, string? Project = null, BranchInfo? Branch = null);
public sealed record ViewLocation(string Group, string? Repo, bool Worktrees, string Search, string Filter, string? Focus, int Scroll);
public enum ViewMode { Changes, Worktrees, Commits }

public sealed class WorkspaceView(Settings settings, ViewState state)
{
    public Settings Settings { get; } = settings;
    public ViewState State { get; } = state;
    public Snapshot Snapshot { get; private set; } = new([], [], DateTime.MinValue);
    public HashSet<string> Selected { get; } = [];
    public HashSet<string> Targets { get; } = [];
    public Dictionary<string, string> SelectionStamps { get; } = [];
    public Func<Worktree, Change, string> Stamp { get; set; } = Operations.ChangeStamp;
    public Func<Worktree, bool> Selectable { get; set; } = t => t.Usable;
    public string Search { get; set; } = "";
    public string Filter { get; set; } = "All changes";
    public ViewMode Mode { get; set; }
    public bool WorktreeMode { get => Mode == ViewMode.Worktrees; set => Mode = value ? ViewMode.Worktrees : ViewMode.Changes; }
    public int Focus { get; set; }
    public int Scroll { get; set; }
    public List<Row> Rows { get; private set; } = [];
    readonly Stack<ViewLocation> back = new();
    public bool RepoMode => State.Repo is not null;
    public string ScopeLabel => RepoMode ? "Repo View · " + State.Repo : State.Group;
    public Row? Current => Rows.ElementAtOrDefault(Focus);
    public static string[] Filters => ["All changes", "Unstaged", "Staged", "Conflicts", "Unpushed", "Selected"];
    public IEnumerable<Worktree> InGroup()
    {
        if (RepoMode) return Snapshot.Worktrees.Where(t => t.Project == State.Repo);
        var group = Settings.Groups.FirstOrDefault(g => g.Name == State.Group);
        return group is null ? Snapshot.Worktrees : Snapshot.Worktrees.Where(group.Includes);
    }
    public IEnumerable<BranchInfo> InactiveBranches()
    {
        var group = Settings.Groups.FirstOrDefault(g => g.Name == State.Group);
        return Snapshot.Branches.Where(b => RepoMode ? b.Project == State.Repo : group is not null && group.Branches.Any(s => s.Project == b.Project && s.Ref == b.Ref))
            .Where(b => !Snapshot.Worktrees.Any(t => t.Project == b.Project && t.ActiveBranchRef == b.Ref));
    }
    public void Navigate(string? repo, string? group = null)
    {
        back.Push(new(State.Group, State.Repo, WorktreeMode, Search, Filter, Current?.Id, Scroll));
        State.Repo = repo; if (group is not null) State.Group = group;
        if (Mode != ViewMode.Commits) WorktreeMode = repo is not null; Search = ""; Filter = "All changes"; Focus = Scroll = 0;
        if (repo is not null) State.Collapsed.Remove("p:" + repo);
        Rebuild(); Focus = Scroll = 0;
    }
    public void Back()
    {
        if (!back.TryPop(out var saved)) { if (RepoMode) { State.Repo = null; WorktreeMode = false; Search = ""; Filter = "All changes"; Rebuild(); } return; }
        State.Group = saved.Group; State.Repo = saved.Repo; WorktreeMode = saved.Worktrees; Search = saved.Search; Filter = saved.Filter;
        if (State.Group != "All projects" && !Settings.Groups.Any(g => g.Name == State.Group)) State.Group = "All projects";
        if (State.Repo is not null && !Settings.Projects.Any(p => p.Name == State.Repo)) State.Repo = null;
        Rebuild(); Focus = Math.Max(0, Rows.FindIndex(r => r.Id == saved.Focus)); Scroll = saved.Scroll;
    }
    public void Cycle(int direction)
    {
        var names = RepoMode ? Settings.Projects.Select(p => p.Name).ToList() : new[] { "All projects" }.Concat(Settings.Groups.Select(g => g.Name)).ToList();
        if (names.Count == 0) return;
        int index = names.IndexOf(State.Repo ?? State.Group); string next = names[(index + direction + names.Count) % names.Count];
        Navigate(RepoMode ? next : null, RepoMode ? null : next);
    }
    public void Update(Snapshot snapshot)
    {
        Snapshot = snapshot;
        if (State.Repo is not null && !Settings.Projects.Any(p => p.Name == State.Repo)) State.Repo = null;
        if (State.Group != "All projects" && !Settings.Groups.Any(g => g.Name == State.Group)) State.Group = "All projects";
        var known = snapshot.Worktrees.SelectMany(t => t.Changes.Select(c => t.FileId(c))).ToHashSet();
        Selected.IntersectWith(known); Targets.IntersectWith(snapshot.Worktrees.Select(t => t.Id));
        foreach (var key in SelectionStamps.Keys.Except(known).ToArray()) SelectionStamps.Remove(key);
        Rebuild();
    }
    bool Match(Worktree t, Change? c = null) => Search.Length == 0 || $"{t.Project} {t.Name} {t.BranchLabel} {c?.Path}".Contains(Search, StringComparison.OrdinalIgnoreCase);
    public bool Visible(Worktree t, Change c) => (State.ShowUntracked || c.Area != ChangeArea.Untracked) && Match(t, c) && Filter switch {
        "Unstaged" => c.Area is ChangeArea.Unstaged or ChangeArea.Untracked,
        "Staged" => c.Area == ChangeArea.Staged, "Conflicts" => c.Area == ChangeArea.Conflict,
        "Unpushed" => t.Ahead > 0, "Selected" => Selected.Contains(t.FileId(c)), _ => true
    };
    public void Rebuild()
    {
        string? focused = Current?.Id; List<Row> rows = [];
        var scopedTrees = InGroup().ToList(); var inactive = InactiveBranches().ToList();
        foreach (var name in Settings.Projects.Select(p => p.Name).Where(p => scopedTrees.Any(t => t.Project == p) || inactive.Any(b => b.Project == p)))
        {
            var trees = scopedTrees.Where(t => t.Project == name).Where(t => WorktreeMode ? Match(t) : t.Changes.Any(c => Visible(t, c)) ||
                (Match(t) && (Filter == "All changes" || Filter == "Unpushed" && t.Ahead > 0))).ToList();
            var branches = inactive.Where(b => b.Project == name && (Search.Length == 0 || (b.Project + " " + b.Name).Contains(Search, StringComparison.OrdinalIgnoreCase)))
                .Where(_ => RepoMode || Filter == "All changes").ToList();
            if (trees.Count == 0 && branches.Count == 0) continue;
            rows.Add(new("p:" + name, "project", name, Project: name));
            if (State.Collapsed.Contains("p:" + name)) continue;
            foreach (var t in trees)
            {
                rows.Add(new("w:" + t.Id, "worktree", $"{t.Name} → {t.BranchLabel}", t, Project: t.Project));
                if (WorktreeMode || State.Collapsed.Contains("w:" + t.Id)) continue;
                foreach (var area in new[] { ChangeArea.Conflict, ChangeArea.Unstaged, ChangeArea.Untracked, ChangeArea.Staged })
                    foreach (var c in t.Changes.Where(c => c.Area == area && Visible(t, c)).OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
                        rows.Add(new(t.FileId(c), "file", c.Path, t, c, t.Project));
            }
            foreach (var branch in branches) rows.Add(new(branch.Id, "branch", branch.Name, Project: name, Branch: branch));
        }
        Rows = rows;
        int index = focused is null ? -1 : Rows.FindIndex(r => r.Id == focused);
        Focus = index >= 0 ? index : Math.Clamp(Focus, 0, Math.Max(0, Rows.Count - 1));
    }
    public IEnumerable<(Worktree Tree, Change Change)> FilesFor(Row? row)
    {
        if (row is null || row.Kind == "branch") return [];
        if (row.Change is not null) return [(row.Tree!, row.Change)];
        return InGroup().Where(t => row.Tree is not null ? t.Id == row.Tree.Id : t.Project == row.Project)
            .SelectMany(t => t.Changes.Where(c => Visible(t, c)).Select(c => (t, c)));
    }
    public IEnumerable<Worktree> TreesFor(Row row) => InGroup().Where(t => row.Tree is not null ? t.Id == row.Tree.Id : t.Project == row.Project);
    public string Check(Row row)
    {
        if (row.Kind == "branch") return "   ";
        var keys = WorktreeMode ? TreesFor(row).Where(Selectable).Select(t => t.Id).ToArray() : FilesFor(row).Select(x => x.Tree.FileId(x.Change)).ToArray();
        var selected = WorktreeMode ? Targets : Selected;
        int count = keys.Count(selected.Contains);
        return keys.Length == 0 ? "   " : count == 0 ? "☐  " : count == keys.Length ? "☑  " : "◩  ";
    }
    public void Toggle(Row? row)
    {
        if (row is null || row.Kind == "branch") return;
        if (WorktreeMode)
        {
            var trees = TreesFor(row).Where(Selectable).ToList(); bool clear = trees.All(t => Targets.Contains(t.Id));
            foreach (var t in trees) { if (clear) Targets.Remove(t.Id); else Targets.Add(t.Id); }
        }
        else
        {
            var files = FilesFor(row).ToList(); bool clear = files.All(x => Selected.Contains(x.Tree.FileId(x.Change)));
            foreach (var (tree, change) in files)
            {
                string key = tree.FileId(change);
                if (clear) { Selected.Remove(key); SelectionStamps.Remove(key); }
                else { Selected.Add(key); SelectionStamps[key] = Stamp(tree, change); }
            }
        }
        Rebuild();
    }
    public void SelectVisible()
    {
        if (WorktreeMode)
        {
            var ids = Rows.Where(r => r.Tree is not null && Selectable(r.Tree)).Select(r => r.Tree!.Id).Distinct().ToList(); bool clear = ids.All(Targets.Contains);
            foreach (var id in ids) { if (clear) Targets.Remove(id); else Targets.Add(id); }
        }
        else
        {
            var files = Rows.Where(r => r.Change is not null).ToList(); bool clear = files.All(r => Selected.Contains(r.Id));
            foreach (var r in files) { if (clear) { Selected.Remove(r.Id); SelectionStamps.Remove(r.Id); } else { Selected.Add(r.Id); SelectionStamps[r.Id] = Stamp(r.Tree!, r.Change!); } }
        }
        Rebuild();
    }
    public List<(Worktree Tree, Change Change)> SelectedFiles() => Snapshot.Worktrees.SelectMany(t => t.Changes.Where(c => Selected.Contains(t.FileId(c))).Select(c => (t, c))).ToList();
    public int HiddenSelections => Selected.Count - Rows.Count(r => r.Change is not null && Selected.Contains(r.Id));
}

public static class Screen
{
    public static string Shorten(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length <= 1) return "…";
        int first = Math.Max(1, (length - 1) / 2);
        return text[..first] + "…" + text[^(length - first - 1)..];
    }
    // Git's two-letter status: left = staged side, right = unstaged side.
    public static string StatusCode(Change c) => c.Area switch {
        ChangeArea.Untracked => "??",
        ChangeArea.Conflict => c.XY.Replace('.', ' ').PadRight(2)[..2],
        ChangeArea.Staged => (c.XY[0] == '.' ? 'M' : c.XY[0]) + " ",
        _ => " " + (c.XY.Length > 1 && c.XY[1] != '.' ? c.XY[1] : 'M')
    };
    public static Canvas Render(WorkspaceView view, int width, int height, string preview, int previewScroll, string notice, bool refreshing) =>
        Render(view, Layout.For(width, height, view.State.SplitRatio), new HitMap(),
            DiffView.LooksLikeDiff(preview) ? DiffView.Render(DiffView.Parse(preview), null) : Styled.FromText(preview), previewScroll, notice, refreshing);

    public static Canvas Render(WorkspaceView view, Layout layout, HitMap hits, IReadOnlyList<StyledLine> preview, int previewScroll, string notice, bool refreshing,
        Action<Canvas, Layout, HitMap>? body = null)
    {
        int width = layout.Footer.Width, height = layout.Footer.Y + 2;
        var c = new Canvas(width, height); hits.Clear();
        if (width < Layout.MinWidth || height < Layout.MinHeight) { c.Text(1, 1, "Git Workspace", Ink.Heading); c.Text(1, 3, "Enlarge terminal to at least 66 columns × 16 rows."); c.Text(1, 5, "Q quit"); return c; }
        c.Text(2, layout.Title.Y, "GIT WORKSPACE", Ink.Heading);
        c.Text(20, layout.Title.Y, view.ScopeLabel + (refreshing ? "  · refreshing…" : ""), Ink.Accent, width - 22);
        ViewBar(c, layout.ViewBar, hits, view);
        c.Line(layout.ViewBar.Y + 1);
        if (body is not null) body(c, layout, hits);
        else List(c, layout.List, hits, view);
        if (layout.Split) {
            for (int y = layout.Preview.Y; y < layout.Preview.Bottom; y++) c.Text(layout.Preview.X - 1, y, "│", Ink.Border);
            var area = new Rect(layout.Preview.X + 1, layout.Preview.Y, layout.Preview.Width - 2, layout.Preview.Height);
            if (body is null) Styled.Draw(c, area, preview, previewScroll);
            hits.Add(layout.Preview, new Hit("preview"));
        }
        c.Line(layout.Context.Y - 1);
        string selection = view.WorktreeMode ? $"{view.Targets.Count} worktrees selected" : $"{view.Selected.Count} changes selected" + (view.HiddenSelections > 0 ? $" · {view.HiddenSelections} hidden" : "");
        c.Text(2, layout.Context.Y, selection + "  · " + notice, Ink.Muted, width - 4);
        Footer.Render(c, layout.Footer, hits, Footer.For(view, null));
        return c;
    }
    static void ViewBar(Canvas c, Rect bar, HitMap hits, WorkspaceView view)
    {
        string[] names = ["Changes", "Worktrees", "Commits"]; int x = 2;
        for (int i = 0; i < names.Length; i++) {
            bool active = (int)view.Mode == i; string label = active ? "[" + names[i] + "]" : " " + names[i] + " ";
            c.Text(x, bar.Y, label, active ? Ink.Heading : Ink.Muted); hits.Add(x, bar.Y, label.Length, new Hit("view", i)); x += label.Length + 2;
        }
        string untracked = "T untracked: " + (view.State.ShowUntracked ? "on" : "off");
        string filter = "F " + view.Filter + (view.Search.Length > 0 ? "  / " + view.Search : "");
        int right = bar.Width - 2 - filter.Length;
        if (right > x + untracked.Length + 3) { c.Text(right, bar.Y, filter, Ink.Muted); hits.Add(right, bar.Y, filter.Length, new Hit("action", Id: "f")); }
        int ux = Math.Max(x + 2, right - untracked.Length - 3);
        if (ux + untracked.Length < bar.Width) { c.Text(ux, bar.Y, untracked, view.State.ShowUntracked ? Ink.Accent : Ink.Warning); hits.Add(ux, bar.Y, untracked.Length, new Hit("action", Id: "t")); }
    }
    static void List(Canvas c, Rect list, HitMap hits, WorkspaceView view)
    {
        int left = list.Width;
        if (view.Focus < view.Scroll) view.Scroll = view.Focus;
        if (view.Focus >= view.Scroll + list.Height) view.Scroll = view.Focus - list.Height + 1;
        for (int y = 0; y < list.Height; y++)
        {
            int index = view.Scroll + y; if (index >= view.Rows.Count) break;
            var r = view.Rows[index]; bool focus = index == view.Focus; int row = list.Y + y;
            if (focus) c.Fill(1, row, left - 2);
            hits.Add(0, row, left, new Hit("row", index));
            string text; int checkX;
            if (r.Kind == "project")
            {
                int count = view.InGroup().Where(t => t.Project == r.Project).Sum(t => t.Changes.Where(ch => view.State.ShowUntracked || ch.Area != ChangeArea.Untracked).Select(ch => ch.Path).Distinct().Count());
                text = (view.State.Collapsed.Contains(r.Id) ? "▸ " : "▾ ") + view.Check(r) + r.Text + $"  · {count} files ";
                text += new string('─', Math.Max(0, left - text.Length - 4));
                c.Text(1, row, text, Ink.Heading, left - 2, focus); hits.Add(1, row, 2, new Hit("fold", index)); checkX = 3;
            }
            else if (r.Kind == "worktree")
            {
                string state = r.Tree!.Error.Length > 0 ? "Unavailable" : r.Tree.Operation.Length > 0 ? r.Tree.Operation : $"↑{r.Tree.Ahead} ↓{r.Tree.Behind}" + (r.Tree.Upstream is null ? " · no upstream" : r.Tree.UpstreamGone ? " · upstream missing" : "");
                int available = left - 13 - state.Length, nameWidth = Math.Max(8, Math.Min(24, available / 3)), branchWidth = Math.Max(10, available - nameWidth - 3);
                text = "  " + (view.State.Collapsed.Contains(r.Id) ? "▸ " : "▾ ") + view.Check(r) + Shorten(r.Tree.Name, nameWidth) + " → " + Shorten(r.Tree.BranchLabel, branchWidth) + "  " + state;
                c.Text(1, row, text, Ink.Accent, left - 2, focus); hits.Add(3, row, 2, new Hit("fold", index)); checkX = 5;
            }
            else if (r.Kind == "branch") { c.Text(1, row, "    ◇ " + Shorten(r.Text, left - 29) + "  · not checked out", Ink.Text, left - 2, focus); continue; }
            else
            {
                var ch = r.Change!; string code = StatusCode(ch);
                var ink = ch.Area switch { ChangeArea.Staged => Ink.Good, ChangeArea.Conflict => Ink.Bad, _ => Ink.Warning };
                c.Text(1, row, "      " + view.Check(r), Ink.Text, left - 2, focus);
                c.Text(10, row, code, ink, 2, focus);
                c.Text(14, row, Shorten(r.Text, Math.Max(4, left - 16)), Ink.Text, left - 15, focus); checkX = 7;
            }
            hits.Add(checkX, row, 3, new Hit("check", index));
        }
        if (view.Rows.Count == 0) c.Text(3, list.Y + 1, view.Settings.Projects.Count == 0 ? "No projects yet. More → Add project." : "No matches. Change the filter or workspace.", Ink.Muted, left - 6);
    }
}
