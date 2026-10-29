using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

// Wire-format reference: WolvenKit RedPackageReader/RedPackageStructs (see README).
// Unknown chunks and property bodies are retained verbatim; this is not an RTTI serializer.
internal sealed class RedPackage
{
    private const int HeaderSize = 24;
    private const int MaximumEntries = 16_384;
    private readonly byte[] _bytes;
    private readonly int _baseOffset;
    private readonly int _descriptors;
    private readonly int[] _offsets;

    public RedPackage(byte[] bytes, bool scriptable)
    {
        _bytes = bytes;
        BinaryData.Slice(bytes, 0, HeaderSize);
        if (bytes[0] != 4 || bytes[1] != 0 || UInt16(bytes, 2) != 6)
        {
            throw new SaveFormatException("Progression editing requires verified RED package version 4 / flags 0 / sections 6.");
        }

        _baseOffset = HeaderSize;
        if (scriptable)
        {
            var roots = Count(bytes, HeaderSize);
            _baseOffset += 4 + roots * sizeof(ulong);
        }

        var nameDesc = BinaryData.Int32(bytes, 8);
        var nameData = BinaryData.Int32(bytes, 12);
        _descriptors = BinaryData.Int32(bytes, 16);
        var chunkData = BinaryData.Int32(bytes, 20);
        if (nameDesc != 0 || nameData < nameDesc || _descriptors < nameData || chunkData < _descriptors ||
            nameData % 4 != 0 || (chunkData - _descriptors) % 8 != 0 ||
            nameData / 4 > MaximumEntries || (chunkData - _descriptors) / 8 > MaximumEntries)
        {
            throw new SaveFormatException("Invalid RED package pool bounds.");
        }

        BinaryData.Slice(bytes, _baseOffset, chunkData);
        Names = ReadNames(bytes, nameDesc, nameData);
        (Types, _offsets) = ReadChunks(bytes, chunkData);
    }

    private IReadOnlyList<string> ReadNames(byte[] bytes, int nameDesc, int nameData)
    {
        var names = new List<string>();
        for (var entry = nameDesc; entry < nameData; entry += 4)
        {
            var descriptor = BinaryData.UInt32(bytes, _baseOffset + entry);
            var offset = (int)(descriptor & 0x00FFFFFF);
            var size = (int)(descriptor >> 24);
            if (offset < nameData || offset > _descriptors - size || size < 1)
            {
                throw new SaveFormatException("Invalid RED package name descriptor.");
            }

            var raw = BinaryData.Slice(bytes, _baseOffset + offset, size);
            if (raw[^1] != 0 || raw[..^1].ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
            {
                throw new SaveFormatException("Unsupported RED package name encoding.");
            }

            names.Add(Encoding.ASCII.GetString(raw[..^1]));
        }

        return names;
    }

    private (IReadOnlyList<string> Types, int[] Offsets) ReadChunks(byte[] bytes, int chunkData)
    {
        var types = new List<string>();
        var offsets = new List<int>();
        for (var entry = _descriptors; entry < chunkData; entry += 8)
        {
            var type = BinaryData.Int32(bytes, _baseOffset + entry);
            var offset = BinaryData.Int32(bytes, _baseOffset + entry + 4);
            if (offset < chunkData || offset > bytes.Length - _baseOffset - 2 ||
                offsets.Count > 0 && offset <= offsets[^1] - _baseOffset)
            {
                throw new SaveFormatException("Invalid RED package chunk descriptor.");
            }

            types.Add(Name(type));
            offsets.Add(_baseOffset + offset);
        }

        return (types, offsets.ToArray());
    }

    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<string> Types { get; }
    public string Name(int index) => index >= 0 && index < Names.Count ? Names[index]
        : throw new SaveFormatException("Invalid RED package name index.");
    public ushort NameIndex(string name)
    {
        var index = Names.ToList().IndexOf(name);
        return index >= 0 ? checked((ushort)index) : throw new SaveFormatException($"RED package lacks name '{name}'.");
    }

    public PackageClass Chunk(int index) => new(this, _bytes, _offsets[index],
        index + 1 < _offsets.Length ? _offsets[index + 1] : _bytes.Length);

    public RedPackage WithNames(IEnumerable<string> required)
    {
        var added = required.Where(name => !Names.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        if (added.Length == 0) return this;
        if (Names.Count + added.Length > MaximumEntries) throw new SaveFormatException("RED name count exceeds package limits.");
        return new RedPackage(PackageNames.Append(_bytes, _baseOffset, BinaryData.Int32(_bytes, 12), _descriptors,
            Types.Count, added), _baseOffset != HeaderSize);
    }

    public byte[] Rebuild(int chunkIndex, byte[] replacement)
    {
        var start = _offsets[chunkIndex];
        var end = chunkIndex + 1 < _offsets.Length ? _offsets[chunkIndex + 1] : _bytes.Length;
        if (_bytes.AsSpan(start, end - start).SequenceEqual(replacement))
        {
            return _bytes.ToArray();
        }

        var delta = replacement.Length - (end - start);
        var result = new byte[checked(_bytes.Length + delta)];
        _bytes.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result, start);
        _bytes.AsSpan(end).CopyTo(result.AsSpan(start + replacement.Length));
        for (var index = chunkIndex + 1; index < _offsets.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(_baseOffset + _descriptors + index * 8 + 4),
                _offsets[index] - _baseOffset + delta);
        }

        _ = new RedPackage(result, _baseOffset != HeaderSize);
        return result;
    }

    internal static ushort UInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(BinaryData.Slice(bytes, offset, 2));

    internal static int Count(ReadOnlySpan<byte> bytes, int offset)
    {
        var count = BinaryData.Int32(bytes, offset);
        return count is >= 0 and <= MaximumEntries ? count : throw new SaveFormatException("Invalid RED package entry count.");
    }
}
