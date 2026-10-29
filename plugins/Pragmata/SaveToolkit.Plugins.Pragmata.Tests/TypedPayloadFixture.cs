using SaveToolkit.Plugins.Pragmata.Format;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Small independently authored typed records, aligned relative to the DSSS header.</summary>
internal sealed class TypedPayloadFixture : IDisposable
{
    internal const uint OptionalHash = 0x12345678;
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;

    public TypedPayloadFixture() => _writer = new BinaryWriter(_stream);
    public byte[] Bytes => _stream.ToArray();
    public void Word(uint value) => _writer.Write(value);
    public void Root(uint hash, int fields) { Word(hash); Class(hash, fields); }
    public void Class(uint hash, int fields) { Word((uint)fields); Word(hash); }
    public void Field(uint hash, int type) { Word(hash); _writer.Write(type); }

    public void Primitive(uint hash, int type, byte[] value)
    {
        Field(hash, type);
        Align(4);
        Word((uint)value.Length);
        Align(value.Length);
        _writer.Write(value);
        Align(4);
    }

    public void String(uint hash, byte[] utf16)
    {
        Field(hash, FieldTags.String);
        Align(4);
        Word((uint)utf16.Length / sizeof(char));
        _writer.Write(utf16);
        Align(4);
    }

    public void Array(uint hash, int memberType, int memberSize, int count, int kind, bool marker = false)
    {
        Field(hash, FieldTags.Array);
        Align(4);
        _writer.Write(memberType);
        Word((uint)memberSize);
        Word((uint)count);
        _writer.Write(kind);
        if (marker)
        {
            Word(0xffeeffee);
            for (var index = 0; index < count; index++) Word((uint)index);
        }
    }

    public static byte[] Gameplay(Action<TypedPayloadFixture>? optionalHeader = null)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(KnownHashes.DetailRoot, 1);
        payload.Primitive(KnownHashes.Slot, FieldTags.Int32, BitConverter.GetBytes(0));
        payload.Root(KnownHashes.HeaderRoot, optionalHeader is null ? 4 : 5);
        payload.Primitive(KnownHashes.SaveDate, FieldTags.Int64, BitConverter.GetBytes(0L));
        payload.Primitive(KnownHashes.Playtime, FieldTags.UInt64, BitConverter.GetBytes(100UL));
        payload.Primitive(KnownHashes.Cleared, FieldTags.Boolean, [0]);
        payload.Primitive(KnownHashes.Difficulty, FieldTags.UInt32, BitConverter.GetBytes(0U));
        optionalHeader?.Invoke(payload);
        payload.Root(KnownHashes.GameRoot, 1);
        payload.Array(KnownHashes.GameSections, FieldTags.Class, 0, 2, 1);
        payload.Class(KnownHashes.InventoryClass, 2);
        payload.Array(KnownHashes.Items, FieldTags.Class, 0, 0, 1, marker: true);
        payload.Primitive(KnownHashes.EquippedItem, FieldTags.UInt32, BitConverter.GetBytes(0U));
        payload.Class(KnownHashes.ClockClass, 3);
        payload.Primitive(KnownHashes.Elapsed, FieldTags.UInt64, BitConverter.GetBytes(100UL));
        payload.Primitive(KnownHashes.Demo, FieldTags.UInt64, BitConverter.GetBytes(0UL));
        payload.Primitive(KnownHashes.Pause, FieldTags.UInt64, BitConverter.GetBytes(0UL));
        return payload.Bytes;
    }

    private void Align(int size)
    {
        var target = ((_stream.Position + 16 + size - 1) & ~(size - 1)) - 16;
        while (_stream.Position < target) _writer.Write((byte)0);
    }

    public void Dispose() { _writer.Dispose(); _stream.Dispose(); }
}
