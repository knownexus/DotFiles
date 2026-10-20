using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// Flag and subcommand hints for the external tools used most often, built from
// ~/.pwsh_history. Two sources feed the suggestions:
//
//   Templates  whole command lines in the shapes actually typed, most-used
//              first. <placeholders> match any value you type in that slot.
//   Flags      subcommand tree + flags per command path ("docker compose up").
//              Children of a path come from the deeper keys, so intermediate
//              nodes ("devenv backend") don't need an entry of their own.
//
// Aliases that forward to a git subcommand (gs, gl, gdf, ...) reuse that
// subcommand's flags once you start typing a '-'.
internal static class CommandHints
{
    private static readonly string[] Templates =
    {
        // ── git ──────────────────────────────────────────────────────────────
        "git rebase origin/main",
        "git rebase origin/master",
        "git rebase -i HEAD~<n>",
        "git rebase --continue",
        "git rebase --abort",
        "git push --set-upstream origin <branch>",
        "git push --set-upstream origin <branch> --force-with-lease",
        "git push -u origin HEAD:<branch>",
        "git push origin HEAD:refs/heads/<branch>",
        "git diff HEAD~1",
        "git diff <commit> <commit>",
        "git diff origin -- . ':(exclude)<path>'",
        "git log --oneline",
        "git log <file>",
        "git show --stat <commit>",
        "git blame <file>",
        "git remote -v",
        "git branch -vv",
        "git reset --hard <commit>",
        "git clone <url> <dir>",
        "git clone --bare <url>",
        "git config --edit",
        "git difftool --no-prompt --extcmd='vim -u NONE -d' <commit>^!",

        // ── your git aliases ─────────────────────────────────────────────────
        "gdf HEAD~1 -- <file>",
        "gs --name-only <commit>",
        "gs -w <commit>",
        "gita -p .",

        // ── docker compose ───────────────────────────────────────────────────
        "docker compose -f docker/mobile-proxy/docker-compose.yml logs mitmproxy --tail 40",
        "docker compose -f docker/mobile-proxy/docker-compose.yml up -d --force-recreate",
        "docker compose -f docker/mobile-proxy/docker-compose.yml ps",
        "docker compose -f docker/mobile-proxy/docker-compose.yml down --remove-orphans",
        "docker compose -f <file> up -d",
        "docker compose -f <file> logs <service> --tail <n>",
        "docker compose -f <file> ps -a",
        "docker compose -f <file> cp <service>:<path> <dest>",
        "docker compose -f <file> exec <service> <command>",

        // ── docker ───────────────────────────────────────────────────────────
        "docker ps --filter name=<name> --format \"{{.Names}}  {{.Status}}\"",
        "docker restart <container>",
        "docker exec -it <container> <command>",
        "docker inspect <container> --format <template>",
        "docker run -d --name <name> -p <host>:<port> -e <VAR=value> <image>",

        // ── devenv ───────────────────────────────────────────────────────────
        "devenv start",
        "devenv start --remote-db",
        "devenv backend up --remote-db --allow-dev-migrations",
        "devenv backend up",
        "devenv backend down",
        "devenv scanner scan --device <id> --allow-remote",
        "devenv scanner scan --device <id> --allow-remote --invalid",
        "devenv scanner scan --device <id> --allow-remote --expired",
        "devenv scanner scan --device <id> --allow-remote --screenshot",
        "devenv scanner scan --device <id> --allow-remote --friend-pass --member <id>",
        "devenv user link --member <id> --b2c <guid>",
        "devenv logs capture --since 5m",
        "devenv build mobile --abi x64 --deploy full --install",
        "devenv build joiners",
        "devenv db sql <file>",
        "devenv app install",

        // ── claude ───────────────────────────────────────────────────────────
        "claude --resume",
        "claude --continue",

        // ── adb ──────────────────────────────────────────────────────────────
        "adb logcat -c",
        "adb shell am force-stop <package>",
        "adb shell settings get global http_proxy",
        "adb shell settings put global http_proxy 10.0.2.2:8888",
        "adb shell pm list packages",
        "adb push <file> /sdcard/Download/",
        "adb -s <serial> emu kill",
        "adb -s <serial> shell input keyevent <code>",
        "adb devices -l",

        // ── wsl / winget ─────────────────────────────────────────────────────
        "wsl -l -v",
        "wsl --status",
        "wsl --shutdown",
        "wsl --update",
        "winget install -e --id <id>",
        "winget upgrade --id <id>",
        "winget list --id <id> --upgrade-available",
        "winget search --id <id> --exact",

        // ── dotnet / gradle ──────────────────────────────────────────────────
        "dotnet build <project> -f <tfm>",
        "dotnet test <project>",
        "dotnet run --project <path> -- <args>",
        "dotnet workload list",
        "dotnet workload restore",
        "./gradlew :runIde",
        "./gradlew :runIde --stacktrace",
        "./gradlew --stop",
        "./gradlew buildPlugin",
        "./gradlew test",
        "./gradlew publishPlugin",

        // ── misc ─────────────────────────────────────────────────────────────
        "curl -v <url>",
        "curl.exe -x http://127.0.0.1:8888 <url> -k -v",
        "Get-Content <file> -Wait -Tail <n>",
    };

