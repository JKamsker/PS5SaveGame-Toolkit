namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class InventoryLayoutTests
{
    [Theory]
    [InlineData("held", 0x9010, 2688, 384)]
    [InlineData("storage", 0x6010, 1920, 128)]
    public void BoundedReaderKeepsExactArrayExtentsAndBothTrailingIndexWords(
        string id, int byteLength, int commonCapacity, int keyCapacity)
    {
        const int recordSize = 12;
        var fixture = new SemanticFixture();
        var slot = EldenRingParser.Parse(fixture.Save.Bytes).Slots[0];
        var character = SemanticReader.Read(fixture.Save.Bytes, slot);
        var container = Assert.Single(character.Containers, container => container.Id == id);
        var start = id == "held" ? fixture.Held : fixture.Storage;
        Assert.Equal(new SaveRange(id, start, byteLength), container.Range);
        Assert.Equal(commonCapacity + keyCapacity, container.Capacity);
        var common = container.Entries.Where(entry => entry.Group == "common").ToArray();
        var key = container.Entries.Where(entry => entry.Group == "key").ToArray();
        Assert.Equal(commonCapacity, common.Length);
        Assert.Equal(keyCapacity, key.Length);
        Assert.Equal(start + sizeof(uint), common[0].Offset);
        Assert.Equal(start + 2 * sizeof(uint) + commonCapacity * recordSize, key[0].Offset);
        Assert.Equal(start + byteLength - 2 * sizeof(uint) - recordSize, key[^1].Offset);
        Assert.Equal(commonCapacity - 1, common[^1].Record);
        Assert.Equal(keyCapacity - 1, key[^1].Record);
        using var document = fixture.Save.Open();
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }
}
