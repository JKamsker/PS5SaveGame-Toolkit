using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>Handwritten little-endian vectors; independent of the reader's constants/helpers.</summary>
internal static class LsmfFixture
{
    internal static byte[] Create(params (string Name, int Element, int Rows, byte[] Data)[] columns)
    {
        var arenaBytes = columns.Sum(c => c.Data.Length);
        var directory = (48 + arenaBytes + 7) / 8 * 8;
        var names = columns.Select(c => Encoding.UTF8.GetBytes(c.Name)).ToArray();
        var namesBytes = names.Sum(n => n.Length);
        var descriptorRelative = (namesBytes + 7) / 8 * 8;
        var directoryBytes = descriptorRelative + 48 * columns.Length;
        var bytes = new byte[directory + directoryBytes + 3]; // unknown trailing data
        Convert.FromHexString("4C534D4601010008").CopyTo(bytes, 0);
        U64(bytes, 16, (ulong)(directory - 48));
        U64(bytes, 24, (ulong)directoryBytes);
        U32(bytes, 32, (uint)descriptorRelative);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(36), (ushort)columns.Length);
        var arena = 48;
        var nameOffset = 0;
        for (var index = 0; index < columns.Length; index++)
        {
            var column = columns[index];
            names[index].CopyTo(bytes, directory + nameOffset);
            var descriptor = directory + descriptorRelative + index * 48;
            U64(bytes, descriptor, (ulong)nameOffset);
            U64(bytes, descriptor + 8, (ulong)names[index].Length);
            U64(bytes, descriptor + 16, 0xFEDCBA9876543210);
            U32(bytes, descriptor + 24, (uint)column.Element);
            U32(bytes, descriptor + 28, uint.MaxValue); // flags remain opaque
            U64(bytes, descriptor + 32, (ulong)column.Rows);
            U64(bytes, descriptor + 40, (ulong)(arena - 48));
            column.Data.CopyTo(bytes, arena);
            arena += column.Data.Length;
            nameOffset += names[index].Length;
        }

        bytes[^1] = 0xa7;
        return bytes;
    }

    internal static byte[] Entities(int copies = 2) => Create(("core.v0.EntityId", 16, copies,
        Enumerable.Range(0, copies).SelectMany(_ => Convert.FromHexString("064FD73E603CDC42F68334F047CB79C6")).ToArray()),
        ("unknown.v91.Component", 4, 1, [0xde, 0xad, 0xbe, 0xef]));

    internal static int Descriptor(byte[] bytes, int index = 0) =>
        (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16)) + 48 +
        (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)) + index * 48;

    internal static byte[] Resource(byte[] blob)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write("LSOF"u8);
        writer.Write(7u);
        writer.Write(0UL);
        foreach (var size in new[] { 14, 0, 12, 12, blob.Length })
        {
            writer.Write(size);
            writer.Write(0); // uncompressed
        }

        writer.Write(0UL); // codec/reserved/compact format
        writer.Write(1u); // one names bucket
        writer.Write((ushort)1);
        writer.Write((ushort)6);
        writer.Write("NewAge"u8);
        writer.Write(0u); // node name
        writer.Write(0); // first attribute
        writer.Write(-1); // root
        writer.Write(0u); // attribute name
        writer.Write(((uint)blob.Length << 6) | 25u); // typed ScratchBuffer
        writer.Write(500); // owner tag, deliberately not node index
        writer.Write(blob);
        return output.ToArray();
    }

    internal static void U64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
    internal static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
