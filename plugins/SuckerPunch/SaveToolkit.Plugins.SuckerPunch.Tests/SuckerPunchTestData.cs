using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal static class SuckerPunchTestData
{
    internal const string TsushimaTitle = "PPSA02225";
    internal const string SuppliesId = "inventory/Supplies/quantity";
    internal const string ArrowId = "inventory/ArrowNormal/quantity";

    internal static ISaveFile Slot(string name, int generation = 0) => SampleSaves.Load(TsushimaTitle).AllDiskFiles
        .Single(file => file.Directory.Name == name && !file.Directory.IsBackup &&
            file.Directory.Generation == generation && file.FileName is "dis.dat" or "memory.dat");

    internal static ISaveDocument Open(ISaveFile file)
    {
        var document = new SuckerPunchPlugin().Open(file);
        _ = document.BuildView();
        return document;
    }

    internal static ISaveFile WithBytes(byte[] bytes, string title = TsushimaTitle) =>
        MemorySaveSet.SingleFile("dis.dat", bytes, title);

    internal static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    internal static uint Checksum(byte[] bytes)
    {
        // Independent scalar implementation for checking the complete exported content.
        var size = Word(bytes, 4);
        var sum = (ulong)size + Word(bytes, 0) + Word(bytes, 12);
        for (var index = 16; index < size; index++)
        {
            sum += bytes[index];
        }

        return unchecked((uint)sum);
    }

    internal static void RepairChecksum(byte[] bytes) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Checksum(bytes));

    internal static FileOutput PrimaryOutput(ISaveDocument document) => Assert.Single(document.Serialize(),
        output => !output.RelativePath.EndsWith("/sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));

    internal static TableNode Inventory(ISaveDocument document) =>
        (TableNode)((TabsNode)document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory").Children[1];

    internal static FieldNode Quantity(ISaveDocument document, string name) =>
        (FieldNode)Inventory(document).Rows.Single(row => row.Key == name).Cells["quantity"];

    internal static ValueEdit Edit(string id, long quantity) => new(id, FieldValue.Of(quantity));
}
