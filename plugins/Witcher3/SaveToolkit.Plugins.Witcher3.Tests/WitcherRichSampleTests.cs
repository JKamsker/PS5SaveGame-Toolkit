using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherRichSampleTests
{
    [Fact]
    public void EverySampleHasExactAbilityCountsGwentCollectionAndMetadata()
    {
        int[] expectedCounts = [102, 16, 4, 13, 6, 14];
        foreach (var file in SampleSaves.Load("PPSA04018").AllDiskFiles.Where(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal)))
        {
            var bytes = file.ReadAllBytes();
            var container = WitcherContainer.Parse(bytes);
            var directory = Sav3Directory.Parse(container);
            var tree = Sav3Tree.Parse(container.Payload, directory);
            var player = Assert.IsType<PlayerData>(PlayerData.Read(container, directory, tree));
            var features = new ObservedFeatures(player, tree, container.Payload, directory.Names);
            Assert.Empty(features.Warnings);
            Assert.Equal(expectedCounts, features.Abilities.Select(a => a.Entries.Count));
            var skills = Assert.Single(features.Abilities, a => a.Name == "skills");
            Assert.Equal(16, skills.Entries.Count(e => e.Record.Integer("level") == 1 &&
                e.Record.Find("isCoreSkill")?.Value[0] == 1));
            Assert.DoesNotContain(features.SkillEdits!.Ranks, r => r.Entry.Id.StartsWith("unnamed-slot-", StringComparison.Ordinal) ||
                r.Entry.Record.Find("isCoreSkill")?.Value[0] == 1);
            Assert.NotEmpty(features.SkillEdits.Ranks);
            Assert.Equal(27, features.Cards!.Count);
            Assert.Equal(35, features.Cards.Sum(c => c.Copies));
            Assert.Contains(features.Cards, c => c.Index == 108 && c.Copies == 1);
            Assert.Contains(features.Cards, c => c.Index == 3 && c.Copies == 2);
            var potion = Assert.Single(player.Inventory!.Items, i => i.Name == "Tawny Owl 1");
            Assert.Equal(2, potion.Attributes.Count);
            Assert.Equal(-1, potion.Durability);
            Assert.Equal("armor", Assert.Single(player.Inventory.Items, i => i.Name == "Starting Armor").Slot);
            using var document = new Witcher3Plugin().Open(file);
            _ = document.BuildView();
            foreach (var array in features.Abilities) { Assert.NotEmpty(document.ExpandNode($"ability/{array.Name}/0")); }
            Assert.NotEmpty(((IQueryableDocument)document).Query(GwentCollection.CollectionId, new() { Limit = 200 }).Rows);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void PinnedPackFlowsThroughRichCellsPickerAndCoreQueryableBindings()
    {
        var plugin = new Witcher3Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(plugin, "PPSA04018");
        var catalog = Assert.IsAssignableFrom<SaveToolkit.Sdk.References.IReferenceCatalog>(references.Catalog("items"));
        Assert.Equal(4011, catalog.Count);
        var file = SampleSaves.Load("PPSA04018").AllDiskFiles.First(f => f.FileName.StartsWith("Manual.", StringComparison.Ordinal) && f.FileName.EndsWith(".sav", StringComparison.Ordinal));
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("witcher-rich", file, plugin, document, references: references);
        _ = session.BuildView();
        var page = session.QueryCollection(InventoryViews.CollectionId, new QueryableCollectionQuery { Text = "Kaer Morhen", Limit = 1 });
        var row = Assert.Single(page.Rows);
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(1, page.FilteredCount);
        var name = Assert.IsType<SectionNode>(row.Cells["name"]);
        Assert.Equal("Kaer Morhen armor", name.Title);
        Assert.Contains("CC-BY-SA-3.0", Assert.IsType<TextNode>(Assert.Single(name.Children)).Text, StringComparison.Ordinal);
        Assert.IsType<SectionNode>(row.Cells["details"]);
        var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.Equal(quantity.Value, document.GetValue(quantity.ValueId));
        var lookup = document.InvokeAction(InventoryViews.LookupActionId, new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("Bodkin Bolt") });
        Assert.Empty(lookup.Changes);
        Assert.Contains("infinite", lookup.Message!, StringComparison.Ordinal);
        Assert.Contains("CC-BY-SA-3.0", lookup.Message!, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => document.InvokeAction(InventoryViews.LookupActionId,
            new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("unmapped") }));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EveryStarterCatalogIdOccursInSamplesAndZeroQuantityIsRejectedAtomically()
    {
        var plugin = new Witcher3Plugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA04018");
        string[] starterIds = ["Bread", "Bottled water", "Crowns", "Torch", "Long Steel Sword", "Witcher Silver Sword",
            "Starting Armor", "Starting Boots", "Starting Pants", "Starting Gloves", "Bodkin Bolt", "Wolf Hour", "Tawny Owl 1",
            "q001_letter_from_yenn", "Monstrous eye", "Monstrous claw", "Venom extract", "Ghoul blood"];
        var mapped = starterIds.Select(id => context.Catalog("items")!.Find(id)!);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SampleSaves.Load("PPSA04018").AllDiskFiles.Where(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal)))
        {
            using var document = plugin.Open(file, context);
            _ = document.BuildView();
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            var page = queryable.Query(InventoryViews.CollectionId, new QueryableCollectionQuery { Limit = 200 });
            foreach (var row in page.Rows) { observed.Add(Assert.IsType<TextNode>(row.Cells["raw"]).Text); }
            var bread = Assert.Single(page.Rows, r => Assert.IsType<TextNode>(r.Cells["raw"]).Text == "Bread");
            var quantity = Assert.IsType<FieldNode>(bread.Cells["quantity"]);
            var oldLevel = document.GetValue("player/level");
            Assert.False(document.ApplyEdits([new("player/level", FieldValue.Of(2)), new(quantity.ValueId, FieldValue.Of(0))]).Succeeded);
            Assert.Equal(oldLevel, document.GetValue("player/level"));
            Assert.Equal(quantity.Value, document.GetValue(quantity.ValueId));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }

        Assert.All(mapped, record => Assert.Contains(record.Key, observed));
    }
}
