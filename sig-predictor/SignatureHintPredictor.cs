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

    // Add cases here for any command you want hints for.
    private static string[]? GetSig(string cmd) => cmd switch
    {
        "grev" => new[] { "<commitA>", "<commitB>" },
        _      => null,
    };

    public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback) => false;

    public SuggestionPackage GetSuggestion(
        PredictionClient client, PredictionContext context, CancellationToken token)
    {
        var text  = context.InputAst.Extent.Text;
        var parts = text.TrimEnd().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0) return default;
        var sig = GetSig(parts[0]);
        if (sig is null) return default;

        var provided = parts.Length - 1;
        if (provided >= sig.Length) return default;

        var suggestion = text.TrimEnd();
        for (var i = provided; i < sig.Length; i++)
            suggestion += " " + sig[i];

        return new SuggestionPackage(new List<PredictiveSuggestion> { new PredictiveSuggestion(suggestion) });
    }

    public void OnCommandLineAccepted (PredictionClient client, IReadOnlyList<string> history)           { }
    public void OnSuggestionDisplayed (PredictionClient client, uint session, int countOrIndex)          { }
    public void OnSuggestionAccepted  (PredictionClient client, uint session, string acceptedSuggestion) { }
    public void OnCommandLineExecuted (PredictionClient client, string commandLine, bool success)        { }
}
