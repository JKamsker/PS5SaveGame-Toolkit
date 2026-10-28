using SaveToolkit.Plugins.UnrealGvas;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasStructuralTests
{
    [Theory]
    [InlineData(1, "Map", 2)]
    [InlineData(2, "Set", 1)]
    [InlineData(3, "Bools", 1)]
    public void PrimitiveContainerRemovalIsBoundedReparsedAndUndoable(int propertyIndex, string propertyName, int expectedChildren)
    {
        var original = GvasFixture.Nested();
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("nested.sav", original));
        _ = document.BuildView();
        var actionId = $"gvas/structure/remove-last/property/0/{propertyIndex}";

        var outcome = document.InvokeAction(actionId, new Dictionary<string, FieldValue>());

        Assert.True(outcome.StructureChanged, outcome.Message);
        Assert.NotNull(outcome.Revert);
        byte[] changed = Assert.Single(document.Serialize()).Content.ToArray();
        var items = GvasFile.Parse(changed).Properties.Single().Children.Single(node => node.Name == propertyName);
        Assert.Equal(expectedChildren, items.Children.Count);
        var redo = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
