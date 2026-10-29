using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Authored nested DDL with absolute name pointers and file-aligned type tables.</summary>
internal sealed class DdlCompoundFixture
{
    private const int DescriptorEnd = 28;
    private const int ObjectHeaderSize = 16;
    internal sealed record Field(string Name, DdlValueKind Kind, byte[] Payload, int Count = 1)
    {
        internal Field[][] Children { get; init; } = [];
        internal static Field Objects(string name, params Field[][] children) =>
            new(name, DdlValueKind.Object, [], children.Length) { Children = children };
    }

    internal DdlCompoundFixture(params Field[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(new byte[DescriptorEnd]);
        var names = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var field in AllFields(fields))
        {
            if (!names.TryAdd(field.Name, (uint)stream.Position)) continue;
            writer.Write(Encoding.UTF8.GetBytes(field.Name));
            writer.Write((byte)0);
        }
        Align();
        SectionOffset = checked((int)stream.Position);
        WriteObject(fields);
        Bytes = stream.ToArray();
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)Bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(12), 1);
        Write(16, DdlSections.PreferencesTag); Write(20, (uint)SectionOffset);
        Write(24, (uint)(Bytes.Length - SectionOffset));

        void Align()
        {
            while (stream.Position % sizeof(uint) != 0) writer.Write((byte)0);
        }

        void WriteObject(Field[] objectFields)
        {
            var start = stream.Position;
            writer.Write(0u); writer.Write(0x03150044u); writer.Write((uint)objectFields.Length); writer.Write(0u);
            foreach (var field in objectFields)
            {
                writer.Write(DdlReader.NameHash(Encoding.UTF8.GetBytes(field.Name)));
                writer.Write(((uint)field.Kind << 24) | ((uint)field.Count << 4));
            }
            foreach (var field in objectFields) writer.Write(names[field.Name]);
            if (objectFields.Length > 0) Align();
            foreach (var field in objectFields)
            {
                if (field.Kind == DdlValueKind.Object)
                    foreach (var child in field.Children) WriteObject(child);
                else writer.Write(field.Payload);
            }
            var end = stream.Position;
            stream.Position = start + 12;
            writer.Write((uint)(end - start - ObjectHeaderSize));
            stream.Position = end;
        }
    }

    internal byte[] Bytes { get; }
    internal int SectionOffset { get; }
    internal void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);

    internal static DdlCompoundFixture NestedArrays() => new(
        new("Flag", DdlValueKind.Bool, [1]),
        Field.Objects("Children",
            [new("Strings", DdlValueKind.String,
                [.. DdlTestFixture.StringPayload("é🎮"), .. DdlTestFixture.StringPayload("tail")], Count: 2),
             new("Numbers", DdlValueKind.UInt16, [1, 0, 2, 0], Count: 2)],
            [Field.Objects("Nested", new Field[] { new("Strings", DdlValueKind.String, DdlTestFixture.StringPayload("nested")) })]),
        new("Tail", DdlValueKind.UInt32, [0x78, 0x56, 0x34, 0x12]));

    private static IEnumerable<Field> AllFields(IEnumerable<Field> fields)
    {
        foreach (var field in fields)
        {
            yield return field;
            foreach (var child in field.Children.SelectMany(AllFields)) yield return child;
        }
    }
}
