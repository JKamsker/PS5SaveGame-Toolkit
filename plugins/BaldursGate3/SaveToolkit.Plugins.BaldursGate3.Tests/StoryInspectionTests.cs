using System.Text;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class StoryInspectionTests
{
    private const int StoryByteLimit = 64 * 1024 * 1024;

    [Fact]
    public void StoryMarkersArePresenceOnlyAndBounded()
    {
        var bytes = Encoding.UTF8.GetBytes("DB_ApprovalRating\0DB_PartOfTheTeam\0");
        var inspection = new StoryInspection([new DocumentEntry("StorySave.bin", bytes, null)]);

        var table = Assert.Single(inspection.Expand("story/databases").OfType<TableNode>());
        Assert.Equal(2, table.Rows.Count);
        Assert.Contains(table.Rows, row => row.Key == "DB_ApprovalRating");
        Assert.Contains("unknown", table.Description!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingStoryPayloadDoesNotInventDatabases()
    {
        var inspection = new StoryInspection([new DocumentEntry("Globals.lsf", [], null)]);

        var table = Assert.Single(inspection.Expand("story/databases").OfType<TableNode>());
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void LargeStoryPayloadUsesBoundedSinglePassSearch()
    {
        var bytes = new byte[2 * 1024 * 1024];
        Encoding.UTF8.GetBytes("DB_ApprovalRating").CopyTo(bytes, 17);
        Encoding.Unicode.GetBytes("DB_Quests").CopyTo(bytes, 1_000_000);
        var inspection = new StoryInspection([new DocumentEntry("StorySave.bin", bytes, null)]);

        var table = Assert.Single(inspection.Expand("story/databases").OfType<TableNode>());
        Assert.Equal(2, table.Rows.Count);
        Assert.Contains(table.Rows, row => row.Key == "DB_ApprovalRating");
        Assert.Contains(table.Rows, row => row.Key == "DB_Quests");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void OccurrencesIncludeAdjacentMarkersAndBothUtf16AlignmentsWithoutCountingPartialSuffixes(bool utf16, int padding)
    {
        var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
        byte[] bytes = [.. new byte[padding], .. encoding.GetBytes("DB_QuestsDB_Quests|DB_ApprovalRating|db_quests|DB_Quest")];
        var table = Markers(bytes);
        Assert.Equal(["DB_ApprovalRating", "DB_Quests"], table.Rows.Select(row => row.Key));
        Assert.Equal(["1", "2"], table.Rows.Select(row => Assert.IsType<TextNode>(row.Cells["occurrences"]).Text));
        Assert.All(table.Rows, row =>
        {
            Assert.Equal(row.Key, Assert.IsType<TextNode>(row.Cells["name"]).Text);
            Assert.Equal("Presence only; tuple values and joins unknown", Assert.IsType<TextNode>(row.Cells["confidence"]).Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryIncompleteMarkerPrefixIsIgnored(bool utf16)
    {
        var bytes = (utf16 ? Encoding.Unicode : Encoding.UTF8).GetBytes("DB_ApprovalRating");
        for (var length = 0; length < bytes.Length; length++)
            Assert.Empty(Markers(bytes[..length]).Rows);
        Assert.Equal("1", Assert.IsType<TextNode>(Assert.Single(Markers(bytes).Rows).Cells["occurrences"]).Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExactInspectionByteLimitIncludesTheFinalMarkerAndLargerPayloadsRemainUnknown(int excess)
    {
        var bytes = new byte[StoryByteLimit + excess];
        "DB_ApprovalRating"u8.CopyTo(bytes.AsSpan(bytes.Length - "DB_ApprovalRating"u8.Length));
        var rows = Markers(bytes).Rows;
        if (excess == 0)
        {
            var row = Assert.Single(rows);
            Assert.Equal("DB_ApprovalRating", row.Key);
            Assert.Equal("1", Assert.IsType<TextNode>(row.Cells["occurrences"]).Text);
        }
        else Assert.Empty(rows);
    }

    private static TableNode Markers(byte[] bytes) => Assert.Single(
        new StoryInspection([new DocumentEntry("StorySave.bin", bytes, null)]).Expand("story/databases").OfType<TableNode>());
}
