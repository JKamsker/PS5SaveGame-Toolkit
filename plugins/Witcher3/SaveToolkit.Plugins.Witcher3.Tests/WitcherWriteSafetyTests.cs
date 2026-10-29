using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherWriteSafetyTests
{
    [Fact]
    public void WriteWarningsStateConsoleRiskAndRetainFormatSpecificQualifications()
    {
        const string consoleRisk = "Not validated on console. The game may reject or overwrite the edited save.";
        Assert.StartsWith(consoleRisk, WriteFields.ConsoleWarning, StringComparison.Ordinal);
        Assert.Contains("The game may rebuild dependent state.", WriteFields.ConsoleWarning, StringComparison.Ordinal);
        Assert.All(new[] { WriteFields.DurabilityWarning, WriteFields.SkillWarning, WriteFields.GwentWarning,
            RepairPlanning.Action.Description!, RefillPlanning.Action.Description! },
            warning => Assert.StartsWith(consoleRisk, warning, StringComparison.Ordinal));
        Assert.Contains("The -1 sentinel is preserved.", WriteFields.DurabilityWarning, StringComparison.Ordinal);
        Assert.Contains("not a proven PS5 limit", WriteFields.DurabilityWarning, StringComparison.Ordinal);
        Assert.Contains("not an in-game purchase or respec", WriteFields.SkillWarning, StringComparison.Ordinal);
        Assert.Contains("decreasing a count may invalidate a deck", WriteFields.GwentWarning, StringComparison.Ordinal);
        Assert.Contains("not a proven legal cap", WriteFields.GwentWarning, StringComparison.Ordinal);
        Assert.Contains("only up to their imported quantity", RefillPlanning.Action.Description!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateFieldEditsAreRejectedBeforeChangingValuesOrHistory(bool previouslyEdited)
    {
        var input = SyntheticPlayerSave.Create();
        var file = WitcherTestFiles.Import(input);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("duplicate-writes", file, plugin, document);
        _ = session.BuildView();
        if (previouslyEdited)
        {
            Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(300))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        }
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var state = session.State;

        var result = session.ApplyEdits([
            EnabledWriteFixture.Incoming("player/level", FieldValue.Of(2)),
            EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(500)),
            EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(600)),
        ], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.ValueId == "inventory/1/quantity");
        Assert.Equal(1, document.GetValue("player/level").AsInt32());
        Assert.Equal(previouslyEdited ? 300 : 250, document.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(state, session.State);
        Assert.Equal(previouslyEdited, session.Undo().Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
