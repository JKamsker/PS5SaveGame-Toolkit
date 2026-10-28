using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ReferenceViewTests
{
    private const string ItemId = "2227368435";

    [Fact]
    public void EmbeddedCatalogsCarryLicensesAndExplicitUnknownNames()
    {
        var plugin = new PragmataPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var pack = Assert.Single(store.Packs);
        Assert.Equal("pragmata.evidence.en", pack.Id);
        var context = store.Pin(plugin, "PPSA02530");
        Assert.Equal(408, context.Catalog("pragmata-identifiers")!.Count);
        Assert.Equal(27, context.Catalog("pragmata-sections")!.Count);
        var items = context.Catalog(ReferenceReadouts.ItemsCatalog)!;
        Assert.Equal(90, items.Count);
        var item = items.Find(ItemId)!;
        Assert.StartsWith("Unmapped item", item.Name, StringComparison.Ordinal);
        Assert.Equal("Unknown", item.Attributes["confidence"]);
        Assert.Null(item.Limit);
        Assert.Equal("CC0-1.0", item.Dataset.Attribution.License);
        Assert.Equal("MIT", context.Catalog("pragmata-identifiers")!.Dataset.Attribution.License);
        Assert.Empty(store.Pin(plugin, "PPSA99999").Packs);
    }

    [Fact]
    public void MissionCrossReferencesMatchObservedPs5IdsWithoutInventingNamesOrCaps()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var catalog = context.Catalog(ReferenceReadouts.ItemEvidenceCatalog)!;
        Assert.Equal(6, catalog.Count);
        Assert.Equal("MIT", catalog.Dataset.Attribution.License);
        Assert.Contains("Egg Targaryen", catalog.Dataset.Attribution.Author, StringComparison.Ordinal);
        var records = catalog.Search(new ReferenceQuery { Limit = 20 }).Items;
        Assert.Equal(6, records.Count);
        foreach (var file in SampleEvidenceTests.Files().Where(file => file.FileName != "data00-1.bin"))
        {
            using var document = (PragmataDocument)plugin.Open(file, context);
            foreach (var record in records)
            {
                var item = document.Layout.Items.Single(item => document.Read(item.Find(KnownHashes.ItemId)!).ToString() == record.Key);
                Assert.Equal("Inferred", record.Attributes["confidence"]);
                Assert.Equal("Unknown", record.Attributes["nameConfidence"]);
                Assert.Equal(3, record.Attributes["missionIds"].Split(',').Length);
                Assert.Null(record.Limit);
                Assert.Equal(Confidence.Unknown, document.References.ItemField(item.Find(KnownHashes.ItemId)!).Confidence);
                Assert.Contains(document.References.ItemDetails(uint.Parse(record.Key, System.Globalization.CultureInfo.InvariantCulture)).OfType<TextNode>(),
                    node => node.Text.Contains("OpenConditionItemID", StringComparison.Ordinal) && node.Text.Contains("Egg Targaryen", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void EmptyContextPreservesExactIdsAndRejectsReferenceWritesAtomically()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var table = Inventory(document);
        var field = table.Rows.Select(r => r.Cells["name"]).OfType<FieldNode>().Single(f => f.Value.AsString() == ItemId);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        var spec = Assert.IsType<ReferenceSpec>(field.Spec);
        Assert.True(spec.ReadOnly);
        Assert.False(spec.AllowCustom);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.False(document.ApplyEdits([new(EditingTests.Quantity(document).Path, FieldValue.Of(1L)),
            new(field.ValueId, FieldValue.Of("1"))]).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void UserPackOverridesNamesWithAttributionButCannotAuthorizeLimitsOrEdits()
    {
        var plugin = new PragmataPlugin();
        var store = new ReferencePackStore([plugin]);
        store.Import(UserPack(), "test-reference.zip");
        var context = store.Pin(plugin, "PPSA02530");
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var document = (PragmataDocument)plugin.Open(file, context);
        var row = Inventory(document).Rows.Single(r => ((FieldNode)r.Cells["name"]).Value.AsString() == ItemId);
        var name = Assert.IsType<FieldNode>(row.Cells["name"]);
        Assert.Equal("Example supplied name", name.Label);
        Assert.Equal(Confidence.Inferred, name.Confidence);
        Assert.Contains("Test authors", name.Description!, StringComparison.Ordinal);
        Assert.Contains("CC0-1.0", name.Description!, StringComparison.Ordinal);
        var details = Assert.IsType<SectionNode>(row.Cells["details"]);
        Assert.Contains(details.Children.OfType<TextNode>(), n => n.Text.Contains("Example supplied description", StringComparison.Ordinal));
        var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.Equal(int.MaxValue, Assert.IsType<IntegerSpec>(quantity.Spec).Max);
        Assert.Contains(context.Datasets(), d => d.Used && d.Pack.Id == "pragmata.test.en");
        Assert.True(name.Spec.ReadOnly);
        Assert.NotNull(JsonSerializer.Serialize<ViewNode>(document.BuildView()));
    }

    [Fact]
    public void ReadOnlyReferenceAliasesRefreshAndFrameworkUndoRestoresOriginalBytes()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var document = (PragmataDocument)plugin.Open(file, context);
        using var session = new DocumentSession("references", file, plugin, document, references: context);
        _ = session.BuildView();
        var target = document.Layout.Items[0].Find(KnownHashes.ItemId)!;
        var reference = document.References.ItemField(target);
        IReadOnlyList<ViewNode> nodes = ((TabsNode)session.BuildView()).Tabs.Single(t => t.Title == "Raw").Children;
        var ancestors = new Stack<SaveNode>();
        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent) ancestors.Push(ancestor);
        foreach (var ancestor in ancestors)
        {
            var section = nodes.OfType<SectionNode>().Single(s => s.LazyId!.StartsWith($"tree/{ancestor.Path}/", StringComparison.Ordinal));
            nodes = session.Expand(section.LazyId!);
        }
        var raw = nodes.OfType<FieldNode>().Single(f => f.ValueId == $"raw/{target.Path}");
        var result = session.ApplyEdits([new(raw.ValueId, JsonSerializer.SerializeToElement("4294967295"))]);
        Assert.True(result.Ok);
        Assert.Equal("4294967295", document.GetValue(reference.ValueId).AsString());
        Assert.Contains(result.Changes, change => change.ValueId == reference.ValueId);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.Equal(reference.Value, document.GetValue(reference.ValueId));
        Assert.True(session.Redo().Ok);
        Assert.Equal("4294967295", document.GetValue(reference.ValueId).AsString());
    }

    [Fact]
    public void NewTabsAreReadOnlyAndExpansionsRemainBounded()
    {
        using var document = EditingTests.Gameplay();
        var tabs = ((TabsNode)document.BuildView()).Tabs;
        foreach (var title in new[] { "Gadgets", "Loadout", "Progress", "Quests", "World" })
        {
            var children = tabs.Single(t => t.Title == title).Children;
            var roots = children.OfType<SectionNode>().ToArray();
            Assert.True(roots.Length > 0 || children.OfType<QueryableTableNode>().Any());
            foreach (var root in roots)
            {
                var nodes = document.ExpandNode(root.LazyId!);
                Assert.InRange(nodes.Count, 0, LazyPages.PageSize);
                Assert.All(nodes.OfType<FieldNode>(), field =>
                {
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(Confidence.Unknown, field.Confidence);
                    Assert.False(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
                });
            }
        }
    }

    private static TableNode Inventory(PragmataDocument document) => ((TabsNode)document.BuildView()).Tabs
        .Single(t => t.Title == "Inventory").Children.OfType<TableNode>().Single();

    private static byte[] UserPack()
    {
        var records = JsonSerializer.SerializeToUtf8Bytes(new { records = new[] { new
        {
            id = ItemId, name = "Example supplied name", category = "example", limit = 1,
            description = "Example supplied description",
        } } });
        var manifest = new
        {
            schema = 1, id = "pragmata.test.en", version = "1.0.0", title = "Test references", plugins = new[] { "pragmata" },
            titleIds = new[] { "PPSA02530" }, locale = "en", datasets = new[] { new
            {
                id = ReferenceReadouts.ItemsCatalog, kind = "records", files = new[] { new { path = "items.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(records)) } },
                attribution = new { sourceUrl = "https://example.org/pragmata", author = "Test authors", license = "CC0-1.0",
                    licenseUrl = "https://creativecommons.org/publicdomain/zero/1.0/", retrieved = "2026-10-28", modifications = "Original test data" },
            } },
        };
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry("pack.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
            using (var entry = zip.CreateEntry("items.json").Open()) entry.Write(records);
        }
        return stream.ToArray();
    }
}
