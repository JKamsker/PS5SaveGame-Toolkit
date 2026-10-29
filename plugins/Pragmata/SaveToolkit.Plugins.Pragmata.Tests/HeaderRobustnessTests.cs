using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class HeaderRobustnessTests
{
    [Theory]
    [InlineData(FieldTags.String)]
    [InlineData(FieldTags.Struct)]
    [InlineData(FieldTags.Array)]
    [InlineData(FieldTags.Class)]
    [InlineData(FieldTags.Float32)]
    [InlineData(FieldTags.Float64)]
    public void OptionalHeaderValuesRemainInspectableAndLossless(int type)
    {
        var plaintext = TypedPayloadFixture.Gameplay(payload => AddOptional(payload, type));
        var bytes = SyntheticDsss.Create(plaintext);
        using var document = new PragmataDocument("data000.bin", bytes);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.NotEmpty(JsonSerializer.Serialize<ViewNode>(view));
        var overview = view.Tabs.Single(tab => tab.Title == "Overview");
        var header = overview.Children.OfType<SectionNode>().Single(section => section.Title == "Save header");
        Assert.Equal(5, header.Children.Count);
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        Assert.Contains(header.Children.OfType<FieldNode>(), field => field.ValueId == playtime.Path);
        var optional = header.Children[^1];
        if (type is FieldTags.Class or FieldTags.Array)
        {
            var section = Assert.IsType<SectionNode>(optional);
            Assert.NotNull(section.LazyId);
            var expanded = document.ExpandNode(section.LazyId);
            Assert.NotEmpty(JsonSerializer.Serialize(expanded));
            Assert.InRange(expanded.Count, 0, 200);
        }
        else
        {
            var text = Assert.IsType<TextNode>(optional);
            if (type is FieldTags.Float32 or FieldTags.Float64)
                Assert.Contains("non-finite", text.Text, StringComparison.Ordinal);
        }
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(FieldTags.Class)]
    [InlineData(FieldTags.Array)]
    public void HeaderAndRawLazyChildrenKeepSeparateAliasesRegardlessOfExpansionOrder(int type)
    {
        var bytes = SyntheticDsss.Create(TypedPayloadFixture.Gameplay(payload => AddOptional(payload, type)));
        using var document = new PragmataDocument("data000.bin", bytes);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var header = document.Layout.Header!;
        var target = header.Find(TypedPayloadFixture.OptionalHash)!.Children.Single();
        var primarySection = view.Tabs.Single(tab => tab.Title == "Overview").Children.OfType<SectionNode>()
            .Single(section => section.Title == "Save header").Children.OfType<SectionNode>().Single();
        var rawRoot = view.Tabs.Single(tab => tab.Title == "Raw").Children.OfType<SectionNode>()
            .Single(section => section.LazyId!.StartsWith($"tree/{header.Path}/", StringComparison.Ordinal));
        var rawSection = document.ExpandNode(rawRoot.LazyId!).OfType<SectionNode>().Single();
        var raw = Assert.IsType<FieldNode>(Assert.Single(document.ExpandNode(rawSection.LazyId!)));
        var primary = Assert.IsType<FieldNode>(Assert.Single(document.ExpandNode(primarySection.LazyId!)));
        Assert.Equal(target.Path, primary.ValueId);
        Assert.Equal($"raw/{target.Path}", raw.ValueId);
        Assert.Equal(primary.Value, raw.Value);
        var outcome = document.ApplyEdits([new(primary.ValueId, SaveToolkit.Sdk.Values.FieldValue.Of(43U))]);
        Assert.True(outcome.Succeeded);
        Assert.Contains(outcome.Changes, change => change.ValueId == raw.ValueId);
        Assert.True(document.ApplyEdits([new(primary.ValueId, primary.Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private static void AddOptional(TypedPayloadFixture payload, int type)
    {
        switch (type)
        {
            case FieldTags.String:
                payload.String(TypedPayloadFixture.OptionalHash, [65, 0, 0, 0]);
                break;
            case FieldTags.Class:
                payload.Field(TypedPayloadFixture.OptionalHash, type);
                payload.Class(0x10203040, 1);
                payload.Primitive(0x99887766, FieldTags.UInt32, BitConverter.GetBytes(42U));
                break;
            case FieldTags.Array:
                payload.Array(TypedPayloadFixture.OptionalHash, FieldTags.UInt32, sizeof(uint), 1, 0);
                payload.Word(42);
                break;
            default:
                var value = type == FieldTags.Float32 ? BitConverter.GetBytes(float.NaN)
                    : type == FieldTags.Float64 ? BitConverter.GetBytes(double.PositiveInfinity) : new byte[16];
                payload.Primitive(TypedPayloadFixture.OptionalHash, type, value);
                break;
        }
    }
}
