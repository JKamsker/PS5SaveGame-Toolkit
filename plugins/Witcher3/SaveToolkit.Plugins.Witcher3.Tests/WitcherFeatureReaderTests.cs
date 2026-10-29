using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherFeatureReaderTests
{
    [Fact]
    public void AbilityReaderRetainsUnnamedDefinitionsAndUnknownProperties()
    {
        var (node, data, names) = AbilityFixture();
        var array = Assert.Single(AbilityReader.Read(node, data, names));
        Assert.Equal(3, array.Entries.Count);
        Assert.Equal("unnamed-slot-0", array.Entries[0].Id);
        Assert.Equal("S_Sword_1", array.Entries[1].Id);
        Assert.Equal("unnamed-slot-2", array.Entries[2].Id);
        Assert.Equal([true, false, false], array.Entries.Select(e => e.IsEmpty));
        Assert.Equal("UnmappedType", Assert.Single(array.Entries[2].Record.Properties).Type);
        Assert.Equal(new byte[] { 0x71, 0xFF }, Assert.Single(array.Entries[2].Record.Properties).Value.ToArray());
    }

    [Theory]
    [InlineData(0)] // Wrong handle prefix.
    [InlineData(6)] // Wrong class name.
    [InlineData(17)] // Impossible array count.
    [InlineData(33)] // Invalid name index in identified record.
    public void AbilityReaderRejectsCorruptFramingAndNameIndices(int offset)
    {
        var (node, data, names) = AbilityFixture();
        data[offset] = 0xFF;
        Assert.Throws<SaveFormatException>(() => AbilityReader.Read(node, data, names));
    }

    [Fact]
    public void AbilityReaderRejectsUnconsumedArrayBytesAndTruncatedManager()
    {
        var (node, data, names) = AbilityFixture();
        // Three records are present; declaring two must fail exact-consumption validation.
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(17), 2);
        Assert.Throws<SaveFormatException>(() => AbilityReader.Read(node, data, names));
        (node, data, names) = AbilityFixture();
        Assert.Throws<SaveFormatException>(() => AbilityReader.Read(node with { ValueSize = data.Length - 1 }, data, names));
    }

    [Fact]
    public void GwentReaderAcceptsBoundedEmptyCollectionsAndExcludesDeckNodes()
    {
        var (manager, nodes, data) = GwentFixture(empty: true);
        Assert.Empty(GwentReader.ReadCollection(manager, nodes, data));
        (manager, nodes, data) = GwentFixture();
        var card = Assert.Single(GwentReader.ReadCollection(manager, nodes, data));
        Assert.Equal((3, 2, 20), (card.Index, card.Copies, card.CopiesOffset));
        // A bogus deck card after SBSelectedDeckIndex must never become collection evidence.
        nodes.Add(new Sav3Node(nodes.Count, 80, 4, "VL", "cardIndex", "Int32", 80, 4));
        manager.Children.Add(nodes.Count - 1);
        Assert.Single(GwentReader.ReadCollection(manager, nodes, data));
    }

    [Theory]
    [InlineData(8, 2)] // Declared ordinary count disagrees.
    [InlineData(20, -1)] // Negative copies.
    [InlineData(28, 1)] // Declared leader count disagrees.
    public void GwentReaderRejectsCorruptCounts(int offset, int value)
    {
        var (manager, nodes, data) = GwentFixture();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
        Assert.Throws<SaveFormatException>(() => GwentReader.ReadCollection(manager, nodes, data));
    }

    [Fact]
    public void GwentReaderRejectsUnpairedRecordsAndMissingBoundary()
    {
        var (manager, nodes, data) = GwentFixture();
        nodes[3].Children.RemoveAt(1);
        Assert.Throws<SaveFormatException>(() => GwentReader.ReadCollection(manager, nodes, data));
        (manager, nodes, data) = GwentFixture();
        manager.Children.Remove(8);
        Assert.Throws<SaveFormatException>(() => GwentReader.ReadCollection(manager, nodes, data));
    }

    private static (Sav3Node Node, byte[] Data, string[] Names) AbilityFixture()
    {
        string[] names = ["W3PlayerAbilityManager", "skills", "array:2,0,SSkill", "skillType", "ESkill", "S_Sword_1", "opaque", "UnmappedType"];
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[] { 0, 1 });
        writer.Write(7); // Opaque handle value.
        writer.Write((ushort)1);
        writer.Write((byte)0);
        writer.Write((ushort)2);
        writer.Write((ushort)3);
        writer.Write(37); // Four size bytes + four count bytes + three record bodies (3 + 13 + 13).
        writer.Write(3);
        writer.Write(new byte[] { 0, 0, 0 });
        writer.Write((byte)0);
        writer.Write((ushort)4);
        writer.Write((ushort)5);
        writer.Write(6);
        writer.Write((ushort)6);
        writer.Write((ushort)0);
        writer.Write((byte)0);
        writer.Write((ushort)7);
        writer.Write((ushort)8);
        writer.Write(6);
        writer.Write(new byte[] { 0x71, 0xFF });
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        var data = buffer.ToArray();
        return (new Sav3Node(0, 0, data.Length, "PORP", "abilityManager", "handle:W3AbilityManager", 0, data.Length), data, names);
    }

    private static (Sav3Node Manager, List<Sav3Node> Nodes, byte[] Data) GwentFixture(bool empty = false)
    {
        var nodes = new List<Sav3Node>();
        var data = new byte[100];
        Sav3Node Add(string name, string tag = "BS", string? type = null, int value = 0)
        {
            var offset = nodes.Count * sizeof(int);
            var node = new Sav3Node(nodes.Count, offset, sizeof(int), tag, name, type, offset, sizeof(int));
            nodes.Add(node);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
            return node;
        }

        var manager = Add("CR4GwintManager");
        var group = Add("SBCollectionCardSize");
        group.Children.Add(Add("Size", "VL", "Uint32", empty ? 0 : 1).Id);
        var record = Add("SBCollectionCard");
        record.Children.Add(Add("cardIndex", "VL", "Int32", 3).Id);
        record.Children.Add(Add("numCopies", "VL", "Int32", 2).Id);
        if (!empty) { group.Children.Add(record.Id); }
        var leaders = Add("SBLeaderCollectionCardSize");
        leaders.Children.Add(Add("Size", "VL", "Uint32").Id);
        var boundary = Add("SBSelectedDeckIndex");
        manager.Children.AddRange([group.Id, leaders.Id, boundary.Id]);
        return (manager, nodes, data);
    }
}
