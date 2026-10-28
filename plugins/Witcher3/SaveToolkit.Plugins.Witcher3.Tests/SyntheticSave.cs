using System.Text;
using K4os.Compression.LZ4;

namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class SyntheticSave
{
    public const int HeaderSize = 64;
    public const int RootOffset = 16;
    public const int ScalarOffset = 20;
    public const int ArchiveOffset = 30;
    public const int BlockOffset = ArchiveOffset + 6 + 16;

    public static byte[] Create(Action<byte[]>? mutatePayload = null)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true);
        writer.Write("SAV3"u8);
        WriteVersion(writer);
        writer.Write("BS"u8);
        writer.Write((ushort)1);
        writer.Write("VL"u8);
        writer.Write((ushort)2);
        writer.Write((ushort)3);
        writer.Write(123U);
        using var archive = new MemoryStream();
        using var archiveWriter = new BinaryWriter(archive, Encoding.ASCII, leaveOpen: true);
        archiveWriter.Write("SXAP"u8);
        WriteVersion(archiveWriter);
        using var block = new MemoryStream();
        using var blockWriter = new BinaryWriter(block, Encoding.ASCII, leaveOpen: true);
        WriteValue(blockWriter, "AVAL", 2, 3, BitConverter.GetBytes(42U));
        WriteValue(blockWriter, "PORP", 6, 7, [0x85, .. "hello"u8]);
        WriteValue(blockWriter, "PORP", 8, 9, [1]);
        archiveWriter.Write("BLCK"u8);
        archiveWriter.Write((ushort)4);
        archiveWriter.Write(checked((int)block.Length));
        archiveWriter.Write(block.ToArray());
        writer.Write("SS"u8);
        writer.Write(checked((int)archive.Length));
        writer.Write(archive.ToArray());
        var rootEnd = checked((int)data.Position);
        var rb = checked((int)data.Position);
        writer.Write("RB"u8);
        writer.Write(1);
        writer.Write((ushort)1);
        writer.Write(HeaderSize + RootOffset);
        var nm = checked((int)data.Position);
        writer.Write("NMMANU"u8);
        string[] names = ["Root", "count", "Uint32", "example", "Int32", "label", "String", "enabled", "Bool"];
        writer.Write(names.Length);
        writer.Write(0);
        foreach (var name in names)
        {
            writer.Write((byte)name.Length);
            writer.Write(Encoding.ASCII.GetBytes(name));
        }

        writer.Write(0);
        writer.Write("ENOD"u8);
        var namesEnd = checked((int)data.Position);
        writer.Write(nm + HeaderSize);
        writer.Write(rb + HeaderSize);
        writer.Write("SC"u8);
        var table = checked((int)data.Position);
        writer.Write(4);
        WriteEntry(writer, RootOffset, rootEnd - RootOffset);
        WriteEntry(writer, ScalarOffset, 10);
        WriteEntry(writer, ArchiveOffset, rootEnd - ArchiveOffset);
        WriteEntry(writer, nm + 2, namesEnd - nm - 2);
        writer.Write(table + HeaderSize);
        writer.Write("SE"u8);
        var payload = data.ToArray();
        mutatePayload?.Invoke(payload);
        return Wrap(payload);
    }

    public static byte[] Wrap(byte[] payload)
    {
        var middle = payload.Length / 2;
        var first = Compress(payload.AsSpan(0, middle));
        var second = Compress(payload.AsSpan(middle));
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write("SNFHFZLC"u8);
        writer.Write(2);
        writer.Write(HeaderSize);
        writer.Write(first.Length);
        writer.Write(middle);
        writer.Write(HeaderSize + first.Length);
        writer.Write(second.Length);
        writer.Write(payload.Length - middle);
        writer.Write(0); // Both supported end-offset conventions in one file.
        writer.Write(Enumerable.Repeat((byte)0xCC, HeaderSize - 40).ToArray());
        writer.Write(first);
        writer.Write(second);
        writer.Write(new byte[] { 0x55, 0xAA }); // Opaque container trailer must survive no-op.
        return output.ToArray();
    }

    private static byte[] Compress(ReadOnlySpan<byte> payload)
    {
        var result = new byte[LZ4Codec.MaximumOutputSize(payload.Length)];
        var length = LZ4Codec.Encode(payload, result);
        return result[..length];
    }

    private static void WriteVersion(BinaryWriter writer)
    {
        writer.Write(64);
        writer.Write(23);
        writer.Write(163);
    }

    private static void WriteEntry(BinaryWriter writer, int offset, int size)
    {
        writer.Write(offset + HeaderSize);
        writer.Write(size);
    }

    private static void WriteValue(BinaryWriter writer, string tag, ushort name, ushort type, byte[] bytes)
    {
        writer.Write(Encoding.ASCII.GetBytes(tag));
        writer.Write(name);
        writer.Write(type);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
