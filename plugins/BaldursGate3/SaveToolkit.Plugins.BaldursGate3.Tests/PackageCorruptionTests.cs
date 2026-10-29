using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class PackageCorruptionTests
{
    private const int MaximumRejectionAllocation = 1024 * 1024;

    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void DeclaredEntrySizesAreCheckedBeforeAllocatingPayloads(byte codec)
    {
        var original = PackageFixture.Create(method: codec, payloads: [("opaque.bin", new byte[64])]);
        foreach (var (field, value) in new (int, uint)[]
        {
            (LspkLayout.EntryStoredSize, uint.MaxValue),
            (LspkLayout.EntryStoredSize, 16 * 1024 * 1024),
            (LspkLayout.Entry18ExpandedSize, uint.MaxValue),
            (LspkLayout.Entry18ExpandedSize, 16 * 1024 * 1024),
            (LspkLayout.Entry18ExpandedSize, 63),
            (LspkLayout.Entry18ExpandedSize, 65),
        })
        {
            var bytes = PackageCorruptionFixture.Rewrite(original,
                table => BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(field), value));
            RejectWithBoundedAllocation(bytes);
        }
        using var document = Open(original);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void TruncatedCompressedPayloadFailsEvenWhenPackageTableAndDigestAreIntact(byte codec)
    {
        var original = PackageFixture.Create(method: codec, payloads:
            [("opaque.bin", Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray())]);
        for (var trim = 1; trim <= 5; trim++)
            RejectWithBoundedAllocation(PackageCorruptionFixture.Rewrite(original, _ => { }, trim));
    }

    [Theory]
    [InlineData(LspkLayout.EntryOffset, 0u, "payload area")]
    [InlineData(LspkLayout.Entry18Part, 1u, "archive part")]
    [InlineData(LspkLayout.Entry18Flags, 15u, "compression method")]
    public void MalformedTableRowsRetainSpecificDiagnostics(int field, uint value, string diagnostic)
    {
        var original = PackageFixture.Create(payloads: [("opaque.bin", new byte[64])]);
        var bytes = PackageCorruptionFixture.Rewrite(original, table =>
        {
            if (field == LspkLayout.EntryOffset) BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(field), value);
            else table[field] = (byte)value;
        });
        Assert.Contains(diagnostic, Assert.Throws<SaveFormatException>(() => Open(bytes)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUtf8FileNameHasADocumentedRejection()
    {
        var bytes = PackageCorruptionFixture.Rewrite(PackageFixture.Create(payloads: [("opaque.bin", [1])]), table => table[0] = 0xff);
        Assert.Throws<SaveFormatException>(() => Open(bytes));
    }

    private static void RejectWithBoundedAllocation(byte[] bytes)
    {
        var allocated = AllocationProbe.Measure(() => Record.Exception(() => Open(bytes)), out var error);
        Assert.IsType<SaveFormatException>(error);
        allocated.AssertLessThan(MaximumRejectionAllocation, $"Invalid package allocated {allocated.MinimumBytes:N0} bytes.");
    }

    private static ISaveDocument Open(byte[] bytes) =>
        new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("corrupt.lsv", bytes, "PPSA18463"));
}
