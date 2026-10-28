using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class TreeRangeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingOverlapIsRejectedWhileAdjacentRangesAndParentContainmentAreAllowed(bool nested)
    {
        var valid = CsavContainer.Parse(Fixture(nested, overlap: false));
        Assert.Equal(nested ? 3 : 2, CsavTree.Parse(valid).Nodes.Count);
        Assert.Equal(valid.Original, valid.Rebuild(valid.Payload));
        var invalid = CsavContainer.Parse(Fixture(nested, overlap: true));
        var error = Assert.Throws<SaveFormatException>(() => CsavTree.Parse(invalid));
        Assert.Contains("sibling node ranges overlap", error.Message);
        Assert.Throws<SaveFormatException>(() => invalid.Rebuild(invalid.Payload));
    }

    private static byte[] Fixture(bool nested, bool overlap)
    {
        const int dataOffset = 45;
        const int payloadSize = 24;
        var count = nested ? 3 : 2;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write("VASC"u8); writer.Write(new byte[21]); writer.Write("FZLC"u8);
        writer.Write(1); writer.Write(dataOffset); writer.Write(payloadSize); writer.Write(payloadSize);
        for (var i = 0; i < payloadSize / sizeof(int); i++) writer.Write(i < count ? i : 0);
        var directory = (int)output.Position;
        writer.Write("EDON"u8); writer.Write((byte)count);
        for (var i = 0; i < count; i++)
        {
            writer.Write((byte)0x81); writer.Write((byte)'x');
            var parent = nested && i == 0;
            writer.Write(parent || i == count - 1 ? -1 : i + 1);
            writer.Write(parent ? 1 : -1);
            writer.Write(dataOffset + i * sizeof(int));
            writer.Write(parent ? payloadSize : overlap && i == count - 2 ? 8 : 4);
        }
        writer.Write(directory); writer.Write("ENOD"u8);
        return output.ToArray();
    }
}
