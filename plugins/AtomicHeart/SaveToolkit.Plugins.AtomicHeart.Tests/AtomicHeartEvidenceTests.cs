using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartEvidenceTests
{
    [Fact]
    public void F12HookExposesStableScalarTagsAndHonorsDecoderBounds()
    {
        var file = SaveToolkit.Testing.MemorySaveSet.SingleFile("slot.dpx.sav",
            SlotFixture.Create(plain: PlainFixture.Create(amount: 17)), "PPSA10695");
        var provider = Assert.IsAssignableFrom<ISaveDecoderProvider>(new AtomicHeartPlugin());
        var hook = Assert.Single(provider.Decoders);
        Assert.Equal("atomic-heart.saveextension-v1", hook.Id);
        Assert.True(hook.CanDecode(file));

        var tree = new HookSaveDecoder(hook).Decode(file, new DecodeLimits { MaxDepth = 8 });
        Assert.False(tree.Truncated);
        Assert.Contains(tree.Values, value => value.Path.Contains("TestRoot", StringComparison.Ordinal)
            && value.Value == "42");
        Assert.Contains(tree.Values, value => value.Path.Contains("Amount", StringComparison.Ordinal)
            && value.Value == "17");

        var truncated = new HookSaveDecoder(hook).Decode(file, new DecodeLimits { MaxDepth = 1 });
        Assert.True(truncated.Truncated);
        Assert.NotEmpty(truncated.Warnings);
    }

    [Fact]
    public void F13CatalogueInspectionIsReadOnlyAndCategoriesAreQueryable()
    {
        var plugin = new AtomicHeartPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA10695");
        var file = SaveToolkit.Testing.MemorySaveSet.SingleFile("slot.dpx.sav", SlotFixture.Create(), "PPSA10695");
        using var document = plugin.Open(file, context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = Assert.Single(tabs.Tabs, tab => tab.Title == "Inventory inspection");
        var action = Assert.Single(inventory.Children.OfType<ActionNode>(), node => node.ActionId == CatalogueInspection.ActionId);
        Assert.False(action.Mutates);
        var parameter = Assert.Single(action.Parameters);
        var spec = Assert.IsType<ReferenceSpec>(parameter.Spec);
        Assert.False(spec.ReadOnly);

        var catalog = context.Catalog(InventoryViews.CatalogId);
        Assert.NotNull(catalog);
        Assert.Contains(ReferenceSortKey.Category, catalog.SortKeys);
        var category = catalog.Categories.FirstOrDefault();
        Assert.NotNull(category);
        var page = catalog.Search(new ReferenceQuery { Category = category, SortBy = ReferenceSortKey.Category, Limit = 200 });
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal(category, item.Category));

        var before = file.ReadAllBytes();
        var outcome = document.InvokeAction(action.ActionId, new Dictionary<string, FieldValue>
        {
            [parameter.ValueId] = parameter.Value,
        });
        Assert.Contains(parameter.Value.AsString(), outcome.Message, StringComparison.Ordinal);
        Assert.Empty(outcome.Changes);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EverySampleHasExactDecodedRecordSpansAndRetainedOriginalBytes()
    {
        foreach (var file in SampleSaves.Load("PPSA10695").AllDiskFiles.Where(f => !f.IsSystemFile))
        {
            var bytes = file.ReadAllBytes();
            var outer = SaveExtensionFile.Parse(bytes);
            var index = SlotDataDecoder.Decode(bytes, outer);
            Assert.Equal(14, outer.Chunks.Count);
            Assert.Equal(1751391, index.Plain.Length);
            Assert.Equal(index.Plain.Length - sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(index.Plain));
            Assert.Equal(index.Plain.Length, outer.Chunks.Sum(c => c.UncompressedBytes));
            Assert.Equal(2, index.Properties.Count);
            Assert.Equal(262, index.Levels.Count);
            Assert.Equal(5, index.Objects.Count(r => r.Kind == "subsystem"));
            Assert.Equal(3502, index.Objects.Count(r => r.Kind == "actor"));
            Assert.Equal(556, index.Objects.Count(r => r.Kind == "component"));
            Assert.Equal(index.Plain.Length, index.Levels[^1].Span.End);
            Assert.InRange(index.Objects.Max(r => r.Span.End), 1, index.Plain.Length);
            Assert.Equal(index.Objects.Count, index.Objects.DistinctBy(r => r.Id).Count());
            Assert.All(index.Objects, record => {
                Assert.InRange(record.Span.Offset, 0, index.Plain.Length);
                Assert.InRange(record.Span.End, record.Payload.End, index.Plain.Length);
                Assert.InRange(record.Payload.Offset, record.Span.Offset, record.Span.End);
                Assert.InRange(record.UnknownTail.Offset, record.Payload.Offset, record.Payload.End);
                Assert.Equal(record.Payload.End, record.UnknownTail.End);
            });
            Assert.Contains(index.Objects, r => r.ClassPath == "None" && r.ClassSubPath.StartsWith("/Game/", StringComparison.Ordinal));
            var inventory = Assert.Single(index.Objects, r => r.Name == "BP_PlayerInventory_C_0");
            Assert.Equal(6, inventory.PrefixIds.Count);
            Assert.Equal(0, inventory.UnknownTail.Length);
            Assert.Contains(inventory.Properties, p => p.Name == "ResourcesList" && p.Children.Count != 0);
            Assert.Contains(index.Objects, r => r.UnknownTail.Length != 0);
            using var document = new AtomicHeartPlugin().Open(file);
            Assert.Equal(index.Plain, document.ReadBlob("plain", 0, int.MaxValue).ToArray());
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void F1FindsOneGenerationAndOneIdenticalBackupRatherThanIndependentGameplayEvidence()
    {
        var set = SampleSaves.Load("PPSA10695");
        var files = set.AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files.Select(f => f.Directory.Generation).Distinct());
        Assert.Single(files.Select(f => f.Sha256).Distinct());
        var decoder = new PluginViewDecoder(new AtomicHeartPlugin(), lazyExpansionBudget: 20000);
        var report = SaveSetDiffer.Compare(set, set, new SaveDiffOptions { Decoder = decoder });
        Assert.False(report.HasChanges);
        Assert.Equal(1, report.Before.Generations);
        Assert.Equal(1, report.Before.Twins.Identical);
        Assert.Equal(0, report.Totals.ChangedBytes);
        Assert.Equal(0, report.Totals.SemanticChanges);
        Assert.Equal(0, report.Totals.SemanticErrors);
        // F1 short-circuits identical files; explicitly walk both decoded views as additional evidence.
        var before = decoder.Decode(files[0], new DecodeLimits());
        var after = decoder.Decode(files[1], new DecodeLimits());
        Assert.False(before.Truncated);
        Assert.False(after.Truncated);
        Assert.Empty(before.Warnings);
        Assert.True(before.Values.Count > 1000, $"Decoded fields: {before.Values.Count}");
        Assert.Equal(0, SemanticDiffer.Compare(before, after).Total);
    }

    [Fact]
    public void AttributedCatalogJoinsExactIdsAndF4KeepsUnknownReferencesReadOnly()
    {
        var plugin = new AtomicHeartPlugin();
        var packs = new ReferencePackStore([plugin]);
        Assert.Empty(packs.LoadErrors);
        var context = packs.Pin(plugin, "PPSA10695");
        var catalog = context.Catalog("items");
        Assert.NotNull(catalog);
        Assert.Equal(45, catalog.Count);
        Assert.Equal("GPL-3.0-or-later", catalog.Dataset.Attribution.License);
        var swede = catalog.Find("DA_Item_Shved");
        Assert.NotNull(swede);
        Assert.Contains("Skorchekd", swede.AttributionText, StringComparison.Ordinal);
        Assert.Contains("Encryqed", swede.AttributionText, StringComparison.Ordinal);
        Assert.Null(catalog.Find("DA_Unknown_Item"));
        Assert.Null(swede.Limit);
        Assert.Null(swede.AssetId);
        foreach (var file in SampleSaves.Load("PPSA10695").AllDiskFiles.Where(f => !f.IsSystemFile))
        {
            using var named = plugin.Open(file, context);
            var table = Assert.IsType<TableNode>(Assert.Single(named.ExpandNode("inventory/0")));
            Assert.NotEmpty(table.Rows);
            var fields = table.Rows.Select(r => Assert.IsType<FieldNode>(r.Cells["asset"])).ToArray();
            Assert.Contains(fields, f => f.Value.AsString() == "DA_Item_Shved" && f.Label == "Swede / Shved" && f.Confidence == Confidence.Inferred);
            Assert.Contains(fields, f => f.Value.AsString() == "DA_Item_ShotgunKS23" && f.Label == "KS-23 Shotgun");
            Assert.Contains(fields, f => f.Confidence == Confidence.Unknown);
            Assert.All(fields, f => {
                Assert.IsType<ReferenceSpec>(f.Spec);
                Assert.Equal(f.Value, named.GetValue(f.ValueId));
            });
            Assert.Contains(fields, f => f.Description!.Contains("Skorchekd", StringComparison.Ordinal));
            var target = fields.First(f => f.Value.AsString() == "DA_Item_Shved");
            Assert.False(target.Spec.ReadOnly);
            Assert.False(named.ApplyEdits([new(target.ValueId, FieldValue.Of("DA_Unknown_Item"))]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(named.Serialize()).Content.ToArray());
            using var raw = plugin.Open(file);
            var rawTable = Assert.IsType<TableNode>(Assert.Single(raw.ExpandNode("inventory/0")));
            Assert.Contains(rawTable.Rows, row => row.Cells["asset"] is FieldNode { Label: "DA_Item_Shved" });
        }
    }
}
