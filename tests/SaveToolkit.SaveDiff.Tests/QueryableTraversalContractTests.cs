using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class QueryableTraversalContractTests
{
    private static readonly byte[] Items = InventoryPlugin.Create((1, 0), (2, 2), (3, 3), (4, 4), (5, 5));
    private static readonly DecodeLimits Limits = new() { QueryPageSize = 2 };

    [Theory]
    [InlineData("collection")]
    [InlineData("total")]
    [InlineData("filtered")]
    [InlineData("offset")]
    [InlineData("limit")]
    [InlineData("oversized")]
    public void InvalidMetadata_StopsAfterOneQueryWithoutEmittingThePage(string fault)
    {
        var plugin = new QueryableInventoryPlugin
        {
            TransformPage = page => fault switch
            {
                "collection" => page with { CollectionId = "wrong" },
                "total" => page with { TotalCount = page.TotalCount + 1 },
                "filtered" => page with { FilteredCount = page.FilteredCount - 1 },
                "offset" => page with { Offset = -1 },
                "limit" => page with { Limit = page.Limit + 1 },
                "oversized" => page with { Rows = [.. page.Rows, page.Rows[0]] },
                _ => throw new ArgumentException("Unknown test case.", nameof(fault)),
            },
        };

        var tree = Decode(plugin);

        Assert.True(tree.Truncated);
        Assert.Empty(tree.Values);
        Assert.Single(plugin.Queries);
        Assert.Contains(tree.Warnings, w => w.Contains("invalid page metadata", StringComparison.Ordinal));
        Assert.True(plugin.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void InvalidPageSize_IsRejectedBeforeAnyQuery(int pageSize)
    {
        var plugin = new QueryableInventoryPlugin();

        Assert.Throws<ArgumentOutOfRangeException>(() => Decode(plugin, Limits with { QueryPageSize = pageSize }));
        Assert.Empty(plugin.Queries);
        Assert.True(plugin.Disposed);
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("pages")]
    [InlineData("bytes")]
    public void NegativeBudgets_AreRejectedBeforeAnyQuery(string budget)
    {
        var limits = budget switch
        {
            "rows" => Limits with { MaxQueryRows = -1 },
            "pages" => Limits with { MaxQueryPages = -1 },
            "bytes" => Limits with { MaxQueryBytes = -1 },
            _ => throw new ArgumentException("Unknown test budget.", nameof(budget)),
        };
        var plugin = new QueryableInventoryPlugin();

        Assert.Throws<ArgumentOutOfRangeException>(() => Decode(plugin, limits));
        Assert.Empty(plugin.Queries);
        Assert.True(plugin.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffsetAndCursorRequests_AdvanceExactlyAndAlwaysIncludeEmptySlots(bool cursor)
    {
        var plugin = new QueryableInventoryPlugin { UseCursor = cursor };

        var tree = Decode(plugin);

        Assert.False(tree.Truncated);
        Assert.Equal(10, tree.Values.Count);
        Assert.Contains(tree.Values, v => v is { Path: "inventory0/1/quantity", Value: "0" });
        Assert.All(plugin.Queries, q => Assert.True(q.IncludeEmpty));
        Assert.Equal([2, 2, 2], plugin.Queries.Select(q => q.Limit));
        int[] expectedOffsets = cursor ? [0, 0, 0] : [0, 2, 4];
        string?[] expectedCursors = cursor ? [null, "2", "4"] : [null, null, null];
        Assert.Equal(expectedOffsets, plugin.Queries.Select(q => q.Offset));
        Assert.Equal(expectedCursors, plugin.Queries.Select(q => q.Cursor));
    }

    [Fact]
    public void PrematureCursorEndAndOversizedCursor_AreBoundedAndReportedIncomplete()
    {
        var ended = new QueryableInventoryPlugin { UseCursor = true, TransformPage = p => p.Offset == 2 ? p with { NextCursor = null } : p };
        var tooLong = new QueryableInventoryPlugin { TransformPage = p => p with { NextCursor = new string('x', QueryableCollectionLimits.MaxCursorLength + 1) } };

        var endedTree = Decode(ended);
        var longTree = Decode(tooLong);

        Assert.True(endedTree.Truncated);
        Assert.Equal(2, ended.Queries.Count);
        Assert.Contains(endedTree.Warnings, w => w.Contains("cursor ended", StringComparison.Ordinal));
        Assert.True(longTree.Truncated);
        Assert.Single(tooLong.Queries);
        Assert.Contains(longTree.Warnings, w => w.Contains("invalid or repeated cursor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyRowIdentity_StopsWithoutInventingAnIdentity(string key)
    {
        var plugin = new QueryableInventoryPlugin { TransformPage = p => p with { Rows = [p.Rows[0] with { Key = key }] } };

        var tree = Decode(plugin);

        Assert.Empty(tree.Values);
        Assert.True(tree.Truncated);
        Assert.Single(plugin.Queries);
        Assert.Contains(tree.Warnings, w => w.Contains("empty row identity", StringComparison.Ordinal));
    }

    [Fact]
    public void TextCellKeys_EscapeTildeAndSlash_AndUseOrdinalCellOrder()
    {
        var plugin = new QueryableInventoryPlugin
        {
            TransformPage = p => p with
            {
                Rows = [new("~/row", new Dictionary<string, ViewNode>
                {
                    ["z"] = new TextNode("last"), ["~/cell"] = new TextNode("escaped"), ["A"] = new TextNode("first"),
                })],
            },
        };

        var tree = Decode(plugin, bytes: InventoryPlugin.Create((1, 1)));

        Assert.False(tree.Truncated);
        Assert.Equal(["first", "last", "escaped"], tree.Values.Select(v => v.Value));
        Assert.Equal("collections/inventory0/rows/~0~1row/cells/~0~1cell", tree.Values[2].Path);
    }

    [Fact]
    public void QueryFailure_DisposesDocumentAndDoesNotProduceACompleteTree()
    {
        var plugin = new QueryableInventoryPlugin { TransformPage = _ => throw new InvalidDataException("broken page") };

        var error = Assert.Throws<InvalidDataException>(() => Decode(plugin));

        Assert.Equal("broken page", error.Message);
        Assert.True(plugin.Disposed);
        Assert.Single(plugin.Queries);
    }

    private static SemanticTree Decode(QueryableInventoryPlugin plugin, DecodeLimits? limits = null, byte[]? bytes = null) =>
        new PluginViewDecoder(plugin).Decode(MemorySaveSet.SingleFile("inv.dat", bytes ?? Items), limits ?? Limits);

}
