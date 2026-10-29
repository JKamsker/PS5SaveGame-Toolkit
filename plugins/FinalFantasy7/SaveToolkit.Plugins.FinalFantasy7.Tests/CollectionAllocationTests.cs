using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

[Collection(typeof(AllocationTestCollection))]
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
        var allocated = AllocationProbe.Measure(() => collections.Query(id, query), out var page);
        Assert.Equal(50, page.Rows.Count);
        allocated.AssertInRange(1, budget);
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

    [Theory]
    [InlineData("catalog-items", null, 500_000)]
    [InlineData("catalog-items", "name", 500_000)]
    [InlineData("owned-materia", null, 1_300_000)]
    [InlineData("owned-materia", "name", 1_300_000)]
    public void ReferenceAndRelationshipQueriesAvoidRebuildingRichOffPageRows(string id, string? sort, long budget)
    {
        using var document = new FinalFantasy7Plugin().Open(Input());
        var collections = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { SortColumn = sort, Limit = 50, IncludeEmpty = true };
        _ = collections.Query(id, query);
        var allocated = AllocationProbe.Measure(() => collections.Query(id, query), out var page);
        Assert.Equal(50, page.Rows.Count);
        allocated.AssertInRange(1, budget);
    }

    [Fact]
    public void RestockPlanDoesNotRebuildTheSameAttributionForEveryStack()
    {
        using var document = new FinalFantasy7Plugin().Open(Input());
        var planner = (SaveToolkit.Sdk.IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(99L) };
        _ = planner.PlanAction(InventoryPlanning.ActionId, parameters, new());
        var allocated = AllocationProbe.Measure(() => planner.PlanAction(InventoryPlanning.ActionId, parameters, new()), out var plan);
        Assert.Empty(plan.Errors);
        Assert.Equal(GameplayFields.InventoryCount, plan.Changes.Count);
        Assert.Empty(plan.SkippedRows);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.All(plan.Changes, change =>
        {
            Assert.Equal(1, change.OldValue.AsInt32());
            Assert.Equal(99, change.NewValue.AsInt32());
        });
        allocated.AssertInRange(1, 2_000_000);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlannersKeepCanonicalKeysAndRejectMalformedOrOutOfRangeSelections(bool materia)
    {
        using var document = new FinalFantasy7Plugin().Open(Input());
        var planner = (SaveToolkit.Sdk.IActionPlanningDocument)document;
        var prefix = materia ? "materia/" : "inventory/";
        string[] invalid = ["", "00", "01", "+1", "-0", "1\0", "1/", "1.0", " 1", "1 ", "1\n", "١", "9999999", "1000000000000"];
        var invalidKeys = invalid.Select(suffix => prefix + suffix).Append("other/1").ToArray();
        var parameters = materia
            ? new Dictionary<string, FieldValue> { ["type"] = FieldValue.Of("10020"), ["ap"] = FieldValue.Of(10L) }
            : new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(99L) };
        var plan = planner.PlanAction(materia ? MateriaPlanning.ActionId : InventoryPlanning.ActionId, parameters,
            new() { RowKeys = [prefix + "1", .. invalidKeys, prefix + "0", prefix + "1"] });
        Assert.Empty(plan.Errors);
        Assert.Equal([prefix + "0/" + (materia ? "ap" : "quantity"), prefix + "1/" + (materia ? "ap" : "quantity")],
            plan.Changes.Select(change => change.ValueId));
        Assert.Equal(invalidKeys.Order(StringComparer.Ordinal), plan.SkippedRows.Select(row => row.RowKey));
        Assert.All(plan.SkippedRows, row => Assert.Equal(materia ? "Not a current materia slot." : "Not a current inventory slot.", row.Reason));
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
