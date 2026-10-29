using SaveToolkit.Sdk;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>Reports mechanical checks over the same bounded presentations as ordinary conformance.</summary>
public static class HonestLabelConformance
{
    /// <summary>Checks a real or synthetic document; only explicitly enforced, unexplained findings fail.</summary>
    public static void Check(ISaveDocument document, string path, PluginMaturity maturity,
        int lazyBudget, int queryBudget, Func<HonestLabelFinding, string?>? exceptionReason = null, bool enforce = false)
    {
        var findings = new HashSet<HonestLabelFinding>();
        var walker = new ConformanceViewWalker(document, path, lazyBudget, queryBudget, node =>
        {
            foreach (var finding in HonestLabelAudit.Inspect(node, maturity, document.CanEdit)) findings.Add(finding);
        });
        _ = walker.Walk();
        var unresolved = new List<string>();
        foreach (var finding in findings)
        {
            var reason = exceptionReason?.Invoke(finding);
            var diagnostic = $"{path}: {finding.Rule} '{finding.ControlId}': {finding.Message}";
            if (string.IsNullOrWhiteSpace(reason)) unresolved.Add(diagnostic);
            else diagnostic += " Exception: " + reason;
            TestContext.Current.TestOutputHelper?.WriteLine("Honest-label advisory: " + diagnostic);
        }
        Assert.True(!enforce || unresolved.Count == 0, string.Join(Environment.NewLine, unresolved));
    }
}
