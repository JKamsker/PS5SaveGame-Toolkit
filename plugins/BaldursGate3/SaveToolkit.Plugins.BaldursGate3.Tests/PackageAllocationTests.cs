using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PackageAllocationTests
{
    private const int PayloadBytes = 4 * 1024 * 1024;
    private const int ReaderOverheadBytes = 1024 * 1024;

    [Theory]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(18u)]
    public void TransferredPayloadsDoNotChangeIndependentExtraction(uint version)
    {
        for (byte codec = 0; codec <= 3; codec++)
        {
            var bytes = PackageFixture.Create(version, codec, ("first.bin", [1, 2, 3]), ("second.bin", [4, 5]));
            var package = LspkPackage.Read(bytes, out var payloads);
            Assert.Equal(new byte[] { 1, 2, 3 }, payloads["first.bin"]);
            Assert.Equal(new byte[] { 4, 5 }, payloads["second.bin"]);
            payloads["first.bin"][0] = 99;
            Assert.Equal(new byte[] { 1, 2, 3 }, package.Extract(package.Entries[0]));
        }
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void OpeningValidatedPackageAllocatesOneExpandedPayload(byte codec)
    {
        var payload = new byte[PayloadBytes];
        Array.Fill(payload, (byte)0x5a);
        var bytes = PackageFixture.Create(method: codec, payloads: [("opaque.bin", payload)]);
        var file = MemorySaveSet.SingleFile("test.lsv", bytes);
        var plugin = new BaldursGate3Plugin();
        using (plugin.Open(file)) { } // JIT and codec setup are not part of the allocation budget.
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        using var document = plugin.Open(file);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < bytes.Length + PayloadBytes + ReaderOverheadBytes,
            $"Opening allocated {allocated:N0} bytes; expanded payloads must be reused after digest validation.");
        Assert.Equal(payload, document.ReadBlob("payload/0", 0, PayloadBytes).ToArray());
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    public void OpeningStillRejectsIncorrectDigestBeforeReturningDocument(byte codec)
    {
        var bytes = PackageFixture.Create(method: codec, payloads: [("opaque.bin", new byte[1024])]);
        bytes[LspkLayout.Digest] ^= 1;
        var error = Assert.Throws<SaveFormatException>(() =>
            new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsv", bytes)));
        Assert.Contains("digest mismatch", error.Message, StringComparison.Ordinal);
    }
}
