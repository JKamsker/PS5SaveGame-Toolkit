using System.Text;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class StoryInspectionTests
{
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
}
