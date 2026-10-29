using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokGearTests
{
    internal const int BlockOffset = 0x480;
    private const ulong Shoulder = 0xDB786D5F83B7B8F8;
    private const ulong Wraps = 0x73169D0052172AC7;

    [Fact]
    public void SyntheticVariableRecordsJoinNamedLevelsWithoutEnablingWrites()
    {
        var bytes = Fixture();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Equal(2, index.Gear.Records.Count);
        Assert.Equal(1u, index.Gear.Records[0].SlotCount);
        Assert.Equal(0u, index.Gear.Records[1].Parent);
        Assert.Equal(index.Nested.WalletBlockOffset, index.Gear.End);
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("gear", new() { Text = "Wraps of Survival", Limit = 1 });
        var row = Assert.Single(page.Rows);
        Assert.Equal("gear/5", row.Key);
        var level = Assert.IsType<FieldNode>(row.Cells["level"]);
        Assert.Equal("3", level.Value.AsString());
        Assert.True(level.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, level.Confidence);
        Assert.Equal(level.Value, document.GetValue(level.ValueId));
        Assert.Contains("No licence published", level.Description);
        Assert.NotEmpty(document.ApplyEdits([new(level.ValueId, FieldValue.Of("9"))]).Errors);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal("gear/0", Assert.Single(query.Query("gear", new() { SortColumn = "instance", Limit = 1 }).Rows).Key);
        Assert.Equal("gear/5", Assert.Single(query.Query("gear", new() { Filters = new Dictionary<string, string>
            { ["definition"] = RagnarokReferences.Key(Wraps) } }).Rows).Key);
        Assert.Throws<ArgumentException>(() => query.Query("gear", new() { Cursor = "unsupported" }));
        var decoder = new HookSaveDecoder(new RagnarokEvidenceDecoder());
        var tree = decoder.Decode(RagnarokTestSupport.GameplayFile(bytes), new());
        var decoded = Assert.Single(tree.Values, v => v.Path == "gear[instance=5]/catalog-level");
        Assert.Equal("3", decoded.Value);
        Assert.Contains("No licence published", decoded.Label);
        Assert.False(tree.Truncated);
    }

    [Theory]
    [InlineData(2, uint.MaxValue)] // Excessive record count.
    [InlineData(6 + 16, uint.MaxValue)] // Excessive nested subrecord count.
    [InlineData(6 + 26 + 8, 0u)] // Duplicate instance.
    [InlineData(6 + 26 + 12, 9u)] // Missing parent instance.
    public void UnprovenFramingIsOmittedAndStillPreserved(int relativeOffset, uint value)
    {
        var bytes = Fixture();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(BlockOffset + relativeOffset), value);
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Empty(index.Gear.Records);
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ExistingSyntheticLayoutsDoNotAcquireSpeculativeGear()
    {
        Assert.Empty(RagnarokRecordIndex.Parse(RagnarokTestSupport.LegacyGameplay(), false).Gear.Records);
        Assert.Empty(RagnarokRecordIndex.Parse(new RagnarokNestedFixture().Create(), false).Gear.Records);
    }

    [Fact]
    public void DefinitionReplacementRetainsInstanceKeyAndUnknownsStayRaw()
    {
        var bytes = Fixture();
        const ulong unknown = 0xF0E1D2C3B4A59687;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(BlockOffset + 6 + 26), unknown);
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("gear", new() { Text = "F0E1D2C3B4A59687" });
        var row = Assert.Single(page.Rows);
        Assert.Equal("gear/5", row.Key);
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["definition"]).Confidence);
        Assert.Equal("Unknown", Assert.IsType<FieldNode>(row.Cells["level"]).Value.AsString());
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CatalogLevelsSortNumericallyBeforePaging()
    {
        var bytes = Fixture();
        const ulong levelTenHandles = 0x3EB1585DFA6541F2;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(BlockOffset + 6), levelTenHandles);
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal("gear/5", Assert.Single(query.Query("gear", new() { SortColumn = "level", Limit = 1 }).Rows).Key);
        Assert.Equal("gear/0", Assert.Single(query.Query("gear", new() { SortColumn = "level", Descending = true, Limit = 1 }).Rows).Key);
        Assert.Single(query.Query("gear", new() { Filters = new Dictionary<string, string> { ["level"] = "10" } }).Rows);
    }

    [Fact]
    public void AmbiguousLengthFramingDoesNotSelectAnArbitraryCandidate()
    {
        var bytes = Fixture();
        // A second independent one-record candidate has the same enclosing end.
        const int candidate = 0x4C0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(candidate), 0x4FC - candidate - 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(candidate + 2), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(candidate + 6), Shoulder);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(candidate + 14), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(candidate + 18), uint.MaxValue);
        Assert.Empty(RagnarokRecordIndex.Parse(bytes, false).Gear.Records);
    }

    internal static byte[] Fixture()
    {
        var bytes = new RagnarokNestedFixture().Create();
        using var stream = new MemoryStream(bytes, writable: true);
        using var writer = new BinaryWriter(stream);
        stream.Position = BlockOffset;
        writer.Write(checked((ushort)(RagnarokNestedFixture.FirstWalletOffset - sizeof(uint) - BlockOffset - sizeof(ushort))));
        writer.Write(2u);
        writer.Write(Shoulder); writer.Write(0u); writer.Write(uint.MaxValue); writer.Write(1u);
        writer.Write(new byte[6]);
        writer.Write(Wraps); writer.Write(5u); writer.Write(0u); writer.Write(0u);
        return bytes;
    }
}
