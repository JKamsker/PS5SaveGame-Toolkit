using System.Security.Cryptography;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ArtworkTests
{
    private const string Rights = "Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29";

    [Theory]
    [InlineData("PPSA01463", "bl3", 315, 12, 42, 29, 326, 723237)]
    [InlineData("PPSA01493", "ttwl", 110, 11, 45, 32, 121, 148366)]
    public void EveryArtworkJoin_ResolvesValidatedDeduplicatedAttributedBytes(string title, string game,
        int skills, int cards, int indices, int parts, int images, int bytes)
    {
        var plugin = new GearboxPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors); // The F2 loader independently checks every hash and image header/type.
        var references = store.Pin(plugin, title);
        var catalog = references.Catalog(ItemCatalogs.ItemCards)!;
        Assert.Equal(cards, catalog.Count);
        Assert.Equal(Rights, catalog.Dataset.Attribution.License);
        var database = SerialDatabase.For(game == "ttwl");
        Assert.Equal(skills, database.Records(GearboxSkills.CatalogId).Count(r => r.AssetId is not null));
        Assert.Equal(cards, database.Records(ItemCatalogs.Balances).Count(r => r.AssetId is not null));
        Assert.Equal(indices, database.Records(ItemCatalogs.Indices).Count(r => r.AssetId is not null));
        Assert.Equal(parts, database.Records(ItemCatalogs.Indices).Count(r => r.AssetId is not null && r.Category!.StartsWith("BPInvPart_", StringComparison.Ordinal)));
        var records = new[] { GearboxSkills.CatalogId, ItemCatalogs.Balances, ItemCatalogs.Indices, ItemCatalogs.ItemCards }
            .SelectMany(id => database.Records(id)).Where(r => r.AssetId is not null).ToArray();
        var assets = records.Select(r => r.AssetId!).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(images, assets.Length);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var id in assets)
        {
            var asset = Assert.IsType<AssetContent>(references.GetAsset(id));
            var provenance = references.AssetProvenance(id)!;
            Assert.Equal(Rights, provenance.Attribution.License);
            Assert.Equal(ReferenceDatasetKind.Assets, provenance.Kind);
            Assert.True(asset.ContentType is "image/webp" or "image/png");
            Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(asset.Content.Span))), "Duplicate encoded image");
            Assert.Equal(asset.Content, store.Pin(plugin, title).GetAsset(id)!.Content); // Same cached buffer, no reload/copy.
            total += asset.Content.Length;
        }
        Assert.Equal(bytes, total);
        Assert.True(total < 4_000_000);
        Assert.All(database.Records(ItemCatalogs.ItemCards), record =>
        {
            Assert.StartsWith("Official item-card flavor text:", record.Description);
            Assert.Contains("lootlemon.com", record.Source);
            Assert.Equal(record.AssetId, references.Catalog(ItemCatalogs.Balances)!.Find(record.Key)!.AssetId);
        });
        Assert.All(database.Records(ItemCatalogs.Indices).Where(r => r.AssetId is not null), record =>
            Assert.Contains("not an image of this individual part", record.Description));
    }

    [Theory]
    [InlineData("PPSA01463", "Hellwalker")]
    [InlineData("PPSA01493", "Liquid Cooling")]
    public void ImagesAndCardText_AppearInTablesAndPickersWithProvenanceAndRawFallback(string title, string item)
    {
        var plugin = new GearboxPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        var file = GearboxTestData.PrimaryCharacter(title);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("artwork", file, plugin, document, references: references);
        _ = session.BuildView();
        var row = Assert.Single(session.QueryCollection("catalogue", new() { Text = item, Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var image = Assert.IsType<ImageNode>(row.Cells["icon"]);
        Assert.NotNull(session.GetAsset(image.AssetId));
        Assert.Contains(Rights, image.Alt);
        var name = Assert.IsType<FieldNode>(row.Cells["name"]);
        Assert.Contains("Official item-card flavor text:", name.Description);
        Assert.Contains(Rights, name.Description);
        var candidate = session.FindReference("balance", row.Key, actionId: "catalogue/inspect");
        Assert.Equal(image.AssetId, candidate.AssetId);
        Assert.Equal(Rights, candidate.AssetDataset!.Attribution.License);
        var skillPage = session.QueryCollection(GearboxSkills.CatalogId, new() { Limit = 20 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(skillPage.Rows, skill =>
        {
            var icon = Assert.IsType<ImageNode>(skill.Cells["icon"]);
            Assert.NotNull(session.GetAsset(icon.AssetId));
        });
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());

        using var fallback = plugin.Open(file);
        _ = fallback.BuildView();
        var fallbackRow = Assert.Single(((IQueryableDocument)fallback).Query("catalogue", new() { Text = item }).Rows);
        Assert.IsType<TextNode>(fallbackRow.Cells["icon"]);
        Assert.Null(fallback.GetAsset(image.AssetId));
        Assert.Contains("Official item-card flavor text:", Assert.IsType<FieldNode>(fallbackRow.Cells["name"]).Description);
        Assert.Equal(file.ReadAllBytes(), fallback.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void LiteralSkillCardEffects_AreRetainedWithoutInventingFormulaResults()
    {
        var skills = SerialDatabase.For(false).Records(GearboxSkills.CatalogId);
        Assert.Equal(134, skills.Count(r => r.Description!.Contains("Reference card effect text", StringComparison.Ordinal)));
        var fadeAway = skills.Single(r => r.Key == "beastmaster/stalker/fade-away");
        Assert.Contains("Cooldown: 45 seconds", fadeAway.Description);
        Assert.Contains("patch/hotfix coverage unknown", fadeAway.Description);
        Assert.DoesNotContain("${", fadeAway.Description);
    }
}
