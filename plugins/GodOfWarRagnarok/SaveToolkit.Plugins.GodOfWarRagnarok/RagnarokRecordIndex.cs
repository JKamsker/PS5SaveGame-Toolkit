using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RecordRegion(string Name, int Offset, int Length, int Count, string Meaning);
internal sealed record ResourceOccurrence(int Offset, ulong Id);

/// <summary>Indexes only bounded structures established in the supplied revision; unknown gaps stay opaque.</summary>
internal sealed class RagnarokRecordIndex
{
    public bool Preferences { get; private init; }
    public string Description { get; private init; } = "";
    public int DirectoryCount { get; private init; }
    public int FlagsOffset { get; private init; }
    public int SecondaryOffset { get; private init; }
    public int SecondaryCount { get; private init; }
    public int WalletOffset { get; private init; }
    public int WalletCount { get; private init; }
    public IReadOnlyList<RecordRegion> Regions { get; private init; } = [];
    public IReadOnlyList<ResourceOccurrence> Occurrences { get; private init; } = [];
    public RagnarokGrammarEvidence Evidence { get; private set; } = new([], [], [], []);
    public RagnarokNestedRecords Nested { get; private init; } = RagnarokNestedRecords.Empty;
    public RagnarokGearRecords Gear { get; private set; } = RagnarokGearRecords.Empty;

    // Legacy consumers expose hero resources without claiming any nested container framing.
    public IReadOnlyList<RagnarokWallet> GetResourceWallets() => Nested.Wallets.Count > 0 ? Nested.Wallets :
        [new RagnarokWallet(RagnarokFormat.HeroWalletId, WalletOffset, WalletCount, 0, 0)];

    public static RagnarokRecordIndex Parse(ReadOnlySpan<byte> bytes, bool preferences)
    {
        if (!RagnarokFormat.MatchesHeader(bytes, bytes.Length, preferences))
        {
            throw new SaveFormatException("Unsupported or truncated Ragnarök PS5 header.");
        }

        if (preferences)
        {
            var preferenceIndex = new RagnarokRecordIndex
            {
                Preferences = true,
                Regions = [new("Preferences record", 0, bytes.Length, 1, "Settings meanings unknown; raw bytes only")],
            };
            preferenceIndex.Evidence = RagnarokGrammar.Analyze(bytes, preferenceIndex);
            return preferenceIndex;
        }

        ValidateGameplayHeader(bytes);
        // The observed EOF sentinel is a separate record, never part of a counted array.
        var payload = bytes[..^sizeof(uint)];
        var (count, flagsOffset, secondaryOffset, secondaryCount) = ReadDirectory(payload);
        var secondaryEnd = secondaryOffset + secondaryCount * sizeof(ulong);
        var (wallet, walletCount) = ReadHeroWallet(bytes, payload, secondaryEnd);
        var index = new RagnarokRecordIndex
        {
            Description = RagnarokFormat.Description(bytes),
            DirectoryCount = count,
            FlagsOffset = flagsOffset,
            SecondaryCount = secondaryCount,
            SecondaryOffset = secondaryOffset,
            WalletOffset = wallet,
            WalletCount = walletCount,
            Nested = RagnarokNestedRecords.Parse(payload, wallet),
            Regions = DescribeRegions(count, flagsOffset, secondaryOffset, secondaryCount, wallet, walletCount),
            Occurrences = FindResourceOccurrences(bytes),
        };
        index.Gear = RagnarokGearRecords.Find(bytes, index);
        index.Evidence = RagnarokGrammar.Analyze(bytes, index);
        return index;
    }

