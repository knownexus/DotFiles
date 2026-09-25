# gws UI foundation, project groups and commits view — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `gws` easier to use: layout-driven rendering with hit regions and popups, unambiguous old/new diffs, a group tab strip with a tree-based group editor that discovers repositories on disk, and a Commits view on the main screen.

**Architecture:** A small in-house UI layer (`Rect`/`Layout`, `HitMap`, styled lines, `DiffView`, popup frames over an `ISurface`) sits on the existing `Canvas`/`Terminal`. Main-screen rendering registers clickable regions instead of App hard-coding x ranges. Modal popups (the existing `Dialogs` methods, re-skinned) form the top of the focus stack; the main screen's list/preview are the bottom. Groups gain `Discovery` + an extended `GroupDraft`; commits gain `History.Graph/Files/FileDiff` + a `CommitsModel` rendered in the main list/preview panes.

**Tech Stack:** C# / .NET 10 console app, no NuGet packages, Git CLI via `Git.Run`, dependency-free integration test runner (`GitWorkspace.Tests`).

**Spec:** `docs/superpowers/specs/2026-09-25-gws-ui-groups-commits-design.md`

## Global Constraints

- No NuGet packages in either project (`TreatWarningsAsErrors` stays on; code must compile warning-free).
- Source files stay **flat** in `pwsh/GitWorkspace/` (no subfolders): `Get-GitWorkspaceBuildDirectory` in `pwsh/profile.d/13-git-workspace.ps1` hashes only top-level `.cs` files, so a `Ui/` folder would silently skip rebuilds. (Deviation from the spec's `Ui/` folder name; same components.)
- Namespace is `GitWorkspace` for all new types.
- Config stays `Version: 1`; new fields (`Roots`, `SplitRatio`) are optional and old files load unchanged.
- Safety behaviour is unchanged: previews before actions, stale-state checks, no force-push, typed `DISCARD`, per-target results, Retry semantics.
- A file with staged and unstaged edits remains two rows with independent checkboxes.
- `gws --demo`, `gws --snapshot [w h]`, `gws status [--json] [--root <path>]` keep working.
- Preview split activates at **100** columns; split ratio clamps to **0.35–0.65**; minimum screen 66×16.
- Discovery default root: `C:/repos` when `Roots` is absent and that directory exists; scan depth one level.
- Commit list loads **200** commits per batch.
- Match surrounding style: dense expression-bodied C#, `sealed` records/classes, `Ink` colours, existing comment density.
- Deliberate simplifications of the spec (keep them; do not reintroduce):
  - No separate `IPanel` interface. The main screen's regions are drawn by `Screen.Render`, plus an optional body delegate for Commits. Modal popups (the `Dialogs` methods) are the top of the focus stack, and `App.HandleCommits` gives the Commits view priority over global keys.
  - No `↓` upstream-only marker. None of the spec's filters (all / unpushed / since base) lists upstream-only commits.
  - Unregistered repositories in the group editor can only be included whole; their worktrees and branches become available once they are registered.

**Test command** (used by every task; `<filter>` is a substring of test names):

```powershell
dotnet build C:\repos\DotFiles\pwsh\GitWorkspace.Tests\GitWorkspace.Tests.csproj -c Release --configfile C:\repos\DotFiles\pwsh\GitWorkspace\NuGet.Config -o C:\repos\DotFiles\pwsh\GitWorkspace.Tests\bin\plan --nologo -v q; if ($LASTEXITCODE -eq 0) { dotnet C:\repos\DotFiles\pwsh\GitWorkspace.Tests\bin\plan\GitWorkspace.Tests.dll --filter "<filter>" }
```

Omit `--filter` to run everything. Output ends with `N passed; M failed.`; exit code 0 means all passed. Build output under `bin/` is git-ignored.

## Review Focus

1. **Empty or unborn branch in the Commits view** (new repo, no commits yet) → shows "No commits yet on this branch." instead of throwing from `rev-parse`. Test in Task 19.
2. **Commit search text containing regex metacharacters** (`fix(`, `[wip]`, `a.b`) → matched literally, never a Git "Unmatched [" error. Test in Task 17.
3. **Discovered folder name colliding case-insensitively with a registered project** (`joiners` vs `Joiners`) → registered as `joiners 2`, no duplicate-name config error. Test in Task 14.
4. **Missing, unreadable or non-repository roots and a root that is itself inside a repository** → skipped silently; subfolders are only reported when they carry their own `.git`/`.git-main`/`.bare`/bare markers. Test in Task 12.
5. **Diffs with CRLF line endings and `\ No newline at end of file`** → no stray `\r` in rendered lines, the marker becomes a muted note and line numbering stays correct after it. Test in Task 6.

---

## File Structure

Create (all in `pwsh/GitWorkspace/`):

| File | Responsibility |
|---|---|
| `Layout.cs` | `Rect`, `Layout.For(width, height, ratio)` screen regions |
| `HitMap.cs` | `Hit`, `HitMap` clickable-region registry |
| `Styled.cs` | `StyledLine`, `Styled.Draw`, `Styled.FromText` |
| `DiffView.cs` | `DiffKind`, `DiffLine`, `DiffSides`, `DiffView.Parse/Render` |
| `Surface.cs` | `ISurface`, `MouseButton`, `MouseDecoder` |
| `Footer.cs` | `Footer.For(view, commits)` context keys + render |
| `TabStrip.cs` | `Tab`, `TabStrip.Tabs/Place/Render` |
| `Discovery.cs` | `DiscoveredRepo`, `Discovery.Roots/Scan` |
| `GroupEditor.cs` | full-screen group editor panel (replaces `GroupDialog.cs`) |
| `CommitGraph.cs` | `CommitFilter`, `GraphRow`, `CommitFile`, graph/search/files/diff Git reads |
| `CommitsView.cs` | `CommitLevel`, `CommitsModel`, `CommitsRender` |

Modify: `Terminal.cs`, `Dialogs.cs`, `WorkspaceView.cs`, `App.cs`, `Navigation.cs`, `HistoryActions.cs`, `History.cs`, `GroupEditor.cs`→(new content), `Models.cs`, `Git.cs`, `Program.cs`, `README.md`, `pwsh/GitWorkspace.Tests/Program.cs`, `pwsh/GitWorkspace.Tests/GroupsAndHistory.cs`.

Delete: `GroupDialog.cs` (Task 15), `HistoryDialog.cs` (Task 20).

Note: the existing file `GroupEditor.cs` holds `GroupMember`/`GroupDraft`. Task 14 renames it to `GroupDraft.cs` (git mv) so `GroupEditor.cs` can hold the panel in Task 15.

Create tests (in `pwsh/GitWorkspace.Tests/`): `UiTests.cs`, `GroupUiTests.cs`, `CommitTests.cs`, `ScriptedSurface.cs`.

---

# Phase 1 — UI foundation

### Task 1: Baseline branch and test filter

**Files:**
- Modify: `pwsh/GitWorkspace.Tests/Program.cs` (the `Test` method and `Main`)

**Interfaces:**
- Produces: test runner flag `--filter <text>`; `static string? filter` in `Tests`.

- [ ] **Step 1: Confirm with the user and create the baseline**

`pwsh/GitWorkspace/`, `pwsh/GitWorkspace.Tests/` and `pwsh/profile.d/13-git-workspace.ps1` are untracked. Ask the user to confirm committing them as a baseline (their other modified files are left alone). Then:

```bash
cd /c/repos/DotFiles
git switch -c gws-ui-overhaul
git add pwsh/GitWorkspace pwsh/GitWorkspace.Tests pwsh/profile.d/13-git-workspace.ps1
git commit -m "gws: baseline before UI overhaul"
```

- [ ] **Step 2: Add the filter to the runner**

In `pwsh/GitWorkspace.Tests/Program.cs`, add a field next to `root` and make `Test` skip non-matching names:

```csharp
    static string? filter;
```

```csharp
    static void Test(string name, Action test)
    {
        if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        try { test(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + "\n" + e); failed++; }
    }
```

In `Main`, directly after the `--sequence-editor` line:

```csharp
        int filterAt = Array.IndexOf(args, "--filter");
        if (filterAt >= 0 && filterAt + 1 < args.Length) filter = args[filterAt + 1];
```

- [ ] **Step 3: Run the whole suite to record the baseline**

Run the test command without `--filter`. Expected: all existing tests PASS, `0 failed`. Then run with `--filter "status parser"`. Expected: `1 passed; 0 failed.`

- [ ] **Step 4: Commit**

```bash
git add pwsh/GitWorkspace.Tests/Program.cs
git commit -m "gws tests: add --filter to run a subset"
```

---

### Task 2: Layout and hit regions

**Files:**
- Create: `pwsh/GitWorkspace/Layout.cs`, `pwsh/GitWorkspace/HitMap.cs`
- Create: `pwsh/GitWorkspace.Tests/UiTests.cs`
- Modify: `pwsh/GitWorkspace.Tests/Program.cs` (call `UiTests()`)

**Interfaces:**
- Produces:
  - `readonly record struct Rect(int X, int Y, int Width, int Height)` with `Right`, `Bottom`, `Empty`, `Contains(int x, int y)`
  - `sealed record Layout(Rect Title, Rect Tabs, Rect ViewBar, Rect List, Rect Preview, Rect Context, Rect Footer, bool Split)`; `const int MinWidth = 66, MinHeight = 16, SplitFrom = 100`; `static Layout For(int width, int height, double ratio)`
  - `sealed record Hit(string Kind, int Index = -1, string? Id = null)`
  - `sealed class HitMap` with `Add(Rect, Hit)`, `Add(int x, int y, int width, Hit)`, `Hit? At(int x, int y)`, `Clear()`, `Count`
  - Hit kinds used later: `"row"`, `"check"`, `"fold"`, `"view"`, `"action"`, `"preview"`, `"tab"`, `"tab-new"`, `"tab-prev"`, `"tab-next"`, `"subject"`

- [ ] **Step 1: Write the failing tests**

Create `pwsh/GitWorkspace.Tests/UiTests.cs`:

```csharp
using GitWorkspace;

static partial class Tests
{
    static void UiTests()
    {
        Test("layout regions split from 100 columns and clamp the ratio", () => {
            var wide = Layout.For(120, 32, 0.5);
            Check(wide.Split, "Split at 120"); Equal(60, wide.List.Width, "List width"); Equal(61, wide.Preview.X, "Preview x");
            Equal(59, wide.Preview.Width, "Preview width"); Equal(5, wide.List.Y, "Body top"); Equal(23, wide.List.Height, "Body height");
            Equal(30, wide.Footer.Y, "Footer y"); Equal(29, wide.Context.Y, "Context y");
            var narrow = Layout.For(80, 24, 0.5); Check(!narrow.Split && narrow.Preview.Empty, "No split below 100"); Equal(80, narrow.List.Width, "Full list");
            Equal(104, Layout.For(160, 40, 0.9).List.Width, "Ratio clamps to 0.65");
            Equal(40, Layout.For(100, 20, 0.35).List.Width, "List keeps 40 columns");
            var tiny = Layout.For(10, 3, 0.5); Check(tiny.List.Height == 0 && !tiny.Split, "Tiny terminal has an empty body");
        });
        Test("hit map returns the last registered region and ignores empty areas", () => {
            var hits = new HitMap();
            hits.Add(new Rect(0, 5, 60, 1), new Hit("row", 0));
            hits.Add(3, 5, 3, new Hit("check", 0));
            hits.Add(new Rect(10, 10, 0, 1), new Hit("action", Id: "never"));
            Equal("check", hits.At(4, 5)?.Kind, "Checkbox wins over row");
            Equal("row", hits.At(20, 5)?.Kind, "Row");
            Check(hits.At(20, 6) is null, "Outside every region");
            Equal(2, hits.Count, "Empty region skipped");
            hits.Clear(); Check(hits.At(4, 5) is null, "Cleared");
        });
    }
}
```

In `pwsh/GitWorkspace.Tests/Program.cs` `Main`, add `UiTests();` on the line before `IgnoreTests();`.

- [ ] **Step 2: Run to verify failure**

Run the test command with `--filter "layout"`. Expected: build FAILS with `The type or namespace name 'Layout' could not be found`.

- [ ] **Step 3: Implement**

Create `pwsh/GitWorkspace/Layout.cs`:

```csharp
namespace GitWorkspace;

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Empty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => !Empty && x >= X && x < Right && y >= Y && y < Bottom;
}

// Rows: 1 title · 2 group tabs · 3 view bar · 4 rule · body · rule · context · footer.
public sealed record Layout(Rect Title, Rect Tabs, Rect ViewBar, Rect List, Rect Preview, Rect Context, Rect Footer, bool Split)
{
    public const int MinWidth = 66, MinHeight = 16, SplitFrom = 100;
    public static Layout For(int width, int height, double ratio)
    {
        int body = Math.Max(0, height - 9);
        bool split = width >= SplitFrom && body > 0;
        int left = split ? Math.Clamp((int)Math.Round(width * Math.Clamp(ratio, 0.35, 0.65)), 40, width - 30) : width;
        return new(new(0, 1, width, 1), new(0, 2, width, 1), new(0, 3, width, 1), new(0, 5, left, body),
            split ? new(left + 1, 5, width - left - 1, body) : new(0, 0, 0, 0),
            new(0, Math.Max(0, height - 3), width, 1), new(0, Math.Max(0, height - 2), width, 1), split);
    }
}
```

Create `pwsh/GitWorkspace/HitMap.cs`:

```csharp
namespace GitWorkspace;

public sealed record Hit(string Kind, int Index = -1, string? Id = null);

// Filled while a frame renders; mouse input is resolved against the last drawn frame.
public sealed class HitMap
{
    readonly List<(Rect Area, Hit Hit)> regions = [];
    public int Count => regions.Count;
    public void Add(Rect area, Hit hit) { if (!area.Empty) regions.Add((area, hit)); }
    public void Add(int x, int y, int width, Hit hit) => Add(new Rect(x, y, width, 1), hit);
    public Hit? At(int x, int y)
    {
        for (int i = regions.Count - 1; i >= 0; i--) if (regions[i].Area.Contains(x, y)) return regions[i].Hit;
        return null;
    }
    public void Clear() => regions.Clear();
}
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "layout"` then `--filter "hit map"`. Expected: `1 passed; 0 failed.` each.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/Layout.cs pwsh/GitWorkspace/HitMap.cs pwsh/GitWorkspace.Tests/UiTests.cs pwsh/GitWorkspace.Tests/Program.cs
git commit -m "gws: layout regions and hit map"
```

---

### Task 3: Mouse buttons, double-click and surfaces

**Files:**
- Create: `pwsh/GitWorkspace/Surface.cs`, `pwsh/GitWorkspace.Tests/ScriptedSurface.cs`
- Modify: `pwsh/GitWorkspace/Terminal.cs` (`Input` record, `Poll`, `Draw`, class declaration)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Produces:
  - `enum MouseButton { None, Left, Right }`
  - `Input` gains `MouseButton Button = MouseButton.None, bool DoubleClick = false` (appended; all existing positional/named uses remain valid)
  - `static class MouseDecoder { static Input? Decode(uint buttons, uint flags, short x, short y, ref uint previous) }`
  - `interface ISurface { int Width { get; } int Height { get; } Canvas? LastFrame { get; } void Draw(Canvas canvas); Input? Poll(int milliseconds = 50); Input Read(); }`
  - `Terminal : ISurface` with `LastFrame`
  - Test helper `sealed class ScriptedSurface(int width, int height, params Input[] keys) : ISurface` with `Frames` (plain text of each drawn frame) and `SetBackdrop(Canvas)`

- [ ] **Step 1: Write the failing tests**

Create `pwsh/GitWorkspace.Tests/ScriptedSurface.cs`:

```csharp
using GitWorkspace;

// Deterministic surface for dialog tests: Read() replays scripted input, Poll() never
// consumes it (Busy() polls while work runs), and every drawn frame is kept as text.
sealed class ScriptedSurface(int width, int height, params Input[] keys) : ISurface
{
    readonly Queue<Input> queue = new(keys);
    public int Width => width;
    public int Height => height;
    public Canvas? LastFrame { get; private set; }
    public List<string> Frames { get; } = [];
    public void SetBackdrop(Canvas canvas) => LastFrame = canvas;
    public void Draw(Canvas canvas) { LastFrame = canvas; Frames.Add(canvas.Plain()); }
    public Input? Poll(int milliseconds = 50) { Thread.Sleep(1); return null; }
    public Input Read() => queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException("Scripted input exhausted");
}
```

Append inside `UiTests()` in `UiTests.cs`:

```csharp
        Test("mouse decoder reports left, right, double click and wheel once per press", () => {
            uint previous = 0;
            var left = MouseDecoder.Decode(1, 0, 4, 6, ref previous); Equal(MouseButton.Left, left?.Button, "Left press"); Equal(4, left?.X, "X");
            Check(MouseDecoder.Decode(1, 1, 5, 6, ref previous) is null, "Held button while moving is not a new click");
            Check(MouseDecoder.Decode(0, 0, 5, 6, ref previous) is null, "Release");
            Equal(MouseButton.Right, MouseDecoder.Decode(2, 0, 1, 1, ref previous)?.Button, "Right press");
            MouseDecoder.Decode(0, 0, 1, 1, ref previous);
            Check(MouseDecoder.Decode(1, 2, 1, 1, ref previous)?.DoubleClick == true, "Double click");
            Equal(-3, MouseDecoder.Decode(unchecked((uint)(120 << 16)), 4, 1, 1, ref previous)?.Wheel, "Wheel up");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "mouse decoder"`. Expected: build FAILS (`MouseDecoder`, `ISurface` not found).

- [ ] **Step 3: Implement**

Create `pwsh/GitWorkspace/Surface.cs`:

```csharp
namespace GitWorkspace;

public enum MouseButton { None, Left, Right }

public interface ISurface
{
    int Width { get; }
    int Height { get; }
    Canvas? LastFrame { get; }
    void Draw(Canvas canvas);
    Input? Poll(int milliseconds = 50);
    Input Read();
}

public static class MouseDecoder
{
    // Windows MOUSE_EVENT_RECORD: flags 0 press/release, 1 moved, 2 double click, 4 wheel.
    public static Input? Decode(uint buttons, uint flags, short x, short y, ref uint previous)
    {
        if (flags == 4) return new(X: x, Y: y, Wheel: (short)(buttons >> 16) > 0 ? -3 : 3);
        if (flags == 2) { previous = buttons; return (buttons & 1) != 0 ? new(X: x, Y: y, Button: MouseButton.Left, DoubleClick: true) : null; }
        uint pressed = buttons & ~previous; previous = buttons;
        if ((pressed & 1) != 0) return new(X: x, Y: y, Button: MouseButton.Left);
        if ((pressed & 2) != 0) return new(X: x, Y: y, Button: MouseButton.Right);
        return null;
    }
}
```

In `pwsh/GitWorkspace/Terminal.cs`:

Replace the `Input` record with:

```csharp
public record Input(ConsoleKey Key = 0, char Character = '\0', bool Control = false, bool Shift = false, int X = -1, int Y = -1, int Wheel = 0,
    MouseButton Button = MouseButton.None, bool DoubleClick = false)
{
    public bool Mouse => X >= 0;
}
```

Change `public sealed class Terminal : IDisposable` to `public sealed class Terminal : ISurface, IDisposable`.

Replace the mouse block inside `Poll` (the `if (record.Type == 2) { ... }` block) with:

```csharp
                    if (record.Type == 2)
                    {
                        var mouse = MouseDecoder.Decode(record.Buttons, record.Flags, record.X, record.Y, ref previousButtons);
                        if (mouse is not null) return mouse;
                    }
```

Add a property after `Height`:

```csharp
    public Canvas? LastFrame { get; private set; }
```

and set it as the first line of `Draw`: `LastFrame = canvas;`

- [ ] **Step 4: Run to verify pass**

Run with `--filter "mouse decoder"`. Expected: PASS. Then run the full suite: `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/Surface.cs pwsh/GitWorkspace/Terminal.cs pwsh/GitWorkspace.Tests/ScriptedSurface.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: report mouse button and double click; ISurface abstraction"
```

---

### Task 4: Canvas boxes, dimming, new inks and styled lines

**Files:**
- Modify: `pwsh/GitWorkspace/Terminal.cs` (`Ink` enum and `Canvas` class)
- Create: `pwsh/GitWorkspace/Styled.cs`
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `Rect` (Task 2)
- Produces:
  - `enum Ink { Text, Muted, Accent, Good, Warning, Bad, Heading, Added, Removed, Hunk, Gutter, Ref, Border }` (existing members keep their order)
  - `Canvas.Clone()`, `Canvas.Dim()`, `Canvas.Box(Rect area, string? title = null)`, `Canvas.InkAt(int x, int y)`
  - `sealed record StyledLine(string Text, Ink Ink)`
  - `static class Styled { void Draw(Canvas c, Rect area, IReadOnlyList<StyledLine> lines, int scroll); List<StyledLine> FromText(string text); }`

- [ ] **Step 1: Write the failing tests**

Append inside `UiTests()`:

```csharp
        Test("canvas boxes clip safely, dim a copy and styled diff lines fill their row", () => {
            var c = new Canvas(30, 8); c.Text(0, 0, "BACKDROP");
            var copy = c.Clone(); copy.Dim(); Equal(Ink.Gutter, copy.InkAt(0, 0), "Dimmed copy"); Equal(Ink.Text, c.InkAt(0, 0), "Original untouched");
            copy.Box(new Rect(2, 1, 12, 4), "Title");
            var lines = copy.Plain().Split(Environment.NewLine);
            Check(lines[1].StartsWith("  ┌─ Title ──┐"), "Top border: " + lines[1]); Check(lines[4].StartsWith("  └──────────┘"), "Bottom border");
            Check(lines[2].Substring(3, 10).Trim().Length == 0, "Interior cleared");
            new Canvas(5, 2).Box(new Rect(-3, -3, 40, 40), "x"); new Canvas(5, 2).Box(new Rect(0, 0, 1, 1)); // no exception
            var styled = new Canvas(20, 3); Styled.Draw(styled, new Rect(0, 0, 20, 3), [new("+ a", Ink.Added), new("plain", Ink.Text)], 0);
            Equal(Ink.Added, styled.InkAt(19, 0), "Added background spans the row");
            Equal(3, Styled.FromText("a\r\nb\nc").Count, "Text lines");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "canvas boxes"`. Expected: build FAILS (`Clone`, `Styled` missing).

- [ ] **Step 3: Implement**

In `Terminal.cs`, replace the `Ink` enum:

```csharp
public enum Ink { Text, Muted, Accent, Good, Warning, Bad, Heading, Added, Removed, Hunk, Gutter, Ref, Border }
```

In `Canvas`, add after `Fill`:

```csharp
    public Ink InkAt(int x, int y) => inks[y, x];
    public Canvas Clone()
    {
        var copy = new Canvas(width, height);
        Array.Copy(chars, copy.chars, chars.Length); Array.Copy(inks, copy.inks, inks.Length); Array.Copy(highlights, copy.highlights, highlights.Length);
        return copy;
    }
    // Popups draw over a dimmed copy of the last frame so the user keeps their context.
    public void Dim()
    {
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) { inks[y, x] = Ink.Gutter; highlights[y, x] = false; }
    }
    public void Box(Rect area, string? title = null)
    {
        if (area.Width < 2 || area.Height < 2) return;
        Text(area.X, area.Y, "┌" + new string('─', area.Width - 2) + "┐", Ink.Border);
        for (int y = area.Y + 1; y < area.Bottom - 1; y++) { Text(area.X, y, "│" + new string(' ', area.Width - 2) + "│", Ink.Border); }
        Text(area.X, area.Bottom - 1, "└" + new string('─', area.Width - 2) + "┘", Ink.Border);
        if (!string.IsNullOrEmpty(title)) Text(area.X + 2, area.Y, " " + title + " ", Ink.Heading, area.Width - 4);
    }
```

In `Canvas.Ansi()`, replace the `colour` switch with:

```csharp
                    string colour = previous switch { Ink.Muted => "38;5;245", Ink.Accent => "38;5;117", Ink.Good => "38;5;114", Ink.Warning => "38;5;221", Ink.Bad => "38;5;210", Ink.Heading => "1;38;5;153",
                        Ink.Added => "38;5;114;48;5;22", Ink.Removed => "38;5;217;48;5;52", Ink.Hunk => "38;5;141", Ink.Gutter => "38;5;240", Ink.Ref => "1;38;5;180", Ink.Border => "38;5;244", _ => "38;5;252" };
```

Create `pwsh/GitWorkspace/Styled.cs`:

```csharp
namespace GitWorkspace;

public sealed record StyledLine(string Text, Ink Ink);

public static class Styled
{
    public static void Draw(Canvas c, Rect area, IReadOnlyList<StyledLine> lines, int scroll)
    {
        for (int i = 0; i < area.Height && scroll + i < lines.Count; i++) {
            var line = lines[scroll + i];
            // Added/removed backgrounds span the row so the side is visible without reading the marker.
            string text = line.Ink is Ink.Added or Ink.Removed ? line.Text.PadRight(area.Width) : line.Text;
            c.Text(area.X, area.Y + i, text, line.Ink, area.Width);
        }
    }
    public static List<StyledLine> FromText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => new StyledLine(l.Replace("\t", "    "), Ink.Text)).ToList();
}
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "canvas boxes"`. Expected: PASS. Run full suite: `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/Terminal.cs pwsh/GitWorkspace/Styled.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: canvas boxes, dimming, diff inks and styled lines"
```

---

### Task 5: DiffView with explicit old/new sides

**Files:**
- Create: `pwsh/GitWorkspace/DiffView.cs`
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `StyledLine`, `Ink` (Task 4), `Change`, `ChangeArea` (Models.cs)
- Produces:
  - `enum DiffKind { Header, Meta, Hunk, Context, Added, Removed, Note }`
  - `sealed record DiffLine(DiffKind Kind, int? Old, int? New, string Text)`
  - `sealed record DiffSides(string Old, string New)` with `static ForChange(Change)`, `static ForCommit(string parentShort, string commitShort, string subject)`, `string Legend`
  - `static class DiffView { List<DiffLine> Parse(string diff); List<StyledLine> Render(IReadOnlyList<DiffLine> lines, DiffSides? sides); bool LooksLikeDiff(string text); }`

- [ ] **Step 1: Write the failing tests**

Append inside `UiTests()`:

```csharp
        Test("diff view numbers old and new lines and states each side", () => {
            string diff = "diff --git a/src/A.cs b/src/A.cs\r\nindex 1..2 100644\r\n--- a/src/A.cs\r\n+++ b/src/A.cs\r\n@@ -10,3 +10,4 @@ class A\r\n keep\r\n-old line\r\n+new line\r\n+added\r\n tail\r\n\\ No newline at end of file\r\n";
            var lines = DiffView.Parse(diff);
            var removed = lines.Single(l => l.Kind == DiffKind.Removed); Equal(11, removed.Old, "Removed old number"); Check(removed.New is null, "Removed has no new number");
            var added = lines.Where(l => l.Kind == DiffKind.Added).ToList(); Equal(11, added[0].New, "Added new number"); Equal(12, added[1].New, "Second added");
            var tail = lines.Last(l => l.Kind == DiffKind.Context); Equal(12, tail.Old, "Context old"); Equal(13, tail.New, "Context new");
            Check(lines.Any(l => l.Kind == DiffKind.Note), "No-newline note"); Check(lines.All(l => !l.Text.Contains('\r')), "CR stripped");
            Equal("src/A.cs", lines.First(l => l.Kind == DiffKind.Header).Text, "File header");
            var styled = DiffView.Render(lines, DiffSides.ForChange(new("src/A.cs", null, ".M", ChangeArea.Unstaged)));
            Equal("− old: staged (index)   + new: working tree", styled[0].Text, "Legend");
            Check(styled.Any(s => s.Ink == Ink.Removed && s.Text.Contains(" 11 ") && s.Text.Contains("− old line")), "Removed rendering");
            Check(styled.Any(s => s.Ink == Ink.Added && s.Text.Contains("+ new line")), "Added rendering");
            Check(!styled.Any(s => s.Text.StartsWith("+++") || s.Text.StartsWith("---")), "Raw file markers hidden");
            Equal("− old: HEAD   + new: staged (index)", DiffSides.ForChange(new("a", null, "M.", ChangeArea.Staged)).Legend, "Staged legend");
            Equal("+ new file (not in Git)", DiffSides.ForChange(new("a", null, "??", ChangeArea.Untracked)).Legend, "Untracked legend");
            Equal("− old: parent 8175ff1   + new: a928430 \"Add QR\"", DiffSides.ForCommit("8175ff1", "a928430", "Add QR").Legend, "Commit legend");
            var rename = DiffView.Parse("diff --git a/old.txt b/new.txt\nsimilarity index 90%\nrename from old.txt\nrename to new.txt\n--- a/old.txt\n+++ b/new.txt\n");
            Equal("old.txt → new.txt", rename.First(l => l.Kind == DiffKind.Header).Text, "Rename header");
            var combined = DiffView.Parse("diff --cc c.txt\n@@@ -1,1 -1,1 +1,3 @@@\n++<<<<<<< ours\n +mine\n- theirs\n");
            Check(combined.Count(l => l.Kind == DiffKind.Added) == 2 && combined.Any(l => l.Kind == DiffKind.Removed), "Combined conflict diff");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "diff view"`. Expected: build FAILS (`DiffView` missing).

- [ ] **Step 3: Implement**

Create `pwsh/GitWorkspace/DiffView.cs`:

```csharp
using System.Text.RegularExpressions;

namespace GitWorkspace;

public enum DiffKind { Header, Meta, Hunk, Context, Added, Removed, Note }
public sealed record DiffLine(DiffKind Kind, int? Old, int? New, string Text);

public sealed record DiffSides(string Old, string New)
{
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

    public static bool LooksLikeDiff(string text) => text.StartsWith("diff --", StringComparison.Ordinal) || text.Contains("\ndiff --", StringComparison.Ordinal) || text.Contains("\n@@ ", StringComparison.Ordinal);

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
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "diff view"`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/DiffView.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: diff view with old/new gutters and side legend"
```

---

### Task 6: Untracked files as synthetic diffs

**Files:**
- Modify: `pwsh/GitWorkspace/Git.cs:152-169` (`Diff`)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `DiffView.Parse` (Task 5)
- Produces: `Git.Diff` returns a unified diff (`--- /dev/null`, `+++ b/<path>`, `@@ -0,0 +1,N @@`) for untracked text files; binary/oversize messages are unchanged.

- [ ] **Step 1: Write the failing test**

Append inside `UiTests()`:

```csharp
        Test("untracked and tracked diffs parse with real line numbers", () => {
            var repo = Repo(); Write(repo, "new.txt", "one\r\ntwo\n"); Write(repo, "file.txt", original.Replace("line 2\n", "line two\n"));
            var tree = Tree(repo);
            var untracked = DiffView.Parse(Git.Diff(tree, tree.Changes.Single(c => c.Area == ChangeArea.Untracked)));
            Equal(2, untracked.Count(l => l.Kind == DiffKind.Added), "Untracked lines"); Equal(2, untracked.Last(l => l.Kind == DiffKind.Added).New, "Untracked numbering");
            Equal("new.txt", untracked.First(l => l.Kind == DiffKind.Header).Text, "Untracked header");
            var tracked = DiffView.Parse(Git.Diff(tree, tree.Changes.Single(c => c.Area == ChangeArea.Unstaged)));
            Equal(2, tracked.Single(l => l.Kind == DiffKind.Removed).Old, "Tracked old number"); Equal(2, tracked.Single(l => l.Kind == DiffKind.Added).New, "Tracked new number");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "untracked and tracked"`. Expected: FAIL, `Untracked lines: expected 2, got 0`.

- [ ] **Step 3: Implement**

In `Git.Diff`, replace `return "Untracked · " + change.Path + "\n\n" + Encoding.UTF8.GetString(bytes);` with:

```csharp
                var content = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                if (bytes.Length == 0) return "Empty untracked file.";
                // A synthetic diff lets untracked files use the same old/new presentation as tracked edits.
                return $"--- /dev/null\n+++ b/{change.Path}\n@@ -0,0 +1,{content.Length} @@\n" + string.Concat(content.Select(l => "+" + l + "\n"));
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "untracked and tracked"`. Expected: PASS. Run full suite: `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/Git.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: untracked previews as synthetic diffs"
```

---

### Task 7: Popup dialogs over the dimmed screen

**Files:**
- Modify: `pwsh/GitWorkspace/Dialogs.cs` (entire file)
- Modify: `pwsh/GitWorkspace/GroupDialog.cs` and `pwsh/GitWorkspace/HistoryDialog.cs` (they call `Page`; keep compiling via `FullPage`)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `ISurface`, `Rect`, `Canvas.Clone/Dim/Box`, `StyledLine`, `Styled`
- Produces:
  - `Dialogs(ISurface terminal)` (was `Terminal`)
  - `(Canvas Canvas, Rect Inner) Frame(Canvas backdrop, string title, string hint, int width, int height)` (private)
  - `Canvas FullPage(string title, string hint)` (internal; replaces `Page` for full-screen panels)
  - `Choose`: options beginning with `"## "` render as unselectable section headings
  - `Review(string title, IReadOnlyList<StyledLine> lines, string accept = "Execute", bool confirm = true)` overload; the `string` overload colours diffs through `DiffView` when `DiffView.LooksLikeDiff` is true
  - Clicking outside a popup cancels it (same as Esc)

- [ ] **Step 1: Write the failing tests**

Append inside `UiTests()`:

```csharp
        Test("popups draw over the dimmed screen, skip headings and survive tiny terminals", () => {
            var backdrop = new Canvas(80, 24); backdrop.Text(0, 0, "BACKDROP");
            var surface = new ScriptedSurface(80, 24, new Input(ConsoleKey.DownArrow), new Input(ConsoleKey.Enter)); surface.SetBackdrop(backdrop);
            var pick = new Dialogs(surface).Choose("More actions", ["## Files", "Stage blocks", "## Stash", "Stash changes"]);
            Equal(3, pick, "Down skips the heading");
            Check(surface.Frames[0].StartsWith("BACKDROP"), "Backdrop visible behind popup"); Check(surface.Frames[0].Contains("┌─ More actions"), "Popup border");
            var outside = new ScriptedSurface(80, 24, new Input(X: 0, Y: 0, Button: MouseButton.Left));
            Check(new Dialogs(outside).Choose("Pick", ["a", "b"]) is null, "Click outside cancels");
            Check(new Dialogs(new ScriptedSurface(20, 6, new Input(ConsoleKey.Escape))).Choose("Tiny", ["a"]) is null, "Tiny terminal");
            Check(new Dialogs(new ScriptedSurface(20, 6, new Input(ConsoleKey.Escape))).Text("Tiny", "A long prompt that will not fit anywhere") is null, "Tiny text prompt");
            var review = new ScriptedSurface(100, 30, new Input(ConsoleKey.Enter));
            Check(new Dialogs(review).Review("Diff", "diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1 +1 @@\n-x\n+y\n"), "Review accepts");
            Check(review.Frames[0].Contains("+ y") && review.Frames[0].Contains("− x"), "Review renders diff sides");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "popups draw"`. Expected: build FAILS (`Dialogs` constructor takes `Terminal`).

- [ ] **Step 3: Implement**

Replace `pwsh/GitWorkspace/Dialogs.cs` with:

```csharp
namespace GitWorkspace;

public sealed partial class Dialogs(ISurface terminal)
{
    // Full-screen panel (group editor, large reviews on small terminals).
    internal Canvas FullPage(string title, string hint)
    {
        var c = new Canvas(terminal.Width, terminal.Height);
        c.Text(2, 1, title, Ink.Heading, c.Width - 4); c.Line(3);
        c.Line(c.Height - 3); c.Text(2, c.Height - 2, hint, Ink.Accent, c.Width - 4);
        return c;
    }
    Canvas Backdrop()
    {
        var last = terminal.LastFrame;
        var c = last is not null && last.Width == terminal.Width && last.Height == terminal.Height ? last.Clone() : new Canvas(terminal.Width, terminal.Height);
        c.Dim(); return c;
    }
    (Canvas Canvas, Rect Inner) Frame(Canvas backdrop, string title, string hint, int width, int height)
    {
        var c = backdrop.Clone();
        int w = Math.Max(2, Math.Min(terminal.Width - 2, Math.Max(30, width))), h = Math.Max(2, Math.Min(terminal.Height - 2, Math.Max(6, height)));
        var box = new Rect(Math.Max(0, (terminal.Width - w) / 2), Math.Max(0, (terminal.Height - h) / 2), w, h);
        c.Box(box, title);
        c.Text(box.X + 2, box.Bottom - 1, " " + hint + " ", Ink.Accent, Math.Max(0, box.Width - 4));
        return (c, new Rect(box.X + 2, box.Y + 1, Math.Max(0, box.Width - 4), Math.Max(0, box.Height - 2)));
    }
    static bool Outside(Input key, Rect inner) => key.Mouse && key.Wheel == 0 && !new Rect(inner.X - 2, inner.Y - 1, inner.Width + 4, inner.Height + 2).Contains(key.X, key.Y);
    static bool Heading(string option) => option.StartsWith("## ", StringComparison.Ordinal);

    public int? Choose(string title, IReadOnlyList<string> options, int initial = 0)
    {
        if (options.Count(o => !Heading(o)) == 0) { Message(title, "No available choices."); return null; }
        var backdrop = Backdrop(); string search = "";
        int focus = Math.Clamp(initial, 0, options.Count - 1), scroll = 0;
        while (true)
        {
            var visible = Enumerable.Range(0, options.Count).Where(i => search.Length == 0 ? true : !Heading(options[i]) && options[i].Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            int width = options.Select(o => o.Length).DefaultIfEmpty(0).Max() + 8, height = visible.Count + 3 + (search.Length > 0 ? 1 : 0);
            var (c, inner) = Frame(backdrop, title, "↑↓ · / search · Enter select · Esc cancel", Math.Max(width, title.Length + 8), height);
            int top = inner.Y + (search.Length > 0 ? 1 : 0), rows = Math.Max(1, inner.Bottom - top);
            if (search.Length > 0) c.Text(inner.X, inner.Y, "/ " + search, Ink.Muted, inner.Width);
            focus = NextSelectable(visible, options, Math.Clamp(focus, 0, Math.Max(0, visible.Count - 1)), 1);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, visible.Count); i++) {
                var option = options[visible[i]]; int y = top + i - scroll;
                if (Heading(option)) { c.Text(inner.X, y, option[3..].ToUpperInvariant(), Ink.Heading, inner.Width); continue; }
                if (i == focus) c.Fill(inner.X - 1, y, inner.Width + 2);
                c.Text(inner.X + 1, y, option, Ink.Text, inner.Width - 1, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = NextSelectable(visible, options, Math.Max(0, focus - 1), -1);
            if (key.Key == ConsoleKey.DownArrow) focus = NextSelectable(visible, options, Math.Min(visible.Count - 1, focus + 1), 1);
            if (key.Key == ConsoleKey.Home) focus = NextSelectable(visible, options, 0, 1);
            if (key.Key == ConsoleKey.End) focus = NextSelectable(visible, options, visible.Count - 1, -1);
            if (key.Key == ConsoleKey.PageDown) focus = NextSelectable(visible, options, Math.Min(visible.Count - 1, focus + rows), 1);
            if (key.Key == ConsoleKey.PageUp) focus = NextSelectable(visible, options, Math.Max(0, focus - rows), -1);
            if (key.Character == '/') { search = Text("Search choices", "Filter the available choices. Empty text shows all.", search) ?? search; focus = scroll = 0; }
            if (key.Key == ConsoleKey.Enter && visible.Count > 0 && !Heading(options[visible[focus]])) return visible[focus];
            if (key.Mouse && key.Wheel != 0) focus = NextSelectable(visible, options, Math.Clamp(focus + key.Wheel, 0, Math.Max(0, visible.Count - 1)), Math.Sign(key.Wheel));
            else if (key.Mouse && key.Y >= top && key.Y < top + rows && scroll + key.Y - top < visible.Count && !Heading(options[visible[scroll + key.Y - top]])) return visible[scroll + key.Y - top];
        }
    }
    static int NextSelectable(List<int> visible, IReadOnlyList<string> options, int from, int direction)
    {
        for (int i = from; i >= 0 && i < visible.Count; i += direction) if (!Heading(options[visible[i]])) return i;
        for (int i = from; i >= 0 && i < visible.Count; i -= direction) if (!Heading(options[visible[i]])) return i;
        return 0;
    }
    public List<int>? Check(string title, IReadOnlyList<string> options, IEnumerable<int>? initial = null)
    {
        if (options.Count == 0) { Message(title, "No available worktrees."); return null; }
        var backdrop = Backdrop(); var selected = (initial ?? []).ToHashSet(); int focus = 0, scroll = 0;
        while (true)
        {
            var (c, inner) = Frame(backdrop, title, "Space checkbox · A all · Enter accept · Esc cancel", options.Max(o => o.Length) + 12, options.Count + 4);
            c.Text(inner.X, inner.Y, $"{selected.Count} selected", Ink.Muted, inner.Width);
            int top = inner.Y + 1, rows = Math.Max(1, inner.Height - 1);
            if (focus < scroll) scroll = focus; if (focus >= scroll + rows) scroll = focus - rows + 1;
            for (int i = scroll; i < Math.Min(scroll + rows, options.Count); i++) {
                int y = top + i - scroll;
                if (i == focus) c.Fill(inner.X - 1, y, inner.Width + 2);
                c.Text(inner.X + 1, y, (selected.Contains(i) ? "☑  " : "☐  ") + options[i], Ink.Text, inner.Width - 1, i == focus);
            }
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(options.Count - 1, focus + 1);
            if (key.Key == ConsoleKey.Spacebar) { if (!selected.Remove(focus)) selected.Add(focus); }
            if (key.Key == ConsoleKey.A) { if (selected.Count == options.Count) selected.Clear(); else selected.UnionWith(Enumerable.Range(0, options.Count)); }
            if (key.Key == ConsoleKey.Enter) return selected.Order().ToList();
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, options.Count - 1);
            else if (key.Mouse && key.Y >= top && key.Y < top + rows && scroll + key.Y - top < options.Count) {
                focus = scroll + key.Y - top; if (!selected.Remove(focus)) selected.Add(focus);
            }
        }
    }
    public string? Text(string title, string prompt, string initial = "")
    {
        var backdrop = Backdrop(); string value = initial; int cursor = value.Length;
        while (true)
        {
            int width = Math.Min(terminal.Width - 2, 76);
            var wrapped = Wrap(prompt, Math.Max(10, width - 6)).ToList();
            var (c, inner) = Frame(backdrop, title, "Enter accept · Esc cancel · Ctrl+U clear", width, wrapped.Count + 5);
            foreach (var (line, index) in wrapped.Take(Math.Max(0, inner.Height - 2)).Select((s, i) => (s, i))) c.Text(inner.X, inner.Y + index, line, Ink.Muted, inner.Width);
            int y = inner.Bottom - 1; int offset = Math.Max(0, cursor - inner.Width + 3);
            c.Fill(inner.X, y, inner.Width); c.Text(inner.X, y, value[offset..], Ink.Text, inner.Width, true);
            c.Text(inner.X + cursor - offset, y, cursor < value.Length ? value[cursor].ToString() : "▏", Ink.Heading, 1, true);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key) || Outside(key, inner)) return null;
            if (key.Key == ConsoleKey.Enter) return value;
            if (key.Control && key.Key == ConsoleKey.U) { value = ""; cursor = 0; }
            else if (key.Key == ConsoleKey.Backspace && cursor > 0) value = value.Remove(--cursor, 1);
            else if (key.Key == ConsoleKey.Delete && cursor < value.Length) value = value.Remove(cursor, 1);
            else if (key.Key == ConsoleKey.LeftArrow) cursor = Math.Max(0, cursor - 1);
            else if (key.Key == ConsoleKey.RightArrow) cursor = Math.Min(value.Length, cursor + 1);
            else if (key.Key == ConsoleKey.Home) cursor = 0;
            else if (key.Key == ConsoleKey.End) cursor = value.Length;
            else if (!key.Mouse && !key.Control && !char.IsControl(key.Character) && key.Character != '\0') { value = value.Insert(cursor, key.Character.ToString()); cursor++; }
        }
    }
    public bool Review(string title, string text, string accept = "Execute", bool confirm = true) =>
        Review(title, DiffView.LooksLikeDiff(text) ? Mixed(text) : Styled.FromText(text), accept, confirm);
    // Plan previews mix prose with patches; only the patch part gets diff styling.
    static List<StyledLine> Mixed(string text)
    {
        int start = text.StartsWith("diff --", StringComparison.Ordinal) ? 0 : text.IndexOf("\ndiff --", StringComparison.Ordinal) + 1;
        if (start <= 0 && !text.StartsWith("diff --", StringComparison.Ordinal)) start = Math.Max(0, text.IndexOf("\n@@ ", StringComparison.Ordinal) + 1);
        return [.. Styled.FromText(text[..start].TrimEnd('\n')).Where(_ => start > 0), .. DiffView.Render(DiffView.Parse(text[start..]), null)];
    }
    public bool Review(string title, IReadOnlyList<StyledLine> lines, string accept = "Execute", bool confirm = true)
    {
        var backdrop = Backdrop(); int scroll = 0;
        while (true)
        {
            var (c, inner) = Frame(backdrop, title, confirm ? $"↑↓ PgUp PgDn scroll · Enter {accept} · Esc cancel" : "↑↓ PgUp PgDn scroll · Enter / Esc close",
                (int)(terminal.Width * 0.9), (int)(terminal.Height * 0.9));
            int count = Math.Max(1, inner.Height);
            scroll = Math.Clamp(scroll, 0, Math.Max(0, lines.Count - count));
            Styled.Draw(c, inner, lines, scroll);
            terminal.Draw(c); var key = terminal.Read();
            if (Cancel(key)) return false;
            if (key.Key == ConsoleKey.Enter) return confirm;
            if (key.Key == ConsoleKey.UpArrow) scroll--;
            if (key.Key == ConsoleKey.DownArrow) scroll++;
            if (key.Key == ConsoleKey.PageUp) scroll -= count;
            if (key.Key == ConsoleKey.PageDown) scroll += count;
            if (key.Key == ConsoleKey.Home) scroll = 0;
            if (key.Key == ConsoleKey.End) scroll = lines.Count - count;
            if (key.Mouse) scroll += key.Wheel;
        }
    }
    public void Message(string title, string text) => Review(title, text, confirm: false);
    public T Busy<T>(string title, Func<T> work)
    {
        var backdrop = Backdrop(); var task = Task.Run(work); int frame = 0;
        while (!task.IsCompleted) {
            var (c, inner) = Frame(backdrop, title, "Working…", 44, 5);
            c.Text(inner.X, inner.Y + 1, new[] { "◐", "◓", "◑", "◒" }[frame++ % 4] + "  Please wait", Ink.Accent, inner.Width);
            terminal.Draw(c); terminal.Poll(90);
        }
        return task.GetAwaiter().GetResult();
    }
    public static bool Cancel(Input key) => key.Key == ConsoleKey.Escape || key.Control && key.Key == ConsoleKey.C;
    public static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var raw in text.Replace("\r", "").Split('\n')) {
            var line = Canvas.Clean(raw.Replace("\t", "    "));
            if (line.Length == 0) { yield return ""; continue; }
            for (int start = 0; start < line.Length; start += width) yield return line.Substring(start, Math.Min(width, line.Length - start));
        }
    }
}
```

Note `Review(..., confirm: false)` returns `false` on Enter (a message has nothing to accept); callers of `Message` ignore the result.

In `GroupDialog.cs` and `HistoryDialog.cs`, replace every call `Page(` with `FullPage(` (both files are replaced later; this keeps them compiling now).

- [ ] **Step 4: Run to verify pass**

Run with `--filter "popups draw"`. Expected: PASS. Run full suite: `0 failed`.

- [ ] **Step 5: Manual smoke check**

Run `dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --demo` in a real terminal, press `W` then Esc, `M` then Esc. Expected: menus appear as bordered boxes over a dimmed main screen. (If you cannot run an interactive terminal, note this step for the user.)

- [ ] **Step 6: Commit**

```bash
git add pwsh/GitWorkspace/Dialogs.cs pwsh/GitWorkspace/GroupDialog.cs pwsh/GitWorkspace/HistoryDialog.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: dialogs render as popups over the dimmed screen"
```

---

### Task 8: Main screen on Layout + HitMap, status column, view bar and split ratio

**Files:**
- Modify: `pwsh/GitWorkspace/WorkspaceView.cs` (`Screen` class; `WorkspaceView.WorktreeMode` → `Mode`)
- Modify: `pwsh/GitWorkspace/Models.cs` (`ViewState.SplitRatio`)
- Modify: `pwsh/GitWorkspace/App.cs` (`Run`, `Handle` mouse block, preview width, `<`/`>` keys)
- Modify: `pwsh/GitWorkspace.Tests/Program.cs:188-193` (canvas test)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `Layout`, `HitMap`, `Hit`, `StyledLine`, `Styled`, `DiffView`
- Produces:
  - `enum ViewMode { Changes, Worktrees, Commits }`; `WorkspaceView.Mode`; `WorkspaceView.WorktreeMode` stays as a computed get/set property (`Mode == ViewMode.Worktrees`) so existing callers compile
  - `ViewState.SplitRatio` (double, default 0.5)
  - `Screen.StatusCode(Change) → string` (2 chars)
  - `Screen.Render(WorkspaceView view, Layout layout, HitMap hits, IReadOnlyList<StyledLine> preview, int previewScroll, string notice, bool refreshing, Action<Canvas, Layout, HitMap>? body = null) → Canvas` — `body` lets later tasks (tab strip, commits) draw regions
  - Old overload `Screen.Render(view, width, height, string preview, previewScroll, notice, refreshing)` kept for `--snapshot`/tests; it wraps the new one
  - Hits: `row`/`check`/`fold` with `Index` = row index; `view` with `Index` 0/1; `action` with `Id` = `"t"` (untracked), `"f"` (filter); `preview`
  - App field `readonly HitMap hits`

- [ ] **Step 1: Write the failing tests**

Append inside `UiTests()`:

```csharp
        Test("main screen uses status letters and registers row, checkbox and view hits", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            var hits = new HitMap(); var layout = Layout.For(120, 32, 0.5);
            string text = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(text.Contains(" M  src/EntryService.cs"), "Unstaged status letter"); Check(text.Contains("M   Controllers/QrCode.cs"), "Staged status letter");
            Check(!text.Contains("Modified"), "Word labels removed");
            int fileRow = view.Rows.FindIndex(r => r.Change?.Path == "src/EntryService.cs"); int y = layout.List.Y + fileRow;
            var check = Enumerable.Range(0, layout.List.Width).Select(x => hits.At(x, y)).First(h => h?.Kind == "check");
            Equal(fileRow, check!.Index, "Checkbox hit index"); Equal("row", hits.At(layout.List.Width - 3, y)?.Kind, "Row hit");
            Equal("view", hits.At(3, layout.ViewBar.Y)?.Kind, "View bar hit"); Equal("preview", hits.At(layout.Preview.X + 5, layout.Preview.Y + 3)?.Kind, "Preview hit");
            Equal("??", Screen.StatusCode(new("x", null, "??", ChangeArea.Untracked)), "Untracked code");
            Equal("UU", Screen.StatusCode(new("x", null, "UU", ChangeArea.Conflict)), "Conflict code");
            Equal("R ", Screen.StatusCode(new("x", "y", "R.", ChangeArea.Staged)), "Staged rename");
            Equal(" D", Screen.StatusCode(new("x", null, ".D", ChangeArea.Unstaged)), "Unstaged delete");
        });
```

In `pwsh/GitWorkspace.Tests/Program.cs`, the existing canvas test checks for a `PREVIEW` heading, which this task removes. Update its two assertions:

```csharp
            string wide = Screen.Render(view, 140, 32, Demo.Diff, 0, "", false).Plain(); Check(wide.Contains("━ src/EntryService.cs") && wide.Contains("AccessControl") && wide.Contains("Joiners") && wide.Contains("mobileApp"), "Wide layout");
            string narrow = Screen.Render(view, 85, 24, Demo.Diff, 0, "", false).Plain(); Check(!narrow.Contains("━ src/EntryService.cs") && narrow.Contains("Space select"), "Narrow layout");
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "main screen uses"`. Expected: build FAILS (no `Render(view, layout, hits, …)` overload / `StatusCode`).

- [ ] **Step 3: Implement the view mode and settings**

In `Models.cs` `ViewState`, add:

```csharp
    public double SplitRatio { get; set; } = 0.5;
```

In `WorkspaceView.cs`, above `WorkspaceView`, add `public enum ViewMode { Changes, Worktrees, Commits }` and replace `public bool WorktreeMode { get; set; }` with:

```csharp
    public ViewMode Mode { get; set; }
    public bool WorktreeMode { get => Mode == ViewMode.Worktrees; set => Mode = value ? ViewMode.Worktrees : ViewMode.Changes; }
```

In `Navigate`, replace `WorktreeMode = repo is not null;` with `if (Mode != ViewMode.Commits) WorktreeMode = repo is not null;`.

- [ ] **Step 4: Implement the renderer**

Replace the `Screen` class in `WorkspaceView.cs` with:

```csharp
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
```

This references `Footer` (Task 9). To compile now, create `pwsh/GitWorkspace/Footer.cs` with a minimal version that Task 9 replaces:

```csharp
namespace GitWorkspace;

public static class Footer
{
    public static List<(string Key, string Label)> For(WorkspaceView view, object? commits) =>
        view.WorktreeMode ? [("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("m", "More…"), ("i", "Rebase…"), ("?", "keys"), ("q", "quit")]
            : [(" ", "Space select"), ("s", "Stage"), ("u", "Unstage"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("m", "More…"), ("?", "keys"), ("q", "quit")];
    public static void Render(Canvas c, Rect area, HitMap hits, List<(string Key, string Label)> keys)
    {
        int x = area.X + 2;
        foreach (var (key, label) in keys) {
            string text = key == " " || key == "\r" ? label : key.ToUpperInvariant() + " " + label;
            if (x + text.Length > area.Right - 1) break;
            c.Text(x, area.Y, text, Ink.Accent); hits.Add(x, area.Y, text.Length, new Hit("action", Id: key)); x += text.Length + 3;
        }
    }
}
```

- [ ] **Step 5: Wire App to layout and hits**

In `App.cs`:

1. Add fields: `readonly HitMap hits = new();` and `List<StyledLine> previewLines = [];`.
2. In `Run`, replace the draw line with:

```csharp
                var layout = Layout.For(terminal.Width, terminal.Height, view.State.SplitRatio);
                terminal.Draw(Screen.Render(view, layout, hits, previewLines, previewScroll, notice, scan is not null));
```

3. Add this as the last statement of `UpdatePreview`, after the final `else` branch. Every branch assigns `preview` and falls through to it; the method's only early return is the unchanged-key check at the top:
   `previewLines = DiffView.LooksLikeDiff(preview) ? DiffView.Render(DiffView.Parse(preview), null) : Styled.FromText(preview);`
   (Task 10 replaces this line.)
4. Replace the whole `if (key.Mouse) { ... }` block at the top of `Handle` with:

```csharp
        if (key.Mouse)
        {
            var hit = hits.At(key.X, key.Y);
            if (key.Wheel != 0) {
                if (hit?.Kind == "preview") previewScroll = Math.Max(0, previewScroll + key.Wheel);
                else view.Focus = Math.Clamp(view.Focus + key.Wheel, 0, Math.Max(0, view.Rows.Count - 1));
                return;
            }
            switch (hit?.Kind)
            {
                case "view": SetMode((ViewMode)hit.Index); break;
                case "action": HandleAction(hit.Id!); break;
                case "check": view.Focus = hit.Index; view.Toggle(view.Current); break;
                case "fold": view.Focus = hit.Index; Collapse(); break;
                case "row": view.Focus = hit.Index; if (key.DoubleClick) Handle(new(ConsoleKey.Enter)); break;
            }
            return;
        }
```

5. Add helpers to `App`:

```csharp
    void SetMode(ViewMode mode)
    {
        if (mode == ViewMode.Commits) { notice = "Commits view arrives in a later update."; return; } // replaced in Task 20
        view.Mode = mode; view.Rebuild(); previewKey = "";
    }
    void HandleAction(string id)
    {
        if (id == " ") Handle(new(ConsoleKey.Spacebar));
        else if (id == "\r") Handle(new(ConsoleKey.Enter));
        else Handle(new(Character: id[0]));
    }
```

6. Replace the Tab line with `if (key.Key == ConsoleKey.Tab) { SetMode(view.WorktreeMode ? ViewMode.Changes : ViewMode.Worktrees); return; }`.
7. Add to the character switch: `case '<': view.State.SplitRatio = Math.Clamp(view.State.SplitRatio - 0.05, 0.35, 0.65); break;` and `case '>': view.State.SplitRatio = Math.Clamp(view.State.SplitRatio + 0.05, 0.35, 0.65); break;`

- [ ] **Step 6: Run to verify pass**

Run with `--filter "main screen uses"`, then `--filter "canvas adapts"`, then the full suite. Expected: all PASS.

- [ ] **Step 7: Check the snapshot**

Run `dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --snapshot 120 32`. Expected: view bar `[Changes]  Worktrees   Commits` with `T untracked` and `F All changes` on the right; file rows show ` M`/`M ` codes; preview split visible at 120 columns.

- [ ] **Step 8: Commit**

```bash
git add pwsh/GitWorkspace/WorkspaceView.cs pwsh/GitWorkspace/Models.cs pwsh/GitWorkspace/App.cs pwsh/GitWorkspace/Footer.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: layout-driven main screen with hit regions and status letters"
```

---

### Task 9: Context footer and sectioned More menu

**Files:**
- Modify: `pwsh/GitWorkspace/Footer.cs` (replace `For`)
- Modify: `pwsh/GitWorkspace/App.cs` (`More`, extract stash/project/activity/diagnostics methods, `Help`)
- Modify: `pwsh/GitWorkspace.Tests/Program.cs:191` (narrow layout assertion)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `Footer.Render`, `Choose` headings (Task 7)
- Produces: `Footer.For(WorkspaceView view, CommitsModel? commits)` — the parameter type is `object?` until Task 19 changes it to `CommitsModel?`. Key ids: single lower-case characters, `" "` for Space, `"\r"` for Enter, `"?"`, `"q"`.

- [ ] **Step 1: Write the failing tests**

Append inside `UiTests()`:

```csharp
        Test("footer keys follow the focused row", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            string Keys() => string.Join(",", Footer.For(view, null).Select(k => k.Key));
            view.Focus = view.Rows.FindIndex(r => r.Kind == "file"); Check(Keys().StartsWith(" ,s,u,c,o,\r"), "File keys: " + Keys());
            view.Focus = view.Rows.FindIndex(r => r.Kind == "worktree"); Check(Keys().Contains("h") && !Keys().Contains(",s,"), "Worktree keys: " + Keys());
            view.WorktreeMode = true; view.Rebuild(); view.Focus = view.Rows.FindIndex(r => r.Kind == "worktree"); Check(Keys().Contains("i"), "Worktree mode rebase");
            view.Navigate("AccessControl"); view.Focus = view.Rows.FindIndex(r => r.Kind == "branch"); Equal("\r,b,p,?,q", Keys(), "Branch row keys");
            Check(Footer.For(view, null).All(k => k.Key.Length == 1), "Single-character ids");
        });
```

In `pwsh/GitWorkspace.Tests/Program.cs`, change `narrow.Contains("Space select")` to `narrow.Contains("Space select") && narrow.Contains("? keys")`.

- [ ] **Step 2: Run to verify failure**

Run with `--filter "footer keys"`. Expected: FAIL (`File keys: ` shows the old fixed list).

- [ ] **Step 3: Implement the footer**

Replace `Footer.For` in `Footer.cs` with:

```csharp
    public static List<(string Key, string Label)> For(WorkspaceView view, object? commits)
    {
        List<(string, string)> keys = view.Current?.Kind switch {
            "file" when !view.WorktreeMode => [(" ", "Space select"), ("s", "Stage"), ("u", "Unstage"), ("c", "Commit…"), ("o", "Open"), ("\r", "Enter review"), ("m", "More…")],
            "branch" => [("\r", "Enter history"), ("b", "Checkout…"), ("p", "Repo")],
            "worktree" or "project" when view.WorktreeMode => [(" ", "Space select"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("i", "Rebase…"), ("h", "History"), ("m", "More…")],
            "worktree" or "project" => [(" ", "Space select"), ("c", "Commit…"), ("b", "Branch…"), ("y", "Sync…"), ("h", "History"), ("m", "More…")],
            _ => [("w", "Groups"), ("m", "More…")]
        };
        keys.Add(("?", "keys")); keys.Add(("q", "quit"));
        return keys;
    }
```

- [ ] **Step 4: Implement the sectioned More menu**

In `App.cs`, replace `More()` with:

```csharp
    void More()
    {
        (string Label, Action? Run)[] items = [
            ("## Files", null),
            ("Stage blocks of the highlighted file…", () => HunkAction(false)), ("Unstage blocks of the highlighted file…", () => HunkAction(true)),
            ("Discard selected unstaged edits…", () => FileAction("Discard unstaged")), ("Delete selected untracked files…", () => FileAction("Delete untracked")),
            ("Open highlighted file in editor", Editor), ("Add files or patterns to .gitignore…", IgnoreMenu),
            ("## Stash", null),
            ("Stash worktree changes…", StashChanges), ("Apply a stash (keep entry)…", ApplyStash),
            ("## Tools", null),
            ("Open configured diff tool", () => ExternalGit(false)), ("Open configured merge tool", () => ExternalGit(true)),
            ("## Repos & groups", null),
            ("Add project…", AddProject), ("Remove project from workspace…", RemoveProject), ("Remove saved group…", RemoveGroup),
            ("## Admin", null),
            ("Activity and results", ShowActivity), ("Retry failed actions from last batch…", RetryFailed), ("Diagnostics", Diagnostics), ("Help", Help)
        ];
        var pick = dialog.Choose("More actions", items.Select(i => i.Label).ToList(), 1);
        if (pick is not null) items[pick.Value].Run?.Invoke();
    }
    void StashChanges()
    {
        if (!CanWrite()) return;
        var targets = PickTargets("Stash changes"); if (targets is null) return;
        var message = dialog.Text("Stash changes", "Message (tracked and untracked files are included).", "Workspace " + DateTime.Now.ToString("g"));
        if (message is not null) Execute(targets.Select(t => Operations.Stash(t, message)).ToList());
    }
    void ApplyStash()
    {
        if (!CanWrite()) return;
        var targets = PickTargets("Apply stash"); if (targets is null) return; List<Plan> plans = [];
        foreach (var t in targets) {
            var stashes = Git.Run(t.Path, "stash", "list", "--format=%gd %s").Require().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var s = dialog.Choose("Apply stash · " + t.Project + " (stashes are shared by its worktrees)", stashes);
            if (s is null) return; plans.Add(Operations.ApplyStash(t, stashes[s.Value].Split(' ')[0]));
        }
        Execute(plans);
    }
    void RemoveProject()
    {
        if (!CanWrite()) return;
        var remove = dialog.Choose("Remove registration (keep all repository files)", settings.Projects.Select(p => p.Name).ToList());
        if (remove.HasValue && dialog.Review("Remove project registration", "Remove " + settings.Projects[remove.Value].Name + " from this workspace configuration. All files and Git data stay in place.")) {
            settings.Projects.RemoveAt(remove.Value); Config.Save(configPath, settings); view.Navigate(null, "All projects"); Refresh();
        }
    }
    void ShowActivity() => dialog.Message("Activity · most recent first", activity.Count == 0 ? "No actions recorded yet." :
        string.Join("\n\n", activity.AsEnumerable().Reverse().Select(a => $"{(a.Success ? "✓" : "!")} {a.Time:g} · {a.Action}\n{a.Target}\n{a.Detail}")));
    void RetryFailed() { if (CanWrite()) { if (failed.Count == 0) dialog.Message("Retry", "No failed actions in this session."); else Execute(failed.ToList()); } }
    void Diagnostics() => dialog.Message("Diagnostics", $"Config: {configPath}\nLocal status refreshed: {view.Snapshot.TakenAt:g}\n\n" +
        string.Join('\n', view.Snapshot.Errors) + "\n\n" + string.Join("\n\n", view.Snapshot.Worktrees.Select(t => $"{t.Title}\n{t.Path}\n{t.Error}\n{t.Prunable}\n{t.Locked}")));
```

In `Screen.Render` (`WorkspaceView.cs`), the footer call stays `Footer.Render(c, layout.Footer, hits, Footer.For(view, null));`.

- [ ] **Step 5: Run to verify pass**

Run with `--filter "footer keys"`, then the full suite. Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add pwsh/GitWorkspace/Footer.cs pwsh/GitWorkspace/App.cs pwsh/GitWorkspace.Tests/UiTests.cs pwsh/GitWorkspace.Tests/Program.cs
git commit -m "gws: context-aware footer and sectioned More menu"
```

---

### Task 10: Side-aware previews and reviews

**Files:**
- Modify: `pwsh/GitWorkspace/App.cs` (`UpdatePreview`, Enter handling, destructive `FileAction` review)
- Test: `pwsh/GitWorkspace.Tests/UiTests.cs`

**Interfaces:**
- Consumes: `DiffSides.ForChange`, `DiffView.Parse/Render`, `Dialogs.Review(string, IReadOnlyList<StyledLine>, …)`
- Produces: `static List<StyledLine> App.PreviewFor(Worktree tree, Change change, string diff, bool changedSinceSelection)` (public static, testable)

- [ ] **Step 1: Write the failing test**

Append inside `UiTests()`:

```csharp
        Test("change previews carry a side legend and stale-selection warning", () => {
            var repo = Repo(); Write(repo, "file.txt", "changed\n"); Git.Run(repo, "add", ".").Require(); Write(repo, "file.txt", "changed again\n");
            var tree = Tree(repo); var staged = tree.Changes.Single(c => c.Area == ChangeArea.Staged);
            var lines = App.PreviewFor(tree, staged, Git.Diff(tree, staged), false);
            Check(lines.Any(l => l.Text == "− old: HEAD   + new: staged (index)"), "Staged legend");
            Check(lines.Any(l => l.Ink == Ink.Added && l.Text.EndsWith("+ changed")), "Staged addition");
            var warned = App.PreviewFor(tree, staged, Git.Diff(tree, staged), true); Equal(Ink.Warning, warned[0].Ink, "Stale warning first");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "change previews"`. Expected: build FAILS (`App.PreviewFor` missing).

- [ ] **Step 3: Implement**

Add to `App.cs`:

```csharp
    public static List<StyledLine> PreviewFor(Worktree tree, Change change, string diff, bool changedSinceSelection)
    {
        List<StyledLine> lines = [];
        if (changedSinceSelection) { lines.Add(new("UPDATED SINCE SELECTION · uncheck and review before acting", Ink.Warning)); lines.Add(new("", Ink.Text)); }
        lines.Add(new(tree.Title, Ink.Accent));
        if (DiffView.LooksLikeDiff(diff) || diff.StartsWith("--- ", StringComparison.Ordinal)) lines.AddRange(DiffView.Render(DiffView.Parse(diff), DiffSides.ForChange(change)));
        else { lines.Add(new(DiffSides.ForChange(change).Legend, Ink.Heading)); lines.AddRange(Styled.FromText(diff)); }
        return lines;
    }
```

Extend `DiffView.LooksLikeDiff` in `DiffView.cs` so synthetic untracked diffs qualify:

```csharp
    public static bool LooksLikeDiff(string text) => text.StartsWith("diff --", StringComparison.Ordinal) || text.StartsWith("--- ", StringComparison.Ordinal) ||
        text.Contains("\ndiff --", StringComparison.Ordinal) || text.Contains("\n@@ ", StringComparison.Ordinal);
```

In `UpdatePreview`, replace the `if (row?.Change is not null) { ... }` branch with:

```csharp
        if (row?.Change is not null)
        {
            string diff = demo ? Demo.Diff : Git.Diff(row.Tree!, row.Change);
            bool stale = !demo && view.SelectionStamps.TryGetValue(row.Id, out var stamp) && stamp != Operations.ChangeStamp(row.Tree!, row.Change);
            preview = diff; previewLines = PreviewFor(row.Tree!, row.Change, diff, stale); return;
        }
```

and replace the last statement added in Task 8 (`previewLines = DiffView.LooksLikeDiff(preview) ? … ;`) with `previewLines = Styled.FromText(preview);`, so the non-file branches render as plain text.

Replace the Enter handler's `dialog.Message("Preview · " + …, preview)` with `dialog.Review("Preview · " + (view.Current?.Text ?? "Workspace"), previewLines, confirm: false)`.

In `FileAction`, replace the `details` construction and its `Review` call with:

```csharp
            List<StyledLine> details = [.. Styled.FromText(explanation), new("", Ink.Text)];
            foreach (var (tree, change) in files) { details.AddRange(PreviewFor(tree, change, Git.Diff(tree, change), false)); details.Add(new("", Ink.Text)); }
            if (!dialog.Review(action, details, "Review file list")) return;
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "change previews"`, then the full suite. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/App.cs pwsh/GitWorkspace/DiffView.cs pwsh/GitWorkspace.Tests/UiTests.cs
git commit -m "gws: previews and destructive reviews state old and new sides"
```

---

### Task 11: Phase 1 documentation and help

**Files:**
- Modify: `pwsh/GitWorkspace/README.md` ("Working in the UI" table and paragraphs about the preview), `pwsh/GitWorkspace/App.cs` (`Help`)

- [ ] **Step 1: Update README**

In the key table: replace "Tab, or click the view label" row with `| Changes / worktree selection | Tab, or click Changes / Worktrees in the view bar |`; add rows `| Resize the preview split | < / > |` and `| Keys for the focused row | footer; ? lists every key |`. Replace "Wide terminals show a diff alongside the list… Minimum size is 66 columns by 16 rows." with:

```markdown
Terminals of 100 columns or more show a preview beside the list; `<` / `>` resize the
split (remembered between launches). Narrow terminals use Enter for a full preview.
Minimum size is 66 columns by 16 rows. Menus and prompts open as popups over the main
screen; Esc or a click outside closes them.

File rows use Git's two-letter status: the left letter is the staged side, the right
letter the unstaged side (` M`, `M `, `A `, `D `, `R `, `??`, `UU`). Every diff starts
with a legend naming its sides, for example `− old: staged (index)   + new: working tree`,
and shows separate `old` / `new` line numbers. Removed lines have a red background and
`−`; added lines a green background and `+`.
```

- [ ] **Step 2: Update in-app help**

In `Help()`, replace the substring `"Tab    Changes / Worktrees          A     Select all visible\n"` with `"Tab    Changes / Worktrees          A     Select all visible\n<  >  Resize the preview split\n"`. Then insert this paragraph immediately before the paragraph that starts `"T toggles untracked files`:
`"Diff legend: the first line names the old (−) and new (+) sides. Two gutters show old and new line numbers; removed lines have a red background, added lines green.\n\n" +`

- [ ] **Step 3: Verify and commit**

Run the full suite: `0 failed`. Then:

```bash
git add pwsh/GitWorkspace/README.md pwsh/GitWorkspace/App.cs
git commit -m "gws docs: popups, status letters, diff legend"
```

---

# Phase 2 — Groups

### Task 12: Discovery and the Roots setting

**Files:**
- Create: `pwsh/GitWorkspace/Discovery.cs`
- Modify: `pwsh/GitWorkspace/Models.cs` (`Settings.Roots`), `pwsh/GitWorkspace/Program.cs` (`status --root`)
- Create: `pwsh/GitWorkspace.Tests/GroupUiTests.cs`; Modify `pwsh/GitWorkspace.Tests/Program.cs` (call `GroupUiTests()`)

**Interfaces:**
- Produces:
  - `Settings.Roots : List<string>?` (omitted from JSON when null)
  - `sealed record DiscoveredRepo(string Name, string Path, string CommonDir, string? RegisteredAs)`
  - `static class Discovery { List<string> Roots(Settings settings); List<DiscoveredRepo> Scan(Settings settings, IEnumerable<string> roots); bool IsRepositoryRoot(string directory); }`

- [ ] **Step 1: Write the failing tests**

Create `pwsh/GitWorkspace.Tests/GroupUiTests.cs`:

```csharp
using System.Text.Json;
using GitWorkspace;

static partial class Tests
{
    static string ScanRoot() { var path = Path.Combine(root, "scan root " + ++sequence); Directory.CreateDirectory(path); return path; }
    static string InitAt(string path) { Directory.CreateDirectory(path); Git.Run(path, "init", "-b", "main").Require(); return path; }
    static void GroupUiTests()
    {
        Test("discovery lists repos one level down, marks registered ones and skips non-repos", () => {
            var scan = ScanRoot();
            var normal = InitAt(Path.Combine(scan, "Normal"));
            var source = Repo(); var layout = Path.Combine(scan, "Layout"); Directory.CreateDirectory(layout);
            Git.Run(source, "clone", "--bare", source, Path.Combine(layout, ".git-main")).Require();
            Git.Run(source, "clone", "--bare", source, Path.Combine(scan, "Bare.git")).Require();
            Directory.CreateDirectory(Path.Combine(scan, "Plain folder"));
            Directory.CreateDirectory(Path.Combine(normal, "nested inside repo"));
            var registered = InitAt(Path.Combine(scan, "Registered"));
            var settings = new Settings { Projects = [new("Already here", registered)] };
            var found = Discovery.Scan(settings, [scan, Path.Combine(root, "missing root"), normal]);
            Equal("Bare.git,Layout,Normal,Registered", string.Join(",", found.Select(f => f.Name)), "Found repos (nested folder under a repo root is not a repo)");
            Equal("Already here", found.Single(f => f.Name == "Registered").RegisteredAs, "Registered by Git directory");
            Check(found.Where(f => f.Name != "Registered").All(f => f.RegisteredAs is null), "Others unregistered");
        });
        Test("roots default to C:/repos only when unset and stay out of saved JSON", () => {
            var unset = new Settings(); var roots = Discovery.Roots(unset);
            Check(Directory.Exists("C:/repos") ? roots.SequenceEqual(["C:/repos"]) : roots.Count == 0, "Default root");
            Equal(0, Discovery.Roots(new Settings { Roots = [] }).Count, "Explicit empty list");
            Check(!JsonSerializer.Serialize(unset, Config.Json).Contains("Roots"), "Null Roots omitted");
            var loaded = JsonSerializer.Deserialize<Settings>("{\"Version\":1,\"Projects\":[],\"Roots\":[\"D:/src\"]}", Config.Json)!;
            Equal("D:/src", loaded.Roots!.Single(), "Roots round-trip");
        });
    }
}
```

In `Program.cs` `Main`, add `GroupUiTests();` after `UiTests();`.

- [ ] **Step 2: Run to verify failure**

Run with `--filter "discovery lists"`. Expected: build FAILS (`Discovery` missing).

- [ ] **Step 3: Implement**

In `Models.cs` `Settings`, add:

```csharp
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Roots { get; set; }
```

Create `pwsh/GitWorkspace/Discovery.cs`:

```csharp
namespace GitWorkspace;

public sealed record DiscoveredRepo(string Name, string Path, string CommonDir, string? RegisteredAs);

public static class Discovery
{
    static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static List<string> Roots(Settings settings) => settings.Roots ?? (Directory.Exists("C:/repos") ? ["C:/repos"] : []);
    // Only folders that carry their own repository markers count; otherwise every
    // subfolder of a checkout would resolve to that checkout's repository.
    public static bool IsRepositoryRoot(string directory) =>
        Directory.Exists(System.IO.Path.Combine(directory, ".git")) || File.Exists(System.IO.Path.Combine(directory, ".git")) ||
        Directory.Exists(System.IO.Path.Combine(directory, ".git-main")) || Directory.Exists(System.IO.Path.Combine(directory, ".bare")) ||
        File.Exists(System.IO.Path.Combine(directory, "HEAD")) && Directory.Exists(System.IO.Path.Combine(directory, "objects"));
    public static List<DiscoveredRepo> Scan(Settings settings, IEnumerable<string> roots)
    {
        var registered = new Dictionary<string, string>(Paths);
        foreach (var p in settings.Projects) { try { registered.TryAdd(Git.ResolveRepository(p.Path), p.Name); } catch (InvalidOperationException) { } }
        var seen = new HashSet<string>(Paths); List<DiscoveredRepo> found = [];
        foreach (var root in roots)
        {
            List<string> directories;
            try { directories = Directory.EnumerateDirectories(System.IO.Path.GetFullPath(root)).Order(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { continue; }
            foreach (var directory in directories.Where(IsRepositoryRoot))
            {
                string common;
                try { common = Git.ResolveRepository(directory); } catch (InvalidOperationException) { continue; }
                if (!seen.Add(common)) continue;
                found.Add(new(System.IO.Path.GetFileName(directory), System.IO.Path.GetFullPath(directory).Replace('\\', '/'), common, registered.GetValueOrDefault(common)));
            }
        }
        return found;
    }
}
```

In `Program.cs`, replace the `--root` discovery loop:

```csharp
                    settings = new();
                    foreach (var repo in Discovery.Scan(settings, [Path.GetFullPath(arguments[root + 1])])) settings.Projects.Add(new(repo.Name, repo.Path));
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "discovery lists"`, `--filter "roots default"`, then the full suite. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/Discovery.cs pwsh/GitWorkspace/Models.cs pwsh/GitWorkspace/Program.cs pwsh/GitWorkspace.Tests/GroupUiTests.cs pwsh/GitWorkspace.Tests/Program.cs
git commit -m "gws: discover repositories under configured roots"
```

---

### Task 13: Group tab strip

**Files:**
- Create: `pwsh/GitWorkspace/TabStrip.cs`
- Modify: `pwsh/GitWorkspace/WorkspaceView.cs` (`ChangeCount`, render tabs in `Screen.Render`), `pwsh/GitWorkspace/App.cs` (tab hits, digits, `Ctrl+N`, `E`, right-click), `pwsh/GitWorkspace/Navigation.cs` (`GroupActions`, `Workspace` popup)
- Test: `pwsh/GitWorkspace.Tests/GroupUiTests.cs`

**Interfaces:**
- Consumes: `HitMap`, `Rect`, `WorkspaceView`
- Produces:
  - `sealed record Tab(string Label, string Target, bool Repo)`
  - `static class TabStrip { List<Tab> Tabs(WorkspaceView view); int Active(WorkspaceView view, List<Tab> tabs); List<(int Index, int X, int Width)> Place(IReadOnlyList<string> labels, int active, int width, out bool moreLeft, out bool moreRight); void Render(Canvas c, Rect area, HitMap hits, WorkspaceView view); }`
  - `WorkspaceView.ChangeCount(string group) → int` (distinct changed paths in the group's worktrees, honouring `ShowUntracked`)
  - Hits: `tab` (`Index` = tab index), `tab-new`, `tab-prev`, `tab-next`
  - `App.GroupActions(WorkspaceGroup group)`; `App.OpenTab(int index)`

- [ ] **Step 1: Write the failing tests**

Append inside `GroupUiTests()`:

```csharp
        Test("tab strip keeps the active tab visible and marks overflow", () => {
            var labels = Enumerable.Range(0, 21).Select(i => i == 0 ? "All projects" : "Group " + i).ToList();
            var placed = TabStrip.Place(labels, 15, 80, out bool left, out bool right);
            Check(placed.Any(p => p.Index == 15), "Active visible"); Check(left, "Earlier tabs hidden"); Check(placed.Last().X + placed.Last().Width <= 80 - 5, "Room for markers and +");
            var few = TabStrip.Place(labels.Take(3).ToList(), 0, 120, out left, out right); Equal(3, few.Count, "All fit"); Check(!left && !right, "No markers");
            Equal(0, TabStrip.Place([], 0, 80, out _, out _).Count, "No tabs");
        });
        Test("tabs include groups, counts and a temporary repo tab with hits", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            Equal("All projects,Secure QR,Maintenance", string.Join(",", TabStrip.Tabs(view).Select(t => t.Label)), "Group tabs");
            Equal(5, view.ChangeCount("All projects"), "All changes"); Equal(1, view.ChangeCount("Maintenance"), "Maintenance count");
            view.Navigate("Joiners"); var tabs = TabStrip.Tabs(view); Check(tabs.Last().Repo && tabs.Last().Label.Contains("Joiners"), "Repo tab"); Equal(tabs.Count - 1, TabStrip.Active(view, tabs), "Repo tab active");
            var c = new Canvas(120, 3); var hits = new HitMap(); TabStrip.Render(c, new Rect(0, 1, 120, 1), hits, view);
            Check(Enumerable.Range(0, 120).Any(x => hits.At(x, 1)?.Kind == "tab-new"), "Plus tab hit");
            Equal(1, Enumerable.Range(0, 120).Select(x => hits.At(x, 1)).First(h => h?.Kind == "tab" && h.Index == 1)!.Index, "Tab hit index");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "tab strip"`. Expected: build FAILS (`TabStrip` missing).

- [ ] **Step 3: Implement TabStrip and ChangeCount**

Add to `WorkspaceView`:

```csharp
    public int ChangeCount(string group)
    {
        var g = Settings.Groups.FirstOrDefault(x => x.Name == group);
        return Snapshot.Worktrees.Where(t => g is null || g.Includes(t))
            .Sum(t => t.Changes.Where(c => State.ShowUntracked || c.Area != ChangeArea.Untracked).Select(c => c.Path).Distinct().Count());
    }
```

Create `pwsh/GitWorkspace/TabStrip.cs`:

```csharp
namespace GitWorkspace;

public sealed record Tab(string Label, string Target, bool Repo);

public static class TabStrip
{
    const int Start = 2, Reserved = 7; // "‹ " before, " ›" and " + " after
    public static List<Tab> Tabs(WorkspaceView view)
    {
        List<Tab> tabs = [new("All projects", "All projects", false), .. view.Settings.Groups.Select(g => new Tab(g.Name, g.Name, false))];
        if (view.State.Repo is { } repo) tabs.Add(new("⌂ " + repo + " ×", repo, true));
        return tabs;
    }
    public static int Active(WorkspaceView view, List<Tab> tabs) => view.RepoMode ? tabs.Count - 1 : Math.Max(0, tabs.FindIndex(t => !t.Repo && t.Target == view.State.Group));
    public static List<(int Index, int X, int Width)> Place(IReadOnlyList<string> labels, int active, int width, out bool moreLeft, out bool moreRight)
    {
        moreLeft = moreRight = false; List<(int, int, int)> placed = [];
        if (labels.Count == 0) return placed;
        int available = Math.Max(0, width - Start - Reserved), first = Math.Clamp(active, 0, labels.Count - 1);
        int Span(int from, int to) => Enumerable.Range(from, to - from + 1).Sum(i => labels[i].Length + 3);
        while (first > 0 && Span(first - 1, active) <= available) first--;
        while (first < active && Span(first, active) > available) first++;
        int last = active; while (last + 1 < labels.Count && Span(first, last + 1) <= available) last++;
        int x = Start;
        for (int i = first; i <= last; i++) { int w = labels[i].Length + 2; if (x + w > Start + available) break; placed.Add((i, x, w)); x += w + 1; }
        moreLeft = first > 0; moreRight = placed.Count == 0 || placed[^1].Item1 < labels.Count - 1;
        return placed;
    }
    public static void Render(Canvas c, Rect area, HitMap hits, WorkspaceView view)
    {
        var tabs = Tabs(view); int active = Active(view, tabs);
        var labels = tabs.Select(t => t.Repo ? t.Label : t.Label + (view.ChangeCount(t.Target) is var n && n > 0 ? " " + n : "")).ToList();
        var placed = Place(labels, active, area.Width, out bool left, out bool right);
        if (left) { c.Text(area.X, area.Y, "‹", Ink.Muted); hits.Add(area.X, area.Y, 2, new Hit("tab-prev")); }
        foreach (var (index, x, width) in placed) {
            bool on = index == active;
            c.Text(area.X + x, area.Y, " " + labels[index] + " ", on ? Ink.Heading : tabs[index].Repo ? Ink.Accent : Ink.Muted, width, on);
            c.Text(area.X + x + width, area.Y, "│", Ink.Border);
            hits.Add(area.X + x, area.Y, width, new Hit("tab", index));
        }
        int end = placed.Count == 0 ? Start : placed[^1].X + placed[^1].Width + 1;
        if (right) { c.Text(area.X + end, area.Y, " ›", Ink.Muted); hits.Add(area.X + end, area.Y, 2, new Hit("tab-next")); end += 2; }
        c.Text(area.X + end + 1, area.Y, " + ", Ink.Accent); hits.Add(area.X + end + 1, area.Y, 3, new Hit("tab-new"));
    }
}
```

In `Screen.Render`, after the title line, add `TabStrip.Render(c, layout.Tabs, hits, view);`.

- [ ] **Step 4: Wire tab input in App and Navigation**

Add to `Navigation.cs`:

```csharp
    void OpenTab(int index)
    {
        var tabs = TabStrip.Tabs(view); if (index < 0 || index >= tabs.Count) return;
        var tab = tabs[index]; if (tab.Repo) return;
        view.Navigate(null, tab.Target); previewKey = "";
    }
    void GroupActions(WorkspaceGroup group)
    {
        var action = dialog.Choose(group.Name, ["Edit members…", "Rename…", "Duplicate…", "Delete…"]);
        switch (action)
        {
            case 0: EditGroup(group); break;
            case 1: CopyGroup(group, false); break;
            case 2: CopyGroup(group, true); break;
            case 3:
                if (CanWrite() && dialog.Review("Delete group", "Delete the saved membership of " + group.Name + "? Repository files, branches and worktrees stay in place.", "Delete group")) {
                    settings.Groups.Remove(group); Config.Save(configPath, settings); view.Navigate(null, "All projects"); previewKey = "";
                }
                break;
        }
    }
    WorkspaceGroup? CurrentGroup() => view.RepoMode ? null : settings.Groups.FirstOrDefault(g => g.Name == view.State.Group);
```

Replace `Workspace()` in `Navigation.cs` with the W popup:

```csharp
    void Workspace()
    {
        var groups = new[] { "All projects" }.Concat(settings.Groups.Select(g => g.Name)).ToList();
        var options = groups.Select((g, i) => (i < 9 ? $"{i + 1}  " : "   ") + g).Concat(["## Actions", "New group…  (Ctrl+N)", "Edit current group…  (E)", "Repo View…  (P)"]).ToList();
        var pick = dialog.Choose("Project groups", options, Math.Max(0, groups.IndexOf(view.State.Group)));
        if (pick is null) return;
        if (pick < groups.Count) { OpenTab(pick.Value); return; }
        switch (pick - groups.Count - 1)
        {
            case 0: EditGroup(); break;
            case 1: if (CurrentGroup() is { } g) GroupActions(g); else dialog.Message("Edit group", "All projects is not a saved group. Create one with Ctrl+N."); break;
            case 2: RepoView(true); break;
        }
    }
```

In `App.Handle`:
- In the mouse `switch`, add before `case "check"`:

```csharp
                case "tab" when key.Button == MouseButton.Right:
                    var right = TabStrip.Tabs(view)[hit.Index];
                    if (!right.Repo && settings.Groups.FirstOrDefault(g => g.Name == right.Target) is { } group) GroupActions(group);
                    break;
                case "tab": if (TabStrip.Tabs(view)[hit.Index].Repo) { view.Back(); previewKey = ""; } else OpenTab(hit.Index); break;
                case "tab-new": EditGroup(); break;
                case "tab-prev": view.Cycle(-1); previewKey = ""; break;
                case "tab-next": view.Cycle(1); previewKey = ""; break;
```

  Clicking the Repo View tab, including its `×`, closes Repo View.
- Before the character switch, add: `if (key.Control && key.Key == ConsoleKey.N) { EditGroup(); return; }` and `if (!key.Control && key.Character is >= '1' and <= '9') { OpenTab(key.Character - '1'); return; }`
- Add to the character switch: `case 'e': if (CurrentGroup() is { } g) GroupActions(g); else notice = "All projects is not a saved group · Ctrl+N creates one"; break;`

- [ ] **Step 5: Run to verify pass**

Run with `--filter "tab"`, then the full suite. Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add pwsh/GitWorkspace/TabStrip.cs pwsh/GitWorkspace/WorkspaceView.cs pwsh/GitWorkspace/App.cs pwsh/GitWorkspace/Navigation.cs pwsh/GitWorkspace.Tests/GroupUiTests.cs
git commit -m "gws: group tab strip with counts, digits and per-tab actions"
```

---

### Task 14: GroupDraft — discovered repos, whole-repo toggle, atomic save

**Files:**
- Rename: `pwsh/GitWorkspace/GroupEditor.cs` → `pwsh/GitWorkspace/GroupDraft.cs` (`git mv`)
- Modify: `pwsh/GitWorkspace/GroupDraft.cs`
- Test: `pwsh/GitWorkspace.Tests/GroupUiTests.cs`

**Interfaces:**
- Consumes: `DiscoveredRepo` (Task 12)
- Produces:
  - Member kinds: `"repo"`, `"worktree"`, `"branch"`, `"new-repo"` (`Project` = `Value` = discovered path)
  - `GroupDraft(Settings settings, Snapshot snapshot, WorkspaceGroup? existing = null)` (unchanged) plus `void AddDiscovered(IEnumerable<DiscoveredRepo> repos)`
  - `enum RepoState { None, Some, Whole }`; `RepoState State(string project)`
  - `void ToggleWhole(string project)` — Whole → individual worktrees ticked; otherwise → whole repo, children cleared
  - `HashSet<string> BranchesShown`; `List<GroupMember> Rows(string search, ISet<string> folded)` — display order: registered repos, then new repos; branches only when shown or ticked
  - `sealed class GroupSaveException(string memberId, string message) : InvalidOperationException(message)` with `MemberId`
  - `WorkspaceGroup Commit(Settings settings, string name, WorkspaceGroup? existing, Func<string, string> resolve)` — validates everything, then mutates `settings` (registrations + group); throws before mutating on any failure
  - `static string UniqueName(string folder, IEnumerable<string> taken)`

- [ ] **Step 1: Rename the file**

```bash
cd /c/repos/DotFiles && git mv pwsh/GitWorkspace/GroupEditor.cs pwsh/GitWorkspace/GroupDraft.cs
```

- [ ] **Step 2: Write the failing tests**

Append inside `GroupUiTests()`:

```csharp
        Test("group draft registers ticked discovered repos atomically with unique names", () => {
            var settings = Demo.Settings(); var draft = new GroupDraft(settings, Demo.Snapshot());
            var scan = ScanRoot(); var joinersLower = InitAt(Path.Combine(scan, "joiners")); var payments = InitAt(Path.Combine(scan, "Payments"));
            draft.AddDiscovered([new("joiners", joinersLower, Git.ResolveRepository(joinersLower), null), new("Payments", payments, Git.ResolveRepository(payments), null),
                new("AccessControl", "C:/repos/AccessControl", "C:/repos/AccessControl/.git-main", "AccessControl")]);
            Equal(2, draft.Members.Count(m => m.Kind == "new-repo"), "Registered discovery not duplicated");
            foreach (var m in draft.Members.Where(m => m.Kind == "new-repo").ToList()) draft.Toggle(m);
            var group = draft.Commit(settings, "Payments work", null, Git.ResolveRepository);
            Check(settings.Projects.Any(p => p.Name == "joiners 2"), "Case-insensitive collision gets a suffix");
            Check(group.Projects.Contains("Payments") && group.Projects.Contains("joiners 2"), "Group uses new names");
            Equal(1, settings.Groups.Count(g => g.Name == "Payments work"), "Group saved");
            Equal("Tools 3", GroupDraft.UniqueName("Tools", ["tools", "Tools 2"]), "Suffix skips taken names");
        });
        Test("group draft save failure leaves settings untouched and names the member", () => {
            var settings = Demo.Settings(); int projects = settings.Projects.Count, groups = settings.Groups.Count;
            var draft = new GroupDraft(settings, Demo.Snapshot()); var gone = Path.Combine(root, "vanished " + ++sequence);
            draft.AddDiscovered([new("Vanished", gone.Replace('\\', '/'), "x", null)]); var member = draft.Members.Single(m => m.Kind == "new-repo"); draft.Toggle(member);
            try { draft.Commit(settings, "Broken", null, Git.ResolveRepository); throw new Exception("Expected failure"); }
            catch (GroupSaveException e) { Equal(member.Id, e.MemberId, "Failing member"); }
            Equal(projects, settings.Projects.Count, "No registration"); Equal(groups, settings.Groups.Count, "No group");
        });
        Test("whole-repo toggle converts between whole and individual worktrees and hides branches until shown", () => {
            var draft = new GroupDraft(Demo.Settings(), Demo.Snapshot());
            var repo = draft.Members.First(m => m.Kind == "repo" && m.Project == "AccessControl"); draft.Toggle(repo);
            Equal(RepoState.Whole, draft.State("AccessControl"), "Whole");
            draft.ToggleWhole("AccessControl"); Equal(RepoState.Some, draft.State("AccessControl"), "Individual");
            Check(draft.Members.Where(m => m.Kind == "worktree" && m.Project == "AccessControl").All(m => draft.Checked.Contains(m.Id)), "Worktrees ticked individually");
            draft.ToggleWhole("AccessControl"); Equal(RepoState.Whole, draft.State("AccessControl"), "Whole again");
            Check(draft.Members.Where(m => m.Project == "AccessControl" && m.Kind != "repo").All(m => !draft.Checked.Contains(m.Id)), "Children cleared");
            var rows = draft.Rows("", new HashSet<string>()); Check(!rows.Any(m => m.Kind == "branch"), "Branches hidden");
            draft.BranchesShown.Add("Joiners"); Check(draft.Rows("", new HashSet<string>()).Any(m => m.Kind == "branch" && m.Project == "Joiners"), "Branches shown");
            Check(!draft.Rows("", new HashSet<string> { "Joiners" }).Any(m => m.Project == "Joiners" && m.Kind != "repo"), "Folded repo hides children");
        });
```

- [ ] **Step 3: Run to verify failure**

Run with `--filter "group draft"`. Expected: build FAILS (`AddDiscovered`, `Commit`, `RepoState` missing).

- [ ] **Step 4: Implement**

In `GroupDraft.cs`, keep `GroupMember` and the existing constructor, `SelectOrAdd`, `Inherited`, `Toggle`, `Build`, and add inside `GroupDraft`:

```csharp
    public HashSet<string> BranchesShown { get; } = [];
    public void AddDiscovered(IEnumerable<DiscoveredRepo> repos)
    {
        foreach (var repo in repos.Where(r => r.RegisteredAs is null))
            if (!Members.Any(m => m.Kind == "new-repo" && m.Value == repo.Path)) Members.Add(new("new-repo", repo.Path, repo.Path, repo.Name));
    }
    public RepoState State(string project)
    {
        if (Checked.Contains(new GroupMember("repo", project, project, "").Id) || Checked.Contains(new GroupMember("new-repo", project, project, "").Id)) return RepoState.Whole;
        return Members.Any(m => m.Project == project && m.Kind is "worktree" or "branch" && Checked.Contains(m.Id)) ? RepoState.Some : RepoState.None;
    }
    public void ToggleWhole(string project)
    {
        var repo = Members.FirstOrDefault(m => m.Kind == "repo" && m.Project == project); if (repo is null) return;
        var children = Members.Where(m => m.Project == project && m.Kind is "worktree" or "branch").ToList();
        if (Checked.Remove(repo.Id)) { foreach (var tree in children.Where(m => m.Kind == "worktree")) Checked.Add(tree.Id); }
        else { foreach (var child in children) Checked.Remove(child.Id); Checked.Add(repo.Id); }
    }
    public List<GroupMember> Rows(string search, ISet<string> folded)
    {
        List<GroupMember> rows = [];
        foreach (var head in Members.Where(m => m.Kind == "repo").Concat(Members.Where(m => m.Kind == "new-repo")))
        {
            var children = head.Kind == "repo" ? Members.Where(m => m.Project == head.Project && (m.Kind == "worktree" ||
                m.Kind == "branch" && (BranchesShown.Contains(head.Project) || Checked.Contains(m.Id)))).ToList() : [];
            bool match(GroupMember m) => search.Length == 0 || (head.Label + " " + m.Label).Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!match(head) && !children.Any(match)) continue;
            rows.Add(head);
            if (!folded.Contains(head.Project)) rows.AddRange(children.Where(match));
        }
        // Saved entries whose repository is no longer registered stay visible, so they can be unticked.
        rows.AddRange(Members.Where(m => m.Kind is "worktree" or "branch" && Checked.Contains(m.Id) && !Members.Any(h => h.Kind == "repo" && h.Project == m.Project)));
        return rows;
    }
    public static string UniqueName(string folder, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(folder)) return folder;
        for (int n = 2; ; n++) if (!used.Contains(folder + " " + n)) return folder + " " + n;
    }
    public WorkspaceGroup Commit(Settings settings, string name, WorkspaceGroup? existing, Func<string, string> resolve)
    {
        name = name.Trim();
        if (settings.Groups.Any(g => g != existing && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("A group with that name already exists.");
        List<Project> added = [];
        foreach (var member in Members.Where(m => m.Kind == "new-repo" && Checked.Contains(m.Id)))
        {
            try { resolve(member.Value); }
            catch (InvalidOperationException e) { throw new GroupSaveException(member.Id, member.Label + ": " + e.Message); }
            added.Add(new(UniqueName(member.Label, settings.Projects.Select(p => p.Name).Concat(added.Select(p => p.Name))), member.Value));
        }
        // Translate ticked new repos into whole-repo entries under their registered names.
        var build = new GroupDraft(); build.Members.AddRange(Members.Where(m => m.Kind != "new-repo")); build.Checked.UnionWith(Checked);
        foreach (var (member, project) in Members.Where(m => m.Kind == "new-repo" && Checked.Contains(m.Id)).Zip(added)) {
            var repo = new GroupMember("repo", project.Name, project.Name, project.Name); build.Members.Add(repo); build.Checked.Add(repo.Id);
        }
        var group = build.Build(name);
        settings.Projects.AddRange(added);
        if (existing is not null && settings.Groups.IndexOf(existing) is var at && at >= 0) settings.Groups[at] = group; else settings.Groups.Add(group);
        return group;
    }
    GroupDraft() { }
```

Add at file scope:

```csharp
public enum RepoState { None, Some, Whole }
public sealed class GroupSaveException(string memberId, string message) : InvalidOperationException(message)
{
    public string MemberId { get; } = memberId;
}
```

Update `Toggle` so `new-repo` members toggle freely (they have no inherited children) — the existing implementation already does, since `Inherited` only checks the `repo` kind.

Update `Build`'s empty-selection check to consider `new-repo` too: no change needed because `Commit` converts them before calling `Build`.

- [ ] **Step 5: Run to verify pass**

Run with `--filter "group draft"`, `--filter "whole-repo"`, then the full suite (the existing "mixed groups round-trip" test must still pass). Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A pwsh/GitWorkspace/GroupDraft.cs pwsh/GitWorkspace/GroupEditor.cs pwsh/GitWorkspace.Tests/GroupUiTests.cs
git commit -m "gws: group draft handles discovered repos, whole-repo toggle and atomic save"
```

---

### Task 15: Full-screen group editor panel

**Files:**
- Create: `pwsh/GitWorkspace/GroupEditor.cs` (panel)
- Delete: `pwsh/GitWorkspace/GroupDialog.cs`
- Modify: `pwsh/GitWorkspace/Navigation.cs` (`EditGroup`, `CopyGroup` unchanged)
- Test: `pwsh/GitWorkspace.Tests/GroupUiTests.cs`

**Interfaces:**
- Consumes: `GroupDraft.Rows/State/ToggleWhole/Toggle/AddDiscovered/BranchesShown`, `Dialogs.FullPage/Text/Message`, `ISurface`
- Produces:
  - `sealed record EditorError(string Message, string? MemberId = null)`
  - `string? Dialogs.EditGroup(GroupDraft draft, string name, Task<List<DiscoveredRepo>>? discovery, Func<string, EditorError?>? validate = null, Func<Task<List<DiscoveredRepo>>>? rescan = null)`. It returns the chosen name on Enter once `validate` returns null, and null on Esc. `validate(name)` returns an error or null. App passes a closure that calls `draft.Commit`, so a failure keeps the editor open with the message shown and the failing row focused. `R` calls `rescan` to search the folders again.

- [ ] **Step 1: Write the failing test**

Append inside `GroupUiTests()`:

```csharp
        Test("group editor edits the name, ticks rows and merges background discovery", () => {
            var draft = new GroupDraft(Demo.Settings(), Demo.Snapshot());
            var discovery = Task.FromResult(new List<DiscoveredRepo> { new("Payments", "C:/nowhere/Payments", "x", null) });
            var keys = new List<Input> { new(Key: ConsoleKey.U, Control: true) };
            keys.AddRange("QR".Select(ch => new Input(Character: ch)));
            keys.AddRange([new(ConsoleKey.Tab), new(ConsoleKey.Spacebar), new(ConsoleKey.End), new(ConsoleKey.Spacebar), new(ConsoleKey.Enter)]);
            var surface = new ScriptedSurface(100, 30, [.. keys]);
            var name = new Dialogs(surface).EditGroup(draft, "New group", discovery);
            Equal("QR", name, "Name typed");
            Equal(RepoState.Whole, draft.State("AccessControl"), "First repo ticked");
            Check(draft.Checked.Contains(draft.Members.Single(m => m.Kind == "new-repo").Id), "Discovered repo ticked");
            Check(surface.Frames.Any(f => f.Contains("★ not registered")), "Discovered marker drawn");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "group editor edits"`. Expected: build FAILS (signature mismatch).

- [ ] **Step 3: Implement**

Delete the old dialog: `git rm pwsh/GitWorkspace/GroupDialog.cs`.

Create `pwsh/GitWorkspace/GroupEditor.cs`:

```csharp
namespace GitWorkspace;

public sealed record EditorError(string Message, string? MemberId = null);

public sealed partial class Dialogs
{
    public string? EditGroup(GroupDraft draft, string name, Task<List<DiscoveredRepo>>? discovery, Func<string, EditorError?>? validate = null,
        Func<Task<List<DiscoveredRepo>>>? rescan = null)
    {
        int focus = 0, scroll = 0; string search = ""; bool editingName = name == "New group"; int cursor = name.Length;
        var folded = new HashSet<string>(); string status = "";
        while (true)
        {
            if (discovery is { IsCompleted: true }) {
                status = discovery.IsCompletedSuccessfully ? "" : "Repository search failed: " + discovery.Exception?.GetBaseException().Message;
                if (discovery.IsCompletedSuccessfully) draft.AddDiscovered(discovery.Result); discovery = null;
            }
            var rows = draft.Rows(search, folded); focus = Math.Clamp(focus, 0, Math.Max(0, rows.Count - 1));
            var c = FullPage("Project group", editingName ? "Type a name · Tab to members · Enter save · Esc cancel"
                : "Space tick · W whole ↔ entries · B branches · ←→ fold · / filter · R rescan · Tab name · Enter save · Esc cancel");
            c.Text(2, 2, "Name: ", Ink.Muted); c.Fill(8, 2, Math.Min(40, c.Width - 10), editingName); c.Text(8, 2, name, Ink.Text, Math.Min(40, c.Width - 10), editingName);
            if (editingName) c.Text(8 + Math.Min(cursor, 39), 2, cursor < name.Length ? name[cursor].ToString() : "▏", Ink.Heading, 1, true);
            c.Text(2, 4, discovery is null ? status.Length > 0 ? status : (search.Length > 0 ? "/ " + search : $"{draft.Checked.Count} ticked") : "Searching for repositories…",
                status.Length > 0 ? Ink.Warning : Ink.Muted, c.Width - 4);
            int top = 5, height = Math.Max(1, c.Height - 3 - top);
            if (focus < scroll) scroll = focus; if (focus >= scroll + height) scroll = focus - height + 1;
            for (int i = scroll; i < Math.Min(scroll + height, rows.Count); i++) {
                var m = rows[i]; int y = top + i - scroll; bool on = i == focus && !editingName;
                if (on) c.Fill(1, y, c.Width - 2);
                c.Text(2, y, Label(draft, m, folded), m.Kind is "repo" or "new-repo" ? Ink.Heading : draft.Inherited(m) ? Ink.Muted : Ink.Text, c.Width - 4, on);
            }
            terminal.Draw(c);
            var key = discovery is null ? terminal.Read() : terminal.Poll(100); if (key is null) continue;
            if (Cancel(key)) return null;
            if (key.Key == ConsoleKey.Tab) { editingName = !editingName; cursor = name.Length; continue; }
            if (key.Key == ConsoleKey.Enter) {
                var error = name.Trim().Length == 0 || name.Trim().Equals("All projects", StringComparison.OrdinalIgnoreCase)
                    ? new EditorError("Choose a group name other than All projects.") : validate?.Invoke(name.Trim());
                if (error is null) return name.Trim();
                status = error.Message;
                if (error.MemberId is not null && draft.Rows("", new HashSet<string>()).FindIndex(r => r.Id == error.MemberId) is var at && at >= 0) {
                    search = ""; folded.Clear(); focus = at; editingName = false;
                }
                continue;
            }
            if (editingName) {
                if (key.Control && key.Key == ConsoleKey.U) { name = ""; cursor = 0; }
                else if (key.Key == ConsoleKey.Backspace && cursor > 0) name = name.Remove(--cursor, 1);
                else if (key.Key == ConsoleKey.LeftArrow) cursor = Math.Max(0, cursor - 1);
                else if (key.Key == ConsoleKey.RightArrow) cursor = Math.Min(name.Length, cursor + 1);
                else if (!key.Mouse && !key.Control && !char.IsControl(key.Character) && key.Character != '\0') name = name.Insert(cursor++, key.Character.ToString());
                continue;
            }
            var current = rows.ElementAtOrDefault(focus);
            if (key.Key == ConsoleKey.UpArrow) focus = Math.Max(0, focus - 1);
            if (key.Key == ConsoleKey.DownArrow) focus = Math.Min(Math.Max(0, rows.Count - 1), focus + 1);
            if (key.Key == ConsoleKey.Home) focus = 0;
            if (key.Key == ConsoleKey.End) focus = Math.Max(0, rows.Count - 1);
            if (key.Key == ConsoleKey.PageDown) focus = Math.Min(Math.Max(0, rows.Count - 1), focus + height);
            if (key.Key == ConsoleKey.PageUp) focus = Math.Max(0, focus - height);
            if (current is not null && key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow && current.Kind == "repo") { if (!folded.Remove(current.Project)) folded.Add(current.Project); }
            char command = char.ToLowerInvariant(key.Character);
            if (command == '/') { search = Text("Filter group entries", "Search repositories, branches and worktree names.", search) ?? search; focus = scroll = 0; }
            if (command == 'r' && rescan is not null && discovery is null) { discovery = rescan(); status = ""; }
            if (current is not null && command == 'w') draft.ToggleWhole(current.Project);
            if (current is not null && command == 'b') {
                if (current.Kind == "new-repo") status = "Branches are available once the repository is registered; tick it to include the whole repo.";
                else if (!draft.BranchesShown.Remove(current.Project)) draft.BranchesShown.Add(current.Project);
            }
            bool toggle = key.Key == ConsoleKey.Spacebar;
            if (key.Mouse && key.Wheel != 0) focus = Math.Clamp(focus + key.Wheel, 0, Math.Max(0, rows.Count - 1));
            else if (key.Mouse && key.Y == 2) { editingName = true; cursor = name.Length; }
            else if (key.Mouse && key.Y >= top && key.Y < top + height && scroll + key.Y - top < rows.Count) { focus = scroll + key.Y - top; toggle = true; editingName = false; }
            if (toggle && rows.ElementAtOrDefault(focus) is { } target) { try { draft.Toggle(target); } catch (InvalidOperationException e) { status = e.Message; } }
        }
    }
    static string Label(GroupDraft draft, GroupMember m, ISet<string> folded)
    {
        string box(bool on) => on ? "☑ " : "☐ ";
        return m.Kind switch {
            "repo" => (folded.Contains(m.Project) ? "▸ " : "▾ ") + draft.State(m.Project) switch { RepoState.Whole => "☑ ", RepoState.Some => "◩ ", _ => "☐ " } + m.Project +
                (draft.State(m.Project) == RepoState.Whole ? "   · whole repo (includes future worktrees)" : "") + (m.Label.EndsWith("(unavailable)") ? "  (unavailable)" : ""),
            "new-repo" => "  " + box(draft.Checked.Contains(m.Id)) + m.Label + "   ★ not registered · " + m.Value,
            _ => "      " + (draft.Inherited(m) ? "☑ " : box(draft.Checked.Contains(m.Id))) + m.Label.Trim() + (draft.Inherited(m) ? "  (included by repo)" : "")
        };
    }
}
```

Replace `EditGroup` in `Navigation.cs`:

```csharp
    void EditGroup(WorkspaceGroup? existing = null)
    {
        if (!CanWrite()) return;
        var draft = new GroupDraft(settings, view.Snapshot, existing);
        Task<List<DiscoveredRepo>> Search() => Task.Run(() => Discovery.Scan(settings, Discovery.Roots(settings)));
        WorkspaceGroup? saved = null;
        var name = dialog.EditGroup(draft, existing?.Name ?? "New group", Search(), candidate => {
            try { saved = draft.Commit(settings, candidate, existing, Git.ResolveRepository); return null; }
            catch (GroupSaveException e) { return new EditorError(e.Message, e.MemberId); }
            catch (InvalidOperationException e) { return new EditorError(e.Message); }
        }, Search);
        if (name is null || saved is null) return;
        Config.Save(configPath, settings); Refresh(); view.Navigate(null, saved.Name); previewKey = "";
        notice = "Saved group " + saved.Name + " · E edits it · 1–9 switch groups";
    }
```

(`Commit` mutates `settings` only after all validation passes, so an Esc after a failed attempt leaves config untouched; a successful `Commit` is always followed by `Config.Save`.)

- [ ] **Step 4: Run to verify pass**

Run with `--filter "group editor edits"`, then the full suite. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A pwsh/GitWorkspace/GroupEditor.cs pwsh/GitWorkspace/GroupDialog.cs pwsh/GitWorkspace/Navigation.cs pwsh/GitWorkspace.Tests/GroupUiTests.cs
git commit -m "gws: full-screen group editor with name field, tree and discovery"
```

---

### Task 16: Search folders setting, W popup polish and Phase 2 docs

**Files:**
- Modify: `pwsh/GitWorkspace/App.cs` (`More` → "Search folders…", `Help`), `pwsh/GitWorkspace/README.md`, `pwsh/GitWorkspace/git-workspaces.example.json`
- Test: `pwsh/GitWorkspace.Tests/GroupUiTests.cs`

**Interfaces:**
- Produces: `static List<string> App.ParseRoots(string text)` (semicolon-separated, trimmed, empty entries dropped, relative paths rejected with `InvalidOperationException`)

- [ ] **Step 1: Write the failing test**

Append inside `GroupUiTests()`:

```csharp
        Test("search folders parse semicolon lists and reject relative paths", () => {
            Equal("C:/repos|D:/src", string.Join("|", App.ParseRoots(" C:/repos ; ;D:/src ")), "Parsed roots");
            Throws(() => App.ParseRoots("repos"), "Relative root accepted");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "search folders"`. Expected: build FAILS (`ParseRoots` missing).

- [ ] **Step 3: Implement**

Add to `App.cs`:

```csharp
    public static List<string> ParseRoots(string text)
    {
        var roots = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (roots.FirstOrDefault(r => !Path.IsPathFullyQualified(r)) is { } bad) throw new InvalidOperationException("Use absolute folders: " + bad);
        return roots;
    }
    void SearchFolders()
    {
        if (!CanWrite()) return;
        var text = dialog.Text("Search folders", "Folders whose immediate subfolders are offered as repositories in the group editor. Separate with ;", string.Join("; ", Discovery.Roots(settings)));
        if (text is null) return;
        settings.Roots = ParseRoots(text); Config.Save(configPath, settings);
        notice = settings.Roots.Count == 0 ? "No search folders · group editor shows registered repos only" : "Search folders saved";
    }
```

In `More()`, add `("Search folders…", SearchFolders)` after `("Add project…", AddProject)`.

In `git-workspaces.example.json`, add `"Roots": ["C:/repos"],` after the `Projects` array.

In `README.md`, replace the "Groups, Repo View and history" section's first two paragraphs with:

```markdown
Groups appear as tabs under the title: `All projects │ Secure QR 3 │ … │ +`. Press `1`–`9`
or click a tab to switch, `[` / `]` to cycle, `Ctrl+N` or `+` to create a group, and `E`
(or right-click a tab) to edit members, rename, duplicate or delete that group. `W` opens
the same choices as a list.

The group editor has a **Name** field and a tree of repositories. It lists registered
repositories and every repository found one level below your search folders (`Roots`
in the config, default `C:\repos`; change them with More → Search folders…). Ticking a
repository marked `★ not registered` registers it under its folder name when you save.
`Space` ticks, `W` switches a repository between **whole repo** (includes future
worktrees) and **individual entries**, `B` shows its branches, `/` filters and `←` / `→`
fold. Registrations and the group are saved together, or not at all.
```

Keep the bullet list about entire repository / worktree / branch entries. In `Help()`, replace the `W creates, edits, …` paragraph with: `"Groups are tabs: 1–9 or click to switch, [ ] cycle, Ctrl+N new, E edit/rename/duplicate/delete, W list. The editor ticks registered and discovered repositories (More → Search folders); new ones are registered on save.\n\n"`.

- [ ] **Step 4: Run to verify pass and commit**

Run with `--filter "search folders"`, then the full suite: `0 failed`.

```bash
git add pwsh/GitWorkspace/App.cs pwsh/GitWorkspace/README.md pwsh/GitWorkspace/git-workspaces.example.json pwsh/GitWorkspace.Tests/GroupUiTests.cs
git commit -m "gws: search folders setting and group docs"
```

---

# Phase 3 — Commits view

### Task 17: Commit graph and literal search

**Files:**
- Create: `pwsh/GitWorkspace/CommitGraph.cs`
- Create: `pwsh/GitWorkspace.Tests/CommitTests.cs`; Modify `pwsh/GitWorkspace.Tests/Program.cs` (call `CommitTests()`)

**Interfaces:**
- Consumes: `CommitInfo` (Models.cs), `History.Resolve`
- Produces:
  - `sealed record CommitFilter(string Search = "", bool UnpushedOnly = false, string? Base = null)`
  - `sealed record GraphRow(string Graph, CommitInfo? Commit, List<string> Refs, bool Unpushed)`
  - `static partial class CommitGraph { List<string> SearchArguments(string search); List<string> LogArguments(string tip, CommitFilter filter, string? upstream, int skip, int count); List<GraphRow> Read(string directory, string tip, CommitFilter filter, string? upstream, int skip = 0, int count = 200); }`

- [ ] **Step 1: Write the failing tests**

Create `pwsh/GitWorkspace.Tests/CommitTests.cs`:

```csharp
using GitWorkspace;

static partial class Tests
{
    static string MergeRepo()
    {
        var repo = Repo(); string initial = History.Resolve(repo, "HEAD"); CommitFile(repo, "Main");
        Git.Run(repo, "switch", "-c", "side", initial).Require(); CommitFile(repo, "Side");
        Git.Run(repo, "switch", "main").Require(); Git.Run(repo, "merge", "--no-ff", "-m", "Merge side", "side").Require();
        Git.Run(repo, "tag", "v1").Require(); return repo;
    }
    static void CommitTests()
    {
        Test("commit graph keeps connector lines, refs and root parents", () => {
            var repo = MergeRepo(); var rows = CommitGraph.Read(repo, History.Resolve(repo, "HEAD"), new(), null);
            var commits = rows.Where(r => r.Commit is not null).ToList();
            Equal(4, commits.Count, "Commits"); Check(rows.Count > commits.Count, "Connector rows kept");
            Check(commits[0].Refs.Contains("tag: v1") && commits[0].Refs.Any(r => r.Contains("main")), "Refs: " + string.Join(",", commits[0].Refs));
            Check(commits[0].Graph.Contains('*'), "Graph prefix"); Equal(2, commits[0].Commit!.Parents.Count, "Merge parents");
            Equal(0, commits[^1].Commit!.Parents.Count, "Root commit");
        });
        Test("commit search is literal, case-insensitive and combines author and path", () => {
            Equal("--author=phil|--grep=fix(|-i|--fixed-strings|--all-match|--|:(literal)-odd [x].txt",
                string.Join("|", CommitGraph.SearchArguments("@phil fix( \":-odd [x].txt\"")), "Arguments");
            var repo = MergeRepo(); Git.Run(repo, "commit", "--allow-empty", "-m", "Fix [wip] (scanner)").Require();
            string tip = History.Resolve(repo, "HEAD");
            Equal("Fix [wip] (scanner)", CommitGraph.Read(repo, tip, new("[wip] (scan"), null).Single(r => r.Commit is not null).Commit!.Subject, "Metacharacters literal");
            Equal(5, CommitGraph.Read(repo, tip, new("\"@workspace test\""), null).Count(r => r.Commit is not null), "Quoted author, case-insensitive");
            var byPath = CommitGraph.Read(repo, tip, new(":Side.txt"), null).Where(r => r.Commit is not null).Select(r => r.Commit!.Subject).ToList();
            Check(byPath.Contains("Side") && !byPath.Contains("Main"), "Path filter: " + string.Join(",", byPath));
            Equal(0, CommitGraph.Read(repo, tip, new("no such message"), null).Count, "No match is empty, not an error");
        });
        Test("unpushed markers and filters use the upstream range", () => {
            var repo = Repo(); var remote = Path.Combine(root, "remote " + ++sequence + ".git"); Git.Run(root, "init", "--bare", remote).Require();
            Git.Run(repo, "remote", "add", "origin", remote).Require(); Git.Run(repo, "push", "-u", "origin", "main").Require();
            CommitFile(repo, "Local"); string tip = History.Resolve(repo, "HEAD");
            var rows = CommitGraph.Read(repo, tip, new(), "origin/main").Where(r => r.Commit is not null).ToList();
            Check(rows[0].Unpushed && !rows[1].Unpushed, "Only the local commit is unpushed");
            Equal(1, CommitGraph.Read(repo, tip, new(UnpushedOnly: true), "origin/main").Count(r => r.Commit is not null), "Unpushed only");
            Equal(1, CommitGraph.Read(repo, tip, new(Base: "origin/main"), null).Count(r => r.Commit is not null), "Since base");
            Equal(2, CommitGraph.Read(repo, tip, new(UnpushedOnly: true), null).Count(r => r.Commit is not null), "No upstream shows everything");
        });
    }
}
```

In `Program.cs` `Main`, add `CommitTests();` after `GroupUiTests();`.

- [ ] **Step 2: Run to verify failure**

Run with `--filter "commit graph"`. Expected: build FAILS (`CommitGraph` missing).

- [ ] **Step 3: Implement**

Create `pwsh/GitWorkspace/CommitGraph.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify pass**

Run with `--filter "commit"`. Expected: the three new tests PASS. Run full suite: `0 failed`.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/CommitGraph.cs pwsh/GitWorkspace.Tests/CommitTests.cs pwsh/GitWorkspace.Tests/Program.cs
git commit -m "gws: commit graph reader with literal search and unpushed markers"
```

---

### Task 18: Commit files and per-file diffs

**Files:**
- Modify: `pwsh/GitWorkspace/CommitGraph.cs`
- Modify: `pwsh/GitWorkspace.Tests/GroupsAndHistory.cs:64-75` (the history test uses `History.Detail`; switch it to `CommitGraph`)
- Test: `pwsh/GitWorkspace.Tests/CommitTests.cs`

**Interfaces:**
- Produces:
  - `sealed record CommitFile(string Status, string Path, string? OldPath, int Added, int Removed)` (`Added`/`Removed` = -1 for binary)
  - `sealed record CommitDetail(string Header, List<CommitFile> Files)`
  - `CommitGraph.Base(string directory, CommitInfo commit, int parent) → string` (parent hash or the empty tree)
  - `CommitGraph.Files(string directory, CommitInfo commit, int parent = 0) → List<CommitFile>`
  - `CommitGraph.Detail(string directory, CommitInfo commit, int parent = 0) → CommitDetail`
  - `CommitGraph.FileDiff(string directory, CommitInfo commit, int parent, CommitFile file) → string`

- [ ] **Step 1: Write the failing tests**

Append inside `CommitTests()`:

```csharp
        Test("commit files list renames, root additions and each merge parent", () => {
            var repo = MergeRepo(); string tip = History.Resolve(repo, "HEAD");
            var commits = CommitGraph.Read(repo, tip, new(), null).Where(r => r.Commit is not null).Select(r => r.Commit!).ToList();
            Equal("Side.txt", CommitGraph.Files(repo, commits[0], 0).Single().Path, "First parent adds Side");
            Equal("Main.txt", CommitGraph.Files(repo, commits[0], 1).Single().Path, "Second parent adds Main");
            var root = CommitGraph.Files(repo, commits[^1]); Equal("A", root.Single().Status, "Root adds"); Equal(40, root.Single().Added, "Root line count");
            Git.Run(repo, "mv", "file.txt", "renamed file.txt").Require(); Git.Run(repo, "commit", "-m", "Rename").Require();
            var rename = CommitGraph.Read(repo, History.Resolve(repo, "HEAD"), new(), null).First(r => r.Commit is not null).Commit!;
            var file = CommitGraph.Files(repo, rename).Single(); Equal("R", file.Status, "Rename status"); Equal("file.txt", file.OldPath, "Old path"); Equal("renamed file.txt", file.Path, "New path");
            var parsed = DiffView.Parse(CommitGraph.FileDiff(repo, rename, 0, file));
            Equal("file.txt → renamed file.txt", parsed.First(l => l.Kind == DiffKind.Header).Text, "Rename diff header");
            var rootDiff = DiffView.Parse(CommitGraph.FileDiff(repo, commits[^1], 0, root.Single())); Equal(40, rootDiff.Count(l => l.Kind == DiffKind.Added), "Root diff all added");
            var detail = CommitGraph.Detail(repo, commits[0]); Check(detail.Header.Contains("Merge side") && detail.Header.Contains("Workspace Test"), "Header");
        });
```

In `GroupsAndHistory.cs`, replace the two `History.Detail(...)` lines in the history test with:

```csharp
            Check(CommitGraph.Files(repo, commits[0], 0).Any(f => f.Path == "Side.txt"), "First parent diff"); Check(CommitGraph.Files(repo, commits[0], 1).Any(f => f.Path == "Main.txt"), "Second parent diff");
            Check(CommitGraph.FileDiff(repo, commits.Last(), 0, CommitGraph.Files(repo, commits.Last()).Single()).Contains("+line 1"), "Root diff");
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "commit files"`. Expected: build FAILS (`CommitGraph.Files` missing).

- [ ] **Step 3: Implement**

Add to `CommitGraph.cs` (file scope record + class members):

```csharp
public sealed record CommitFile(string Status, string Path, string? OldPath, int Added, int Removed);
public sealed record CommitDetail(string Header, List<CommitFile> Files);
```

```csharp
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
```

Numstat `-z` records renames as `added\tremoved\t` followed by `old\0new\0` — the `n += 3` handles that. `diff` gets `--literal-pathspecs` from `Git.StartInfo`, so paths are literal.

Keep `History.Detail` for now: `HistoryActions.BrowseHistory` still uses it until Task 20 removes the history page.

- [ ] **Step 4: Run to verify pass**

Run with `--filter "commit files"`, `--filter "history reads"`, then the full suite. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/CommitGraph.cs pwsh/GitWorkspace.Tests/CommitTests.cs pwsh/GitWorkspace.Tests/GroupsAndHistory.cs
git commit -m "gws: commit file lists and per-file diffs"
```

---

### Task 19: Commits model — loading, navigation, drill-down and cache

**Files:**
- Create: `pwsh/GitWorkspace/CommitsView.cs` (model part)
- Modify: `pwsh/GitWorkspace/HistoryActions.cs` (`HistoryTarget` gains `Upstream`), `pwsh/GitWorkspace/Footer.cs` (parameter type)
- Test: `pwsh/GitWorkspace.Tests/CommitTests.cs`

**Interfaces:**
- Consumes: `CommitGraph.Read/Detail/FileDiff`, `History.Resolve`, `HistoryTarget`
- Produces:
  - `HistoryTarget(string Project, string Directory, string Revision, string Label, string? WorktreeId = null, string? BranchRef = null, string? Upstream = null)`
  - `enum CommitLevel { Commits, Files, FileDiff }`
  - `sealed class CommitsModel` with:
    - `CommitsModel(HistoryTarget target, Func<string, CommitFilter, int, int, List<GraphRow>> read, Func<CommitInfo, int, CommitDetail> detail, Func<CommitInfo, int, CommitFile, string> diff, Func<string, string> resolve, int batch = 200)`. `read(tip, filter, skip, count)` is called with `count = batch`.
    - `static CommitsModel ForGit(HistoryTarget target)`
    - properties `Target`, `Tip` (string?), `Empty` (string?), `Filter` (CommitFilter, settable via `SetFilter`), `Rows`, `Focus`, `Scroll`, `Level`, `FileFocus`, `Parent`, `Exhausted`
    - `GraphRow? Current`, `CommitInfo? Commit`
    - `void Load()` (resolve tip + first batch), `void EnsureLoaded(int visibleRows)`, `void Move(int delta)`, `void SetFilter(CommitFilter filter)`, `void Enter()`, `void Back()` (returns bool: false when already at Commits), `void NextParent()`
    - `Task<CommitDetail>? DetailTask()`, `Task<string>? DiffTask()` (cached by `hash:parent[:path]`)
  - `Footer.For(WorkspaceView view, CommitsModel? commits)`

- [ ] **Step 1: Write the failing tests**

Append inside `CommitTests()`:

```csharp
        Test("commits model skips connector rows, loads more and drills into files", () => {
            var repo = MergeRepo(); for (int i = 0; i < 5; i++) CommitFile(repo, "Extra" + i);
            var model = new CommitsModel(new("Test", repo, "HEAD", "Test"), (tip, filter, skip, count) => CommitGraph.Read(repo, tip, filter, null, skip, count),
                (c, p) => CommitGraph.Detail(repo, c, p), (c, p, f) => CommitGraph.FileDiff(repo, c, p, f), rev => History.Resolve(repo, rev), batch: 4);
            model.Load(); Check(model.Commit is not null, "Focus on a commit");
            for (int i = 0; i < 12; i++) { model.Move(1); model.EnsureLoaded(3); Check(model.Current?.Commit is not null, "Never focus a connector"); }
            Check(model.Rows.Count(r => r.Commit is not null) > 4, "Loaded a second batch"); Equal(9, model.Rows.Count(r => r.Commit is not null), "All commits once");
            CommitFile(repo, "Later"); model.Move(1); model.EnsureLoaded(3); Equal(9, model.Rows.Count(r => r.Commit is not null), "Pinned tip");
            model.Focus = model.Rows.FindIndex(r => r.Commit?.Subject == "Merge side"); model.Enter(); Equal(CommitLevel.Files, model.Level, "Files level");
            Equal("Side.txt", model.DetailTask()!.GetAwaiter().GetResult().Files.Single().Path, "Detail loaded");
            model.NextParent(); Equal(1, model.Parent, "Second parent"); Equal("Main.txt", model.DetailTask()!.GetAwaiter().GetResult().Files.Single().Path, "Parent switched");
            model.Enter(); Equal(CommitLevel.FileDiff, model.Level, "Diff level"); Check(model.DiffTask()!.GetAwaiter().GetResult().Contains("+Main"), "File diff");
            Check(model.Back() && model.Level == CommitLevel.Files, "Back to files"); Check(model.Back() && model.Level == CommitLevel.Commits, "Back to commits"); Check(!model.Back(), "Already at top");
        });
        Test("commits model reports an unborn branch and an empty search without throwing", () => {
            var empty = Repo(initial: false); var model = CommitsModel.ForGit(new("Empty", empty, "HEAD", "Empty")); model.Load();
            Equal("No commits yet on this branch.", model.Empty, "Unborn"); Check(model.Commit is null && model.Rows.Count == 0, "No rows");
            model.Move(1); model.Enter(); Equal(CommitLevel.Commits, model.Level, "Enter does nothing");
            var repo = Repo(); var searched = CommitsModel.ForGit(new("Test", repo, "HEAD", "Test")); searched.Load(); searched.SetFilter(new("zzz-not-found"));
            Equal("No commits match this search or filter.", searched.Empty, "Empty search");
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "commits model"`. Expected: build FAILS (`CommitsModel` missing).

- [ ] **Step 3: Implement**

In `HistoryActions.cs`, change the record to add `Upstream` and populate it:

```csharp
public sealed record HistoryTarget(string Project, string Directory, string Revision, string Label, string? WorktreeId = null, string? BranchRef = null, string? Upstream = null);
```

```csharp
    HistoryTarget Target(Worktree tree) => new(tree.Project, tree.Usable ? tree.Path : tree.CommonDir,
        tree.Usable ? "HEAD" : tree.Head, tree.Title, tree.Id, tree.ActiveBranchRef, tree.UpstreamGone ? null : tree.Upstream);
    HistoryTarget Target(BranchInfo branch) => new(branch.Project, branch.CommonDir, branch.Ref, branch.Project + " / " + branch.Name,
        view.Snapshot.Worktrees.FirstOrDefault(t => t.Project == branch.Project && t.ActiveBranchRef == branch.Ref)?.Id, branch.Ref, branch.Upstream);
```

Create `pwsh/GitWorkspace/CommitsView.cs`:

```csharp
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
```

In `Footer.cs`, change `object? commits` to `CommitsModel? commits` (callers pass `null`).

- [ ] **Step 4: Run to verify pass**

Run with `--filter "commits model"`, then the full suite. Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add pwsh/GitWorkspace/CommitsView.cs pwsh/GitWorkspace/HistoryActions.cs pwsh/GitWorkspace/Footer.cs pwsh/GitWorkspace.Tests/CommitTests.cs
git commit -m "gws: commits model with pinned tip, incremental loading and drill-down"
```

---

### Task 20: Commits view on the main screen

**Files:**
- Modify: `pwsh/GitWorkspace/CommitsView.cs` (add `CommitsRender`), `pwsh/GitWorkspace/Footer.cs` (commits keys), `pwsh/GitWorkspace/WorkspaceView.cs` (`Screen.Render` delegates body in Commits mode), `pwsh/GitWorkspace/App.cs` (`SetMode`, key routing, `HandleCommits`, draw), `pwsh/GitWorkspace/HistoryActions.cs` (`BrowseHistory` → `OpenCommits`, rebase-from-commit), `pwsh/GitWorkspace/Navigation.cs` (reset on group change), `pwsh/GitWorkspace/Program.cs` (`--snapshot … commits`), `pwsh/GitWorkspace/Program.cs` `Demo` (graph rows)
- Delete: `pwsh/GitWorkspace/HistoryDialog.cs`
- Test: `pwsh/GitWorkspace.Tests/CommitTests.cs`

**Interfaces:**
- Consumes: `CommitsModel`, `DiffView`, `DiffSides.ForCommit`, `Styled`, `Layout`, `HitMap`, `Footer`
- Produces:
  - `static class CommitsRender { void List(Canvas c, Rect area, HitMap hits, CommitsModel model); List<StyledLine> Preview(CommitsModel model); void Body(Canvas c, Layout layout, HitMap hits, CommitsModel model, int previewScroll); }`
  - Commits list hits: `row` with `Index` = row index in `model.Rows`; subject line hit `subject`
  - `Footer.For` commits keys: Commits level `\r` "Enter files", `/` search, `f` filter, `b` subject, `i` rebase, `v` revert, `y` copy hash, `z` zoom, then `Y`→"Shift+Y sync", `M`→"Shift+M more"; Files level `\r` "Enter diff", `d` difftool, `m` parent (merge only), Esc back; FileDiff: `d`, Esc back
  - `Demo.Graph()` → `List<GraphRow>`; `gws --snapshot <w> <h> commits`

- [ ] **Step 1: Write the failing tests**

Append inside `CommitTests()`:

```csharp
        Test("commits view renders graph rows, refs and the drill-down preview at every width", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot()); view.Mode = ViewMode.Commits;
            var model = Demo.CommitsModel(); model.Load(); model.DetailTask()!.Wait();
            foreach (var width in new[] { 80, 120, 160 }) {
                var hits = new HitMap(); var layout = Layout.For(width, 32, 0.5);
                string text = Screen.Render(view, layout, hits, [], 0, "", false, (c, l, h) => CommitsRender.Body(c, l, h, model, 0)).Plain();
                Check(text.Contains("[Commits]") && text.Contains("d3a721f") && text.Contains("Validate signed QR"), $"List at {width}");
                if (width >= 100) Check(text.Contains("Author") || text.Contains("Phillip"), $"Preview at {width}");
                Equal("row", hits.At(4, layout.List.Y)?.Kind, $"Row hit at {width}");
            }
            Check(CommitsRender.Preview(model).Any(l => l.Text.Contains("src/EntryService.cs")), "Files in preview");
            model.Enter(); model.Enter(); model.DiffTask()!.Wait(); var diff = CommitsRender.Preview(model);
            Check(diff.Any(l => l.Text.StartsWith("− old: parent a928430")), "Commit legend names the parent");
            var keys = string.Join(",", Footer.For(view, model).Select(k => k.Key)); Check(keys.Contains("d") && !keys.Contains("s"), "Diff-level keys: " + keys);
            model.Back(); model.Back(); keys = string.Join(",", Footer.For(view, model).Select(k => k.Key)); Check(keys.StartsWith("\r,/,f,b,i,v,y,z,Y,M"), "Commit keys: " + keys);
        });
```

- [ ] **Step 2: Run to verify failure**

Run with `--filter "commits view renders"`. Expected: build FAILS (`Demo.CommitsModel`, `CommitsRender` missing).

- [ ] **Step 3: Demo data**

In `Program.cs` `Demo`, add:

```csharp
    public static List<GraphRow> Graph() => [
        new("* ", Commits()[0], ["HEAD → feature/secure-qr"], true),
        new("* ", Commits()[1], ["origin/feature/secure-qr"], false),
        new("* ", Commits()[2], ["tag: v0.1"], false)];
    public static CommitsModel CommitsModel() => new(new("AccessControl", "C:/repos/AccessControl/master", "HEAD", "AccessControl / master → feature/secure-qr"),
        (tip, filter, skip, count) => skip == 0 ? Graph() : [],
        (commit, parent) => new($"commit {commit.Hash}\nAuthor:     {commit.Author}\nAuthorDate: {commit.Date}\n\n    {commit.Subject}", [new("M", "src/EntryService.cs", null, 4, 1)]),
        (commit, parent, file) => Diff, revision => "d3a721f904ed");
```

- [ ] **Step 4: Implement the renderer**

Append to `CommitsView.cs`:

```csharp
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
```

In `Footer.For`, insert at the start of the method:

```csharp
        if (view.Mode == ViewMode.Commits && commits is not null)
        {
            List<(string, string)> k = commits.Level switch {
                CommitLevel.Commits => [("\r", "Enter files"), ("/", "search"), ("f", "filter"), ("b", "branch"), ("i", "rebase"), ("v", "revert"), ("y", "copy hash"), ("z", "zoom"), ("Y", "Shift+Y sync"), ("M", "Shift+M more")],
                CommitLevel.Files => [("\r", "Enter diff"), ("d", "difftool"), .. commits.Commit?.Parents.Count > 1 ? new[] { ("m", "parent") } : [], ("z", "zoom"), ("\u001b", "Esc back")],
                _ => [("d", "difftool"), ("z", "zoom"), ("\u001b", "Esc back")]
            };
            k.Add(("?", "keys")); k.Add(("q", "quit")); return k;
        }
```

In `Footer.Render`, treat ids `"\u001b"` like `" "`/`"\r"` (label only): change the text line to `string text = key is " " or "\r" or "\u001b" || key.Length == 1 && char.IsUpper(key[0]) ? label : key.ToUpperInvariant() + " " + label;`.

In `Screen.Render`, add a final parameter `CommitsModel? commits = null` to the new overload and use `Footer.For(view, commits)` for the footer. In `Screen.ViewBar`, directly after the loop that draws the three view labels, add `if (view.Mode == ViewMode.Commits) return;`. The untracked and filter toggles belong to Changes and Worktrees, and Commits uses that space for its subject.

- [ ] **Step 5: Wire App**

Delete `HistoryDialog.cs` (`git rm pwsh/GitWorkspace/HistoryDialog.cs`) and remove `History.Detail` from `History.cs`. Nothing uses it after this task, because the tests switched to `CommitGraph` in Task 18.

In `App.cs`, add a field `CommitsModel? commits;` and:

1. Draw: in `Run`, replace the draw line with the following. The pattern variable keeps the lambda free of nullable warnings, which `TreatWarningsAsErrors` would turn into build errors.

```csharp
                var layout = Layout.For(terminal.Width, terminal.Height, view.State.SplitRatio);
                terminal.Draw(view.Mode == ViewMode.Commits && commits is { } model
                    ? Screen.Render(view, layout, hits, [], 0, notice, scan is not null, (c, l, h) => CommitsRender.Body(c, l, h, model, previewScroll), model)
                    : Screen.Render(view, layout, hits, previewLines, previewScroll, notice, scan is not null));
```

2. Replace `SetMode`:

```csharp
    void SetMode(ViewMode mode)
    {
        if (mode == ViewMode.Commits && commits is null) {
            var target = view.Current?.Tree is { } tree ? Target(tree) : view.Current?.Branch is { } branch ? Target(branch) :
                view.InGroup().FirstOrDefault(t => t.Usable) is { } first ? Target(first) : null;
            if (target is null) { notice = "No worktree in this view to show commits for."; return; }
            OpenCommits(target); return;
        }
        view.Mode = mode; view.Rebuild(); previewKey = ""; previewScroll = 0;
    }
    void OpenCommits(HistoryTarget target)
    {
        commits = demo ? Demo.CommitsModel() : CommitsModel.ForGit(target); commits.Load();
        view.Mode = ViewMode.Commits; previewScroll = 0;
        notice = "Commits · Enter files · / search (@author :path) · B another branch · Tab back to changes";
    }
```

3. Tab cycles three modes: `if (key.Key == ConsoleKey.Tab) { SetMode(view.Mode switch { ViewMode.Changes => ViewMode.Worktrees, ViewMode.Worktrees => ViewMode.Commits, _ => ViewMode.Changes }); return; }` — and when leaving Commits keep `commits` so returning restores position.

4. Route keys. Directly after the mouse block (before arrow handling), add `if (view.Mode == ViewMode.Commits && commits is not null && HandleCommits(key)) return;`. Inside the mouse block, directly after `var hit = hits.At(key.X, key.Y);`, add the Commits mouse handling. A double-click opens the preview full-screen, as the spec asks:

```csharp
            if (view.Mode == ViewMode.Commits && commits is { } open)
            {
                if (key.Wheel != 0) { if (hit?.Kind == "preview") previewScroll = Math.Max(0, previewScroll + key.Wheel); else open.Move(key.Wheel); return; }
                if (hit?.Kind == "row") {
                    open.Focus = hit.Index; while (open.Back()) { } previewScroll = 0;
                    if (key.DoubleClick) dialog.Review(open.Target.Label, CommitsRender.Preview(open), confirm: false);
                    return;
                }
                if (hit?.Kind == "subject") { PickCommitSubject(); return; }
            }
```

   Also make `HandleAction` map the footer's Esc item: add `else if (id == "\u001b") Handle(new(ConsoleKey.Escape));` before the final `else`.

5. Add:

```csharp
    bool HandleCommits(Input key)
    {
        var m = commits!;
        if (key.Key == ConsoleKey.UpArrow) { m.Move(-1); previewScroll = 0; return true; }
        if (key.Key == ConsoleKey.DownArrow) { m.Move(1); previewScroll = 0; return true; }
        if (key.Key == ConsoleKey.Home && m.Level == CommitLevel.Commits) { m.Focus = 0; m.Move(0); return true; }
        if (key.Key == ConsoleKey.Enter) { m.Enter(); previewScroll = 0; return true; }
        if (key.Key is ConsoleKey.Escape or ConsoleKey.Backspace && m.Back()) { previewScroll = 0; return true; }
        if (key.Character is 'Y' or 'M') { if (key.Character == 'Y') SyncMenu(); else More(); return true; }
        switch (key.Character)
        {
            case '/': var s = dialog.Text("Search commits", "Words match the message, @name the author, :path a file. All matching is literal and case-insensitive. Empty clears.", m.Filter.Search);
                if (s is not null) m.SetFilter(m.Filter with { Search = s }); return true;
            case 'f': CommitFilterMenu(m); return true;
            case 'b': PickCommitSubject(); return true;
            case 'r': m.Load(); return true;
            case 'm': m.NextParent(); return true;
            case 'z': dialog.Review(m.Target.Label, CommitsRender.Preview(m), confirm: false); return true;
            case 'y': if (m.Commit is { } c) CopyHash(c.Hash); return true;
            case 'i': if (m.Commit is { } first) RebaseFromCommit(m, first); return true;
            case 'v': if (m.Commit is { } revert) RevertFromCommits(m, revert); return true;
            case 'd': if (m.Commit is { } dc && m.File is { } file && CanWrite()) {
                    List<string> args = ["difftool", "--no-prompt", CommitGraph.Base(m.Target.Directory, dc, m.Parent), dc.Hash, "--"]; if (file.OldPath is not null) args.Add(file.OldPath); args.Add(file.Path);
                    RunExternal(Git.StartInfo(m.Target.Directory, args, false)); }
                return true;
            case 's' or 'u' or 'c': notice = "Switch to Changes (Tab) to stage or commit."; return true;
        }
        return false;
    }
    void CommitFilterMenu(CommitsModel m)
    {
        var pick = dialog.Choose("Filter commits", ["All commits", "Unpushed only" + (m.Target.Upstream is null ? " (no upstream)" : ""), "Since a base (branch, tag or commit)…"]);
        if (pick == 0) m.SetFilter(m.Filter with { UnpushedOnly = false, Base = null });
        if (pick == 1) m.SetFilter(m.Filter with { UnpushedOnly = true, Base = null });
        if (pick == 2 && dialog.Text("Since base", "Show commits after this base, e.g. main or origin/main.", m.Filter.Base ?? "main") is { Length: > 0 } b) m.SetFilter(m.Filter with { UnpushedOnly = false, Base = b });
    }
    void PickCommitSubject()
    {
        var targets = view.InGroup().Where(t => t.Head != "(initial)").Select(Target).Concat(view.InactiveBranches().Select(Target)).ToList();
        var pick = dialog.Choose("Commits · choose branch or worktree", targets.Select(t => t.Label + (t.WorktreeId is null ? " · not checked out" : "")).ToList());
        if (pick is not null) OpenCommits(targets[pick.Value]);
    }
    void CopyHash(string hash)
    {
        try {
            var start = new ProcessStartInfo("clip.exe") { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
            using var process = Process.Start(start)!; process.StandardInput.Write(hash); process.StandardInput.Close(); process.WaitForExit(5000);
            notice = "Copied " + hash;
        } catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { notice = "Clipboard unavailable · " + hash; }
    }
    void RevertFromCommits(CommitsModel m, CommitInfo commit)
    {
        if (!CanWrite()) return;
        var tree = view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == m.Target.WorktreeId && t.Usable);
        if (tree is null) { dialog.Message("Revert commit", "Revert needs this branch checked out in a worktree. Use B → Checkout branch first."); return; }
        Execute([Operations.Revert(tree, commit.Hash)]); m.Load();
    }
```

6. In `HistoryActions.cs`, replace `BrowseHistory` with a router and extract the rebase check:

```csharp
    void BrowseHistory(HistoryTarget? target = null)
    {
        target ??= view.Current?.Tree is { } tree ? Target(tree) : view.Current?.Branch is { } branch ? Target(branch) : PickHistory(view.Current?.Project);
        if (target is not null) OpenCommits(target);
    }
    void RebaseFromCommit(CommitsModel m, CommitInfo commit)
    {
        if (!CanWrite()) return;
        Refresh();
        var checkout = view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == m.Target.WorktreeId && t.Usable);
        if (checkout is null) { dialog.Message("Interactive rebase", "This branch needs an available worktree before rebasing. Use B → Checkout branch, or create a worktree for it, then open its commits."); return; }
        if (Git.Head(checkout) != m.Tip || checkout.ActiveBranchRef != m.Target.BranchRef) { dialog.Message("History changed", "This worktree moved after its commits were listed. The list has been refreshed; select the intended commit again."); OpenCommits(Target(checkout)); return; }
        RebaseInteractive(checkout, commit);
        OpenCommits(Target(view.Snapshot.Worktrees.FirstOrDefault(t => t.Id == checkout.Id) ?? checkout));
    }
```

   In `RebaseInteractive`, `if (mode == 0) { BrowseHistory(Target(tree)); return; }` stays (it now opens Commits).

7. In `Navigation.cs` `OpenTab` and in `App.Handle` for `[`/`]`: after navigating, if `view.Mode == ViewMode.Commits`, set `commits = null; SetMode(ViewMode.Commits);` so the subject resets to the first worktree of the new group.

8. The `H` key: `case 'h': BrowseHistory(); break;` stays and now opens Commits. Enter on a branch row / Repo View worktree also calls `BrowseHistory()` (unchanged).

- [ ] **Step 6: Snapshot flag**

In `Program.cs` `--snapshot`, after building `view`, add:

```csharp
                bool commitsMode = arguments.Contains("commits");
                if (commitsMode) {
                    view.Mode = ViewMode.Commits; var model = Demo.CommitsModel(); model.Load(); model.DetailTask()!.Wait();
                    var layout = Layout.For(Math.Clamp(w, 1, 300), Math.Clamp(h, 1, 100), 0.5);
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.Write(Screen.Render(view, layout, new HitMap(), [], 0, "Commits", false, (c, l, hits) => CommitsRender.Body(c, l, hits, model, 0), model).Plain()); return 0;
                }
```

Parse `w`/`h` only when the arguments after `--snapshot` are numeric: replace the two `int.Parse` lines with `int.TryParse(arguments.ElementAtOrDefault(index + 1), out var pw) ? pw : 140` and similarly `32`.

- [ ] **Step 7: Run to verify pass**

Run with `--filter "commits view renders"`, then the full suite. Expected: all PASS. Then run `dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --snapshot 120 32 commits`. Expected: `[Commits]` active, graph dots `●`, `↑d3a721f`, ref labels, preview with the header and `1 files  +4 −1`.

- [ ] **Step 8: Commit**

```bash
git add -A pwsh/GitWorkspace pwsh/GitWorkspace.Tests/CommitTests.cs
git commit -m "gws: commits view on the main screen replaces the history page"
```

Check before committing that `git status` shows no stray `bin/` or `obj/` output under `pwsh/`; `pwsh/.gitignore` should exclude it.

```bash
git status --short pwsh
```

---

### Task 21: Phase 3 documentation and final verification

**Files:**
- Modify: `pwsh/GitWorkspace/README.md` (history section, key table), `pwsh/GitWorkspace/App.cs` (`Help`), `pwsh/profile.d/13-git-workspace.ps1` (completion adds `commits`)

- [ ] **Step 1: Update README**

In the key table replace `| Review history of the focused branch or worktree | H |` with `| Commits of the focused branch or worktree | H, or Tab to Commits |` and `| Changes / worktree selection | … |` with `| Changes / Worktrees / Commits | Tab, or click the view bar |`. Replace the paragraph starting "`H` opens commit history…" with:

```markdown
`H` (or Tab to **Commits**) shows the focused branch or worktree's commits beside the
preview: a graph, short hash, relative date, subject and ref labels such as `(HEAD →
main)`, `(origin/main)` and `(tag: v1)`. `↑` marks commits not yet pushed to the
upstream. More commits load as you scroll; the tip is fixed when the list opens and `R`
reloads. `/` searches: words match the message, `@name` the author and `:path` a file,
all literal and case-insensitive. `F` shows all, unpushed only, or commits since a base.
`B` or clicking the title picks another branch or worktree in the current tab.

`Enter` moves into the commit's changed files, and `Enter` again shows one file's diff
with the legend `− old: parent <hash>   + new: <hash> "<subject>"`. `M` picks the parent
of a merge commit, `D` opens your difftool, `Esc` steps back and `Z` zooms the preview.
`I` starts an interactive rebase from the highlighted commit, `V` reverts it, and `Y`
copies its hash. `Shift+Y` / `Shift+M` open Sync and More while Commits has focus.
```

Remove the sentence about `N` / `P` paging.

- [ ] **Step 2: Update help and completion**

In `Help()`, replace the `"History: Enter opens the commit patch, N/P page, …"` paragraph with: `"Commits (H or Tab): Enter files → Enter diff, Esc back. / search (@author, :path), F filter, B branch, M merge parent, D difftool, I rebase from commit, V revert, Y copy hash, Z zoom. Shift+Y / Shift+M reach Sync and More.\n\n"`.

In `pwsh/profile.d/13-git-workspace.ps1`, add `'commits'` to the completion list after `'--snapshot'`.

- [ ] **Step 3: Full verification**

Run the full test command without `--filter`. Expected: `0 failed`. Run each snapshot and eyeball it:

```powershell
dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --snapshot 80 24
dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --snapshot 160 40
dotnet run --project C:\repos\DotFiles\pwsh\GitWorkspace\GitWorkspace.csproj -c Release -- --snapshot 120 32 commits
```

Expected: no line wider than the terminal width, the tab row visible, and `? keys` in the footer. `Q quit` can be dropped at 80 columns when the row's keys fill the line.

Ask the user to run `gws --demo` in Windows Terminal and check: tab clicks, right-click on a tab, double-click a row, popups over a dimmed screen, `<`/`>` resizing, Commits drill-down. Record their result in the final report; do not claim interactive verification you did not perform.

- [ ] **Step 4: Commit**

```bash
git add pwsh/GitWorkspace/README.md pwsh/GitWorkspace/App.cs pwsh/profile.d/13-git-workspace.ps1
git commit -m "gws docs: commits view keys and search syntax"
```
