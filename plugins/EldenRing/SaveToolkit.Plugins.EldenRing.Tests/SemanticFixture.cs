namespace SaveToolkit.Plugins.EldenRing.Tests;

internal sealed class SemanticFixture
{
    public SyntheticSave Save { get; } = new([0]);
    public int Player => Save.Players[0];
    public int Held => Player + 0x3A4;
    public int ProjectileCount => Held + 0x9010 + 0x74 + 0x8C + 0x18;
    public int Storage => Held + 0x9010 + 0x2F3;
    public int RegionCount => Storage + 0x6010 + 0x100;

    public void Instance(int record, uint handle, uint id)
    {
        var entry = ItemMapReader.Read(Save.Bytes.AsSpan(SyntheticSave.Slot(0), SyntheticSave.SlotSize), 150)
            .Entries.Single(entry => entry.Record == record);
        Save.Write32(SyntheticSave.Slot(0) + entry.Offset, handle);
        Save.Write32(SyntheticSave.Slot(0) + entry.Offset + 4, id);
    }

    public void Row(string container, string group, int record, uint handle, uint quantity, uint inventoryIndex)
    {
        var start = container == "held" ? Held : Storage;
        var common = container == "held" ? 2688 : 1920;
        var counter = start + (group == "common" ? 0 : 4 + common * 12);
        var row = counter + 4 + record * 12;
        var count = EldenRingParser.ReadUInt32(Save.Bytes, counter);
        Save.Write32(counter, count + 1);
        Save.Write32(row, handle);
        Save.Write32(row + 4, quantity);
        Save.Write32(row + 8, inventoryIndex);
    }
}
