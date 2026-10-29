using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class WriteSafetyTests
{
    private const string WarningOpening = "Not validated on console. The game may reject or overwrite the edited save.";

    [Fact]
    public void EditableFieldsAndStackPreviewsKeepCommonWarningAndSpecificQualifications()
    {
        using var opened = new Cyberpunk2077Plugin().Open(ProgressionSampleTests.Files()[0]);
        var document = Assert.IsType<CyberpunkDocument>(opened);
        var editable = CyberpunkSampleTests.ExpandAll(document).Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.NotEmpty(editable);
        Assert.All(editable, field => Assert.StartsWith(WarningOpening, field.Warning));
        Assert.Contains(editable, field => field.Warning!.Contains("Existing perks and point balances are preserved.", StringComparison.Ordinal));
        Assert.Contains(editable, field => field.Warning!.Contains("Rewards, unlocks, perks and XP curves are not recalculated", StringComparison.Ordinal));
        Assert.Contains(editable, field => field.Warning!.Contains("not a verified game limit", StringComparison.Ordinal));

        var facts = document.Query("facts", new());
        Assert.NotEmpty(facts.Rows);
        Assert.All(facts.Rows, row =>
        {
            var field = Assert.IsType<FieldNode>(row.Cells["value"]);
            Assert.StartsWith(WarningOpening, field.Warning);
            Assert.Contains("quest stages, callbacks and related facts are not updated", field.Warning);
            Assert.Equal(Confidence.Unknown, field.Confidence);
        });

        foreach (var action in new[] { StackEditor.SetTarget, StackEditor.Refill })
        {
            var parameters = action == StackEditor.SetTarget
                ? new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(700L) } : [];
            var plan = document.PlanAction(action, parameters, new ActionScope());
            Assert.Empty(plan.Errors);
            var warning = Assert.Single(plan.Warnings);
            Assert.StartsWith(WarningOpening, warning);
            Assert.Contains("CSAV compression, lengths and offsets are repaired", warning);
            Assert.Contains("integrity checks in opaque data and console wrappers are unknown", warning);
            Assert.Contains("Charges/replenishment and gameplay caps are unverified", warning);
        }
    }
}
