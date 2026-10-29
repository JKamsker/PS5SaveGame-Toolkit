using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HonestLabelTests
{
    [Fact]
    public void GenericScalarOutcomeRetainsConsoleAndGameplayWarnings()
    {
        var bytes = GvasFixture.RepeatedIntegers(1);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("generic.sav", bytes));
        var field = Assert.Single(GvasFormatTests.Expand(document));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var unchanged = document.ApplyEdits([new(field.ValueId, field.Value)]);
        Assert.True(unchanged.Succeeded);
        Assert.All(unchanged.Changes, change => Assert.Equal(change.OldValue, change.NewValue));
        Assert.Empty(unchanged.Warnings);

        var changed = document.ApplyEdits([new(field.ValueId, FieldValue.Of(3))]);

        Assert.True(changed.Succeeded);
        Assert.Contains(field.Warning!, changed.Warnings);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void GenericStructuralOutcomeRetainsConsoleAndDependencyWarnings()
    {
        var bytes = GvasFixture.Nested();
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", bytes));
        _ = document.BuildView();

        var changed = document.InvokeAction("gvas/structure/remove-last/property/0/1", new Dictionary<string, FieldValue>());

        Assert.True(changed.StructureChanged);
        Assert.Contains(changed.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
        Assert.Contains(changed.Warnings, warning => warning.Contains("Game constraints and effects are unverified", StringComparison.Ordinal));
        _ = document.RestoreMemento(changed.Revert!);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
