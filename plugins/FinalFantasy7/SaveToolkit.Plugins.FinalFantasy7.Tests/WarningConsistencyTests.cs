using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class WarningConsistencyTests
{
    private const string ConsoleWarning = "Not validated on console. The game may reject or overwrite the edited save.";

    [Theory]
    [InlineData("PPSA01911", "ff7remake000")]
    [InlineData("PPSA01911", "ff7remakeplus000")]
    [InlineData("PPSA08664", "ff7rebirth000")]
    [InlineData("PPSA18936", "ff7rebirth000")]
    public void PublishedWarningsKeepCommonOpeningAndFormatQualifications(string title, string name)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        using var document = new FinalFantasy7Plugin().Open(file);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var overview = view.Tabs.Single(tab => tab.Title == "Overview");
        var integrity = Assert.Single(overview.Children.OfType<TextNode>(), node => node.Tone == Tone.Warning);
        Assert.StartsWith(ConsoleWarning, integrity.Text);
        Assert.Contains("Internal integrity is unconfirmed", integrity.Text);
        Assert.Contains("Keep a backup", integrity.Text);

        var gameplay = Assert.Single(overview.Children.OfType<SectionNode>());
        var gil = Assert.Single(gameplay.Children.OfType<FieldNode>(), field => field.ValueId == "game/gil");
        Assert.StartsWith(ConsoleWarning, gil.Warning);
        Assert.Contains("Limits are editor policy, not proven game caps", gil.Warning);
        if (title == "PPSA18936")
            Assert.Contains("PC retail field mapping is inferred for the PS5 demo", gil.Warning);

        var characters = Assert.IsType<TableNode>(Assert.Single(view.Tabs.Single(tab => tab.Title == "Characters").Children));
        var editable = characters.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>()
            .Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.NotEmpty(editable);
        Assert.All(editable, field => Assert.StartsWith(ConsoleWarning, field.Warning));

        var progress = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("progress", new() { Limit = 1 });
        var bit = Assert.IsType<FieldNode>(Assert.Single(progress.Rows).Cells["value"]);
        Assert.StartsWith(ConsoleWarning, bit.Warning);
        Assert.Contains("Rewards, actors, counters, prerequisites and chapter-reset dependencies are not repaired", bit.Warning);
        Assert.Contains("softlocks are possible", bit.Warning);
    }
}
