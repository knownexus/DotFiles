using GitWorkspace;

static partial class Tests
{
    static void TabTests()
    {
        Test("tab labels and counts match the demo groups", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            var tabs = view.Tabs();
            Equal(5, tabs.Single(t => t.Label == "All projects").Count, "All projects count");
            Equal(1, tabs.Single(t => t.Label == "Maintenance").Count, "Maintenance count");
            Check(tabs.Any(t => t.Label == "Secure QR"), "Secure QR tab present: " + string.Join(",", tabs.Select(t => t.Label)));
        });
        Test("selecting a distant tab among many groups keeps its label reachable at 80 columns", () => {
            var settings = Demo.Settings();
            for (int i = 1; i <= 20; i++) settings.Groups.Add(new("Extra " + i.ToString("00"), ["AccessControl"], []));
            var configPath = Path.Combine(root, "tab-app-" + ++sequence + ".json");
            var app = new App(settings, configPath, demo: true);
            app.TestView.Update(Demo.Snapshot());
            app.SelectTab(15);
            var layout = Layout.For(80, 24, 0.5);
            // Mirrors what App.Run's frame loop does once it notices the active tab changed -
            // TabTests cannot drive the real Run() loop, so it replays the same public call.
            app.TestView.State.TabOffset = Screen.EnsureTabVisible(app.TestView.Tabs(), layout.Tabs.Width, app.TestView.State.TabOffset);
            var hits = new HitMap();
            string text = Screen.Render(app.TestView, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            var selected = app.TestView.Tabs().First(t => t.Index == 15);
            Check(text.Contains(selected.Label), "Selected tab label visible: " + text);
            var row = Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).ToList();
            Check(row.Any(h => h?.Id == "tab-left"), "Left scroll hit present");
            Check(row.Any(h => h?.Id == "new"), "New group hit present within width");
        });
        Test("three demo tabs fit at 120 columns without scroll controls", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            var layout = Layout.For(120, 32, 0.5); var hits = new HitMap();
            string text = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(text.Contains("All projects") && text.Contains("Secure QR") && text.Contains("Maintenance"), "All labels present: " + text);
            var row = Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).ToList();
            Check(!row.Any(h => h?.Id is "tab-left" or "tab-right"), "No scroll hits needed: " + string.Join(",", row.Where(h => h is not null).Select(h => h!.Id)));
        });
        Test("Repo View tab is active with a close hit after navigating into a repository", () => {
            var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot());
            view.Navigate("Joiners");
            var tabs = view.Tabs(); var repoTab = tabs.Single(t => t.Id.StartsWith("repo:", StringComparison.Ordinal));
            Check(repoTab.Active && repoTab.Label.Contains("Joiners"), "Repo tab active: " + repoTab);
            var layout = Layout.For(120, 32, 0.5); var hits = new HitMap();
            Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false);
            Check(Enumerable.Range(0, layout.Tabs.Width).Select(x => hits.At(x, layout.Tabs.Y)).Any(h => h?.Id == "repo-close"), "repo-close hit present");
        });
        Test("tab counts drop untracked files when Show-untracked is off", () => {
            var settings = Demo.Settings(); var state = new ViewState();
            var view = new WorkspaceView(settings, state);
            var snapshot = Demo.Snapshot();
            snapshot.Worktrees.Single(t => t.Project == "AccessControl").Changes.Add(new("new-file.txt", null, "??", ChangeArea.Untracked));
            view.Update(snapshot);
            Equal(6, view.Tabs().Single(t => t.Label == "All projects").Count, "Untracked change counted while shown");
            state.ShowUntracked = false; view.Update(snapshot);
            Equal(5, view.Tabs().Single(t => t.Label == "All projects").Count, "Untracked change dropped once hidden");
        });
        Test("tab counts refresh when the group list changes without a new snapshot", () => {
            var settings = Demo.Settings(); var view = new WorkspaceView(settings, new()); view.Update(Demo.Snapshot());
            Equal(1, view.Tabs().Single(t => t.Label == "Maintenance").Count, "Initial maintenance count");
            settings.Groups.Add(new("Everything", ["AccessControl", "Joiners", "mobileApp"], []));
            Equal(5, view.Tabs().Single(t => t.Label == "Everything").Count, "New group counted correctly (cache invalidated by group list change)");
        });
        Test("scrolling tabs left is respected and rendering never persists TabOffset (checkpoint review #7a/#7c)", () => {
            var settings = Demo.Settings(); settings.Groups.Clear();
            for (int i = 1; i <= 20; i++) settings.Groups.Add(new("Group " + i.ToString("00"), ["AccessControl"], []));
            var state = new ViewState { Group = "Group 20", TabOffset = 18 };
            var view = new WorkspaceView(settings, state); view.Update(Demo.Snapshot());
            var layout = Layout.For(80, 24, 0.5); var hits = new HitMap();
            string before = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(before.Contains("[Group 20"), "Active tab initially visible: " + before);
            // Mimic App.Tab's "tab-left" handling: a click can jump straight back to the first tabs
            // even though the active tab (Group 20) then falls off-screen to the right.
            state.TabOffset = 0; hits.Clear();
            string after = Screen.Render(view, layout, hits, Styled.FromText("preview"), 0, "", false).Plain();
            Check(after.Contains("All projects") && after.Contains("Group 01"), "Scrolling left reveals earlier tabs: " + after);
            Equal(0, state.TabOffset, "Render must not write back TabOffset - no side effect from drawing");
        });
        Test("SelectTab only nudges the offset when the newly selected tab was hidden (checkpoint review #7b)", () => {
            var settings = Demo.Settings(); settings.Groups.Clear();
            for (int i = 1; i <= 20; i++) settings.Groups.Add(new("Group " + i.ToString("00"), ["AccessControl"], []));
            var configPath = Path.Combine(root, "tab-select-" + ++sequence + ".json");
            var app = new App(settings, configPath, demo: true);
            app.TestView.Update(Demo.Snapshot());
            app.TestView.State.TabOffset = 5;
            app.SelectTab(3); // hidden to the left of the current window (3 < 5)
            Equal(3, app.TestView.State.TabOffset, "Offset drops to reveal a tab hidden to the left");
            app.TestView.State.TabOffset = 0;
            app.SelectTab(0); // "All projects" is already visible at offset 0
            Equal(0, app.TestView.State.TabOffset, "Offset untouched when the tab is already visible");
            app.TestView.State.TabOffset = 5;
            app.SelectTab(10); // to the right of the window; SelectTab alone must not force offset = 10
            Check(app.TestView.State.TabOffset != 10, "Offset is not simply set to the selected tab's own index: " + app.TestView.State.TabOffset);
        });
        Test("demo mode never writes when deleting a group from the tab popup (Critical, checkpoint review #6)", () => {
            var settings = Demo.Settings();
            var configPath = Path.Combine(root, "demo-delete-" + ++sequence + ".json");
            const string marker = "{\"marker\":\"untouched\"}";
            File.WriteAllText(configPath, marker);
            var app = new App(settings, configPath, demo: true);
            app.TestView.Update(Demo.Snapshot());
            var surface = new ScriptedSurface(100, 30, new Input(ConsoleKey.DownArrow), new Input(ConsoleKey.DownArrow), new Input(ConsoleKey.DownArrow), new Input(ConsoleKey.Enter), new Input(ConsoleKey.Enter));
            app.TestAttachSurface(surface);
            int index = settings.Groups.FindIndex(g => g.Name == "Maintenance") + 1;
            app.EditGroupTab(index);
            Equal(marker, File.ReadAllText(configPath), "Demo mode must never touch the real config file");
            Check(settings.Groups.Any(g => g.Name == "Maintenance"), "Group must not be removed in demo mode");
        });
    }
}
