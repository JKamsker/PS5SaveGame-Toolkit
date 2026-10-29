using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class HonestLabelAuditTests
{
    [Fact]
    public void VerifiedEvidence_RequiresNonWhitespaceDescription_EvenForReadOnlyFields()
    {
        var field = Value() with { Spec = IntegerSpec.Int32 with { ReadOnly = true }, Confidence = Confidence.Verified, Description = " " };
        var finding = Assert.Single(HonestLabelAudit.Inspect(field, PluginMaturity.ReadOnly, false));
        Assert.Equal(HonestLabelRule.VerifiedEvidence, finding.Rule);
        Assert.Empty(HonestLabelAudit.Inspect(field with { Description = "Decoded int32 storage; meaning not verified." }, PluginMaturity.ReadOnly, false));
    }

    [Fact]
    public void WritableControl_ReportsMaturityReadOnlyAndWarningIndependently()
    {
        var findings = HonestLabelAudit.Inspect(Value(), PluginMaturity.ReadOnly, false);
        Assert.Equal(new[] { HonestLabelRule.WriteMaturity, HonestLabelRule.ReadOnlyDocument, HonestLabelRule.WriteWarning }, findings.Select(finding => finding.Rule));
        Assert.Empty(HonestLabelAudit.Inspect(Value() with { Warning = "Console acceptance unverified." }, PluginMaturity.Experimental, true));
    }

    [Fact]
    public void Action_UsesPluginMaturityAndChecksItsOwnWarningAndParameterEvidence()
    {
        var action = new ActionNode("restore", "Restore imported")
        {
            Parameters = [Value() with { Confidence = Confidence.Verified }],
        };
        var findings = HonestLabelAudit.Inspect(action, PluginMaturity.Experimental, true);
        Assert.Equal(new[] { HonestLabelRule.WriteWarning, HonestLabelRule.VerifiedEvidence }, findings.Select(finding => finding.Rule));
        Assert.Empty(HonestLabelAudit.Inspect(action with
        {
            Confirm = "Experimental write; console acceptance unverified.",
            Parameters = [Value() with { Confidence = Confidence.Inferred }],
        }, PluginMaturity.Experimental, true));
    }

    [Fact]
    public void InspectionParameters_AreInputsRatherThanWritableDocumentFields()
    {
        var action = new ActionNode("inspect", "Inspect") { Mutates = false, Parameters = [Value()] };
        Assert.Empty(HonestLabelAudit.Inspect(action, PluginMaturity.ReadOnly, false));
    }

    [Fact]
    public void OrdinaryActionDescription_IsReportedAsUnrecognizedMetadataRatherThanMissingWarningText()
    {
        var action = new ActionNode("write", "Write") { Description = "Experimental; not validated on console." };
        var finding = Assert.Single(HonestLabelAudit.Inspect(action, PluginMaturity.Experimental, true));
        Assert.Equal(HonestLabelRule.WriteWarning, finding.Rule);
        Assert.Contains("metadata recognized", finding.Message);
        Assert.Contains("review ordinary descriptions", finding.Message);
    }

    [Fact]
    public void WarningTone_RequiresText_AndStableMaturityRemainsAdvisory()
    {
        var action = new ActionNode("write", "Write") { Tone = Tone.Warning, Description = " " };
        Assert.Equal(HonestLabelRule.WriteWarning, Assert.Single(HonestLabelAudit.Inspect(action, PluginMaturity.Experimental, true)).Rule);
        var warned = action with { Description = "Known in-game validation is still limited to one build." };
        Assert.Equal(HonestLabelRule.WriteMaturity, Assert.Single(HonestLabelAudit.Inspect(warned, PluginMaturity.Stable, true)).Rule);
    }

    private static FieldNode Value() => new("value", "Raw value", IntegerSpec.Int32, FieldValue.Of(1));
}
