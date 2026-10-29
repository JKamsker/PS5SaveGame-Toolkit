using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Append-only MANU growth for progression properties, retaining every original name index.</summary>
internal sealed class NameTableEdits(IReadOnlyList<string> original)
{
    private const int TrailerSize = 8;
    private readonly List<string> _names = original.ToList();

    public int Index(string name)
    {
        var index = _names.IndexOf(name);
        if (index < 0)
        {
            if (_names.Count == ushort.MaxValue) { throw new SaveFormatException("Witcher name dictionary is full."); }
            _names.Add(name);
            index = _names.Count - 1;
        }

        return index + 1;
    }

    public byte[] IntegerProperty(string name, int value) =>
        TaggedRecord.Property(Index(name), Index("Int32"), BinaryData.EncodeInt32(value));

    public Sav3Patch? Patch(byte[] data, Sav3Directory directory, int headerSize)
    {
        if (_names.Count == original.Count) { return null; }
        var table = directory.VariableTableOffset;
        var nm = BinaryData.PayloadOffset(data, table - 10, headerSize);
        var oldLength = table - 10 - nm - 2;
        using var manu = new MemoryStream();
        manu.Write(data.AsSpan(nm + 2, oldLength - TrailerSize));
        foreach (var name in _names.Skip(original.Count))
        {
            manu.WriteByte(checked((byte)name.Length));
            manu.Write(Encoding.ASCII.GetBytes(name));
        }

        manu.Write(data.AsSpan(nm + 2 + oldLength - TrailerSize, TrailerSize));
        var bytes = manu.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), _names.Count);
        return new Sav3Patch(nm + 2, oldLength, bytes);
    }
}
