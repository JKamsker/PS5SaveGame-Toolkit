using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ProgressTests
{
    [Theory]
    [InlineData("PPSA01911", 69)]
    [InlineData("PPSA08664", 470)]
    [InlineData("PPSA18936", 470)]
    public void AllPrimarySamplesExposeDistinctReadOnlyBitsAndRoundTrip(string title, int expectedCount)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file =>
            schema.TryGetRole(file.FileName, out var common) && !common && !file.Directory.IsBackup))
        {
            var bytes = file.ReadAllBytes();
            using var document = new FinalFantasy7Plugin().Open(file);
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; offset < expectedCount; offset += 200)
            {
                var page = queryable.Query("progress", new() { Offset = offset, Limit = 200 });
                Assert.Equal(expectedCount, page.TotalCount);
                foreach (var row in page.Rows)
                {
                    var address = ((TextNode)row.Cells["address"]).Text;
                    Assert.True(addresses.Add(address));
                    var parts = row.Key.Split('/');
                    var byteOffset = Convert.ToInt32(parts[2], 16);
                    var bit = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    var field = Assert.IsType<FieldNode>(row.Cells["value"]);
                    Assert.Equal((bytes[byteOffset] >> bit) & 1, field.Value.AsInt32());
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(Confidence.Inferred, field.Confidence);
                    Assert.DoesNotContain("Intel Entries", ((TextNode)row.Cells["category"]).Text, StringComparison.Ordinal);
                }
            }

            Assert.Equal(expectedCount, addresses.Count);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void RegionalFiltersKeepDiscoveryCompletionAndRepairSeparate()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = queryable.Query("progress", new()
        {
            Filters = new Dictionary<string, string> { ["region"] = "Grasslands", ["category"] = "Chocobo Stops / Repaired" },
        });
        Assert.Equal(8, page.FilteredCount);
        Assert.All(page.Rows, row => Assert.Equal("Grasslands", ((TextNode)row.Cells["region"]).Text));
        Assert.NotEqual(0, queryable.Query("progress", new() { Text = "Discovered" }).FilteredCount);
    }
}
