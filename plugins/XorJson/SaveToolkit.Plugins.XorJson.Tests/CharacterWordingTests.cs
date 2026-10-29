using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CharacterWordingTests
{
    private const string CharacterJson = """
        {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
         "CharacterSaveV1":{"MetaData":{"IsOnline":false,"Level":7,"PowerLevel":12},
          "future":42,"TypeTag":"SW.Item.Sword"}}
        """;

    [Fact]
    public void CharacterWarningSeparatesObservedStorageFromUnverifiedMeaning()
    {
        const string expectedWarning = "Experimental character write. Not validated on console. " +
            "The FCharacterSaveV1 storage layout is observed; gameplay meanings and dependencies remain unverified. " +
            "The game may reject, repair or clamp the save. No checksum or dependency behavior is known.";
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var file = MemorySaveSet.SingleFile("hero.sav", System.Text.Encoding.UTF8.GetBytes(CharacterJson), "PPSA16064");
        using var document = plugin.Open(file, references);
        Assert.True(document.CanEdit);
        var fields = XorJsonTests.Fields(document);
        Assert.All(fields, field => Assert.Equal(expectedWarning, field.Warning));
        Assert.All(fields.Where(field => field.Label != "TypeTag"), field => Assert.Equal(Confidence.Unknown, field.Confidence));
        Assert.Equal(Confidence.Inferred, fields.Single(field => field.Label == "TypeTag").Confidence);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void CharacterHeadlinesUseStoredLabelsAndRetainNativePaths()
    {
        var file = MemorySaveSet.SingleFile("hero.sav", System.Text.Encoding.UTF8.GetBytes(CharacterJson));
        using var document = new XorJsonPlugin().Open(file);
        var hero = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Hero");
        var stats = Assert.Single(hero.Children.OfType<StatsNode>()).Items;
        Assert.Equal(["Stored level", "Stored power level"], stats.Select(stat => stat.Label));
        Assert.Equal(["7", "12"], stats.Select(stat => stat.Value));
        Assert.Contains("CharacterSaveV1.MetaData.Level", stats[0].Hint!);
        Assert.Contains("CharacterSaveV1.MetaData.PowerLevel", stats[1].Hint!);
    }
}
