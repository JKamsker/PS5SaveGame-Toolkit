using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasTypeBudgetTests
{
    [Fact]
    public void WideCompleteTypeTreesShareAFileWideBudget()
    {
        var bytes = Save(GvasFile.MaxNodes / 2, GvasFile.MaxNodes / 2 + 1);
        var error = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
        Assert.Contains("type name", error.Message);
        Assert.Contains("safety limit", error.Message);
    }

    [Fact]
    public void CompleteTypeTreesAtTheExactBudgetRemainOpaqueAndLossless()
    {
        var bytes = Save(GvasFile.MaxNodes / 2, GvasFile.MaxNodes / 2);
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(2, parsed.Properties.Count);
        Assert.All(parsed.Properties, node => Assert.Contains("read-only", node.Note));
        Assert.Equal(bytes, parsed.Serialize());
    }

    private static byte[] Save(params int[] counts) => GvasFixture.Build(w =>
    {
        for (var i = 0; i < counts.Length; i++)
        {
            GvasFixture.String(w, "Unknown" + i);
            Type(w, counts[i]);
            w.Write(0); w.Write((byte)0);
        }
    }, 5, 4);

    private static void Type(BinaryWriter writer, int nodes)
    {
        const int MaxChildren = 16;
        GvasFixture.String(writer, "FutureType");
        int children = Math.Min(nodes - 1, MaxChildren);
        writer.Write(children);
        int remaining = nodes - 1;
        for (var i = 0; i < children; i++)
        {
            int childNodes = remaining / (children - i);
            Type(writer, childNodes);
            remaining -= childNodes;
        }
    }
}
