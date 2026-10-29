using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokCollectionOrderingTests
{
    [Theory]
    [InlineData("resources")]
    [InlineData("xp")]
    [InlineData("collectibles")]
    [InlineData("nested-ids")]
    [InlineData("index-words")]
    [InlineData("flags")]
    [InlineData("directory")]
    public void AllSchemasRetainGlobalPredicatesTypedSortingAndPaging(string collectionId)
    {
        var fixture = new RagnarokNestedFixture();
        var bytes = fixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x456), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x45A), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fixture.WalletOffset(1) + 20 + 16 + 8), 2);
        using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));
        var info = Assert.Single(document.Collections, collection => collection.Id == collectionId);
        var native = ReadAll(document, collectionId, new());
        Assert.Equal(info.TotalCount, native.Length);
        Assert.Equal(native.Length, native.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var column in info.Columns)
        {
            foreach (var descending in new[] { false, true })
            {
                var comparer = Comparer<TableRow>.Create((left, right) =>
                {
                    var primary = column.Key is "quantity" or "metadata" or "word"
                        ? ulong.Parse(Text(left, column.Key), CultureInfo.InvariantCulture).CompareTo(ulong.Parse(Text(right, column.Key), CultureInfo.InvariantCulture))
                        : StringComparer.OrdinalIgnoreCase.Compare(Text(left, column.Key), Text(right, column.Key));
                    return primary == 0 ? StringComparer.Ordinal.Compare(left.Key, right.Key) : descending ? -primary : primary;
                });
                Assert.Equal(native.Order(comparer).Select(row => row.Key),
                    ReadAll(document, collectionId, new() { SortColumn = column.Key, Descending = descending }).Select(row => row.Key));
            }
            if (native.Length == 0 || column.Key is "inspect" or "restore") continue;
            var text = Text(native[^1], column.Key);
            Assert.Equal(native.Where(row => string.Equals(Text(row, column.Key), text, StringComparison.OrdinalIgnoreCase)).Select(row => row.Key),
                ReadAll(document, collectionId, new() { Filters = new Dictionary<string, string> { [column.Key] = text.ToUpperInvariant() } }).Select(row => row.Key));
        }
        if (native.Length > 0)
        {
            var text = Text(native[^1], "offset");
            var expected = native.Where(row => row.Cells.Values.Any(cell => CellText(cell).Contains(text, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(expected.Select(row => row.Key), ReadAll(document, collectionId, new() { Text = $"  {text.ToLowerInvariant()}  " }).Select(row => row.Key));
        }
        Assert.Empty(document.Query(collectionId, new() { Offset = int.MaxValue }).Rows);
        var clamped = document.Query(collectionId, new() { Offset = -1, Limit = 0 });
        Assert.Equal(0, clamped.Offset);
        Assert.Equal(1, clamped.Limit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static TableRow[] ReadAll(RagnarokDocument document, string collection, QueryableCollectionQuery query)
    {
        var result = new List<TableRow>();
        while (true)
        {
            var page = document.Query(collection, query with { Offset = result.Count, Limit = 1 });
            result.AddRange(page.Rows);
            if (result.Count == page.FilteredCount) return result.ToArray();
            Assert.NotEmpty(page.Rows);
        }
    }

    private static string Text(TableRow row, string column) => CellText(row.Cells.GetValueOrDefault(column));
    private static string CellText(ViewNode? cell) => cell switch
    {
        TextNode text => text.Text,
        FieldNode field when field.Value.Kind == FieldValueKind.Integer => field.Value.AsInteger().ToString(CultureInfo.InvariantCulture),
        FieldNode field => field.Value.AsString(),
        _ => "",
    };
}
