using System.Buffers.Binary;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartFixtureHelperTests
{
    [Theory]
    [InlineData("slot")]
    [InlineData("save")]
    public void RawFixtureOpenRetainsFilenameDefaultTitleAndAbsentReferences(string fileName)
    {
        var bytes = SlotFixture.Create();
        using var document = SlotFixture.Open(bytes, fileName);
        var result = Assert.Single(document.Serialize());
        Assert.Equal($"PPSA00000/sdimg_TEST/{fileName}", result.RelativePath);
        Assert.Equal(bytes, result.Content.ToArray());
        Assert.Null(document.GetAsset("missing"));
    }

    [Fact]
    public void PropertySizeLocatorFindsTheDeclaredSizeAfterBothFStrings()
    {
        var plain = PlainFixture.Create();
        var index = new RecordReader(plain).Read();
        var root = Assert.Single(index.Properties);
        Assert.Equal(4 + sizeof(int) + "TestRoot\0".Length + sizeof(int) + "IntProperty\0".Length,
            PropertyFixture.SizeOffset(plain, root.Tag.Offset));
        Assert.Equal(sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(PropertyFixture.SizeOffset(plain, root.Tag.Offset))));
    }
}
