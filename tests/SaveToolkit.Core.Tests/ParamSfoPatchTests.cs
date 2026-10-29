using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class ParamSfoPatchTests
{
    private const int HeaderSize = 20;
    private const int IndexSize = 16;
    private const ushort Utf8 = 0x0204;
    private const ushort Utf8Special = 0x0004;
    private const ushort Integer = 0x0404;
    private static readonly ParamSfoPatchLimits Limits = new(4096, 128, 32, 32);

    [Fact]
    public void ParseRetainsOrderUnknownBytesAndTrailingData()
    {
        var source = Build(
            [new Entry("TITLE", Utf8, "Hello", 12),
             new Entry("COUNT", Integer, 42, 4),
             new Entry("SPECIAL", Utf8Special, "é", 8)],
            [0xA1, 0xB2, 0xC3]);

        var document = ParamSfoDocument.Parse(source, Limits);

        Assert.Equal(["TITLE", "COUNT", "SPECIAL"], document.Entries.Select(entry => entry.Key));
        Assert.Equal(source, document.OriginalBytes.ToArray());
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(document.Entries[1].ValueBytes.Span));
        Assert.Equal([0xA1, 0xB2, 0xC3], source[^3..]);
    }

    [Fact]
    public void PatchChangesOnlyLengthAndTargetValueAndPreservesReservedSuffix()
    {
        var source = Build(new Entry("TITLE", Utf8, "Hello", 12), new Entry("OTHER", Utf8Special, "Keep", 12));
        var document = ParamSfoDocument.Parse(source, Limits);
        var plan = document.PlanExistingUtf8("TITLE", "Hi");
        var output = document.Apply(plan).ToArray();
        var target = document.Entries[0];
        var indexStart = HeaderSize;
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(12));
        var valueStart = dataTable + target.DataOffset;

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(indexStart + 4)));
        Assert.Equal("Hi\0", Encoding.UTF8.GetString(output, valueStart, 3));
        Assert.Equal(source[valueStart + 3], output[valueStart + 3]);
        Assert.Equal(source[^1], output[^1]);
        var changed = Enumerable.Range(0, source.Length).Where(index => source[index] != output[index]).ToArray();
        Assert.Equal([indexStart + 4, valueStart + 1, valueStart + 2], changed);
    }

    [Fact]
    public void NoOpApplyIsByteIdenticalAndDoesNotMutateSource()
    {
        var source = Build(new Entry("TITLE", Utf8, "Same", 8));
        var document = ParamSfoDocument.Parse(source, Limits);
        var plan = document.PlanExistingUtf8("TITLE", "Same");

        var output = document.Apply(plan).ToArray();

        Assert.True(plan.IsNoOp);
        Assert.Equal(source, output);
        Assert.Equal(source, document.OriginalBytes.ToArray());
    }

    [Fact]
    public void StrictUtf8SupportsMultibyteAndEmptyTextAtCapacityBoundary()
    {
        var source = Build(new Entry("TITLE", Utf8, "é", 5), new Entry("EMPTY", Utf8Special, "", 1));
        var document = ParamSfoDocument.Parse(source, Limits);

        var multibyte = document.Apply(document.PlanExistingUtf8("TITLE", "😀")).ToArray();
        Assert.Equal("😀\0", Encoding.UTF8.GetString(multibyte, DataStart(source, document.Entries[0]), 5));
        var empty = document.PlanExistingUtf8("EMPTY", "");
        Assert.True(empty.IsNoOp);
    }

    [Fact]
    public void EmbeddedNulAndCapacityOrGlobalLimitsAreRejected()
    {
        var source = Build(new Entry("TITLE", Utf8, "old", 5));
        var document = ParamSfoDocument.Parse(source, new ParamSfoPatchLimits(4096, 128, 32, 3));

        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("TITLE", "a\0b"));
        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("TITLE", "éé"));
        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("TITLE", "abcd"));
        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("TITLE", "\uD800"));

        var empty = ParamSfoDocument.Parse(source, new ParamSfoPatchLimits(4096, 128, 32, 1));
        Assert.Equal("", empty.PlanExistingUtf8("TITLE", "").NewText);
        var below = ParamSfoDocument.Parse(source, new ParamSfoPatchLimits(4096, 128, 32, 1));
        Assert.Throws<ParamSfoPatchException>(() => below.PlanExistingUtf8("TITLE", "a"));
        var exact = ParamSfoDocument.Parse(source, new ParamSfoPatchLimits(4096, 128, 32, 2));
        var exactPlan = exact.PlanExistingUtf8("TITLE", "a");
        Assert.Equal("a", exactPlan.NewText);
        Assert.Equal("a\0", Encoding.UTF8.GetString(exact.Apply(exactPlan).ToArray(), DataStart(source, exact.Entries[0]), 2));
        var forgedAbove = exactPlan with { NewText = "ab" };
        Assert.Throws<ParamSfoPatchException>(() => exact.Apply(forgedAbove));
        var multibyteExact = ParamSfoDocument.Parse(source, new ParamSfoPatchLimits(4096, 128, 32, 3));
        Assert.Equal("é", multibyteExact.PlanExistingUtf8("TITLE", "é").NewText);
        Assert.Throws<ParamSfoPatchException>(() => exact.PlanExistingUtf8("TITLE", "é"));
    }

    [Fact]
    public void InvalidOriginalUtf8IsRejectedOnlyWhenTargeted()
    {
        var source = Build(new Entry("TITLE", Utf8, "ok", 8));
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(12));
        source[dataTable] = 0xFF;
        var document = ParamSfoDocument.Parse(source, Limits);

        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("TITLE", "new"));
    }

    [Fact]
    public void DuplicateKeysAndMalformedRegionsAreRejected()
    {
        var duplicate = Build(new Entry("TITLE", Utf8, "a", 4), new Entry("TITLE", Utf8, "b", 4));
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(duplicate, Limits));

        var invalidLength = Build(new Entry("TITLE", Utf8, "a", 4));
        BinaryPrimitives.WriteInt32LittleEndian(invalidLength.AsSpan(HeaderSize + 4), 5);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(invalidLength, Limits));

        var missingKeyTerminator = Build(new Entry("TITLE", Utf8, "a", 4));
        var keyTable = BinaryPrimitives.ReadInt32LittleEndian(missingKeyTerminator.AsSpan(8));
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(missingKeyTerminator.AsSpan(12));
        missingKeyTerminator.AsSpan(keyTable, dataTable - keyTable).Fill(0x41);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(missingKeyTerminator, Limits));

        var overflowingSlot = Build(new Entry("TITLE", Utf8, "a", 4));
        BinaryPrimitives.WriteInt32LittleEndian(overflowingSlot.AsSpan(HeaderSize + 8), 1000);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(overflowingSlot, Limits));

        var invalidTables = Build(new Entry("TITLE", Utf8, "a", 4));
        BinaryPrimitives.WriteInt32LittleEndian(invalidTables.AsSpan(8), int.MaxValue);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(invalidTables, Limits));

        var overlap = Build(new Entry("ONE", Utf8, "a", 4), new Entry("TWO", Utf8, "b", 4));
        BinaryPrimitives.WriteInt32LittleEndian(overlap.AsSpan(HeaderSize + IndexSize + 12), 0);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(overlap, Limits));
    }

    [Fact]
    public void TruncatedHeadersAndIndexTablesAreRejected()
    {
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse([], Limits));
        var truncatedHeader = new byte[HeaderSize - 1];
        "\0PSF"u8.CopyTo(truncatedHeader);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(truncatedHeader, Limits));

        var source = Build(new Entry("TITLE", Utf8, "a", 4));
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(16), 2);
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(source, Limits));
    }

    [Fact]
    public void StaleOrForgedPlansAreRejectedBeforeOutputAndDocumentIsImmutable()
    {
        var source = Build(new Entry("TITLE", Utf8, "old", 8));
        var document = ParamSfoDocument.Parse(source, Limits);
        var plan = document.PlanExistingUtf8("TITLE", "new");
        var changed = source.ToArray();
        changed[^1] ^= 0xFF;
        var staleDocument = ParamSfoDocument.Parse(changed, Limits);
        var forged = plan with { DataOffset = plan.DataOffset + 1 };

        Assert.Throws<ParamSfoPatchException>(() => staleDocument.Apply(plan));
        Assert.Throws<ParamSfoPatchException>(() => document.Apply(forged));
        Assert.Equal(source, document.OriginalBytes.ToArray());
    }

    [Fact]
    public void EntriesAreReadOnlyAndForeignOrWrongIndexPlansCannotCrossDocuments()
    {
        var source = Build(new Entry("TITLE", Utf8, "old", 8), new Entry("OTHER", Utf8Special, "keep", 8));
        var foreignBytes = source.ToArray();
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(foreignBytes.AsSpan(12));
        foreignBytes[dataTable + 6] ^= 0x01;
        var document = ParamSfoDocument.Parse(source, Limits);
        var foreignDocument = ParamSfoDocument.Parse(foreignBytes, Limits);
        var foreignEntry = foreignDocument.Entries[0];

        Assert.False(document.Entries is ParamSfoEntry[]);
        var mutableView = Assert.IsAssignableFrom<IList<ParamSfoEntry>>(document.Entries);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = foreignEntry);

        var foreignPlan = foreignDocument.PlanExistingUtf8("TITLE", "new") with
        {
            OriginalFileSha256 = document.OriginalFileSha256
        };
        Assert.Throws<ParamSfoPatchException>(() => document.Apply(foreignPlan));
        var wrongIndex = document.PlanExistingUtf8("TITLE", "new") with { EntryIndex = 1 };
        Assert.Throws<ParamSfoPatchException>(() => document.Apply(wrongIndex));
        Assert.Equal(source, document.OriginalBytes.ToArray());
    }

    [Fact]
    public void UnsupportedEntryFormatsCannotBeTargeted()
    {
        var source = Build(new Entry("COUNT", Integer, 1, 4));
        var document = ParamSfoDocument.Parse(source, Limits);

        Assert.Throws<ParamSfoPatchException>(() => document.PlanExistingUtf8("COUNT", "2"));
    }

    [Fact]
    public void ForgedPlansCannotTargetUnknownOrIntegerFormatsAndNullTextIsControlled()
    {
        var source = Build(
            new Entry("OPAQUE", 0x9999, "a", 4),
            new Entry("COUNT", Integer, 49, 4),
            new Entry("TITLE", Utf8, "old", 8));
        var document = ParamSfoDocument.Parse(source, Limits);

        foreach (var entry in document.Entries.Take(2))
        {
            var forged = new ParamSfoPatchPlan(
                entry.Key, entry.Format, entry.Index, entry.DataOffset, entry.Length, entry.MaximumLength,
                "1", "2", document.OriginalFileSha256, entry.EntrySha256);
            Assert.Throws<ParamSfoPatchException>(() => document.Apply(forged));
            Assert.Equal(source, document.OriginalBytes.ToArray());
        }

        var valid = document.PlanExistingUtf8("TITLE", "new");
        var nullReplacement = valid with { NewText = null! };
        Assert.Throws<ParamSfoPatchException>(() => document.Apply(nullReplacement));
        Assert.Equal(source, document.OriginalBytes.ToArray());
    }

    [Fact]
    public void LargeFiniteDocumentUsesBoundedRangeSweep()
    {
        var entries = Enumerable.Range(0, 16_000)
            .Select(index => new Entry(KeyFor(index), 0x9999, "", 0))
            .ToArray();
        var source = Build(entries, []);
        var limits = new ParamSfoPatchLimits(source.Length, 128, 8, 32);

        var document = ParamSfoDocument.Parse(source, limits);

        Assert.Equal(entries.Length, document.Entries.Count);
        Assert.Throws<ParamSfoPatchException>(() =>
            ParamSfoDocument.Parse(source,
                new ParamSfoPatchLimits(source.Length - 1, limits.MaxEntryBytes, limits.MaxKeyBytes,
                    limits.MaxEncodedValueBytes)));
    }

    private static int DataStart(byte[] source, ParamSfoEntry entry) =>
        BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(12)) + entry.DataOffset;

    private static byte[] Build(params Entry[] entries) => Build(entries, [0xEE, 0xDD]);

    private static byte[] Build(Entry[] entries, byte[] trailing)
    {
        var indexEnd = HeaderSize + (entries.Length * IndexSize);
        var keyTable = Align(indexEnd);
        var keyBytes = entries.SelectMany(entry => Encoding.ASCII.GetBytes(entry.Key + "\0")).ToArray();
        var dataTable = Align(keyTable + keyBytes.Length);
        var dataLength = entries.Sum(entry => entry.MaximumLength);
        var bytes = new byte[dataTable + dataLength + trailing.Length];
        "\0PSF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x00000101);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), keyTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), dataTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), entries.Length);
        keyBytes.CopyTo(bytes, keyTable);
        var keyOffset = 0;
        var dataOffset = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var start = HeaderSize + index * IndexSize;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start), checked((ushort)keyOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start + 2), entry.Format);
            var logical = entry.MaximumLength == 0
                ? 0
                : entry.Format == Integer ? sizeof(int) : Encoding.UTF8.GetByteCount((string)entry.Value) + 1;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 4), logical);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 8), entry.MaximumLength);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 12), dataOffset);
            var valueStart = dataTable + dataOffset;
            if (entry.MaximumLength > 0)
            {
                if (entry.Format == Integer)
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(valueStart), (int)entry.Value);
                else
                    Encoding.UTF8.GetBytes((string)entry.Value + "\0").CopyTo(bytes, valueStart);
            }
            keyOffset += entry.Key.Length + 1;
            dataOffset += entry.MaximumLength;
        }
        trailing.CopyTo(bytes, dataTable + dataLength);
        return bytes;
    }

    private static int Align(int value) => (value + 3) & ~3;

    private static string KeyFor(int index)
    {
        Span<char> chars = stackalloc char[3];
        chars[2] = (char)('A' + (index % 26));
        chars[1] = (char)('A' + (index / 26 % 26));
        chars[0] = (char)('A' + (index / (26 * 26) % 26));
        return new string(chars);
    }

    private sealed record Entry(string Key, ushort Format, object Value, int MaximumLength);
}
