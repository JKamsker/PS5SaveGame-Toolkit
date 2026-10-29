using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Small wire-format fixtures; no private saves or parser-derived offsets.</summary>
internal sealed class DdlTestFixture
{
    internal const int ObjectHeaderSize = 16;
    internal const uint SectionTag = DdlSections.PreferencesTag;
    private const int Dat1DescriptorEnd = 28;
    private const int FieldDescriptorSize = 12;
    private const int HashAndTypeSize = 8;
    private readonly int[] _valueOffsets;
    private readonly int[] _nameOffsets;

    internal sealed record Field(string Name, DdlValueKind Kind, byte[] Payload, int Count = 1);

    internal DdlTestFixture(params Field[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(new byte[Dat1DescriptorEnd]);
        _nameOffsets = new int[fields.Length];
        for (var index = 0; index < fields.Length; index++)
        {
            _nameOffsets[index] = checked((int)stream.Position);
            writer.Write(Encoding.UTF8.GetBytes(fields[index].Name));
            writer.Write((byte)0);
        }
        Align();
        SectionOffset = checked((int)stream.Position);
        writer.Write(0u);
        writer.Write(0x0315_0044u);
        writer.Write((uint)fields.Length);
        writer.Write(0u); // Patched with the final object body size below.
        foreach (var field in fields)
        {
            writer.Write(DdlReader.NameHash(Encoding.UTF8.GetBytes(field.Name)));
            writer.Write(((uint)field.Kind << 24) | ((uint)field.Count << 4));
        }
        foreach (var offset in _nameOffsets) writer.Write((uint)offset);
        _valueOffsets = new int[fields.Length];
        for (var index = 0; index < fields.Length; index++)
        {
            _valueOffsets[index] = checked((int)stream.Position);
            writer.Write(fields[index].Payload);
        }
        Align();
        Bytes = stream.ToArray();
        Write(0, 0x4441_5431);
        Write(4, 8);
        Write(8, (uint)Bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(12), 1);
        Write(16, SectionTag);
        Write(20, (uint)SectionOffset);
        Write(24, (uint)(Bytes.Length - SectionOffset));
        Write(SectionOffset + 12, (uint)(Bytes.Length - SectionOffset - ObjectHeaderSize));

        void Align()
        {
            while (stream.Position % sizeof(uint) != 0) writer.Write((byte)0);
        }
    }

    internal byte[] Bytes { get; }
    internal int SectionOffset { get; }
    internal int DescriptorOffset(int index) => SectionOffset + ObjectHeaderSize + index * HashAndTypeSize;
    internal int NamePointerOffset(int index) => SectionOffset + ObjectHeaderSize +
        _nameOffsets.Length * HashAndTypeSize + index * sizeof(uint);
    internal int NameOffset(int index) => _nameOffsets[index];
    internal int ValueOffset(int index) => _valueOffsets[index];
    internal int ValuesOffset => SectionOffset + ObjectHeaderSize + _nameOffsets.Length * FieldDescriptorSize;
    internal void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);

    internal static DdlTestFixture Root() => new(
        new("CareerBolts", DdlValueKind.UInt32, [0x78, 0x56, 0x34, 0x12]),
        new("Playtime", DdlValueKind.Float, [0, 0, 0xC0, 0x3F]),
        new("IsChallengeMode", DdlValueKind.Bool, [1]),
        new("Version", DdlValueKind.String, StringPayload("abc")),
        new("Empty", DdlValueKind.Null, [0], Count: 0));

    internal static byte[] StringPayload(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        var payloadSize = text.Length + 1;
        var bytes = new byte[ObjectHeaderSize + payloadSize + (4 - payloadSize % 4) % 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)text.Length);
        text.CopyTo(bytes, ObjectHeaderSize);
        return bytes;
    }
}
