using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

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
        try { ValidateBlock(source, expectedSize); }
        catch (SaveFormatException)
        {
            throw new SaveFormatException("Invalid CSAV LZ4 block or expanded length mismatch.");
        }
    }

    private static void ValidateBlock(ReadOnlySpan<byte> source, int expectedSize)
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
                Require(expanded == expectedSize, "LZ4 decompressed size mismatch.");
                Require(lastMatchStart < 0 || (literals >= MinimumFinalLiterals &&
                    expanded - lastMatchStart >= MinimumLastMatchDistance), "Invalid LZ4 block ending.");
                return;
            }

            var offset = BinaryPrimitives.ReadUInt16LittleEndian(BinaryData.Slice(source, position, sizeof(ushort)));
            position += sizeof(ushort);
            Require(offset > 0 && offset <= expanded, "Invalid LZ4 match offset.");
            var match = Length(source, ref position, token & LengthMask, expectedSize - expanded - MinimumMatch);
            lastMatchStart = expanded;
            expanded += match + MinimumMatch;
        }

        Require(false, "Truncated LZ4 block: missing final literal sequence.");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new SaveFormatException("Invalid CSAV LZ4 block or expanded length mismatch. " + reason);
    }

    private static int Length(ReadOnlySpan<byte> source, ref int position, int length, int maximum)
    {
        Require(length <= maximum, "LZ4 decompressed data exceeds its declared size.");
        if (length != LengthMask) return length;
        byte extension;
        do
        {
            Require(position < source.Length, "Truncated LZ4 length.");
            extension = source[position++];
            Require(extension <= maximum - length, "LZ4 decompressed data exceeds its declared size.");
            length += extension;
        } while (extension == LengthContinuation);
        return length;
    }
}