    private static readonly Dictionary<string, string[]> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── git ──────────────────────────────────────────────────────────────
        ["git rebase"]      = new[] { "-i", "--continue", "--abort", "--skip", "--onto <newbase>", "--autosquash", "--rebase-merges", "--update-refs" },
        ["git push"]        = new[] { "--set-upstream", "-u", "--force-with-lease", "-f", "--tags", "--delete", "--dry-run", "--no-verify" },
        ["git log"]         = new[] { "--oneline", "--graph", "--all", "-p", "--stat", "--follow", "--name-only", "-n <count>", "--author <name>", "--since <date>", "--grep <pattern>" },
        ["git diff"]        = new[] { "--cached", "--stat", "--name-only", "-w", "--word-diff" },
        ["git show"]        = new[] { "--stat", "--name-only", "-w", "--oneline", "--format <format>" },
        ["git add"]         = new[] { "-p", "-u", "-A", "-N", "--dry-run" },
        ["git commit"]      = new[] { "-m <message>", "--amend", "--no-edit", "--fixup <commit>", "-a" },
        ["git stash push"]  = new[] { "-m <message>", "-u", "-p", "--keep-index" },
        ["git stash pop"]   = Array.Empty<string>(),
        ["git stash list"]  = Array.Empty<string>(),
        ["git stash show"]  = new[] { "-p" },
        ["git branch"]      = new[] { "-a", "-r", "-vv", "--show-current", "--merged", "-d <branch>", "-D <branch>", "-m <name>" },
        ["git reset"]       = new[] { "--hard", "--soft", "--mixed" },
        ["git clone"]       = new[] { "--bare", "--depth <n>", "--branch <name>", "--recurse-submodules" },
        ["git remote"]      = new[] { "-v" },
        ["git remote update"] = Array.Empty<string>(),
        ["git remote prune"]  = new[] { "--dry-run" },
        ["git blame"]       = new[] { "-w", "-C", "-L <start>,<end>" },
        ["git fetch"]       = new[] { "--all", "--prune", "--tags" },
        ["git pull"]        = new[] { "--rebase", "--ff-only" },
        ["git switch"]      = new[] { "-c <branch>", "--detach" },
        ["git checkout"]    = new[] { "-b <branch>" },
        ["git config"]      = new[] { "--edit", "--global", "--list", "--get <key>" },
        ["git cherry-pick"] = new[] { "--continue", "--abort", "-n" },
        ["git clean"]       = new[] { "-n", "-fd", "-fdx" },
        ["git worktree add"]    = new[] { "-b <branch>", "--detach" },
        ["git worktree list"]   = Array.Empty<string>(),
        ["git worktree remove"] = new[] { "--force" },
        ["git worktree prune"]  = Array.Empty<string>(),
        ["git difftool"]    = new[] { "--no-prompt", "--extcmd=<command>", "--dir-diff" },
        ["git init"]        = Array.Empty<string>(),
        ["git prune"]       = Array.Empty<string>(),

