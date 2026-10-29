namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Counts LZ4 literals/matches without allocating the declared output buffer.</summary>
internal static class Lz4BlockValidation
{
    private const int LengthMask = 15;
    private const int LiteralShift = 4;
    private const int MinimumMatch = 4;
    private const int MinimumFinalLiterals = 5;
    private const int MinimumLastMatchDistance = 12;
    private const byte LengthContinuation = byte.MaxValue;

    internal static void Validate(ReadOnlySpan<byte> source, int expectedSize)
    {
        var position = 0;
        var expanded = 0;
        var lastMatchStart = -1;
        while (position < source.Length)
        {
            var token = source[position++];
            var literals = Length(source, ref position, token >> LiteralShift, expectedSize - expanded);
            BinaryData.Slice(source, position, literals);
            position += literals;
            expanded += literals;
            if (position == source.Length)
            {
                BinaryData.Require(expanded == expectedSize, "LZ4 decompressed size mismatch.");
                BinaryData.Require(lastMatchStart < 0 || (literals >= MinimumFinalLiterals &&
                    expanded - lastMatchStart >= MinimumLastMatchDistance), "Invalid LZ4 block ending.");
                return;
            }

            var offset = BinaryData.U16(source, position);
            position += sizeof(ushort);
            BinaryData.Require(offset > 0 && offset <= expanded, "Invalid LZ4 match offset.");
            var match = Length(source, ref position, token & LengthMask, expectedSize - expanded - MinimumMatch);
            lastMatchStart = expanded;
            expanded += match + MinimumMatch;
        }

        BinaryData.Require(false, "Truncated LZ4 block: missing final literal sequence.");
    }

    private static int Length(ReadOnlySpan<byte> source, ref int position, int length, int maximum)
    {
        BinaryData.Require(length <= maximum, "LZ4 decompressed data exceeds its declared size.");
        if (length != LengthMask) return length;
        byte extension;
        do
        {
            BinaryData.Require(position < source.Length, "Truncated LZ4 length.");
            extension = source[position++];
            BinaryData.Require(extension <= maximum - length, "LZ4 decompressed data exceeds its declared size.");
            length += extension;
        } while (extension == LengthContinuation);
        return length;
    }
}
