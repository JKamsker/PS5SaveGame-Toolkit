using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class EditBookkeepingTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void ConsecutiveDirectEdits_RetainChangesAndRollbackWithoutRebuilding(string title)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var document = (GearboxDocument)new GearboxPlugin().Open(file);
        _ = document.BuildView();
        var first = document.ApplyEdits([new("currency/618814354", FieldValue.Of(10))]);
        Assert.True(first.Succeeded);
        var second = document.ApplyEdits([new("currency/618814354", FieldValue.Of(11))]);
        Assert.True(second.Succeeded);
        var change = Assert.Single(second.Changes, c => c.ValueId == "currency/618814354");
        Assert.Equal(FieldValue.Of(10), change.OldValue);
        Assert.Equal(FieldValue.Of(11), change.NewValue);
        Assert.NotNull(second.Revert);
        Assert.True(second.StructureChanged);
        var bytes = document.Serialize().Single().Content.ToArray();
        var rejected = document.ApplyEdits([new("currency/618814354", FieldValue.Of(12)), new("character/level", FieldValue.Of(0))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(second.Revert);
        _ = document.BuildView();
        Assert.Equal(FieldValue.Of(10), document.GetValue("currency/618814354"));
    }
}
