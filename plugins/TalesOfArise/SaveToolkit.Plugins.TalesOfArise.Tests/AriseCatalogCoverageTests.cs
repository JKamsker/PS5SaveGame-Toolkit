using System.Globalization;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseCatalogCoverageTests
{
    [Theory]
    [InlineData("items", 271)]
    [InlineData("equipment", 675)]
    [InlineData("artes", 309)]
    [InlineData("skills", 64)]
    [InlineData("titles", 108)]
    [InlineData("skill-nodes", 540)]
    [InlineData("characters", 62)]
    [InlineData("progress", 128)]
    public void EveryRecordHasPinnedShareAlikeProvenanceAndNoInventedSaveIdentity(string id, int expectedCount)
    {
        var plugin = new TalesOfArisePlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(store.Pin(plugin, "PPSA02745").Catalog(id));
        Assert.Equal(expectedCount, catalog.Count);
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/3.0/", catalog.Dataset.Attribution.LicenseUrl);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                Assert.True(keys.Add(record.Key));
                Assert.False(string.IsNullOrWhiteSpace(record.Description));
                Assert.Matches(@"\?oldid=\d+$", record.Source!);
                Assert.Contains(record.Source!, plugin.Info.References);
                Assert.Contains(record.Source!, record.AttributionText);
                Assert.Equal(record.Source!.Split("oldid=")[1], record.Attributes["sourceRevision"]);
                Assert.Null(record.Limit); // Published stack/skill facts must not silently become editing constraints.
                if (id != "items" || !int.TryParse(record.Key, CultureInfo.InvariantCulture, out var numeric))
                {
                    Assert.Equal("Unknown", record.Attributes["serializedId"]);
                }
                else
                {
                    Assert.InRange(numeric, 1, 13);
                    Assert.Contains("inferred", record.Attributes["identityEvidence"]);
                }
            }
        }

        Assert.Equal(expectedCount, keys.Count);
    }

    [Fact]
    public void SkillPanelsRetainIndividualCostsPrerequisitesAndBonuses()
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        var panels = context.Catalog("titles")!;
        var nodes = context.Catalog("skill-nodes")!;
        var ironMask = panels.Find("title:alphen:iron-mask")!;
        Assert.Equal("Attack +10", ironMask.Attributes["completionBonus"]);
        Assert.Contains("Alphen", ironMask.Attributes["requirements"]);
        var sonicThrust = nodes.Find("title:alphen:iron-mask:node:4")!;
        Assert.Equal("Sonic Thrust", sonicThrust.Name);
        Assert.Equal("100", sonicThrust.Attributes["spCost"]);
        Assert.Equal(ironMask.Key, sonicThrust.Attributes["titleKey"]);
        var perTitle = new Dictionary<string, int>();
        for (var offset = 0; offset < nodes.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var node in nodes.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                var title = node.Attributes["titleKey"];
                Assert.NotNull(panels.Find(title));
                Assert.Equal("Unknown", node.Attributes["state"]);
                perTitle[title] = perTitle.GetValueOrDefault(title) + 1;
            }
        }

        Assert.Equal(panels.Count, perTitle.Count);
        Assert.All(perTitle.Values, count => Assert.Equal(5, count));
    }

    [Fact]
    public void SourceVariantsAndReferenceUnknownsAreKeptWithoutInventedCorrections()
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        var variants = context.Catalog("equipment")!.Search(new ReferenceQuery { Text = "Noble Scarlet", Category = "Costume" }).Items;
        Assert.Equal(2, variants.Count);
        Assert.Equal(2, variants.Select(record => record.Key).Distinct().Count());
        Assert.Contains(variants, record => record.Attributes.ContainsKey("sourceVariant"));
        Assert.Contains("?", context.Catalog("skills")!.Find("skill:increased-critical-damage")!.Description);
        Assert.Equal("Shionne Vymer Imeris Daymore", context.Catalog("characters")!.Find("character:shionne")!.Name);
        Assert.Equal(25, context.Catalog("items")!.Search(new ReferenceQuery { Category = "Fishing item" }).Total);
    }
}
