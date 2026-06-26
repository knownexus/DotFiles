using System;
using System.Collections.Generic;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Prediction;
using System.Threading;

public sealed class SignatureHintPredictor : ICommandPredictor
{
    public Guid   Id          { get; } = new Guid("3d5b8f9a-1c2e-4d6f-a7b8-9c0d1e2f3a4b");
    public string Name        => "SignatureHints";
    public string Description => "Inline argument placeholder hints for custom shell functions";
    public Dictionary<string, string>? FunctionsToDefine => null;

    // Every known alias — including no-param ones — so prefix matching works for all of them.
    private static readonly string[] AllCommands = new[]
    {
        // File system
        "ls", "l", "ll", "la", "lla", "cd", "mcd", "tre", "rm", "rmr", "md",
        "touch", "open", "v", "vb", "odx", "odx1", "remspace",
        // Navigation
        "cdh", "go-repos", "rp", "rpa", "rpam", "rpn", "rpnm", "rpc", "rpp", "rpm", "rpnc",
        // Search
        "g", "g1", "g2", "rgs", "rgs1", "rgs2", "fch", "fchs", "psgrep", "which",
        // Shell
        "sb", "r", "fr", "path", "brk", "aliases", "gitaliases", "allaliases", "build-predictor",
        // Scripts
        "merge-explorer", "grev",
        // Git — project
        "make-project",
        // Git — status / info
        "gstat", "g!", "git!", "gbl", "gs", "gits", "gitsn",
        "gl", "gitl", "gl2", "gitl2", "gitlg", "gitlg2",
        // Git — staging / committing
        "ga", "gita", "gau", "gitau", "gc", "git-commit", "gitca", "gca",
        "gitcm", "uncommit", "gdf", "gdfs", "gdf1",
        "gres", "gitres", "gress", "gitrmc", "ignoreme", "dontignoreme", "localignore",
        "gar", "gaw", "gaw1",
        // Git — branch / checkout
        "gitb", "gitc", "gitcb", "gitcd", "grh",
        // Git — fetch / pull
        "gitf", "gitpl",
        // Git — push / remote
        "gitcp", "pushdr", "pushdr-f", "gclean", "gpf", "gitfp", "gitpf", "gp1",
        "gru", "grpo", "giturl", "gitremote-reset", "remote!",
        // Git — stash
        "gp", "git-stash-pop", "gst", "gstl", "stash", "stashp",
        // Git — rebase
        "gri", "grim", "rebaseon", "grof",
        "grd", "grm", "gro", "groi", "grod",
        "gitra", "gitra2", "gitrc", "groot", "gup",
        // Git — reverted
        "grev-clean", "grev-scan",
        // Git — misc
        "rs", "update", "updated",
        // Git — worktree
        "wt-go", "wt-list", "wt-feature", "wt-fix", "wt-c",
        "wt-done", "wt-done-f", "wt!", "wt-prune", "wt-setup", "wt-workspace", "fix-fetch-refspecs",
    };

