using System.Text.Json;

namespace GitWorkspace;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var arguments = args.ToList(); string configPath = Config.DefaultPath;
            int configAt = arguments.IndexOf("--config");
            if (configAt >= 0) {
                if (configAt + 1 >= arguments.Count) throw new ArgumentException("--config needs a file path.");
                configPath = Path.GetFullPath(arguments[configAt + 1]); arguments.RemoveRange(configAt, 2);
            }
            if (arguments.Contains("--help") || arguments.Contains("help")) {
                Console.WriteLine("gws                        Open the workspace checklist\ngws status [--json]         Read-only overview of every registered worktree\ngws status --root <path>    Discover projects immediately under a directory\ngws add <name> <path>       Register an existing repository\ngws --demo                 Interactive sample (no repository changes)\ngws --snapshot [width height]  Print the sample layout\ngws --config <path>         Override the local configuration file\n\nConfig: " + configPath); return 0;
            }
            if (arguments.Contains("--snapshot")) {
                int index = arguments.IndexOf("--snapshot");
                int w = arguments.Count > index + 1 ? int.Parse(arguments[index + 1]) : 140;
                int h = arguments.Count > index + 2 ? int.Parse(arguments[index + 2]) : 32;
                var view = new WorkspaceView(Demo.Settings(), new()); view.Update(Demo.Snapshot()); view.Focus = 2;
                foreach (var row in view.Rows.Where(r => r.Change is not null).Take(2)) view.Selected.Add(row.Id);
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.Write(Screen.Render(view, Math.Clamp(w, 1, 300), Math.Clamp(h, 1, 100), Demo.Diff, 0, "Local status updated just now", false).Plain()); return 0;
            }
            bool demo = arguments.Contains("--demo");
            var settings = demo ? Demo.Settings() : Config.Read(configPath);
            if (arguments.FirstOrDefault() == "add") {
                if (arguments.Count != 3) throw new ArgumentException("Usage: gws add <name> <absolute-project-path>");
                var path = Path.GetFullPath(arguments[2]); string common = Git.ResolveRepository(path);
                if (settings.Projects.Any(p => p.Name.Equals(arguments[1], StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Project name already registered.");
                foreach (var p in settings.Projects) { string? old = null; try { old = Git.ResolveRepository(p.Path); } catch (InvalidOperationException) { }
                    if (string.Equals(old, common, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Repository already registered as " + p.Name); }
                settings.Projects.Add(new(arguments[1], path)); Config.Save(configPath, settings); Console.WriteLine("Registered " + arguments[1]); return 0;
            }
            if (arguments.FirstOrDefault() == "status") {
                int root = arguments.IndexOf("--root");
                if (root >= 0) {
                    if (root + 1 >= arguments.Count) throw new ArgumentException("--root needs a directory.");
                    settings = Discovery.SettingsForRoot(arguments[root + 1], settings);
                }
                var snapshot = Git.Scan(settings);
                if (arguments.Contains("--json")) Console.WriteLine(JsonSerializer.Serialize(snapshot, Config.Json));
                else {
                    if (settings.Projects.Count == 0) Console.WriteLine("No registered projects. Run gws and choose More → Add project.");
                    foreach (var project in snapshot.Worktrees.GroupBy(t => t.Project)) {
                        Console.WriteLine("\n" + project.Key); Console.WriteLine(new string('─', Math.Min(70, Math.Max(20, project.Key.Length))));
                        foreach (var t in project) Console.WriteLine($"  {Canvas.Clean(t.Name)} → {Canvas.Clean(t.Branch)}\n    {Canvas.Clean(t.Summary)}");
                    }
                    foreach (var error in snapshot.Errors) Console.Error.WriteLine("! " + Canvas.Clean(error));
                }
                return snapshot.Errors.Count > 0 || snapshot.Worktrees.Any(t => t.Error.Length > 0) ? 1 : 0;
            }
            if (arguments.Any(a => a != "--demo")) throw new ArgumentException("Unknown argument. Run gws --help.");
            return new App(settings, configPath, demo).Run();
        }
        catch (Exception e) { Console.Error.WriteLine("gws: " + e.Message); return 1; }
    }
}

public static class Demo
{
    public const string Diff = "diff --git a/src/EntryService.cs b/src/EntryService.cs\n--- a/src/EntryService.cs\n+++ b/src/EntryService.cs\n@@ -21,5 +21,8 @@ public EntryResult Validate(string code)\n-    return Parse(code);\n+    if (string.IsNullOrWhiteSpace(code))\n+        return EntryResult.Invalid;\n+\n+    return ValidateSignature(code);\n";
    public static Settings Settings() => new() { Projects = [new("AccessControl", "C:/repos/AccessControl"), new("Joiners", "C:/repos/Joiners"), new("mobileApp", "C:/repos/mobileApp")],
        Groups = [new("Secure QR", ["Joiners"], []) { Branches = [new("AccessControl", "refs/heads/feature/secure-qr"), new("mobileApp", "refs/heads/feature/qr-data")] }, new("Maintenance", ["mobileApp"], [])] };
    public static List<CommitInfo> Commits() => [new("d3a721f904ed", "d3a721f", "Phillip", "2026-09-24", "Validate signed QR entry requests", ["a928430d8fc2"]), new("a928430d8fc2", "a928430", "Phillip", "2026-09-23", "Add QR response contract", ["8175ff13ae44"]), new("8175ff13ae44", "8175ff1", "Phillip", "2026-09-22", "Initial service", [])];
    public static Snapshot Snapshot()
    {
        var a = new Worktree { Project = "AccessControl", ProjectRoot = "C:/repos/AccessControl", Path = "C:/repos/AccessControl/master", CommonDir = "C:/repos/AccessControl/.git-main", Branch = "feature/secure-qr", Ahead = 2, Upstream = "origin/feature/secure-qr", Changes = [new("src/EntryService.cs", null, ".M", ChangeArea.Unstaged), new("appsettings.json", null, ".M", ChangeArea.Unstaged)] };
        var j = new Worktree { Project = "Joiners", ProjectRoot = "C:/repos/Joiners", Path = "C:/repos/Joiners/fix/qr-response", CommonDir = "C:/repos/Joiners/.git-main", Branch = "feature/secure-qr", Upstream = "origin/feature/secure-qr", Changes = [new("Controllers/QrCode.cs", null, "M.", ChangeArea.Staged), new("Services/Email.cs", null, ".M", ChangeArea.Unstaged)] };
        var m = new Worktree { Project = "mobileApp", ProjectRoot = "C:/repos/mobileApp", Path = "C:/repos/mobileApp/qr-code-data-flag", CommonDir = "C:/repos/mobileApp/.git-main", Branch = "feature/qr-data", Behind = 1, Upstream = "origin/feature/qr-data", Changes = [new("src/scanner.ts", null, ".M", ChangeArea.Unstaged)] };
        return new([a, j, m], [], DateTime.Now) { Branches = [new(a.Project, a.CommonDir, "refs/heads/" + a.Branch, "d3a721f904ed", "Validate QR"), new(a.Project, a.CommonDir, "refs/heads/main", "8175ff13ae44", "Initial service"), new(j.Project, j.CommonDir, "refs/heads/" + j.Branch, "d3a721f904ed", "QR response"), new(m.Project, m.CommonDir, "refs/heads/" + m.Branch, "d3a721f904ed", "QR data")] };
    }
}