    private static void ValidateGameplayHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < RagnarokFormat.DirectoryItemsOffset ||
            RagnarokFormat.UInt32(bytes, RagnarokFormat.MetadataKindOffset) != RagnarokFormat.MetadataKind ||
            RagnarokFormat.UInt32(bytes, RagnarokFormat.MetadataRevisionOffset) != RagnarokFormat.Revision ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[RagnarokFormat.DirectoryMarkerOffset..]) != RagnarokFormat.DirectoryMarker ||
            RagnarokFormat.UInt32(bytes, bytes.Length - sizeof(uint)) != uint.MaxValue)
        {
            throw new SaveFormatException("Invalid Ragnarök metadata, directory marker or observed trailing sentinel.");
        }
    }

    private static (int Count, int FlagsOffset, int SecondaryOffset, int SecondaryCount) ReadDirectory(ReadOnlySpan<byte> payload)
    {
        var count = RagnarokFormat.CheckedCount(payload, RagnarokFormat.DirectoryCountOffset, sizeof(ulong));
        var flagsOffset = RagnarokFormat.DirectoryItemsOffset + count * sizeof(ulong);
        RagnarokFormat.RequireRange(payload, flagsOffset, (uint)count, sizeof(uint));
        var ids = new HashSet<ulong>();
        ulong previous = 0;
        for (var row = 0; row < count; row++)
        {
            var id = RagnarokFormat.UInt64(payload, RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong));
            if (row > 0 && id <= previous)
            {
                throw new SaveFormatException("Ragnarök directory IDs are not strictly increasing.");
            }

            ids.Add(id);
            previous = id;
        }

        var secondaryCountOffset = flagsOffset + count * sizeof(uint);
        var secondaryCount = RagnarokFormat.CheckedCount(payload, secondaryCountOffset, sizeof(ulong));
        var secondaryOffset = secondaryCountOffset + sizeof(uint);
        for (var row = 0; row < secondaryCount; row++)
        {
            if (!ids.Contains(RagnarokFormat.UInt64(payload, secondaryOffset + row * sizeof(ulong))))
            {
                throw new SaveFormatException("A secondary Ragnarök ID is missing from the directory.");
            }
        }

        return (count, flagsOffset, secondaryOffset, secondaryCount);
    }

    private static (int Offset, int Count) ReadHeroWallet(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> payload, int secondaryEnd)
    {
        Span<byte> walletPattern = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(walletPattern, RagnarokFormat.HeroWalletId);
        var wallet = bytes.IndexOf(walletPattern);
        if (wallet < 0 || bytes[(wallet + walletPattern.Length)..].IndexOf(walletPattern) >= 0 ||
            wallet > bytes.Length - RagnarokFormat.WalletHeaderSize)
        {
            throw new SaveFormatException("Expected exactly one bounded Ragnarök hero-wallet record.");
        }

        if (wallet - sizeof(uint) < secondaryEnd)
        {
            throw new SaveFormatException("Ragnarök wallet length prefix overlaps metadata or directory arrays.");
        }

        var walletCount = RagnarokFormat.UInt32(bytes, wallet + sizeof(ulong));
        RagnarokFormat.RequireRange(payload, wallet + RagnarokFormat.WalletHeaderSize, walletCount, RagnarokFormat.WalletRowSize);
        return (wallet, (int)walletCount);
    }

    private static IReadOnlyList<RecordRegion> DescribeRegions(int count, int flagsOffset, int secondaryOffset,
        int secondaryCount, int wallet, int walletCount) =>
    [
        new("Outer header", 0, RagnarokFormat.HeaderSize, 1, "Observed constants; integrity unknown"),
        new("Metadata", RagnarokFormat.MetadataOffset, RagnarokFormat.MetadataSize, 1, "Location and inferred time/counter"),
        new("Sorted ID array", RagnarokFormat.DirectoryItemsOffset, count * sizeof(ulong), count, "Unknown ID semantics"),
        new("Paired words", flagsOffset, count * sizeof(uint), count, "Inferred correspondence to sorted IDs"),
        new("Secondary ID array", secondaryOffset, secondaryCount * sizeof(ulong), secondaryCount, "IDs also present in sorted array"),
        new("Hero wallet rows", wallet + RagnarokFormat.WalletHeaderSize, walletCount * RagnarokFormat.WalletRowSize,
            walletCount, "PC layout matches PS5; container role inferred"),
    ];

    private static IReadOnlyList<ResourceOccurrence> FindResourceOccurrences(ReadOnlySpan<byte> bytes)
    {
        const int maximumOccurrences = 4096;
        var result = new List<ResourceOccurrence>();
        Span<byte> pattern = stackalloc byte[sizeof(ulong)];
        foreach (var id in ResourceNames.Ids)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(pattern, id);
            var start = 0;
            while (start <= bytes.Length - RagnarokFormat.WalletRowSize)
            {
                var relative = bytes[start..].IndexOf(pattern);
                if (relative < 0)
                {
                    break;
                }

                var offset = start + relative;
                if (offset <= bytes.Length - RagnarokFormat.WalletRowSize)
                {
                    if (result.Count == maximumOccurrences)
                    {
                        throw new SaveFormatException("Too many resource anchors for this Ragnarök revision.");
                    }

                    result.Add(new(offset, id));
                }

                start = offset + 1;
            }
        }

        return result.OrderBy(r => r.Offset).ToArray();
    }
}
