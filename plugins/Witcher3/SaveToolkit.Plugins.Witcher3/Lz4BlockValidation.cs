using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Counts raw LZ4 output without allocating it. K4os still owns decompression.</summary>
internal static class Lz4BlockValidation
{
    private const int MinimumMatchLength = 4;
    private const int MinimumLastLiterals = 5;
    private const int MinimumMatchStartDistance = 12;

    public static void Validate(ReadOnlySpan<byte> data, int expandedSize)
    {
        var position = 0;
        var written = 0;
        var hasMatch = false;
        while (position < data.Length)
        {
            var token = data[position++];
            var literals = ReadLength(data, ref position, token >> 4, expandedSize - written);
            if (literals > data.Length - position) { throw InvalidBlock(); }
            position += literals;
            written += literals;
            if (position == data.Length)
            {
                if (written != expandedSize || hasMatch && literals < MinimumLastLiterals) { throw InvalidBlock(); }
                return;
            }

            if (data.Length - position < sizeof(ushort)) { throw InvalidBlock(); }
            var distance = BinaryData.UInt16(data, position);
            position += sizeof(ushort);
            if (distance == 0 || distance > written || written > expandedSize - MinimumMatchStartDistance)
            {
                throw InvalidBlock();
            }

            var match = ReadLength(data, ref position, token & 0x0F, expandedSize - written - MinimumMatchLength);
            written += MinimumMatchLength + match;
            hasMatch = true;
        }

        // A final literal sequence is required, even when a match filled the declared output.
        throw InvalidBlock();
    }

    private static int ReadLength(ReadOnlySpan<byte> data, ref int position, int length, int available)
    {
        const int extendedLength = 15;
        if (length > available) { throw InvalidBlock(); }
        if (length != extendedLength) { return length; }
        byte next;
        do
        {
            if (position == data.Length) { throw InvalidBlock(); }
            next = data[position++];
            if (next > available - length) { throw InvalidBlock(); }
            length += next;
        } while (next == byte.MaxValue);
        return length;
    }

    private static SaveFormatException InvalidBlock() => new("Invalid Witcher 3 LZ4 block or expanded length mismatch.");
}
