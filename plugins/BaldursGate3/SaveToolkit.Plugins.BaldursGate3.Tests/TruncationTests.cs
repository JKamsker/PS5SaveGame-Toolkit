using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class TruncationTests
{
    [Theory]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(18u)]
    public void EveryIncompletePackagePrefixHasADocumentedRejection(uint version)
    {
        for (byte codec = 0; codec <= 3; codec++)
        {
            var bytes = PackageFixture.Create(version, codec, ("opaque.bin", new byte[64]));
            RejectPrefixes("truncated.lsv", bytes);
            using var document = Open("complete.lsv", bytes);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(5u, false, (byte)0, false)]
    [InlineData(5u, true, (byte)1, false)]
    [InlineData(6u, false, (byte)2, false)]
    [InlineData(6u, true, (byte)2, true)]
    [InlineData(7u, false, (byte)2, false)]
    [InlineData(7u, true, (byte)2, true)]
    [InlineData(7u, false, (byte)3, false)]
    [InlineData(7u, true, (byte)3, false)]
    public void EveryIncompleteResourcePrefixHasADocumentedRejection(uint version, bool adjacency, byte codec, bool framed)
    {
        var bytes = ResourceFixture.Create(version, adjacency, codec, framed);
        RejectPrefixes("truncated.lsf", bytes);
        using var document = Open("complete.lsf", bytes);
        document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LsmfRequiresItsWholeDirectoryButPreservesUnknownTrailingBytes()
    {
        var bytes = LsmfFixture.Entities();
        var required = LsmfReader.ArenaBase + (int)BinaryData.U64(bytes, 16) + (int)BinaryData.U64(bytes, 24);
        for (var length = 0; length < required; length++)
        {
            var prefix = bytes.AsMemory(0, length);
            Assert.Throws<SaveFormatException>(() => LsmfReader.Read(prefix));
        }
        for (var length = required; length <= bytes.Length; length++)
            Assert.Equal(bytes[..length], LsmfReader.Read(bytes.AsMemory(0, length)).Bytes.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryIncompleteXmlPrefixHasADocumentedRejection(bool utf16)
    {
        var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<save><region id='Test'>text</region></save>")).ToArray();
        RejectPrefixes("truncated.lsx", bytes);
        using var document = Open("complete.lsx", bytes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void RejectPrefixes(string name, byte[] bytes)
    {
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            var error = Record.Exception(() => Open(name, prefix));
            Assert.True(error is SaveFormatException, $"{name}, prefix {length}/{bytes.Length}: {error}");
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }

    private static ISaveDocument Open(string name, byte[] bytes) =>
        new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile(name, bytes, "PPSA18463"));
}
