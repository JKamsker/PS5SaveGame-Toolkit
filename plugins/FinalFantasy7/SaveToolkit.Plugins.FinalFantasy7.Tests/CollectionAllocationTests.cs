using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CollectionAllocationTests
{
    [Theory]
    [InlineData("inventory", null, null, 1_000_000)]
    [InlineData("inventory", "1", null, 1_400_000)]
    [InlineData("inventory", null, "quantity", 1_000_000)]
    [InlineData("materia", null, null, 750_000)]
    [InlineData("materia", "1", null, 1_000_000)]
    [InlineData("materia", null, "quantity", 750_000)]
    public void SlotQueriesAllocateForNativeModelsAndRequestedRows(string id, string? text, string? sort, long budget)
    {
        using var document = new FinalFantasy7Plugin().Open(Input());
        var collections = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { Text = text, SortColumn = sort, Limit = 50, IncludeEmpty = true };
        _ = collections.Query(id, query); // Exclude JIT, initial reference strings and first-time bindings.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = collections.Query(id, query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(50, page.Rows.Count);
        Assert.InRange(allocated, 1, budget);
    }

    [Fact]
    public void QuantityQueriesReadEditsAndRestoredValuesWithoutStaleProjectionCaches()
    {
        using var document = new FinalFantasy7Plugin().Open(Input());
        var collections = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { SortColumn = "quantity", Descending = true, Limit = 1 };
        var original = collections.Query("inventory", query);
        Assert.Equal("inventory/0", Assert.Single(original.Rows).Key);
        Assert.False(document.ApplyEdits([new("inventory/1/quantity", FieldValue.Of(8L))]).Succeeded);
        // Register a second, initially off-page scalar before editing it.
        _ = collections.Query("inventory", new() { Filters = new Dictionary<string, string> { ["slot"] = "1" } });
        Assert.True(document.ApplyEdits([new("inventory/1/quantity", FieldValue.Of(8L))]).Succeeded);
        Assert.Equal("inventory/1", Assert.Single(collections.Query("inventory", query).Rows).Key);
        Assert.Equal("8", ((FieldNode)Assert.Single(collections.Query("inventory", new()
        {
            Filters = new Dictionary<string, string> { ["quantity"] = "8" },
        }).Rows).Cells["quantity"]).Value.AsInteger().ToString());
        Assert.True(document.ApplyEdits([new("inventory/1/quantity", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal("inventory/0", Assert.Single(collections.Query("inventory", query).Rows).Key);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile Input()
    {
        const string title = "PPSA08664";
        var bytes = SyntheticGameplay.Create(title).Bytes;
        var schema = Ff7Schema.ForTitle(title)!;
        var layout = GameplayLayout.For(schema);
        var type = uint.Parse(EmbeddedCatalogs.Records(schema, "ff7r-items").First(record => record.Category == "Items").Key,
            System.Globalization.CultureInfo.InvariantCulture);
        for (var slot = 0; slot < GameplayFields.InventoryCount; slot++)
        {
            Write(layout.InventoryOffset + slot * layout.InventoryStride, type);
            Write(layout.InventoryOffset + slot * layout.InventoryStride + GameplayLayout.QuantityDelta, 1);
        }
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
        {
            Write(layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, 10020);
            bytes[layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride] = 1;
        }
        return MemorySaveSet.SingleFile("ff7rebirth000", bytes, title);

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
