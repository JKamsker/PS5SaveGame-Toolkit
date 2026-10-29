using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokWallet(ulong Id, int Offset, int ResourceCount, int IdCount, int IndexCount)
{
    public int ResourcesOffset => Offset + RagnarokFormat.WalletHeaderSize;
    public int IdsOffset => ResourcesOffset + ResourceCount * RagnarokFormat.WalletRowSize;
    public int IndicesOffset => IdsOffset + IdCount * sizeof(ulong);
    public int End => IndicesOffset + IndexCount * sizeof(uint);
}

internal sealed record RagnarokFlagGroup(ulong Id, int Offset, int Count)
{
    public const int RowSize = 12;
    public int RowsOffset => Offset + sizeof(ulong) + sizeof(uint);
    public int End => RowsOffset + Count * RowSize;
}

/// <summary>Measured nested framing; container roles and flag meanings remain unproven.</summary>
internal sealed record RagnarokNestedRecords(
    int WalletBlockOffset, int WalletBlockEnd, IReadOnlyList<RagnarokWallet> Wallets,
    int LinksOffset, int LinksCount, int FlagsOffset, int FlagsEnd, IReadOnlyList<RagnarokFlagGroup> FlagGroups)
{
    public const int LinkRowSize = 22;
    private const int MaximumContainers = 256;
    private const int GroupPrefixSize = sizeof(ulong) + 2 * sizeof(uint);
    public static RagnarokNestedRecords Empty { get; } = new(0, 0, [], 0, 0, 0, 0, []);

    public static RagnarokNestedRecords Parse(ReadOnlySpan<byte> bytes, int heroOffset)
    {
        Require(heroOffset - sizeof(uint), sizeof(uint), bytes.Length);
        var length = RagnarokFormat.UInt32(bytes, heroOffset - sizeof(uint));
        // A zero word establishes no enclosing block. Retain the prior hero-only explorer.
        if (length == 0)
        {
            // Legacy presentation still requires all arrays declared by the wallet to fit.
            _ = ReadWallet(bytes, heroOffset, bytes.Length);
            return Empty;
        }

        var end = End(bytes, heroOffset, length);
        var wallets = new List<RagnarokWallet>();
        var identities = new HashSet<ulong>();
        var cursor = heroOffset;
        while (cursor < end)
        {
            var wallet = ReadWallet(bytes, cursor, end);
            if (!identities.Add(wallet.Id) || wallets.Count == MaximumContainers)
            {
                throw new SaveFormatException("Duplicate or excessive nested Ragnarök wallet identities.");
            }

            // These look index-like, but early saves disprove zero-based references into the ID list.
            // Preserve their raw values instead of enforcing an unproven relationship.
            wallets.Add(wallet);
            cursor = wallet.End;
        }

        // A u16 byte length encloses a u32 count and that many 22-byte opaque link records.
        var linksLength = UShort(bytes, end);
        var linksEnd = End(bytes, end + sizeof(ushort), linksLength);
        var linkCount = Count(bytes, end + sizeof(ushort));
        if (linksLength != sizeof(uint) + linkCount * LinkRowSize)
        {
            throw new SaveFormatException("Nested Ragnarök link count does not consume its declared byte length.");
        }

        var flagsLength = UShort(bytes, linksEnd);
        var flagsOffset = linksEnd + sizeof(ushort);
        var flagsEnd = End(bytes, flagsOffset, flagsLength);
        Require(flagsOffset, GroupPrefixSize, flagsEnd);
        if (RagnarokFormat.UInt64(bytes, flagsOffset) != ulong.MaxValue ||
            RagnarokFormat.UInt32(bytes, flagsOffset + sizeof(ulong)) != 1)
        {
            throw new SaveFormatException("Unsupported nested Ragnarök flag-group prefix.");
        }

        var groupCount = Count(bytes, flagsOffset + sizeof(ulong) + sizeof(uint));
        if (groupCount > MaximumContainers)
        {
            throw new SaveFormatException("Excessive nested Ragnarök flag-group count.");
        }

        var groups = new List<RagnarokFlagGroup>();
        identities.Clear();
        cursor = flagsOffset + GroupPrefixSize;
        for (var group = 0; group < groupCount; group++)
        {
            Require(cursor, sizeof(ulong) + sizeof(uint), flagsEnd);
            var id = RagnarokFormat.UInt64(bytes, cursor);
            if (!identities.Add(id))
            {
                throw new SaveFormatException("Duplicate nested Ragnarök flag-group identity.");
            }

            var record = new RagnarokFlagGroup(id, cursor, Count(bytes, cursor + sizeof(ulong)));
            Require(cursor, record.End - cursor, flagsEnd);
            groups.Add(record);
            cursor = record.End;
        }

        if (cursor != flagsEnd)
        {
            throw new SaveFormatException("Nested Ragnarök flags do not consume their declared byte length.");
        }

        return new(heroOffset - sizeof(uint), end, wallets, end + sizeof(ushort) + sizeof(uint),
            linkCount, linksEnd, flagsEnd, groups);
    }

    private static RagnarokWallet ReadWallet(ReadOnlySpan<byte> bytes, int offset, int end)
    {
        Require(offset, RagnarokFormat.WalletHeaderSize, end);
        var wallet = new RagnarokWallet(RagnarokFormat.UInt64(bytes, offset), offset,
            Count(bytes, offset + sizeof(ulong)),
            Count(bytes, offset + sizeof(ulong) + sizeof(uint)),
            Count(bytes, offset + sizeof(ulong) + 2 * sizeof(uint)));
        Require(offset, wallet.End - offset, end);
        return wallet;
    }

    private static int Count(ReadOnlySpan<byte> bytes, int offset)
    {
        Require(offset, sizeof(uint), bytes.Length);
        var count = RagnarokFormat.UInt32(bytes, offset);
        if (count > RagnarokFormat.MaximumRecordCount)
        {
            throw new SaveFormatException("Nested Ragnarök count exceeds the supported bound.");
        }

        return (int)count;
    }

    private static int End(ReadOnlySpan<byte> bytes, int offset, uint length)
    {
        if (offset < 0 || offset > bytes.Length || length > bytes.Length - offset)
        {
            throw new SaveFormatException("Nested Ragnarök byte length exceeds the containing file.");
        }

        return offset + (int)length;
    }

    private static ushort UShort(ReadOnlySpan<byte> bytes, int offset)
    {
        Require(offset, sizeof(ushort), bytes.Length);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    }

    private static void Require(int offset, int length, int end)
    {
        if (offset < 0 || length < 0 || offset > end - length)
        {
            throw new SaveFormatException("Nested Ragnarök record crosses its enclosing byte range.");
        }
    }
}
