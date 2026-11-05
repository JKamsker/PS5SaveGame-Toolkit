using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayMoveAdversarialTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void EverySourceAndFinalIndexMatchesIndependentListModel(int count)
    {
        var values = Enumerable.Range(0, count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var original = "[[" + string.Join(',', values) + "],[true,false]]";
        for (var from = 0; from < count; from++)
        for (var destinationIndex = 0; destinationIndex < 2; destinationIndex++)
        for (var at = 0; at <= (destinationIndex == 0 ? count - 1 : 2); at++)
        {
            var tree = JsonTokenTree.Parse(original);
            var source = tree.Nodes[0].Children[0];
            var destination = tree.Nodes[0].Children[destinationIndex];
            var parameters = JsonArrayMoveTests.Parameters(source.Children[from].ValueId, destination.ValueId, at)
                .ToDictionary(p => p.Name, p => p.Value);
            var plan = JsonStructureActions.Plan(tree, JsonArrayMoveTests.Action(source), parameters, new());
            Assert.Equal(Encoding.UTF8.GetBytes(original), JsonTreeWriter.Write(tree));
            if (destinationIndex == 0 && from == at)
            {
                Assert.NotEmpty(plan.Errors);
                Assert.Empty(plan.Changes);
            }
            else
            {
                Assert.Empty(plan.Errors);
                var expected = new[] { values.ToList(), new List<string> { "true", "false" } };
                expected[0].RemoveAt(from);
                expected[destinationIndex].Insert(at, values[from]);
                JsonStructureActions.Mutate(tree, JsonArrayMoveTests.Action(source), parameters);
                Assert.Equal(Encode(expected), JsonTreeWriter.Write(tree));
            }
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(20261105)]
    public void ChainedMovesMatchIndependentByteOracleThroughCompleteUndoRedo(int seed)
    {
        const int steps = 48;
        string[][] entries =
        [
            ["{ \"\\u0061\" : -0,\"a\":1.2300E+02 }", "null", "[ 1e999999999999999999999,\"\\uD83D\\uDE00\" ]"],
            ["\"\\u0041\\/\"", "false"],
            ["{\"\":{},\"\":[]}"],
            [],
        ];
        var model = entries.Select(items => items.ToList()).ToArray();
        var original = Encoding.UTF8.GetString(Encode(model));
        var initial = JsonTokenTree.Parse(original);
        var arrayIds = initial.Nodes[0].Children.Select(n => n.Index).ToArray();
        var identities = initial.Nodes[0].Children.Select(n => n.Children.Select(c => c.ValueId).ToList()).ToArray();
        var scalarIds = initial.Nodes.Where(n => !n.IsContainer).Select(n => n.ValueId).ToArray();
        using var fixture = new JsonSessionFixture(original);
        var importedFields = fixture.Fields().Where(f => scalarIds.Contains(f.ValueId)).ToDictionary(f => f.ValueId, f => f.Value);
        var states = new List<byte[]> { fixture.Bytes };
        var random = new Random(seed);
        for (var step = 0; step < steps; step++)
        {
            var available = Enumerable.Range(0, model.Length).Where(i => model[i].Count > 0).ToArray();
            var source = available[random.Next(available.Length)];
            var destination = (source + 1 + random.Next(model.Length - 1)) % model.Length;
            var from = random.Next(model[source].Count);
            var at = random.Next(model[destination].Count + 1);
            var id = identities[source][from];
            JsonArrayEditingTests.Apply(fixture, $"array/{arrayIds[source]}/move-to",
                JsonArrayMoveTests.Parameters(id, $"value/{arrayIds[destination]}", at));
            var text = model[source][from];
            model[source].RemoveAt(from);
            model[destination].Insert(at, text);
            identities[source].RemoveAt(from);
            identities[destination].Insert(at, id);
            Assert.Equal(Encode(model), fixture.Bytes);
            // Resolve live bindings after every move; repeated tokens and duplicate keys retain identity.
            var fields = fixture.Fields().Where(f => scalarIds.Contains(f.ValueId)).ToDictionary(f => f.ValueId, f => f.Value);
            Assert.Equal(importedFields.OrderBy(p => p.Key), fields.OrderBy(p => p.Key));
            states.Add(fixture.Bytes);
        }
        for (var step = steps - 1; step >= 0; step--)
        {
            Assert.True(fixture.Session.Undo().Ok);
            Assert.Equal(states[step], fixture.Bytes);
        }
        Assert.False(fixture.Session.State.CanUndo);
        for (var step = 1; step <= steps; step++)
        {
            Assert.True(fixture.Session.Redo().Ok);
            Assert.Equal(states[step], fixture.Bytes);
        }
    }

    [Fact]
    public void EveryNestedDestinationRejectsExactlyCyclesAndSamePosition()
    {
        const string original = "[[[1,[]],2],[[],[3]],[]]";
        var imported = JsonTokenTree.Parse(original);
        foreach (var moving in imported.Nodes.Where(n => n.Parent?.Kind == System.Text.Json.JsonTokenType.StartArray))
        foreach (var destination in imported.Nodes.Where(n => n.Kind == System.Text.Json.JsonTokenType.StartArray))
        {
            var sameArray = moving.Parent == destination;
            var maximum = destination.Children.Count - (sameArray ? 1 : 0);
            // Derive cycles from the source's descendant set, independently of parent walking.
            var cycle = Descendants(moving).Contains(destination.Index);
            for (var at = 0; at <= maximum; at++)
            {
                var tree = imported.Clone();
                var parameters = JsonArrayMoveTests.Parameters(moving.ValueId, destination.ValueId, at)
                    .ToDictionary(p => p.Name, p => p.Value);
                var plan = JsonStructureActions.Plan(tree, JsonArrayMoveTests.Action(moving.Parent!), parameters, new());
                Assert.Equal(cycle || (sameArray && at == moving.Position), plan.Errors.Count > 0);
                Assert.Equal(Encoding.UTF8.GetBytes(original), JsonTreeWriter.Write(tree));
                if (plan.Errors.Count != 0) continue;
                JsonStructureActions.Mutate(tree, JsonArrayMoveTests.Action(moving.Parent!), parameters);
                var live = tree.FindValue(moving.ValueId)!;
                Assert.Equal(destination.Index, live.Parent!.Index);
                Assert.Equal(at, live.Position);
                Assert.Equal(imported.ById.Keys.Order(), tree.ById.Keys.Order());
                var bytes = JsonTreeWriter.Write(tree);
                Assert.Equal(bytes, JsonTreeWriter.Write(JsonTokenTree.Parse(bytes)));
            }
        }

        static IEnumerable<int> Descendants(JsonValueToken node) =>
            new[] { node.Index }.Concat(node.Children.SelectMany(Descendants));
    }

    [Fact]
    public void LargeArrayTailMoveKeepsBoundedPagesAndByteExactHistory()
    {
        const int count = 20_000;
        const string subtree = "{ \"\\u0061\":-0,\"a\":1.2300E+02 }";
        var prefix = string.Join(',', Enumerable.Repeat("null", count - 1));
        var original = Encoding.UTF8.GetBytes("[[" + prefix + "," + subtree + "],[]]");
        var imported = JsonTokenTree.Parse(original);
        var source = imported.Nodes[0].Children[0];
        var destination = imported.Nodes[0].Children[1];
        var moving = source.Children[^1];
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", original));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var parameters = JsonArrayMoveTests.Parameters(moving.ValueId, destination.ValueId, 0)
            .ToDictionary(p => p.Name, p => p.Value);
        var plan = planner.PlanAction(JsonArrayMoveTests.Action(source), parameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count);
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
        var outcome = planner.ApplyActionPlan(JsonArrayMoveTests.Action(source), parameters, new(), plan);
        Assert.True(outcome.Succeeded);
        var expected = Encoding.UTF8.GetBytes("[[" + prefix + "],[" + subtree + "]]");
        Assert.Equal(expected, document.Serialize()[0].Content.ToArray());
        _ = document.BuildView();
        var page = query.Query(JsonSearch.ArrayPrefix + source.Index, new() { Limit = int.MaxValue });
        Assert.Equal(200, page.Rows.Count);
        Assert.Equal(count - 1, page.TotalCount);
        Assert.Empty(query.Query(JsonSearch.ArrayPrefix + source.Index, new() { Offset = count - 1 }).Rows);
        var moved = Assert.Single(query.Query(JsonSearch.CollectionId, new() { Text = moving.ValueId }).Rows);
        Assert.Equal(moving.ValueId, moved.Key);
        var redo = document.RestoreMemento(Assert.IsAssignableFrom<DocumentMemento>(outcome.Revert));
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(expected, document.Serialize()[0].Content.ToArray());
    }

    private static byte[] Encode(IEnumerable<IEnumerable<string>> arrays) =>
        Encoding.UTF8.GetBytes("[" + string.Join(',', arrays.Select(items => "[" + string.Join(',', items) + "]")) + "]");
}
