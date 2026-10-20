using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// The complete flag lists, asked of the tools themselves instead of typed out
// by hand, so every option shows up and stays in step with the installed
// version:
//
//   git     `git <cmd> --git-completion-helper` (what git's own bash completion
//           uses) plus `git --list-cmds` for subcommands and your git aliases.
//   docker, dotnet   the Options section of `<cmd> <sub> --help`.
//
// Lookups never block the keystroke: the first time a path is seen it's fetched
// in the background (the common git ones are pre-warmed at load) and the next
// keystroke has the full list. Results are cached for the session.
internal static class Dynamic
{
    private sealed record Entry(string[] Flags, string[] Children)
    {
        public static readonly Entry Empty = new(Array.Empty<string>(), Array.Empty<string>());
    }

    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Started = new(StringComparer.OrdinalIgnoreCase);
    private static volatile string[] _gitCommands = Array.Empty<string>();

    // Roots whose `--help` output is safe to run and has a parseable Options section.
    private static readonly HashSet<string> HelpRoots = new(StringComparer.OrdinalIgnoreCase) { "docker", "dotnet" };

    // `git log` / `git show` take revision options that aren't in the completion
    // helper (git's bash completion hardcodes them too); they also take every
    // `git diff` option, which is merged in below.
    private static readonly string[] RevisionFlags =
    {
        "--oneline", "--graph", "--all", "--decorate", "--stat", "-p", "--follow", "--name-only", "--name-status",
        "--author=", "--committer=", "--grep=", "--since=", "--until=", "--after=", "--before=",
        "-n <count>", "--max-count=", "--skip=", "--reverse", "--first-parent", "--merges", "--no-merges",
        "--format=", "--pretty=", "--date=", "--abbrev-commit", "--relative-date", "--left-right", "--cherry-pick",
        "--branches", "--remotes", "--tags", "-S <string>", "-G <regex>", "-L <range>:<file>",
        "--walk-reflogs", "--ancestry-path", "--simplify-by-decoration", "--boundary", "--source", "-i", "--invert-grep",
    };

    private static readonly string[] Warm =
    {
        "git diff", "git log", "git show", "git add", "git commit", "git push", "git pull", "git fetch",
        "git rebase", "git branch", "git checkout", "git switch", "git reset", "git restore", "git stash",
        "git stash push", "git merge", "git cherry-pick", "git clone", "git remote", "git tag", "git status",
        "git blame", "git clean", "git worktree", "git worktree add", "git config",
    };

    static Dynamic()
    {
        Task.Run(() =>
        {
            LoadGitCommands();
            foreach (var path in Warm) { Started.TryAdd(path, 0); Fetch(path); }
        });
    }

    public static bool IsKnownPath(string path)
    {
        var words = path.Split(' ');
        if (!words[0].Equals("git", StringComparison.OrdinalIgnoreCase)) return false;
        if (words.Length == 1) return true;
        var parent = string.Join(" ", words[..^1]);
        return Children(parent).Contains(words[^1], StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> Children(string path)
    {
        if (path.Equals("git", StringComparison.OrdinalIgnoreCase)) return _gitCommands;
        return Get(path)?.Children ?? Array.Empty<string>();
    }

    public static IEnumerable<string> Flags(string path)
    {
        IEnumerable<string> flags = Get(path)?.Flags ?? Array.Empty<string>();
        if (path.Equals("git log", StringComparison.OrdinalIgnoreCase) || path.Equals("git show", StringComparison.OrdinalIgnoreCase))
            flags = flags.Concat(RevisionFlags).Concat(Get("git diff")?.Flags ?? Array.Empty<string>());
        return flags;
    }

    private static Entry? Get(string path)
    {
        if (Cache.TryGetValue(path, out var entry)) return entry;
        var words = path.Split(' ');
        var supported = words.Length >= 2 &&
                        (words[0].Equals("git", StringComparison.OrdinalIgnoreCase) || HelpRoots.Contains(words[0]));
        if (supported && Started.TryAdd(path, 0)) Task.Run(() => Fetch(path));
        return null;
    }

    private static void Fetch(string path)
    {
        try
        {
            var words = path.Split(' ');
            Cache[path] = words[0].Equals("git", StringComparison.OrdinalIgnoreCase)
                ? ParseGitHelper(Run("git", words[1..].Append("--git-completion-helper"), GitScratchRepo()))
                : ParseHelp(Run(words[0], words[1..].Append("--help"), null));
        }
        catch
        {
            Cache[path] = Entry.Empty;
        }
    }

    // "--patch --unified --stat-width= ... -- --no-patch ..." : the part after
    // the lone "--" is only the negated forms, so it's dropped. Bare words
    // (stash's "apply clear drop pop ...") are subcommands.
    private static Entry ParseGitHelper(string output)
    {
        var words = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).TakeWhile(w => w != "--").ToArray();
        return new Entry(
            words.Where(w => w.StartsWith('-')).ToArray(),
            words.Where(w => !w.StartsWith('-')).ToArray());
    }

    // "  -d, --detach            Detached mode"        -> --detach
    // "      --name string       Assign a name"        -> --name <string>
    // "  -c, --configuration <CONFIGURATION>  ..."     -> --configuration <configuration>
    private static readonly Regex HelpOption = new(
        @"^\s{1,12}(?:-[A-Za-z0-9?],\s*)?(--[A-Za-z0-9][\w.-]*)(?: (<[^>]+>|[a-z][\w-]*)(?=\s{2,}|$))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static Entry ParseHelp(string output)
    {
        var flags = HelpOption.Matches(output)
            .Select(m => m.Groups[2].Success ? $"{m.Groups[1].Value} <{m.Groups[2].Value.Trim('<', '>').ToLowerInvariant()}>" : m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new Entry(flags, Array.Empty<string>());
    }

    private static void LoadGitCommands()
    {
        try
        {
            _gitCommands = Run("git", new[] { "--list-cmds=list-mainporcelain,others,alias,nohelpers" }, null)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { /* git missing: curated subcommands still work */ }
    }

    // The completion helper refuses to run outside a repository, so keep an
    // empty one in %TEMP% to run it from — that way it works from any directory.
    private static string GitScratchRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sig-predictor-git");
        if (!Directory.Exists(Path.Combine(dir, ".git")))
        {
            Directory.CreateDirectory(dir);
            Run("git", new[] { "init", "-q", dir }, null);
        }
        return dir;
    }

    private static string Run(string file, IEnumerable<string> args, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["NO_COLOR"] = "1";

        using var process = Process.Start(psi);
        if (process is null) return "";
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return "";
        }
        return stdout.Wait(1000) ? stdout.Result : "";
    }
}
