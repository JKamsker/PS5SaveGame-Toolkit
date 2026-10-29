using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsEnvelopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrappedDatabaseDocumentRemainsReadOnly(bool astroneer)
    {
        byte[] wrapped = GvasEnvelopeTests.Wrap(HogwartsInventoryFixture.Create().ReadAllBytes(), astroneer);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("wrapped.sav", wrapped, "PPSA01603"));

        Assert.False(document.CanEdit);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query(HogwartsInventoryFixture.Collection, new() { Limit = 2 });
        Assert.DoesNotContain(page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), field => !field.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new("unknown", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(wrapped, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
