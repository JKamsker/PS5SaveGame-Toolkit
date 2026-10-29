using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal static class YoteiTestData
{
    public const string Title = "PPSA26344";
    public const string MonId = "yotei/inventory/Mon/quantity";
    public const string ArrowId = "yotei/inventory/ArrowNormal/quantity";
    public const string ActionId = "yotei/inventory/set-quantity";
    public static IReadOnlyList<ISaveFile> Files => SampleSaves.Load(Title).AllDiskFiles.Where(file => new SuckerPunchPlugin().Probe(file).IsMatch).ToArray();
    public static ISaveFile Slot(string directory) => Files.Single(file => file.Directory.Name == directory);
    public static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    public static ISaveFile WithBytes(byte[] bytes) => MemorySaveSet.SingleFile("dis.dat", bytes, Title);
    public static ISaveDocument Open(ISaveFile file)
    {
        var document = new SuckerPunchPlugin().Open(file);
        _ = document.BuildView();
        return document;
    }
    public static TableNode Inventory(ISaveDocument doc) => ((TabsNode)doc.BuildView()).Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>().Single();

    public static IReadOnlyList<(string Name, int Data)> ReadTable(byte[] bytes, int start, int count, int trailer)
    {
        var rows = new List<(string, int)>();
        var pos = start + 4;
        for (var index = 0; index < count; index++)
        {
            var length = checked((int)Word(bytes, pos));
            Assert.InRange(length, 1, 64);
            var name = Encoding.ASCII.GetString(bytes, pos + 4, length);
            pos += 4 + length;
            rows.Add((name, pos));
            pos += trailer;
        }
        return rows;
    }
    public static IReadOnlyList<(string Name, int Data)> InventoryRecords(byte[] bytes)
    {
        var start = bytes.AsSpan().IndexOf("\x30\0\0\0\x0b\0\0\0ArrowNormal"u8);
        Assert.True(start >= 0);
        return ReadTable(bytes, start, 48, 21);
    }
    public static uint Checksum(byte[] bytes)
    {
        ulong sum = Word(bytes, 0) + Word(bytes, 8);
        for (var pos = 16; pos < Word(bytes, 8); pos++) sum += bytes[pos];
        return unchecked((uint)sum);
    }
    public static void Repair(byte[] bytes) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), Checksum(bytes));
}
