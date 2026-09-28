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
            var alt = Path.Combine(scan, "Alt"); Directory.CreateDirectory(alt);
            Git.Run(source, "clone", "--bare", source, Path.Combine(alt, ".bare")).Require();
            Directory.CreateDirectory(Path.Combine(scan, "Plain folder"));
            Directory.CreateDirectory(Path.Combine(normal, "nested inside repo"));
            var registered = Path.Combine(scan, "Registered"); Directory.CreateDirectory(registered);
            Git.Run(source, "clone", "--bare", source, Path.Combine(registered, ".git-main")).Require();
            var settings = new Settings { Projects = [new("Already here", registered)] };
            var found = Discovery.Scan(settings, [scan, Path.Combine(root, "missing root"), normal]);
            Equal("Alt,Bare.git,Layout,Normal,Registered", string.Join(",", found.Select(f => f.Name)), "Found repos (nested folder under a repo root is not a repo)");
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
        Test("SettingsForRoot preserves registered names and fails loudly on a missing root", () => {
            var scan = ScanRoot();
            var registered = InitAt(Path.Combine(scan, "Registered"));
            var settings = new Settings { Projects = [new("Already", registered)] };
            var forRoot = Discovery.SettingsForRoot(scan, settings);
            Check(forRoot.Projects.Any(p => p.Name == "Already" && p.Path == Path.GetFullPath(registered).Replace('\\', '/')), "Registered name preserved");
            var threw = false;
            try { Discovery.SettingsForRoot(Path.Combine(root, "missing root"), settings); }
            catch (DirectoryNotFoundException) { threw = true; }
            Check(threw, "Missing root should throw");
        });
        Test("SettingsForRoot gives a colliding folder name a numeric suffix, keeping the registered name intact", () => {
            var scan = ScanRoot();
            var registeredFolder = InitAt(Path.Combine(scan, "bar"));
            var collidingFolder = InitAt(Path.Combine(scan, "foo"));
            var settings = new Settings { Projects = [new("foo", registeredFolder)] };
            var forRoot = Discovery.SettingsForRoot(scan, settings);
            Equal("foo", forRoot.Projects.Single(p => p.Path == Path.GetFullPath(registeredFolder).Replace('\\', '/')).Name, "Registered name kept");
            Equal("foo 2", forRoot.Projects.Single(p => p.Path == Path.GetFullPath(collidingFolder).Replace('\\', '/')).Name, "Colliding folder name suffixed");
        });
        Test("old settings without a Roots field still default to C:/repos", () => {
            var old = Path.Combine(root, "old settings " + ++sequence + ".json");
            File.WriteAllText(old, "{\"Version\":1,\"Projects\":[],\"Groups\":[]}");
            var loaded = Config.Read(old);
            Check(loaded.Roots is null, "Missing field deserializes to null");
            Check(Directory.Exists("C:/repos") ? Discovery.Roots(loaded).SequenceEqual(["C:/repos"]) : Discovery.Roots(loaded).Count == 0, "Old settings default root");
        });
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
    }
}