        // ── docker compose ───────────────────────────────────────────────────
        ["docker compose"]         = new[] { "-f <file>", "-p <project>" },
        ["docker compose up"]      = new[] { "-d", "--force-recreate", "--build", "--remove-orphans", "--no-deps" },
        ["docker compose down"]    = new[] { "--remove-orphans", "-v" },
        ["docker compose logs"]    = new[] { "--tail <n>", "-f", "--since <time>", "-t" },
        ["docker compose ps"]      = new[] { "-a", "--services" },
        ["docker compose exec"]    = new[] { "-T" },
        ["docker compose cp"]      = Array.Empty<string>(),
        ["docker compose restart"] = Array.Empty<string>(),
        ["docker compose build"]   = new[] { "--no-cache", "--pull" },

        // ── docker ───────────────────────────────────────────────────────────
        ["docker ps"]           = new[] { "-a", "--filter <filter>", "--format <template>", "-q" },
        ["docker run"]          = new[] { "-d", "--rm", "-it", "--name <name>", "-p <host>:<port>", "-e <VAR=value>", "-v <src>:<dst>", "--network <network>" },
        ["docker exec"]         = new[] { "-it", "-i", "-e <VAR=value>" },
        ["docker logs"]         = new[] { "-f", "--tail <n>", "--since <time>" },
        ["docker inspect"]      = new[] { "--format <template>" },
        ["docker restart"]      = Array.Empty<string>(),
        ["docker stop"]         = Array.Empty<string>(),
        ["docker rm"]           = new[] { "-f" },
        ["docker images"]       = Array.Empty<string>(),
        ["docker context ls"]   = Array.Empty<string>(),
        ["docker context use"]  = Array.Empty<string>(),
        ["docker version"]      = Array.Empty<string>(),
        ["docker info"]         = Array.Empty<string>(),
        ["docker system prune"] = new[] { "-a", "--volumes" },

        // ── devenv (shapes seen in history) ──────────────────────────────────
        ["devenv start"]          = new[] { "--remote-db" },
        ["devenv stop"]           = Array.Empty<string>(),
        ["devenv status"]         = Array.Empty<string>(),
        ["devenv clean"]          = Array.Empty<string>(),
        ["devenv reinstall"]      = Array.Empty<string>(),
        ["devenv backend up"]     = new[] { "--remote-db", "--allow-dev-migrations" },
        ["devenv backend down"]   = Array.Empty<string>(),
        ["devenv scanner scan"]   = new[] { "--device <id>", "--allow-remote", "--member <id>", "--friend-pass", "--invalid", "--expired", "--screenshot" },
        ["devenv user link"]      = new[] { "--member <id>", "--b2c <guid>" },
        ["devenv logs capture"]   = new[] { "--since <duration>" },
        ["devenv build mobile"]   = new[] { "--abi <abi>", "--deploy <mode>", "--install" },
        ["devenv build joiners"]  = Array.Empty<string>(),
        ["devenv app install"]    = Array.Empty<string>(),
        ["devenv db sql"]         = Array.Empty<string>(),
        ["devenv db ui"]          = new[] { "--off" },
        ["devenv setup ios-sim"]  = Array.Empty<string>(),
        ["devenv setup shell"]    = Array.Empty<string>(),
        ["devenv mac doctor"]     = Array.Empty<string>(),
        ["devenv mac fix"]        = Array.Empty<string>(),

        // ── claude ───────────────────────────────────────────────────────────
        ["claude"] = new[] { "--resume", "--continue", "-p <prompt>", "--model <model>" },

        // ── adb ──────────────────────────────────────────────────────────────
        ["adb"]                           = new[] { "-s <serial>", "-d", "-e" },
        ["adb devices"]                   = new[] { "-l" },
        ["adb logcat"]                    = new[] { "-c", "-d", "-s <tag>" },
        ["adb push"]                      = Array.Empty<string>(),
        ["adb install"]                   = new[] { "-r", "-g" },
        ["adb uninstall"]                 = Array.Empty<string>(),
        ["adb root"]                      = Array.Empty<string>(),
        ["adb emu kill"]                  = Array.Empty<string>(),
        ["adb shell am force-stop"]       = Array.Empty<string>(),
        ["adb shell am start"]            = new[] { "-a <action>", "-t <mime>", "-d <uri>" },
        ["adb shell pm list packages"]    = new[] { "-3", "-f" },
        ["adb shell settings get global"] = Array.Empty<string>(),
        ["adb shell settings put global"] = Array.Empty<string>(),
        ["adb shell input keyevent"]      = Array.Empty<string>(),
        ["adb shell pidof"]               = Array.Empty<string>(),

