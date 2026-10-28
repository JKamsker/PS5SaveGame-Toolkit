using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCompletionRegressionTests
{
    [Fact]
    public void WrongTypesAndOutOfRangeDirectEditsRejectAtomicallyWithoutThrowing()
    {
        var file = EnabledWriteFixture.Files()[0];
        using var document = new Witcher3Plugin().Open(file);
        _ = document.BuildView();
        var item = EnabledWriteFixture.Read(file.ReadAllBytes()).Player.Inventory!.Items.Single(i => i.Name == "Bread");
        foreach (var value in new[] { FieldValue.Of("text"), FieldValue.Of(true), FieldValue.Of(1.25), FieldValue.Of(-1), FieldValue.Of(Int128.MaxValue) })
        {
            foreach (var id in new[] { item.ValueId, "player/xp", "player/level", "player/abilityPoints" })
            {
                Assert.False(document.ApplyEdits([new("player/xp", FieldValue.Of(20)), new(id, value)]).Succeeded);
                Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            }
        }
    }

    [Fact]
    public void TaggedRecordsCannotReadPastTheirParentEvenIfUnderlyingBytesContainAValidRecord()
    {
        byte[] bytes = [0, 0, 0];
        foreach (var end in new[] { int.MinValue, -1, 0, 1, 2, 4, int.MaxValue })
        {
            Assert.Throws<SaveFormatException>(() => { var position = 0; return TaggedRecord.Read(bytes, ref position, end, []); });
        }
        var position = 0;
        Assert.Empty(TaggedRecord.Read(bytes, ref position, 3, []).Properties);
        Assert.Equal(3, position);
    }

    [Fact]
    public void LargeSkillGraphsStayBoundedAndHaveDirectPathsToLateUnloadedDetailPages()
    {
        var entries = Enumerable.Range(0, 4096).Select(i => new AbilityEntry($"unknown-{i}", new TaggedRecord([]))).ToArray();
        var graphs = new AbilityGraphs([new("skills", entries)], [], null, null).Build("skills").Cast<GraphNode>().ToArray();
        Assert.Equal(8, graphs.Length);
        Assert.All(graphs, graph => {
            SpatialViewValidation.Validate(graph);
            Assert.Equal(512, graph.Items.Count);
            Assert.All(graph.Items, item => Assert.Equal(GraphState.Unknown, item.State));
        });
        var target = graphs[^1].Items[^1].Target!;
        Assert.Equal(3, target.Path.Count);
        Assert.Contains("section:ability/skills/4032", target.Path);
        // Oversized derived field IDs degrade to an explanation instead of escaping Core's publication bounds.
        var oversized = new AbilityEntry(new string('a', 255), new TaggedRecord([]));
        Assert.Equal(Tone.Warning, Assert.IsType<TextNode>(Assert.Single(
            new AbilityGraphs([new("skills", [oversized])], [], null, null).Build("skills"))).Tone);
    }
}
