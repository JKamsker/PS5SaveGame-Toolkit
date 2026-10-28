using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PartyViewTests
{
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
