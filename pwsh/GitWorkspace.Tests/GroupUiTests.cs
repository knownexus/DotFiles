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
    }
}
