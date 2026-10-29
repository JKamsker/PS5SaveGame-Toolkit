using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseIconCharacterizationTests
{
    private const string MediaLicense = "Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29";
    private const string ItemsId = "items";

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void InventoryAndBothCatalogQueryPaths_PreserveCompleteIconAndAttribution(bool bytes, bool provenance)
    {
        var pinned = AriseTestReferences.CreateContext();
        var record = pinned.Catalog(ItemsId)!.Find("1")!;
        var context = new IconContext(pinned, record, bytes, provenance);
        using var document = Open(context);
        var inventory = Assert.Single(document.Query("inventory", new()
        {
            Filters = new Dictionary<string, string> { ["id"] = "1" },
        }).Rows);
        var expected = new ImageNode(record.AssetId!, $"Apple Gel: {record.Attributes["iconRole"]}. {MediaLicense}");
        Assert.Equal(expected, inventory.Cells["icon"]);
        Assert.Contains(record.AttributionText, Assert.IsType<FieldNode>(inventory.Cells["id"]).Description);
        Assert.Equal(AriseAssets.Details(record), Assert.IsType<FieldNode>(inventory.Cells["name"]).Description);

        var references = new AriseReferences(context);
        foreach (var text in new string?[] { null, "Apple Gel" })
        {
            var row = references.Query(ItemsId, new() { Text = text, Limit = 1 }).Rows.Single();
            Assert.Equal(expected, row.Cells["icon"]);
            Assert.Equal($"{record.Description}\n{record.AttributionText}\nIcon: {record.Attributes["iconSource"]}; " +
                $"{record.Attributes["iconRole"]}. {MediaLicense}", Assert.IsType<TextNode>(row.Cells["details"]).Text);
            Assert.Equal($"Read-only reference details. {record.AttributionText}", Assert.IsType<ActionNode>(row.Cells["inspect"]).Description);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("shared item category")]
    public void IconRole_PreservesExactTextAndPublisherNotice(string? role)
    {
        var pinned = AriseTestReferences.CreateContext();
        var record = pinned.Catalog(ItemsId)!.Find("1")!;
        var attributes = record.Attributes.ToDictionary();
        attributes.Remove("iconRole");
        if (role is not null)
            attributes["iconRole"] = role;
        var context = new IconContext(pinned, record with { Attributes = attributes });
        var icon = new AriseReferences(context).Query(ItemsId, new() { Limit = 1 }).Rows.Single().Cells["icon"];
        Assert.Equal(new ImageNode(record.AssetId!, $"Apple Gel: {role ?? "game icon"}. {MediaLicense}"), icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing:icon")]
    public void MissingOrUnresolvableAssetId_PreservesExactNode(string? assetId)
    {
        var pinned = AriseTestReferences.CreateContext();
        var record = pinned.Catalog(ItemsId)!.Find("1")! with { AssetId = assetId };
        var context = new IconContext(pinned, record);
        using var document = Open(context);
        var inventory = document.Query("inventory", new()
        {
            Filters = new Dictionary<string, string> { ["id"] = "1" },
        }).Rows.Single();
        ViewNode expected = assetId is null ? new TextNode("—")
            : new ImageNode(assetId, $"Apple Gel: {record.Attributes["iconRole"]}. {MediaLicense}");
        Assert.Equal(expected, inventory.Cells["icon"]);
        Assert.Equal(expected, new AriseReferences(context).Query(ItemsId, new() { Limit = 1 }).Rows.Single().Cells["icon"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65535")]
    public void NoRecord_EmptyAndUnknownInventorySlotsRetainEmDash(string id)
    {
        using var document = Open(AriseTestReferences.CreateContext());
        var page = document.Query("inventory", new()
        {
            IncludeEmpty = true, Filters = new Dictionary<string, string> { ["id"] = id }, Limit = 1,
        });
        Assert.Equal(new TextNode("—"), Assert.Single(page.Rows).Cells["icon"]);
    }

    [Fact]
    public void CompleteCatalogRows_RetainViewFingerprint()
    {
        var references = new AriseReferences(AriseTestReferences.CreateContext());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var collection in references.Collections)
        {
            for (var offset = 0; offset < collection.TotalCount; offset += ReferenceQuery.MaxLimit)
            {
                var page = references.Query(collection.Id, new() { Offset = offset, Limit = ReferenceQuery.MaxLimit });
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(page));
            }
        }
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        Assert.Equal("54c72d56a59b51c414015e21c12c5a84ce14239610e4b713ea8c1d3b1694e7f9", actual);
    }

    [Theory]
    [InlineData("SaveData_000.sav", "8940b7874cd36458c1d27a05f08da23d5f6c26c112130a92cf03205eb420e080")]
    [InlineData("SaveData_001.sav", "82082a0763e0170ec1638f43ee7cd6e542e0587720e105a57331d16e3334ceff")]
    [InlineData("SaveData_002.sav", "23184ee4532422c943b4465d77bd32aac82afa825bfc46e3d363178c228d237d")]
    public void SampleViewAndInventory_RetainViewFingerprint(string fileName, string expected)
    {
        var file = AriseSampleTests.GameplayFiles().First(file => file.FileName == fileName);
        using var document = new TalesOfArisePlugin().Open(file, AriseTestReferences.CreateContext());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Single(view.Tabs.SelectMany(tab => tab.Children).OfType<ActionNode>(), action => action.ActionId == AriseInventoryActions.RestoreImportedAction);
        // The restore control is covered separately; retain the exact fingerprint of every existing node.
        view = view with { Tabs = view.Tabs.Select(tab => tab with
        {
            Children = tab.Children.Where(node => node is not ActionNode { ActionId: AriseInventoryActions.RestoreImportedAction }).ToArray(),
        }).ToArray() };
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes((ViewNode)view));
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(document.LoadInventory()));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var count = queryable.Collections.Single(collection => collection.Id == "inventory").TotalCount;
        for (var offset = 0; offset < count; offset += ReferenceQuery.MaxLimit)
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(queryable.Query("inventory", new()
            {
                Offset = offset, Limit = ReferenceQuery.MaxLimit, IncludeEmpty = true,
            })));
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        Assert.Equal(expected, actual);
    }

    private static AriseDocument Open(IReferenceContext context) => Assert.IsType<AriseDocument>(new TalesOfArisePlugin().Open(
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, new AriseTestContainer().Encrypt(), AriseTestContainer.TitleId), context));

    private sealed class IconContext(IReferenceContext inner, ReferenceRecord record, bool bytes = true, bool provenance = true) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalogId == ItemsId
            ? new IconCatalog(inner.Catalog(ItemsId)!, record) : inner.Catalog(catalogId, gameBuild);
        public AssetContent? GetAsset(string assetId) => bytes ? inner.GetAsset(assetId) : null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => provenance ? inner.AssetProvenance(assetId) : null;
    }

    private sealed class IconCatalog(IReferenceCatalog inner, ReferenceRecord record) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => 1;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => inner.SortKeys;
        public ReferenceRecord? Find(string key) => key == record.Key ? record : null;
        public ReferencePage Search(ReferenceQuery query) => new(query.Offset == 0 ? [record] : [], 1, query.Offset);
    }
}
