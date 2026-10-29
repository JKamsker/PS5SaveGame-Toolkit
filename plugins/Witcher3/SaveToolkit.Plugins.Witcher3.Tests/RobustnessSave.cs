using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class RobustnessSave
{
    public const int VariablesOffset = 16;

    public static byte[] Payload(byte[] variables, params Sav3Entry[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("SAV3"u8);
        writer.Write(64);
        writer.Write(23);
        writer.Write(163);
        writer.Write(variables);
        var rb = (int)stream.Position;
        writer.Write("RB"u8);
        writer.Write(0);
        var nm = (int)stream.Position;
        writer.Write("NMMANU"u8);
        writer.Write(3);
        writer.Write(0);
        foreach (var name in new[] { "root", "value", "Int32" })
        {
            writer.Write((byte)name.Length);
            writer.Write(Encoding.ASCII.GetBytes(name));
        }
        writer.Write(0);
        writer.Write("ENOD"u8);
        var namesEnd = (int)stream.Position;
        writer.Write(nm + SyntheticSave.HeaderSize);
        writer.Write(rb + SyntheticSave.HeaderSize);
        writer.Write("SC"u8);
        var table = (int)stream.Position;
        writer.Write(entries.Length + 1);
        foreach (var entry in entries.Append(new Sav3Entry(nm + 2, namesEnd - nm - 2)))
        {
            writer.Write(entry.Offset + SyntheticSave.HeaderSize);
            writer.Write(entry.Size);
        }
        writer.Write(table + SyntheticSave.HeaderSize);
        writer.Write("SE"u8);
        return stream.ToArray();
    }

    public static byte[] Archive(int blocks)
    {
        var data = new byte[6 + blocks * 10];
        "SS"u8.CopyTo(data);
        WriteInt(data, 2, data.Length - 6);
        for (var offset = 6; offset < data.Length; offset += 10)
        {
            "BLCK"u8.CopyTo(data.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), 1);
            WriteInt(data, offset + 6, data.Length - offset - 10);
        }
        return data;
    }

    public static byte[] StoredChunk(byte[] stored, int expanded, int headerSize = 28)
    {
        var bytes = new byte[headerSize + stored.Length];
        "SNFHFZLC"u8.CopyTo(bytes);
        WriteInt(bytes, 8, 1);
        WriteInt(bytes, 12, headerSize);
        WriteInt(bytes, 16, stored.Length);
        WriteInt(bytes, 20, expanded);
        stored.CopyTo(bytes, headerSize);
        return bytes;
    }

    public static int Table(byte[] payload) => BinaryData.Int32(payload, payload.Length - 6) - SyntheticSave.HeaderSize;
    public static int Names(byte[] payload) => BinaryData.Int32(payload, Table(payload) - 10) - SyntheticSave.HeaderSize;
    public static int Roots(byte[] payload) => BinaryData.Int32(payload, Table(payload) - 6) - SyntheticSave.HeaderSize;
    public static void WriteInt(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    public static ISaveDocument Open(byte[] payload) => OpenContainer(SyntheticSave.Wrap(payload));
    public static ISaveDocument OpenContainer(byte[] bytes) => new Witcher3Plugin().Open(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018"));
}
