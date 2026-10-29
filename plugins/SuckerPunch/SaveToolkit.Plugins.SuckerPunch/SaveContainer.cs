using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch;

internal enum ContainerKind
{
    Tsushima,
    YoteiEmpty,
    YoteiUnknown,
}

internal sealed record SaveContainer(
    ContainerKind Kind,
    uint Magic,
    uint Word1,
    uint Word2,
    uint Word3,
    bool ChecksumValid)
{
    internal const int TsushimaLength = 555_008;
    internal const int YoteiLength = 616_448;
    internal const uint TsushimaMagic = 0x14E;
    internal const uint YoteiMagic = 0x150;

    public static SaveContainer Parse(ReadOnlySpan<byte> bytes, string titleId)
    {
        var isTsushima = titleId.Equals("PPSA02225", StringComparison.OrdinalIgnoreCase);
        var expectedLength = isTsushima ? TsushimaLength : YoteiLength;
        if (bytes.Length != expectedLength)
        {
            throw new SaveFormatException($"Unexpected Sucker Punch save size ({bytes.Length} bytes).");
        }

        if (IsAllZero(bytes))
        {
            if (!isTsushima)
            {
                return new SaveContainer(ContainerKind.YoteiEmpty, 0, 0, 0, 0, false);
            }

            throw new SaveFormatException("Ghost of Tsushima container is empty.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var word1 = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var word2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var word3 = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);

        if (magic == TsushimaMagic)
        {
            if (!isTsushima || word1 < 16 || word1 > bytes.Length)
            {
                throw new SaveFormatException("Invalid Ghost of Tsushima container header.");
            }

            var calculated = CalculateTsushimaChecksum(bytes, word1, magic, word3);
            if (calculated != word2)
            {
                throw new SaveFormatException("Ghost of Tsushima save checksum is invalid.");
            }

            return new SaveContainer(ContainerKind.Tsushima, magic, word1, word2, word3, true);
        }

        if (magic == YoteiMagic && !isTsushima)
        {
            var calculated = YoteiFormat.Checksum(bytes);
            return new SaveContainer(ContainerKind.YoteiUnknown, magic, word1, word2, word3, calculated == word3);
        }

        throw new SaveFormatException("Unrecognised Sucker Punch save magic.");
    }

    public static uint CalculateTsushimaChecksum(ReadOnlySpan<byte> bytes, uint contentSize, uint magic, uint version)
    {
        if (contentSize < 16 || contentSize > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(contentSize));
        }

        ulong sum = contentSize + magic + version;
        foreach (var value in bytes[16..(int)contentSize])
        {
            sum += value;
        }

        return unchecked((uint)sum);
    }

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }
}