        // ── wsl / winget ─────────────────────────────────────────────────────
        ["wsl"]            = new[] { "-l", "-v", "--status", "--shutdown", "--update", "--install", "--version", "-d <distro>" },
        ["winget install"] = new[] { "-e", "--id <id>", "--source winget" },
        ["winget upgrade"] = new[] { "--id <id>", "--all" },
        ["winget list"]    = new[] { "--id <id>", "--upgrade-available" },
        ["winget search"]  = new[] { "--id <id>", "--exact" },

        // ── dotnet / gradle ──────────────────────────────────────────────────
        ["dotnet build"]            = new[] { "-c <configuration>", "-f <tfm>", "--no-restore" },
        ["dotnet test"]             = new[] { "--filter <expression>", "--no-build", "-c <configuration>" },
        ["dotnet run"]              = new[] { "--project <path>", "-c <configuration>" },
        ["dotnet workload list"]    = Array.Empty<string>(),
        ["dotnet workload install"] = Array.Empty<string>(),
        ["dotnet workload restore"] = Array.Empty<string>(),
        ["./gradlew"]               = new[] { "--stacktrace", "--stop", "--info", "--offline" },
        ["./gradlew :runIde"]       = new[] { "--stacktrace" },
        ["./gradlew runIde"]        = new[] { "--stacktrace" },
        ["./gradlew buildPlugin"]   = new[] { "--stacktrace" },
        ["./gradlew test"]          = new[] { "--stacktrace" },
        ["./gradlew publishPlugin"] = new[] { "--stacktrace" },

