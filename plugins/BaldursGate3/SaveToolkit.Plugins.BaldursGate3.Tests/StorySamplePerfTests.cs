using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class StorySamplePerfTests(ITestOutputHelper output)
{
    private const int MaximumExpansionAllocation = 64 * 1024;

    [Fact]
    public void SampleStoryMarkerFirstExpansionStaysBounded()
    {
        var file = SampleLiveNewAgeTests.Save();
        _ = LspkPackage.Read(file.ReadAllBytes(), out var payloads);
        Assert.NotEmpty(payloads["StorySave.bin"]);
        var expected = ExpectedOccurrences(payloads["StorySave.bin"]);
        // This corpus has no known raw markers. Synthetic tests supply positive occurrence controls.
        SaveToolkit.Sdk.ISaveDocument current = null!;
        var allocated = AllocationProbe.Measure(() => current.ExpandNode("story/databases"), out var expanded,
            beforeRun: () =>
            {
                current?.Dispose();
                current = new BaldursGate3Plugin().Open(file);
            });
        using var document = current;
        output.WriteLine($"Story marker first expansion: {allocated}.");
        allocated.AssertLessThan(MaximumExpansionAllocation, $"Story marker expansion allocated {allocated.MinimumBytes:N0} bytes.");
        var table = Assert.Single(expanded.OfType<TableNode>());
        var stat = Assert.Single(Assert.Single(expanded.OfType<StatsNode>()).Items);
        Assert.Equal("Known marker families", stat.Label);
        Assert.Equal(expected.Count.ToString(CultureInfo.InvariantCulture), stat.Value);
        Assert.Equal(expected.Keys, table.Rows.Select(row => row.Key));
        foreach (var row in table.Rows)
        {
            Assert.Equal(row.Key, Assert.IsType<TextNode>(row.Cells["name"]).Text);
            Assert.Equal(expected[row.Key].ToString(CultureInfo.InvariantCulture),
                Assert.IsType<TextNode>(row.Cells["occurrences"]).Text);
            Assert.Equal("Presence only; tuple values and joins unknown", Assert.IsType<TextNode>(row.Cells["confidence"]).Text);
        }
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static Dictionary<string, int> ExpectedOccurrences(byte[] bytes)
    {
        // Independent string/regex oracle, including both byte alignments for UTF-16LE.
        // The inspector searches bytes directly; neither its scanner nor its marker list is reused.
        string[] markers = ["DB_Players", "DB_Avatars", "DB_PartOfTheTeam", "DB_ApprovalRating",
            "DB_Quests", "Journal", "Waypoint", "Recipe", "Camp"];
        string[] decoded = [Encoding.Latin1.GetString(bytes),
            Encoding.Unicode.GetString(bytes, 0, bytes.Length & ~1),
            Encoding.Unicode.GetString(bytes, 1, (bytes.Length - 1) & ~1)];
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var marker in markers)
        {
            var count = decoded.Sum(text => Regex.Count(text, Regex.Escape(marker), RegexOptions.CultureInvariant));
            if (count > 0) result.Add(marker, count);
        }
        return result;
    }
}
