using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class StackSentinelTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("-1.0")]
    [InlineData("-10e-1")]
    [InlineData("-0.01E+2")]
    public void NonStackableSentinelIsSkippedRegardlessOfNumericSpelling(string sentinel)
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var json = """
            {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
             "CharacterSaveV1":{"MetaData":{"IsOnline":false},"Inventory":{"Entries":[
             {"ItemData":{"TypeTag":"SW.Item.Sword","TargetSlotOverride":"None"},"StackCount":SENTINEL},
             {"ItemData":{"TypeTag":"SW.Item.Sword","TargetSlotOverride":"None"},"StackCount":1.00}]}}}
            """.Replace("SENTINEL", sentinel, StringComparison.Ordinal);
        var original = Encoding.UTF8.GetBytes(json);
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", original), references);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = query.Query("character/inventory", new QueryableCollectionQuery()).Rows;
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.ExactNumber("2") };
        var scope = new ActionScope(rows.Select(row => row.Key).ToArray());

        var plan = planner.PlanAction("character/set-stacks", parameters, scope);

        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        var skipped = Assert.Single(plan.SkippedRows);
        Assert.Equal(rows[0].Key, skipped.RowKey);
        Assert.Contains("sentinel", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(planner.ApplyActionPlan("character/set-stacks", parameters, scope, plan).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(json.Replace("\"StackCount\":1.00", "\"StackCount\":2", StringComparison.Ordinal)),
            document.Serialize().Single().Content.ToArray());
        Assert.True(document.ApplyEdits([new(plan.Changes[0].ValueId, plan.Changes[0].OldValue)]).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
