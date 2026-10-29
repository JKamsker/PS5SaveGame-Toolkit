using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSelectedResetTests
{
    [Fact]
    public void SelectedResetKeepsCurrentDocumentOrderAfterMovesAndDeduplicatesKeys()
    {
        var tree = JsonTokenTree.Parse("[1,2,3]");
        tree.Nodes[1].Current = JsonEditingTests.Number("11");
        tree.Nodes[3].Current = JsonEditingTests.Number("33");
        JsonStructureActions.Mutate(tree, "array/0/move", new Dictionary<string, FieldValue>
        {
            ["from"] = FieldValue.Of(0L), ["to"] = FieldValue.Of(2L),
        });
        var plan = JsonResetActions.Plan(tree, new() { RowKeys = ["value/1", "value/3", "value/1"] });
        Assert.Equal(["value/3", "value/1"], plan.Changes.Select(change => change.ValueId));
        Assert.Equal([JsonEditingTests.Number("3"), JsonEditingTests.Number("1")], plan.Changes.Select(change => change.NewValue));
        Assert.Equal(JsonEditingTests.Number("11"), tree.FindValue("value/1")!.Current);
        Assert.Empty(JsonResetActions.Plan(tree, new() { RowKeys = [] }).Changes);
    }

    [Fact]
    public void SelectedResetPreservesMissingReadonlyAddedAndContainerHandling()
    {
        var tree = JsonTokenTree.Parse("[1e999999999999999999999,0]");
        tree.Nodes[2].Imported = false;
        var plan = JsonResetActions.Plan(tree, new() { RowKeys = ["value/2", "value/1", "value/0"] });
        Assert.Empty(plan.Changes);
        Assert.Equal(["value/0", "value/1", "value/2"], plan.SkippedRows.Select(row => row.RowKey));
        Assert.NotEmpty(JsonResetActions.Plan(tree, new() { RowKeys = ["value/99"] }).Errors);
        Assert.NotEmpty(JsonResetActions.Plan(tree, new() { RowKeys = ["value/01"] }).Errors);
    }
}
