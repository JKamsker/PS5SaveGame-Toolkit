using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Fresh synthetic files and reference contexts, plus traversal of already materialized views.</summary>
internal static class RagnarokTestSupport
{
    public static ISaveFile GameplayFile(byte[] bytes) => MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330");

    public static ReferenceContext PinnedReferences(GodOfWarRagnarokPlugin? plugin = null)
    {
        plugin ??= new GodOfWarRagnarokPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, "PPSA08330");
    }

    public static byte[] LegacyGameplay()
    {
        var bytes = new byte[2048];
        Write(bytes, 8, 8);
        Write(bytes, 12, 0x00513040);
        Write(bytes, 16, 696);
        Write(bytes, 20, 0x008C0400);
        Write(bytes, 0x48, 35);
        Write(bytes, 0x4C, 0x00513040);
        bytes[0x50] = (byte)'A';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x440), 0x61C0);
        Write(bytes, 0x442, 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x446), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x44E), 2);
        Write(bytes, 0x45E, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x462), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x500), 0x00004895C643225F);
        Write(bytes, 0x508, 1);
        Convert.FromHexString("9E5240D27406E85A").CopyTo(bytes, 0x514);
        Write(bytes, 0x51C, 1234);
        Write(bytes, bytes.Length - 4, uint.MaxValue);
        return bytes;
    }

    public static IEnumerable<ViewNode> EagerNodes(ViewNode node)
    {
        yield return node;
        IEnumerable<ViewNode> children = node switch
        {
            TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children),
            SectionNode section => section.Children,
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(EagerNodes))
        {
            yield return child;
        }
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
