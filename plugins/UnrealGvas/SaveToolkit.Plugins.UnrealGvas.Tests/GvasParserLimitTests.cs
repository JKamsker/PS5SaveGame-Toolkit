using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasParserLimitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepthLimitCannotBeSwallowedByCustomStructFallback(bool headerless)
    {
        var error = Assert.Throws<SaveFormatException>(() => Parse(w => WriteNested(w, 33), headerless));
        Assert.Contains("nesting safety limit", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepthBoundaryStillAcceptsCompleteTaggedStructs(bool headerless)
    {
        var parsed = Parse(w => WriteNested(w, 32), headerless);
        Assert.Equal(32, GvasFile.Descendants(parsed.Properties).Count());
        Assert.All(GvasFile.Descendants(parsed.Properties), node => Assert.Null(node.Note));
    }

    [Theory]
    [InlineData("ArrayProperty", -1)]
    [InlineData("ArrayProperty", GvasFile.MaxNodes)]
    [InlineData("SetProperty", int.MaxValue)]
    [InlineData("MapProperty", GvasFile.MaxNodes / 2)]
    public void ImpossibleContainerCountsInsideStructsRejectBeforeReadingEntries(string type, int count)
    {
        var error = Assert.Throws<SaveFormatException>(() => Parse(w => GvasFixture.Property(w, "Root", "StructProperty",
            GvasFixture.Body(b =>
            {
                GvasFixture.Property(b, "Items", type, GvasFixture.Body(items =>
                {
                    if (type is "SetProperty" or "MapProperty") items.Write(0);
                    items.Write(count);
                }), "IntProperty", "IntProperty");
                GvasFixture.String(b, "None");
            }), "Custom"), false));
        Assert.True(error.Message.Contains("count", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("node safety limit", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public void NodeLimitCannotBeSwallowedByCustomStructFallback()
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Root", "StructProperty", GvasFixture.Body(body =>
        {
            for (var i = 0; i < GvasFile.MaxNodes; i++)
                GvasFixture.Property(body, "Flag", "BoolProperty", []);
            GvasFixture.String(body, "None");
        }), "Custom"));
        var error = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
        Assert.Contains("node safety limit", error.Message);
    }

    [Fact]
    public void NodeBoundaryStillAcceptsTheFullBudget()
    {
        var parsed = Parse(w => GvasFixture.Property(w, "Items", "ArrayProperty", GvasFixture.Body(items =>
        {
            items.Write(GvasFile.MaxNodes - 1);
            items.Write(new byte[(GvasFile.MaxNodes - 1) * sizeof(int)]);
        }), "IntProperty"), false);
        Assert.Equal(GvasFile.MaxNodes, GvasFile.Descendants(parsed.Properties).Count());
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(17, true)]
    public void CompleteTypeDepthLimitIsEnforcedInsideCustomStructs(int depth, bool rejected)
    {
        void Type(BinaryWriter w, int remaining)
        {
            GvasFixture.String(w, "FutureType");
            w.Write(remaining > 0 ? 1 : 0);
            if (remaining > 0) Type(w, remaining - 1);
        }
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.String(w, "Root");
            GvasFixture.String(w, "StructProperty"); w.Write(1);
            GvasFixture.String(w, "Custom"); w.Write(0);
            var body = GvasFixture.Body(b =>
            {
                GvasFixture.String(b, "Nested"); Type(b, depth);
                b.Write(0); b.Write((byte)0);
                GvasFixture.String(b, "None");
            });
            w.Write(body.Length); w.Write((byte)0); w.Write(body);
        }, 5, 4);
        if (rejected)
            Assert.Contains("type name nesting", Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes)).Message);
        else
            Assert.Contains("Unsupported value type", Assert.Single(Assert.Single(GvasFile.Parse(bytes).Properties).Children).Note);
    }

    private static GvasFile Parse(Action<BinaryWriter> properties, bool headerless)
    {
        if (!headerless) return GvasFile.Parse(GvasFixture.Build(properties));
        var header = GvasFile.Parse(GvasFixture.Build(_ => { })).Header;
        return GvasFile.ParseHeaderless(GvasFixture.Body(w => { properties(w); GvasFixture.String(w, "None"); }), header);
    }

    private static void WriteNested(BinaryWriter writer, int levels)
    {
        if (levels == 0) return;
        GvasFixture.Property(writer, "Nested", "StructProperty", GvasFixture.Body(w =>
        {
            WriteNested(w, levels - 1);
            GvasFixture.String(w, "None");
        }), "Custom");
    }
}
