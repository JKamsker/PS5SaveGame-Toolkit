using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class InventoryDecoderTests
{
    private const int InventorySize = 251;
    private const string CellPrefix = "collections/inventory~1items/rows/";
    private const string IdSuffix = "/cells/id";

    [Fact]
    public void SharedF12TraversalReadsEveryInventoryPageWithDeterministicIdentities()
    {
        var bytes = SyntheticInventory.Create(InventorySize);
        var file = File(bytes);
        var decoder = new PluginViewDecoder(new PragmataPlugin());
        var tree = decoder.Decode(file, new() { QueryPageSize = 73 });
        Assert.False(tree.Truncated);
        var ids = InventoryCells(tree);
        Assert.Equal(InventorySize, ids.Count);
        Assert.Equal(InventorySize, ids.Select(value => value.Path).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(ids, value => value.Value == $"0x000000fb / {InventorySize}");
        Assert.DoesNotContain(tree.Values, value => value.Path == "quantity"); // Action parameter, not saved data.
        Assert.Equal(tree.Values.OrderBy(value => value.Path, StringComparer.Ordinal),
            decoder.Decode(file, new() { QueryPageSize = 200 }).Values.OrderBy(value => value.Path, StringComparer.Ordinal));
        Assert.Equal(bytes, file.ReadAllBytes());
    }

    [Fact]
    public void SharedF12TraversalDetectsAQuantityEditBeyondTheFirstPage()
    {
        var bytes = SyntheticInventory.Create(InventorySize);
        using var document = new PragmataDocument("data000.bin", bytes);
        var quantity = document.Layout.Items[^1].Find(KnownHashes.Quantity)!;
        _ = document.Field(quantity);
        Assert.True(document.ApplyEdits([new(quantity.Path, FieldValue.Of(17L))]).Succeeded);
        var decoder = new PluginViewDecoder(new PragmataPlugin());
        var before = decoder.Decode(File(bytes), new());
        var after = decoder.Decode(File(document.Serialize().Single().Content.ToArray()), new());
        var diff = SemanticDiffer.Compare(before, after);
        Assert.False(diff.InputTruncated);
        Assert.Equal(1, diff.Total); // Raw and F8 share the canonical quantity identity.
        Assert.Equal(quantity.Path, Assert.Single(diff.Changes).Path);
        Assert.All(diff.Changes, change =>
        {
            Assert.Equal((250 % 11).ToString(CultureInfo.InvariantCulture), change.Before);
            Assert.Equal("17", change.After);
        });
    }

    [Theory]
    [InlineData("rows", 201, 201)]
    [InlineData("pages", 1, 100)]
    [InlineData("pages", 0, 0)]
    [InlineData("bytes", 1, 0)]
    public void SharedF12TraversalReportsCapsWithoutInventingRemovals(string bound, int cap, int expectedRows)
    {
        var file = File(SyntheticInventory.Create(InventorySize));
        var decoder = new PluginViewDecoder(new PragmataPlugin());
        var limits = new DecodeLimits { QueryPageSize = 100 };
        limits = bound switch
        {
            "rows" => limits with { MaxQueryRows = cap },
            "pages" => limits with { MaxQueryPages = cap },
            "bytes" => limits with { MaxQueryBytes = cap },
            _ => throw new ArgumentException("Unknown test bound.", nameof(bound)),
        };
        var capped = decoder.Decode(file, limits);
        Assert.True(capped.Truncated);
        Assert.Equal(expectedRows, InventoryCells(capped).Count);
        Assert.Contains(capped.Warnings, warning => warning.Contains("Collection 'inventory/items'", StringComparison.Ordinal)
            && warning.Contains($"{bound.TrimEnd('s')} cap {cap}", StringComparison.Ordinal));
        Assert.True(SemanticDiffer.Compare(decoder.Decode(file, new()), capped).InputTruncated);
    }

    private static ISaveFile File(byte[] bytes) => MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530");
    private static IReadOnlyList<SemanticValue> InventoryCells(SemanticTree tree) => tree.Values
        .Where(value => value.Path.StartsWith(CellPrefix, StringComparison.Ordinal) && value.Path.EndsWith(IdSuffix, StringComparison.Ordinal)).ToArray();
}
