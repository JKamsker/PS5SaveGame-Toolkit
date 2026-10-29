using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasWriteSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedEmbeddedNulStringCanBeRestoredWithoutPermittingNewNulStrings(bool wide)
    {
        const string imported = "before\0suffix";
        byte[] bytes = GvasFixture.Build(writer => GvasFixture.Property(writer, "Name", "StrProperty",
            GvasFixture.Body(body => GvasFixture.String(body, imported, wide))));
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("text.sav", bytes));
        var field = Assert.Single(GvasFormatTests.Expand(document));
        var edited = document.ApplyEdits([new(field.ValueId, FieldValue.Of("renamed"))]);
        Assert.True(edited.Succeeded);
        byte[] renamed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of("different\0text"))]).Succeeded);
        Assert.Equal(renamed, Assert.Single(document.Serialize()).Content.ToArray());

        var restored = document.ApplyEdits([new(field.ValueId, Assert.Single(edited.Changes).OldValue)]);

        Assert.True(restored.Succeeded, string.Join("; ", restored.Errors.Select(error => error.Message)));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of("renamed"))]).Succeeded);
        Assert.Equal(renamed, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
