using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record Sav3Entry(int Offset, int Size);

internal sealed record Sav3Directory(IReadOnlyList<string> Names, IReadOnlyList<Sav3Entry> Entries, int VariableTableOffset)
{
    public static Sav3Directory Parse(WitcherContainer container)
    {
        var data = container.Payload.AsSpan();
        const int minimumSize = 32;
        if (data.Length < minimumSize || !data[..4].SequenceEqual("SAV3"u8) || !data[^2..].SequenceEqual("SE"u8))
        {
            throw new SaveFormatException("Witcher 3 expanded payload lacks its SAV3 header or SE footer.");
        }

        var table = BinaryData.PayloadOffset(data, data.Length - 6, container.HeaderSize);
        if (table < minimumSize || table > data.Length - 10 || !BinaryData.Slice(data, table - 2, 2).SequenceEqual("SC"u8))
        {
            throw new SaveFormatException("Invalid SAV3 variable table pointer or SC marker.");
        }

        var nm = BinaryData.PayloadOffset(data, table - 10, container.HeaderSize);
        var rb = BinaryData.PayloadOffset(data, table - 6, container.HeaderSize);
        if (rb < 16 || rb > nm - 6 || nm > table - 24 ||
            !BinaryData.Slice(data, rb, 2).SequenceEqual("RB"u8) ||
            !BinaryData.Slice(data, nm, 6).SequenceEqual("NMMANU"u8))
        {
            throw new SaveFormatException("Invalid SAV3 RB/NM name-table pointers.");
        }

        var rbCount = BinaryData.Int32(data, rb + 2);
        if (rbCount < 0 || rbCount > (nm - rb - 6) / 6)
        {
            throw new SaveFormatException("Invalid SAV3 RB table size.");
        }

        for (var index = 0; index < rbCount; index++)
        {
            var entry = rb + 6 + index * 6;
            var offset = BinaryData.PayloadOffset(data, entry + 2, container.HeaderSize);
            // The uint16 is opaque: upstream calls it size, but sample values resemble name indices.
            if (offset < 16 || offset >= rb)
            {
                throw new SaveFormatException("SAV3 RB entry exceeds the variable region.");
            }
        }

        var names = ReadNames(BinaryData.Slice(data, nm + 2, table - 10 - nm - 2));
        var count = BinaryData.Int32(data, table);
        const int maxEntries = 200_000;
        if (count is < 1 or > maxEntries || (long)table + 4 + (long)count * 8 != data.Length - 6)
        {
            throw new SaveFormatException("SAV3 variable table length does not match the SE footer.");
        }

        var entries = new Sav3Entry[count];
        for (var index = 0; index < count; index++)
        {
            var entry = table + 4 + index * 8;
            var offset = BinaryData.PayloadOffset(data, entry, container.HeaderSize);
            var size = BinaryData.Int32(data, entry + 4);
            if (offset < 16 || size < 1 || offset > table - 10 - size)
            {
                throw new SaveFormatException("SAV3 variable entry exceeds its data region.");
            }

            entries[index] = new Sav3Entry(offset, size);
        }

        Array.Sort(entries, (left, right) => left.Offset.CompareTo(right.Offset));
        return new Sav3Directory(names, entries, table);
    }

    private static string[] ReadNames(ReadOnlySpan<byte> data)
    {
        var count = BinaryData.Int32(data, 4);
        if (count < 1 || count > ushort.MaxValue || count > data.Length - 20)
        {
            throw new SaveFormatException("Invalid SAV3 MANU name count.");
        }

        var names = new string[count];
        var position = 12;
        for (var index = 0; index < count; index++)
        {
            var length = BinaryData.Slice(data, position++, 1)[0];
            names[index] = BinaryData.Ascii(BinaryData.Slice(data, position, length));
            position += length;
        }

        if (position != data.Length - 8 || !data[^4..].SequenceEqual("ENOD"u8))
        {
            throw new SaveFormatException("Invalid SAV3 MANU name-table terminator.");
        }

        return names;
    }
}
