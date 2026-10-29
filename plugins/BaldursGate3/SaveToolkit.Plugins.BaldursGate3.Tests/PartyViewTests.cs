using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PartyViewTests
{
    [Fact]
    public void LeaderAndSummaryCallbacksKeepEntryOrderAndTheFirstMatchingSummary()
    {
        var leader = new ResourceAttribute("LeaderName", LarianAttributeType.String, "leader\0"u8.ToArray());
        var metadata = new LarianResource("synthetic", [], [leader, leader], []);
        var summary = Encoding.UTF8.GetBytes("{\"Active Party\":{\"Characters\":[{\"Origin\":\"Raw\",\"Level\":2}]}}");
        var callbacks = new List<string>();
        var nodes = PartyView.Build(
            [new("meta.lsf", [], metadata), new("META.lsf", [], metadata), new("meta.lsf", [], metadata),
                new("saveinfo.JSON", summary, null), new("SaveInfo.json", "null"u8.ToArray(), null)], BindAttribute, BindSummary);
        Assert.Equal(["resource/0/attribute/0", "resource/2/attribute/0", "party/0/origin-label", "party/0/Origin", "party/0/Level"], callbacks);
        Assert.Collection(nodes,
            node => Assert.Equal(Tone.Warning, Assert.IsType<TextNode>(node).Tone),
            node => Assert.Equal("resource/0/attribute/0", Assert.IsType<FieldNode>(node).ValueId),
            node => Assert.Equal("resource/2/attribute/0", Assert.IsType<FieldNode>(node).ValueId),
            node => Assert.IsType<StatsNode>(node),
            node =>
            {
                var row = Assert.Single(Assert.IsType<TableNode>(node).Rows);
                Assert.Equal("Raw", Assert.IsType<FieldNode>(row.Cells["origin"]).Value.AsString());
                Assert.Equal(2, Assert.IsType<FieldNode>(row.Cells["level"]).Value.AsInt32());
                Assert.Equal("Unavailable", Assert.IsType<TextNode>(row.Cells["currentXp"]).Text);
                Assert.Equal("Unavailable", Assert.IsType<TextNode>(row.Cells["totalXp"]).Text);
            },
            node => Assert.IsType<TextNode>(node));

        FieldNode BindAttribute(string id, string label, ResourceAttribute attribute) => BindSummary(id, label, AttributeDecoder.Read(attribute));
        FieldNode BindSummary(string id, string label, FieldValue value)
        {
            callbacks.Add(id);
            return new FieldNode(id, label, new TextSpec(), value);
        }
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Active Party\":{\"Characters\":false}}")]
    public void UnsupportedOptionalSummaryIsPreservedAndCannotBreakTheExplorer(string json)
    {
        var original = PackageFixture.Create(payloads: [("SaveInfo.json", Encoding.UTF8.GetBytes(json))]);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", original));
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var party = Assert.Single(view.Tabs, t => t.Title == "Party");
        Assert.Empty(party.Children.OfType<TableNode>());
        Assert.Contains(party.Children.OfType<TextNode>(), text => text.Text.Contains("SaveInfo.json", StringComparison.Ordinal));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PartyRowsAreBoundedAndOnlyExistingNumbersExposeSummaryEdits()
    {
        var characters = string.Join(',', Enumerable.Repeat("{\"Origin\":\"Generic\",\"Level\":1,\"Experience Points (Current level)\":30,\"Experience Points (Total)\":30}", 129));
        var json = Encoding.UTF8.GetBytes("{\"Active Party\":{\"Characters\":[" + characters + "]}}");
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", PackageFixture.Create(payloads: [("SaveInfo.json", json)])));
        var party = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title == "Party");
        var table = Assert.Single(party.Children.OfType<TableNode>());
        Assert.Equal(128, table.Rows.Count);
        Assert.All(table.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), field =>
        {
            Assert.Equal(field.Value.Kind != FieldValueKind.Integer, field.Spec.ReadOnly);
            Assert.Equal(field.Label == "Origin label" ? Confidence.Unknown : Confidence.Inferred, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(99L))]).Succeeded);
            if (!field.Spec.ReadOnly) Assert.Contains("Not validated on console", field.Warning, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RepeatedAndOversizedLeaderMetadataCannotBuildAnUnboundedView()
    {
        var small = new ResourceAttribute("LeaderName", LarianAttributeType.String, "name\0"u8.ToArray());
        var repeated = new LarianResource("synthetic", [], Enumerable.Repeat(small, 20_000).ToArray(), []);
        var bound = 0;
        var nodes = PartyView.Build([new DocumentEntry("meta.lsf", [], repeated)], Bind, NoSummary);
        Assert.Equal(1, bound);
        Assert.True(nodes.Count < 10);
        var large = small with { Data = Encoding.UTF8.GetBytes(new string('a', ResourceExplorer.MaxInlineBytes) + '\0') };
        _ = PartyView.Build([new DocumentEntry("meta.lsf", [], repeated with { Attributes = [large] })], Bind, NoSummary);
        Assert.Equal(1, bound); // oversized text was not decoded or bound

        FieldNode Bind(string id, string label, ResourceAttribute attribute)
        {
            bound++;
            return new FieldNode(id, label, new TextSpec(), AttributeDecoder.Read(attribute));
        }

        static FieldNode NoSummary(string id, string label, FieldValue value) => throw new InvalidOperationException("No JSON fixture.");
    }
}
