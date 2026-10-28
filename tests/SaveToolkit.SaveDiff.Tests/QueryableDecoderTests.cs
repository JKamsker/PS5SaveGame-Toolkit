using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class QueryableDecoderTests
{
    private static readonly DecodeLimits Limits = new() { QueryPageSize = 2 };
    private static readonly byte[] Items = InventoryPlugin.Create((1, 1), (2, 2), (3, 3), (4, 4), (5, 5));

    private static SemanticTree Decode(QueryableInventoryPlugin plugin, DecodeLimits? limits = null, byte[]? bytes = null) =>
        new PluginViewDecoder(plugin).Decode(MemorySaveSet.SingleFile("inv.dat", bytes ?? Items), limits ?? Limits);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TraversesEveryPage_WithStableFieldAndTextIdentities(bool cursor, bool lazy)
    {
        var plugin = new QueryableInventoryPlugin { UseCursor = cursor, Lazy = lazy };
        var before = Decode(plugin);
        var after = Decode(plugin, bytes: InventoryPlugin.Create((5, 9), (3, 3), (1, 1), (4, 4), (2, 2)));

        var diff = SemanticDiffer.Compare(before, after);

        Assert.False(before.Truncated);
        Assert.Equal(10, before.Values.Count);
        Assert.Equal(before.Values.Select(v => v.Path), after.Values.Select(v => v.Path));
        Assert.Equal(2, diff.Changed);
        Assert.Contains(diff.Changes, c => c is { Path: "inventory0/5/quantity", Before: "5", After: "9" });
        Assert.Contains(diff.Changes, c => c is { Path: "collections/inventory0/rows/item~15/cells/state", Before: "stored:5", After: "stored:9" });
        Assert.Equal(6, plugin.Queries.Count);
        Assert.All(plugin.Queries, q => { Assert.Null(q.SortColumn); Assert.Null(q.Text); Assert.Empty(q.Filters); });
        Assert.True(plugin.Disposed);
    }

    [Fact]
    public void ReorderedRecords_ProduceIdenticalTrees_AndAddedRemovedRowsAlign()
    {
        var before = Decode(new(), bytes: InventoryPlugin.Create((1, 1), (2, 2), (3, 3)));
        var reordered = Decode(new(), bytes: InventoryPlugin.Create((3, 3), (1, 1), (2, 2)));
        var after = Decode(new(), bytes: InventoryPlugin.Create((4, 4), (1, 1), (3, 3)));

        Assert.Equal(before.Values, reordered.Values);
        var diff = SemanticDiffer.Compare(before, after);
        Assert.Equal((2, 2, 0), (diff.Added, diff.Removed, diff.Changed));
    }

    [Fact]
    public void RichCells_DecodeTitleScalarsAndNestedFields_WithoutProvenanceNoise()
    {
        var plugin = new QueryableInventoryPlugin
        {
            TransformPage = p => p with
            {
                Rows = [new("record", new Dictionary<string, ViewNode>
                {
                    ["state"] = new SectionNode("Stored scalar", [new TextNode("Source and licence")]),
                    ["details"] = new SectionNode("Details", [p.Rows[0].Cells["quantity"]]),
                })],
                TotalCount = 1,
                FilteredCount = 1,
            },
        };
        var tree = Decode(plugin, bytes: InventoryPlugin.Create((1, 1)));

        Assert.False(tree.Truncated);
        Assert.Contains(tree.Values, v => v is { Path: "collections/inventory0/rows/record/cells/state", Value: "Stored scalar" });
        Assert.Contains(tree.Values, v => v.Path == "inventory0/1/quantity");
        Assert.DoesNotContain(tree.Values, v => v.Value == "Source and licence");
    }

    [Fact]
    public void RowCap_LimitsQueriesAndMarksInputTruncated()
    {
        var plugin = new QueryableInventoryPlugin();
        var tree = Decode(plugin, Limits with { MaxQueryRows = 3 });

        Assert.Equal(6, tree.Values.Count);
        Assert.Equal([2, 1], plugin.Queries.Select(q => q.Limit));
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("row cap 3", StringComparison.Ordinal));
        Assert.True(SemanticDiffer.Compare(tree, tree).InputTruncated);
    }

    [Fact]
    public void PageCap_LimitsCalls_AndBudgetsAreGlobalAcrossCollections()
    {
        var plugin = new QueryableInventoryPlugin { CollectionCount = 2 };
        var tree = Decode(plugin, Limits with { MaxQueryPages = 4 });

        Assert.Equal(4, plugin.Queries.Count);
        Assert.Equal(14, tree.Values.Count);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("page cap 4", StringComparison.Ordinal));
    }

    [Fact]
    public void RowBudget_IsGlobalAcrossCollections()
    {
        var tree = Decode(new() { CollectionCount = 2 }, Limits with { MaxQueryRows = 6 });

        Assert.Equal(12, tree.Values.Count);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Values, v => v.Path == "inventory1/1/quantity");
        Assert.DoesNotContain(tree.Values, v => v.Path == "inventory1/2/quantity");
    }

    [Fact]
    public void ByteCap_CountsReadOnlyContent_AndRejectsTheWholeOverBudgetPage()
    {
        var plugin = new QueryableInventoryPlugin
        {
            TransformPage = p => p with { Rows = [new("oversized", new Dictionary<string, ViewNode> { ["text"] = new TextNode(new string('界', 10_000)) })] },
        };
        var tree = Decode(plugin, Limits with { MaxQueryBytes = 1_000 });

        Assert.Empty(tree.Values);
        Assert.Single(plugin.Queries);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("byte cap 1000", StringComparison.Ordinal));
    }

    [Fact]
    public void ByteBudget_IsGlobal_AndCountsUtf8Bytes()
    {
        // A complete first page fits, but a second copy cannot reuse its byte budget.
        using var document = new QueryableInventoryPlugin().Open(MemorySaveSet.SingleFile("inv.dat", Items));
        var queryable = (SaveToolkit.Sdk.Collections.IQueryableDocument)document;
        var pageBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(queryable.Query("inventory0", new() { Limit = 2 })).Length;
        var plugin = new QueryableInventoryPlugin();
        var tree = Decode(plugin, Limits with { MaxQueryBytes = pageBytes });

        Assert.Equal(4, tree.Values.Count);
        Assert.Single(plugin.Queries);
        Assert.True(tree.Truncated);
    }

    [Fact]
    public void OversizedPage_ExhaustsByteBudgetForAllCollections()
    {
        var plugin = new QueryableInventoryPlugin { CollectionCount = 2 };
        var tree = Decode(plugin, Limits with { MaxQueryBytes = 1 });

        Assert.Empty(tree.Values);
        Assert.Single(plugin.Queries);
        Assert.True(tree.Truncated);
    }

    [Fact]
    public void ByteCap_IncludesEscapedUnicodeInSerializedResponses()
    {
        var plugin = new QueryableInventoryPlugin
        {
            TransformPage = p => p with { Rows = [new("unicode", new Dictionary<string, ViewNode> { ["text"] = new TextNode(new string('界', 100)) })] },
        };
        using var document = plugin.Open(MemorySaveSet.SingleFile("inv.dat", Items));
        var page = ((SaveToolkit.Sdk.Collections.IQueryableDocument)document).Query("inventory0", new() { Limit = 2 });
        var json = System.Text.Json.JsonSerializer.Serialize(page);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        Assert.Contains("\\u754C", json, StringComparison.Ordinal);

        var tree = Decode(plugin, Limits with { MaxQueryBytes = bytes - 1 });

        Assert.Empty(tree.Values);
        Assert.True(tree.Truncated);
    }

    [Fact]
    public void InvalidPageMetadata_StopsBeforeEmittingRows()
    {
        var plugin = new QueryableInventoryPlugin { TransformPage = p => p with { Offset = 7 } };
        var tree = Decode(plugin);

        Assert.Empty(tree.Values);
        Assert.Single(plugin.Queries);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("invalid page", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 100, 10000)]
    [InlineData(100, 0, 10000)]
    [InlineData(100, 100, 0)]
    public void ZeroBudgets_DoNotQuery(int rows, int pages, long bytes)
    {
        var plugin = new QueryableInventoryPlugin();
        var tree = Decode(plugin, Limits with { MaxQueryRows = rows, MaxQueryPages = pages, MaxQueryBytes = bytes });

        Assert.Empty(plugin.Queries);
        Assert.True(tree.Truncated);
    }

    [Fact]
    public void ExactRowAndPageCaps_DoNotMarkCompleteCollectionsTruncated()
    {
        var tree = Decode(new(), Limits with { MaxQueryRows = 5, MaxQueryPages = 3 });

        Assert.False(tree.Truncated);
        Assert.Equal(10, tree.Values.Count);
    }

    [Fact]
    public void EmptyCollections_DoNotConsumeBudgetsOrMarkTruncated()
    {
        var plugin = new QueryableInventoryPlugin { CollectionCount = 2 };
        var tree = Decode(plugin, Limits with { MaxQueryRows = 0, MaxQueryPages = 0, MaxQueryBytes = 0 }, InventoryPlugin.Create());

        Assert.Empty(plugin.Queries);
        Assert.Empty(tree.Values);
        Assert.False(tree.Truncated);
    }

    [Fact]
    public void RepeatedTablesAndEagerFields_AreNotDuplicated()
    {
        var plugin = new QueryableInventoryPlugin { DuplicateTables = true, DuplicateField = true };
        var tree = Decode(plugin);

        Assert.Equal(10, tree.Values.Count);
        Assert.Equal(3, plugin.Queries.Count);
        Assert.Empty(tree.Warnings);
    }

    [Fact]
    public void ValueAndDepthLimits_StillApplyToQueryableCells()
    {
        var limited = Decode(new(), Limits with { MaxValues = 3 });
        var shallow = Decode(new(), Limits with { MaxDepth = 1 });

        Assert.Equal(3, limited.Values.Count);
        Assert.True(limited.Truncated);
        Assert.Empty(shallow.Values);
        Assert.True(shallow.Truncated);
    }

    [Fact]
    public void DuplicateRowsAcrossPages_StopWithoutFabricatingIdentities()
    {
        var plugin = new QueryableInventoryPlugin { TransformPage = p => p with { Rows = [p.Rows[0] with { Key = "same" }] } };
        var tree = Decode(plugin);

        Assert.Equal(2, plugin.Queries.Count);
        Assert.Equal(2, tree.Values.Count);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyPagesWithRemainingRows_AreReportedAsIncomplete()
    {
        var plugin = new QueryableInventoryPlugin { TransformPage = p => p with { Rows = [] } };
        var tree = Decode(plugin);

        Assert.Single(plugin.Queries);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("empty page", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedCursors_StopWithinBudget()
    {
        var plugin = new QueryableInventoryPlugin { UseCursor = true, TransformPage = p => p with { NextCursor = "2" } };
        var tree = Decode(plugin);

        Assert.Equal(2, plugin.Queries.Count);
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, w => w.Contains("repeated cursor", StringComparison.Ordinal));
    }
}
