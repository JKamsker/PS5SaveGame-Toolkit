using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseWordPerformanceTests
{
    private const int PageOffset = 12000;
    private const int PageSize = 20;
    private const int KiB = 1024;

    [Theory]
    [InlineData("offset", false)]
    [InlineData("offset", true)]
    [InlineData("hex", false)]
    [InlineData("hex", true)]
    [InlineData("value", false)]
    [InlineData("value", true)]
    public void DeepPagesPreserveNumericSortAndAscendingOffsetTies(string column, bool descending)
    {
        var (save, section, collection) = Fixture();
        var query = new QueryableCollectionQuery { SortColumn = column, Descending = descending, Offset = PageOffset, Limit = PageSize };
        var offsets = Enumerable.Range(0, section.Size / sizeof(uint)).Select(index => index * sizeof(uint)).ToArray();
        uint Key(int offset) => column == "offset" ? (uint)offset : AriseFormat.U32(save.Payload, section.Offset + offset);
        var expected = (descending ? offsets.OrderByDescending(Key) : offsets.OrderBy(Key)).ThenBy(offset => offset)
            .Skip(PageOffset).Take(PageSize).ToArray();
        var page = collection.Query(query, Field);
        Assert.Equal(section.Size / sizeof(uint), page.FilteredCount);
        Assert.Equal(expected.Select(offset => $"{section.Id:X8}/{offset:X6}"), page.Rows.Select(row => row.Key));
        Assert.Equal(expected.Select(offset => (long)AriseFormat.U32(save.Payload, section.Offset + offset)),
            page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["value"]).Value.AsInt64()));

        FieldNode Field(string id, string label, int offset) => new(id, label, IntegerSpec.UInt32, FieldValue.Of((long)AriseFormat.U32(save.Payload, offset)));
    }

    [Theory]
    [InlineData("000001", null, null)]
    [InlineData(null, "offset", "+0x000014")]
    [InlineData(null, "hex", "0x00000006")]
    [InlineData(null, "value", "6")]
    [InlineData("0000", "value", "6")]
    public void SearchAndExactFiltersRetainCompleteMembership(string? text, string? column, string? filter)
    {
        var (save, section, collection) = Fixture();
        var expected = Enumerable.Range(0, section.Size / sizeof(uint)).Select(index => index * sizeof(uint))
            .Where(offset => Matches(offset)).ToArray();
        var page = collection.Query(new()
        {
            Text = text, Filters = column is null ? new Dictionary<string, string>() : new() { [column] = filter! },
            Offset = expected.Length > PageSize ? 7 : 0, Limit = PageSize, Descending = true,
        }, (id, label, _) => new(id, label, IntegerSpec.UInt32, FieldValue.Of(0L)));
        Assert.Equal(expected.Length, page.FilteredCount);
        Assert.Equal(expected.Reverse().Skip(page.Offset).Take(PageSize).Select(offset => $"{section.Id:X8}/{offset:X6}"), page.Rows.Select(row => row.Key));

        bool Matches(int offset)
        {
            var value = AriseFormat.U32(save.Payload, section.Offset + offset);
            var cells = new Dictionary<string, string>
            {
                ["offset"] = $"+0x{offset:X6}", ["hex"] = $"0x{value:X8}", ["value"] = value.ToString(CultureInfo.InvariantCulture),
            };
            return (text is null || cells.Values.Any(cell => cell.Contains(text, StringComparison.OrdinalIgnoreCase)))
                && (column is null || string.Equals(cells[column], filter, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SortedAndFilteredPagesReadCurrentBytesAndHandlePageEdges()
    {
        var (save, section, collection) = Fixture();
        var query = new QueryableCollectionQuery { SortColumn = "value", Descending = true, Limit = 1 };
        _ = collection.Query(query, Field);
        BinaryPrimitives.WriteUInt32LittleEndian(save.Payload.AsSpan(section.Offset + 16), uint.MaxValue);
        Assert.Equal($"{section.Id:X8}/000010", Assert.Single(collection.Query(query, Field).Rows).Key);
        Assert.Single(collection.Query(query with { Filters = new Dictionary<string, string> { ["value"] = "4294967295" } }, Field).Rows);
        Assert.Empty(collection.Query(query with { Offset = int.MaxValue }, Field).Rows);
        Assert.Single(collection.Query(query with { Limit = -1, Offset = -1 }, Field).Rows);
        Assert.Equal(200, collection.Query(query with { Limit = int.MaxValue }, Field).Rows.Count);

        FieldNode Field(string id, string label, int offset) => new(id, label, IntegerSpec.UInt32, FieldValue.Of((long)AriseFormat.U32(save.Payload, offset)));
    }

    [Theory]
    [InlineData("offset", null, 128 * KiB)]
    [InlineData("value", null, 1024 * KiB)]
    [InlineData("offset", "0000", 1024 * KiB)]
    public void LargestCollectionPagesHaveBoundedAllocations(string sort, string? text, int bound)
    {
        var (_, _, collection) = Fixture();
        var query = new QueryableCollectionQuery { SortColumn = sort, Text = text, Offset = PageOffset, Limit = PageSize };
        _ = collection.Query(query, Field);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = collection.Query(query, Field);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(PageSize, page.Rows.Count);
        Assert.True(allocated < bound, $"{sort}/{text ?? "unfiltered"} page allocated {allocated:N0} B; bound {bound:N0} B.");

        static FieldNode Field(string id, string label, int _) => new(id, label, IntegerSpec.UInt32, FieldValue.Of(0L));
    }

    private static (AriseSave Save, AriseSection Section, AriseWordCollection Collection) Fixture()
    {
        var fixture = new AriseTestContainer();
        var section = fixture.Sections.MaxBy(section => section.Size)!;
        for (var offset = 0; offset < section.Size; offset += sizeof(uint))
        {
            fixture.Write(section.Offset + offset, (uint)(offset / sizeof(uint) % 7));
        }

        var save = new AriseSave([], fixture.Payload, fixture.Sections);
        return (save, section, new AriseWordCollection(save, section));
    }
}