    private static string[]? GetSig(string cmd) => cmd switch
    {
        // ── File system ──────────────────────────────────────────────────────────
        "ls" or "l" or "ll"         => new[] { "[path]", "[-Recurse]", "[-Filter <glob>]" },
        "la" or "lla"               => new[] { "[path]", "[-Recurse]", "[-Filter <glob>]" },
        "cd"                        => new[] { "<path>" },
        "mcd"                       => new[] { "<path>" },
        "tre"                       => new[] { "[path]" },
        "rm"  or "rmr"              => new[] { "<path>" },
        "md"                        => new[] { "<path>" },
        "touch"                     => new[] { "<path>" },
        "open"                      => new[] { "<path>" },
        "v"                         => new[] { "<file>" },
        "odx" or "odx1"             => new[] { "<file>" },
        "remspace"                  => new[] { "<path>" },

        // ── Search ───────────────────────────────────────────────────────────────
        "g"   or "g1"  or "g2"      => new[] { "<pattern>", "[path]" },
        "rgs" or "rgs1" or "rgs2"   => new[] { "<pattern>" },
        "fch" or "fchs"             => new[] { "<pattern>" },
        "psgrep"                    => new[] { "<name>" },
        "which"                     => new[] { "<command>" },

        // ── Shell utilities ──────────────────────────────────────────────────────
        "fr"                        => new[] { "<find>", "<replace>", "[path]" },

        // ── Git — project ────────────────────────────────────────────────────────
        "make-project"              => new[] { "<name>", "[-Licence MIT|GPL-2|GPL-3|Apache-2|LGPL-2.1]" },

        // ── Git — status / info ──────────────────────────────────────────────────
        "gbl"                       => new[] { "<file>" },
        "gs"  or "gits"             => new[] { "[commit]" },
        "gitsn"                     => new[] { "[commit]" },
        "gl"  or "gitl"
            or "gl2" or "gitl2"
            or "gitlg" or "gitlg2"  => new[] { "[options]" },

        // ── Git — staging / committing ───────────────────────────────────────────
        "ga"  or "gita"
            or "gau" or "gitau"     => new[] { "<file>" },
        "gc"  or "git-commit"       => new[] { "[-m <message>]" },
        "gitcm"                     => new[] { "<message>" },
        "gdf"                       => new[] { "[commit|file]" },
        "gres" or "gitres"
            or "gress" or "gitrmc"  => new[] { "<file>" },
        "ignoreme"
            or "dontignoreme"       => new[] { "<file>" },
        "localignore"               => new[] { "<pattern> [pattern ...]" },
        "gar"                       => new[] { "[repo-name]" },
        "gaw1"                      => new[] { "<include-pattern>" },

        // ── Git — branch / checkout ──────────────────────────────────────────────
        "gitb"                      => new[] { "[branch]" },
        "gitc"                      => new[] { "<branch>" },
        "gitcb"                     => new[] { "<branch>" },
        "grh"                       => new[] { "[commit]" },

        // ── Git — fetch / pull ───────────────────────────────────────────────────
        "gitf"                      => new[] { "[remote]" },
        "gitpl"                     => new[] { "[remote]", "[branch]" },

        // ── Git — push / remote ──────────────────────────────────────────────────
        "gitcp"                     => new[] { "<commit>" },
        "pushdr"                    => new[] { "<remote-branch>" },
        "pushdr-f"                  => new[] { "<remote-branch>" },
        "gclean"                    => new[] { "[path]" },
        "grof"                      => new[] { "<branch>" },

        // ── Git — rebase ─────────────────────────────────────────────────────────
        "gri"  or "grim"            => new[] { "[target|HEAD~N]" },
        "rebaseon"                  => new[] { "<branch-fragment>" },

        // ── Git — reverted changes ───────────────────────────────────────────────
        "grev"                      => new[] { "<commitA>", "<commitB>" },
        "grev-clean"                => new[] { "<commitA>", "<commitB>", "[-DryRun]" },
        "grev-scan"                 => new[] { "[range]" },

        // ── Git — stash ──────────────────────────────────────────────────────────
        "gst" or "stash"            => new[] { "[message]" },

        // ── Git — worktree ───────────────────────────────────────────────────────
        "wt-go" or "wt-done"
            or "wt-done-f"          => new[] { "<name>" },
        "wt-feature"                => new[] { "<ticket-id>", "<desc>", "[-Base <branch>]" },
        "wt-fix"                    => new[] { "<ticket-id>", "<desc>", "[-Base <branch>]" },
        "wt-c"                      => new[] { "<branch>" },
        "wt-setup"                  => new[] { "<branches>", "[-Name <n>]", "[-CloneUrl <url>]" },
        "wt-workspace"              => new[] { "-Path", "<path>", "-Repos", "<url,...>", "-Branches", "<branch,...>" },
        "fix-fetch-refspecs"        => new[] { "[root]" },

        _                           => null,
    };

    public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback) => false;

    public SuggestionPackage GetSuggestion(
        PredictionClient client, PredictionContext context, CancellationToken token)
    {
        var text  = context.InputAst.Extent.Text;
        var parts = text.TrimEnd().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0) return default;

        var cmd = parts[0];
        var sig = GetSig(cmd);

        // Exact command match — append remaining argument hints.
        if (sig != null)
        {
            var provided = parts.Length - 1;
            if (provided >= sig.Length) return default;
            var suggestion = text.TrimEnd();
            for (var i = provided; i < sig.Length; i++)
                suggestion += " " + sig[i];
            return new SuggestionPackage(new List<PredictiveSuggestion> { new PredictiveSuggestion(suggestion) });
        }

        // Partial prefix — suggest all known commands that start with the typed text.
        // Only fires when the user has typed a single word (no space yet).
        if (parts.Length == 1 && cmd.Length >= 2)
        {
            var suggestions = new List<PredictiveSuggestion>();
            foreach (var known in AllCommands)
            {
                if (!known.StartsWith(cmd, StringComparison.OrdinalIgnoreCase) || known == cmd)
                    continue;

                var knownSig = GetSig(known);
                var full = knownSig != null && knownSig.Length > 0
                    ? known + " " + string.Join(" ", knownSig)
                    : known;

                suggestions.Add(new PredictiveSuggestion(full));
                if (suggestions.Count >= 5) break;
            }

            if (suggestions.Count > 0)
                return new SuggestionPackage(suggestions);
        }

        return default;
    }

    public void OnCommandLineAccepted (PredictionClient client, IReadOnlyList<string> history)           { }
    public void OnSuggestionDisplayed (PredictionClient client, uint session, int countOrIndex)          { }
    public void OnSuggestionAccepted  (PredictionClient client, uint session, string acceptedSuggestion) { }
    public void OnCommandLineExecuted (PredictionClient client, string commandLine, bool success)        { }
}
