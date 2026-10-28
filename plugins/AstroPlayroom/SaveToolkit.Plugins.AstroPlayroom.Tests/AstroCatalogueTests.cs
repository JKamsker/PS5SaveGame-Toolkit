using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroCatalogueTests
{
    [Fact]
    public void EmbeddedPackHasCompleteExactKeysAttributionAndNoInventedLimitsOrMedia()
    {
        var (plugin, store, context) = AstroCatalogueTestData.References();
        var pack = Assert.Single(store.Packs);
        Assert.Equal("astro.catalogue.en", pack.Id);
        Assert.Empty(pack.Info.GameBuilds);
        var catalog = context.Catalog("astro_collectibles")!;
        Assert.Equal(164, catalog.Count);
        Assert.Equal(22, context.Catalog("astro_worlds")!.Count);
        var records = catalog.Search(new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }).Items;
        Assert.Equal(164, records.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(50, records.Count(r => r.Attributes["mapping"] == "candidate-identity"));
        foreach (var record in records.Concat(context.Catalog("astro_worlds")!.Search(new ReferenceQuery()).Items))
        {
            Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
            Assert.Equal("https://creativecommons.org/licenses/by-sa/3.0/", record.Dataset.Attribution.LicenseUrl);
            Assert.Contains(record.Source!, plugin.Info.References);
            Assert.Equal(AstroCatalogueTestData.Fingerprint, record.Attributes["schemaFingerprint"]);
            Assert.Contains("2026-10-28", record.Attributes["sourceVersion"], StringComparison.Ordinal);
            Assert.Null(record.Limit);
            Assert.Null(record.AssetId);
            Assert.NotEmpty(record.Dataset.Attribution.Modifications);
        }
    }

    [Fact]
    public void BothSampleGenerationsHaveNamedReadOnlyRowsHonestCountsAndIdenticalNoOpOutput()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            Assert.Equal(AstroCatalogueTestData.Fingerprint, AstroCatalogueTestData.SchemaHash(document));
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
            CheckGroup("Artifacts", "Artifact", 50, 50, file.Directory.Generation == 0 ? 0 : 3);
            CheckGroup("Puzzle pieces", "Puzzle", 96, 0, file.Directory.Generation == 0 ? 0 : 9);
            CheckGroup("Props", "Prop", 18, 0, file.Directory.Generation == 0 ? 0 : 1);
            var ds2 = AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/ds2");
            Assert.Equal("DualShock 2", Assert.IsType<FieldNode>(ds2.Cells["name"]).Value.AsString());

            void CheckGroup(string title, string group, int count, int mapped, int owned)
            {
                var section = AstroCatalogueTestData.Group(document, title);
                Assert.Equal($"{owned}/{count} owned; {mapped} mapped / {count - mapped} unmapped; 0 unknown ownership", section.Badge);
                var rows = AstroCatalogueTestData.Table(document, title).Rows;
                Assert.Equal(count, rows.Count);
                Assert.Equal(count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
                var savedKeys = AstroTestData.Fields(document).Where(f => f.Description!.StartsWith($"PreInstall/{group}/", StringComparison.Ordinal)
                    && f.Label == "IsGot").Select(f => $"{group}/{f.Description!.Split('/')[2]}").Order(StringComparer.Ordinal);
                Assert.Equal(savedKeys, rows.Select(row => Assert.IsType<TextNode>(row.Cells["key"]).Text).Order(StringComparer.Ordinal));
                foreach (var row in rows)
                {
                    var name = Assert.IsType<FieldNode>(row.Cells["name"]);
                    Assert.True(name.Spec.ReadOnly);
                    Assert.Equal(group == "Artifact" ? Confidence.Inferred : Confidence.Unknown, name.Confidence);
                    Assert.Contains("CC-BY-SA-3.0", name.Description, StringComparison.Ordinal);
                    Assert.Contains("https://", name.Description, StringComparison.Ordinal);
                    Assert.Equal(name.Value, document.GetValue(name.ValueId));
                    Assert.False(document.ApplyEdits([new ValueEdit(name.ValueId, FieldValue.Of("wrong"))]).Succeeded);
                    Assert.IsType<SectionNode>(row.Cells["details"]);
                }
            }
        }
    }

    [Fact]
    public void InternalPuzzleGroupsPreserveFiveThreeSplitsAndNeverClaimPhysicalOrdinals()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var catalog = context.Catalog("astro_collectibles")!;
        var puzzles = catalog.Search(new ReferenceQuery { Text = "Puzzle/", Limit = ReferenceQuery.MaxLimit }).Items;
        foreach (var (prefix, expected) in new[] { ("gpu_forest", 5), ("gpu_climbing1", 3), ("cooling_beach", 5),
            ("cooling_spring1", 3), ("memory_weather", 5), ("memory_ball1", 3), ("gpu_highplatforming", 5), ("gpu_climbing2", 3) })
        {
            Assert.Equal(expected, puzzles.Count(r => r.Attributes["internalGroup"] == prefix));
        }

        Assert.All(puzzles, record => Assert.Equal("unresolved", record.Attributes["mapping"]));
        Assert.Contains("none are proven", Assert.IsType<TextNode>(AstroCatalogueTestData.Group(document, "Puzzle pieces").Children[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPackOrMismatchedSchemaUsesRawKeysAndDoesNotModifyUnknownRecords()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        using var withoutPack = plugin.Open(file);
        var ds2 = AstroCatalogueTestData.Row(withoutPack, "Artifacts", "Artifact/ds2");
        Assert.Equal("Artifact/ds2", Assert.IsType<FieldNode>(ds2.Cells["name"]).Value.AsString());
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(ds2.Cells["name"]).Confidence);
        var text = "\r\n<PreInstall><Artifact><new_item><IsGot>true</IsGot></new_item></Artifact><Common><ArmadilloUnlockPhase>3</ArmadilloUnlockPhase></Common></PreInstall>opaque";
        var bytes = AstroTestData.Bytes(text);
        using var newer = plugin.Open(AstroTestData.File(bytes), context);
        Assert.NotEqual(AstroCatalogueTestData.Fingerprint, AstroCatalogueTestData.SchemaHash(newer));
        Assert.Equal("1/1 owned; 0 mapped / 1 unmapped; 0 unknown ownership", AstroCatalogueTestData.Group(newer, "Artifacts").Badge);
        Assert.Equal(bytes, AstroTestData.Output(newer));
        Assert.All(AstroTestData.Fields(newer).Where(f => f.Description!.Contains("Armadillo", StringComparison.Ordinal)), f => Assert.True(f.Spec.ReadOnly));
        Assert.Empty(AstroCatalogueTestData.Catalogue(newer).Children.OfType<ActionNode>());
    }
}
