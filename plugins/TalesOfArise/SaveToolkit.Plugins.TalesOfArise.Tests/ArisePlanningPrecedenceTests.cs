using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class ArisePlanningPrecedenceTests
{
    private const string ActionId = "inventory/set-quantity";
    private const string InvalidRow = "inventory/934/quantity";

    [Theory]
    [InlineData("unresolved-query")]
    [InlineData("unexpanded")]
    [InlineData("expanded")]
    public void CompoundInvalidRequests_PreserveErrorOrderBeforeRowValidation(string request)
    {
        using var document = Open(new AriseTestContainer());
        var scope = new ActionScope([InvalidRow, InvalidRow]);
        var expected = new List<FieldError>();
        if (request == "unresolved-query")
        {
            scope = new ActionScope { QueryScope = new("equipment", new QueryableCollectionQuery(), 0) };
            expected.Add(new(ActionId, "Query selection must target the 'inventory' collection."));
            expected.Add(new(ActionId, "The framework must resolve the query selection to stable inventory row keys before planning."));
        }
        else if (request == "unexpanded")
        {
            expected.Add(new(ActionId, "Expand the inventory table before planning a document or explicit selection, or use an inventory query scope."));
        }
        else
        {
            _ = document.ExpandNode("inventory");
        }

        expected.Add(new("quantity", "Quantity must be a whole number from 0 to 15."));
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var plan = document.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(16L) }, scope);
        Assert.Equal(expected, plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.SkippedRows);
        Assert.Single(plan.Warnings);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("inventory/0/quantity"));
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnknownAction_PrecedesInvalidScopeAndParameters()
    {
        using var document = Open(new AriseTestContainer());
        var error = Assert.Throws<KeyNotFoundException>(() => document.PlanAction("unknown", new Dictionary<string, FieldValue>(),
            new ActionScope { QueryScope = new("equipment", new QueryableCollectionQuery(), 0) }));
        Assert.Equal("Unknown Tales of Arise planned action 'unknown'.", error.Message);
    }

    [Fact]
    public void EmptyAndUnknownIdentities_PrecedeOverBoundQuantitySkip()
    {
        var fixture = new AriseTestContainer();
        var inventory = fixture.Body("SaveData_ItemManager");
        fixture.Payload[inventory + 3 * AriseTestContainer.ItemRecordSize] = 16;
        fixture.Payload[inventory + 4 * AriseTestContainer.ItemRecordSize] = 16;
        using var document = Open(fixture);
        _ = document.ExpandNode("inventory");
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var plan = document.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(12L) },
            new ActionScope(["inventory/4/quantity", "inventory/3/quantity", "inventory/2/quantity"]));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(new[]
        {
            new SkippedActionRow("inventory/4/quantity", "Empty slot; no item is inserted or changed."),
            new SkippedActionRow("inventory/3/quantity", "Numeric ID is not one of the recognised consumable mappings; preserved."),
            new SkippedActionRow("inventory/2/quantity", "Existing quantity 16 exceeds the inferred 15 bound; preserved."),
        }, plan.SkippedRows);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static AriseDocument Open(AriseTestContainer fixture) => Assert.IsType<AriseDocument>(new TalesOfArisePlugin().Open(
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, fixture.Encrypt(), AriseTestContainer.TitleId)));
}
