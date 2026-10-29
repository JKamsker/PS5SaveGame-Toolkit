using System.Buffers.Binary;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasCorruptionBoundaryTests
{
    [Fact]
    public void EveryPhysicalTruncationOfNestedStructArrayMapAndSetRejects()
    {
        var bytes = GvasFixture.Nested()[..^4]; // Exclude the permitted game-owned suffix.
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes.AsSpan(0, length)));
        Assert.Equal(bytes, GvasFile.Parse(bytes).Serialize());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(GvasFile.MaxNodes + 1)]
    [InlineData(GvasFile.MaxNodes)]
    public void BadCustomVersionCountsRejectBeforeAllocatingTheTable(int count)
    {
        var bytes = GvasFixture.Build(_ => { }, customVersions: 0);
        const int FormatBytes = sizeof(int);
        int countOffset = bytes.AsSpan().IndexOf("++Test\0"u8) + "++Test\0"u8.Length + FormatBytes;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(countOffset), count);
        _ = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes)); // Warm exception/JIT paths.
        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
        const int RejectionAllocationBudget = 64 * 1024;
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < RejectionAllocationBudget);
        Assert.Contains(count == GvasFile.MaxNodes ? "Truncated GVAS custom versions" : "Invalid GVAS count", error.Message);
        Assert.False(new UnrealGvasPlugin().Probe(MemorySaveSet.SingleFile("save", bytes)).IsMatch);
    }

    [Theory]
    [InlineData("IntProperty", null, 3)]
    [InlineData("StrProperty", null, 3)]
    [InlineData("StructProperty", "Vector", 11)]
    [InlineData("StructProperty", "Guid", 15)]
    [InlineData("ArrayProperty", "IntProperty", 3)]
    [InlineData("SetProperty", "IntProperty", 7)]
    [InlineData("MapProperty", "IntProperty", 7)]
    public void ShortKnownPayloadsHaveFormatDiagnostics(string type, string? inner, int length)
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Broken", type, new byte[length], inner, "IntProperty"));
        Assert.StartsWith("Truncated GVAS", Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes)).Message);
    }

    [Fact]
    public void UnsupportedLayoutsAndRemovedEntriesRemainOpaqueAndLossless()
    {
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.Property(w, "Native", "StructProperty", [0xFF, 0xEE, 0xDD], "FutureNative");
            GvasFixture.Property(w, "Map", "MapProperty", GvasFixture.Body(b => { b.Write(1); b.Write(42); }), "IntProperty", "IntProperty");
            GvasFixture.Property(w, "Future", "FutureProperty", [0xAA, 0xBB]);
        });
        var parsed = GvasFile.Parse(bytes);
        Assert.All(parsed.Properties, node =>
        {
            Assert.Null(node.Scalar);
            Assert.Empty(node.Children);
            Assert.Contains("read-only", node.Note);
        });
        Assert.Equal(bytes, parsed.Serialize());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCompleteTagTruncationRejectsIncludingNestedTypeNamesAndOptionalFields(bool nested)
    {
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.String(w, "Value");
            GvasFixture.String(w, nested ? "ArrayProperty" : "IntProperty"); w.Write(nested ? 1 : 0);
            if (nested) { GvasFixture.String(w, "IntProperty"); w.Write(0); }
            w.Write(nested ? 8 : 4); w.Write((byte)3); // Index and property GUID.
            w.Write(0); w.Write(new byte[16]);
            if (nested) w.Write(1);
            w.Write(42);
        }, 5, 4, suffix: false);
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes.AsSpan(0, length)));
        Assert.Equal(bytes, GvasFile.Parse(bytes).Serialize());
    }
}
