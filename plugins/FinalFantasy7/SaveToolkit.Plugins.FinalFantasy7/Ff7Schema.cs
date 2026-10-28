using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal enum Ff7Variant
{
    Remake,
    Rebirth,
    RebirthDemo,
}

/// <summary>Only measured PS5 layouts are accepted; equal container sizes do not imply equal gameplay schemas.</summary>
internal sealed record Ff7Schema(Ff7Variant Variant, string Name, string Prefix, string CommonName)
{
    public const int CommonFileLength = 1_056;
    public const int CharacterCount = 7;
    private const int RemakeFileLength = 9_434_752;
    private const int RebirthFileLength = 23_381_104;
    private const int CommonContentLength = 0x400;
    private const int RemakeGlobalLength = 0x50000;
    private const int RemakeChapterLength = 0x66100;
    private const int RebirthGlobalLength = 0xE4320;
    private const int RebirthChapterLength = 0x14F50;
    private const int RebirthMlsdLength = 0x69030;
    private const int RebirthNvsdLength = 0x2440;

    public bool IsRemake => Variant == Ff7Variant.Remake;
    public bool IsDemo => Variant == Ff7Variant.RebirthDemo;

    public static Ff7Schema? ForTitle(string title) => title switch
    {
        "PPSA01911" => new(Ff7Variant.Remake, "Remake Intergrade", "ff7remake", "ff7remakecommon"),
        "PPSA08664" => new(Ff7Variant.Rebirth, "Rebirth", "ff7rebirth", "ff7rebirthcommon"),
        "PPSA18936" => new(Ff7Variant.RebirthDemo, "Rebirth Demo", "ff7rebirth", "ff7rebirthdemocommon"),
        _ => null,
    };

    public int FileLength(bool common) => common ? CommonFileLength : IsRemake ? RemakeFileLength : RebirthFileLength;
    public int RecordCount(bool common) => common ? 2 : IsRemake ? 24 : 275;

    public bool TryGetRole(string name, out bool common)
    {
        if (name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        common = name.Equals(CommonName, StringComparison.OrdinalIgnoreCase);
        if (common)
        {
            return true;
        }

        var prefix = IsRemake && name.StartsWith("ff7remakeplus", StringComparison.OrdinalIgnoreCase)
            ? "ff7remakeplus" : Prefix;
        return name.Length == prefix.Length + 3 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            name.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    public bool MatchesFirstHeader(ReadOnlySpan<byte> header, bool common) =>
        header.Length >= LinkedRecords.ContentOffset &&
        BinaryPrimitives.ReadUInt32LittleEndian(header) == (common ? 0x00010005U : 0x00010000U) &&
        BinaryPrimitives.ReadUInt32LittleEndian(header[LinkedRecords.NextPointerOffset..]) == (uint)(Shape(0, common).Length + LinkedRecords.HeaderLength) &&
        header.Slice(LinkedRecords.HeaderLength, 16).SequenceEqual(
            common ? "COSDCOSDCOSDCOSD"u8 : "RESDRESDRESDRESD"u8);

    public RecordShape Shape(int index, bool common)
    {
        if (index == RecordCount(common) - 1)
        {
            return new(4, "TAIL", 0, 0);
        }

        if (common)
        {
            return new(5, "COSD", 1, CommonContentLength);
        }

        if (IsRemake)
        {
            return index switch
            {
                0 => new(0, "RESD", 7, RemakeGlobalLength),
                19 => new(2, "RESD", 7, RemakeGlobalLength),
                < 19 => new(1, "LOSD", 13, RemakeChapterLength),
                _ => new(3, "LOSD", 13, RemakeChapterLength),
            };
        }

        return index switch
        {
            0 => new(0, "RESD", 14, RebirthGlobalLength),
            <= 80 => new(1, "LOSD", 27, RebirthChapterLength),
            81 => new(2, "RESD", 14, RebirthGlobalLength),
            <= 161 => new(3, "LOSD", 27, RebirthChapterLength),
            <= 169 => new(11, "MLSD", 2, RebirthMlsdLength),
            <= 177 => new(10, "MLSD", 2, RebirthMlsdLength),
            <= 225 => new(14, "NVSD", 1, RebirthNvsdLength),
            _ => new(15, "NVSD", 1, RebirthNvsdLength),
        };
    }
}

internal sealed record RecordShape(byte Kind, string Tag, uint Version, int Length);