        // ── misc ─────────────────────────────────────────────────────────────
        ["curl"]        = new[] { "-v", "-k", "-s", "-L", "-x <proxy>", "-H <header>", "-X <method>", "-d <data>", "-o <file>" },
        ["curl.exe"]    = new[] { "-v", "-k", "-s", "-L", "-x <proxy>", "-H <header>", "-X <method>", "-d <data>", "-o <file>" },
        ["Get-Content"] = new[] { "-Wait", "-Tail <n>" },
    };

    // Profile aliases that forward to a git subcommand — they share its flags.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gs"] = "git show",   ["gits"] = "git show",
        ["gl"] = "git log",    ["gitl"] = "git log",
        ["gdf"] = "git diff",
        ["gita"] = "git add",  ["gitau"] = "git add",
        ["gitca"] = "git commit",
        ["gst"] = "git stash push", ["stash"] = "git stash push",
    };

    private static readonly Regex Tokenizer = new("\"[^\"]*\"?|'[^']*'?|\\S+", RegexOptions.Compiled);

    // Parsed once: template tokens and the set of known roots.
    private static readonly string[][] TemplateTokens =
        Templates.Select(t => Tokenizer.Matches(t).Select(m => m.Value).ToArray()).ToArray();

    private static bool IsPlaceholder(string token) => token.StartsWith('<');

    private static string FlagName(string spec) => spec.Split(' ')[0];

    // ".\gradlew" and "./gradlew" are the same command.
    private static string NormalizeCommand(string token) =>
        token.StartsWith(".\\") ? "./" + token[2..] : token;

    private static bool IsKnownPath(string path) =>
        Flags.ContainsKey(path) || Flags.Keys.Any(k => k.StartsWith(path + " ", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Children(string path)
    {
        var prefix = path + " ";
        return Flags.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split(' ')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // Flags of this path and its ancestors ("docker compose up" also sees -f).
    private static IEnumerable<string> FlagsInScope(string path)
    {
        var words = path.Split(' ');
        for (var n = words.Length; n >= 1; n--)
            if (Flags.TryGetValue(string.Join(" ", words.Take(n)), out var specs))
                foreach (var spec in specs) yield return spec;
    }

    public static void Collect(string text, ICollection<string> output, int max)
    {
        var typed = text.TrimEnd();
        var matches = Tokenizer.Matches(typed);
        if (matches.Count == 0) return;
        var tokens = matches.Select(m => m.Value).ToArray();
        if (tokens.Any(t => t is "|" or ";" or "&&" or "||")) return; // pipelines: leave alone

        var seen = new HashSet<string>(output, StringComparer.OrdinalIgnoreCase);
        void Add(string s)
        {
            if (output.Count < max && !s.Equals(typed, StringComparison.OrdinalIgnoreCase) && seen.Add(s))
                output.Add(s);
        }

        var last = tokens[^1];
        var head = typed[..matches[^1].Index]; // everything before the last token
        var command = NormalizeCommand(tokens[0]);

        // 1. Templates — whole command lines you've typed before.
        foreach (var template in TemplateTokens)
        {
            if (tokens.Length > template.Length) continue;
            var ok = true; var completedLast = last;
            for (var i = 0; i < tokens.Length && ok; i++)
            {
                var u = i == 0 ? command : tokens[i];
                var t = template[i];
                if (IsPlaceholder(t) || u.Equals(t, StringComparison.OrdinalIgnoreCase)) continue;
                if (i == tokens.Length - 1 && t.StartsWith(u, StringComparison.OrdinalIgnoreCase) && (i > 0 || u.Length >= 3))
                    completedLast = t;
                else
                    ok = false;
            }
            if (!ok) continue;
            var rest = string.Join(" ", template.Skip(tokens.Length));
            Add((head + completedLast + (rest.Length > 0 ? " " + rest : "")).TrimEnd());
        }

        // 2. Subcommands and flags.
        string path;
        var aliased = Aliases.TryGetValue(tokens[0], out var aliasPath);
        if (aliased) path = aliasPath!;
        else if (IsKnownPath(command)) path = command;
        else return;

        // Aliases are complete commands on their own; only hint flags once a '-' is typed.
        if (aliased && !last.StartsWith('-')) return;

        // Walk the typed words, descending into subcommands and skipping flag values.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRole = tokens.Length == 1 ? "command" : "value";
        string? pendingValue = null; // placeholder the last flag still expects
        for (var i = 1; i < tokens.Length; i++)
        {
            var u = tokens[i];
            pendingValue = null;
            if (u.StartsWith('-'))
            {
                var spec = FlagsInScope(path).FirstOrDefault(s => FlagName(s).Equals(u, StringComparison.OrdinalIgnoreCase));
                if (spec is null) { lastRole = "partial-flag"; continue; }
                used.Add(u);
                lastRole = "flag";
                var parts = spec.Split(' ', 2);
                if (parts.Length == 2)
                {
                    if (i + 1 < tokens.Length) { i++; lastRole = "value"; }
                    else pendingValue = parts[1];
                }
            }
            else if (IsKnownPath(path + " " + u))
            {
                path += " " + u;
                lastRole = "command";
            }
            else lastRole = i == tokens.Length - 1 ? "partial-word" : "value";
        }

        switch (lastRole)
        {
            case "flag" when pendingValue != null:
                Add(typed + " " + pendingValue);
                break;

            case "partial-flag":
                foreach (var spec in FlagsInScope(path))
                    if (FlagName(spec).StartsWith(last, StringComparison.OrdinalIgnoreCase) && !used.Contains(FlagName(spec)))
                        Add(head + spec);
                break;

            case "partial-word":
                var anyChild = false;
                foreach (var child in Children(path))
                    if (child.StartsWith(last, StringComparison.OrdinalIgnoreCase)) { Add(head + child); anyChild = true; }
                if (!anyChild && Flags.TryGetValue(path, out var afterValue)) // it was a value, e.g. a branch
                    foreach (var spec in afterValue)
                        if (!used.Contains(FlagName(spec)))
                            Add(typed + " " + spec);
                break;

            default: // a complete word: offer the next level, then unused flags
                foreach (var child in Children(path))
                    Add(typed + " " + child);
                if (Flags.TryGetValue(path, out var specs))
                    foreach (var spec in specs)
                        if (!used.Contains(FlagName(spec)))
                            Add(typed + " " + spec);
                break;
        }
    }
}
