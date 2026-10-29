using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceEquivalenceTests
{
    [Fact]
    public void EveryManifestResourcePack_MatchesCoreRecordsQueriesAssetsAndSelection()
    {
        var plugins = EmbeddedPackSnapshotTests.Plugins();
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        var packCount = 0;
        foreach (var assembly in plugins.Select(p => p.GetType().Assembly).Distinct())
        foreach (var (_, manifest) in EmbeddedPackSnapshotTests.Manifests(assembly))
        {
            var packId = manifest.GetProperty("id").GetString()!;
            var pack = store.Find(packId)!;
            foreach (var pluginId in pack.Info.PluginIds)
            foreach (var title in pack.Info.TitleIds.Count == 0 ? new string?[] { null, "PPSA99999" } : pack.Info.TitleIds.Cast<string?>())
            {
                var sdk = EmbeddedReferenceContext.Load(assembly, pluginId, title);
                var expected = new ReferenceContext(store.Packs.Where(p => p.AppliesTo(pluginId, title)).ToArray(), ["en"]);
                EqualJson(expected.Packs, sdk.Packs);
                foreach (var dataset in pack.Info.Datasets.Where(d => d.Kind == ReferenceDatasetKind.Records))
                foreach (var build in new string?[] { null, "unknown-build" }.Concat(pack.Info.GameBuilds))
                    CompareCatalog(expected.Catalog(dataset.Id, build), sdk.Catalog(dataset.Id, build));

                foreach (var dataset in manifest.GetProperty("datasets").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "assets"))
                foreach (var file in dataset.GetProperty("files").EnumerateArray())
                {
                    var id = packId + ":" + file.GetProperty("path").GetString();
                    var asset = expected.GetAsset(id)!;
                    var actual = sdk.GetAsset(id)!;
                    Assert.Equal(asset.ContentType, actual.ContentType);
                    Assert.True(asset.Content.Span.SequenceEqual(actual.Content.Span), id);
                    EqualJson(expected.AssetProvenance(id), sdk.AssetProvenance(id));
                }
                Assert.Null(sdk.Catalog("missing-catalog"));
                Assert.Null(sdk.GetAsset("icons/unqualified.png"));
                Assert.Null(sdk.AssetProvenance("missing:icon.png"));
            }
            packCount++;
        }
        Assert.Equal(39, packCount);
    }

    private static void CompareCatalog(IReferenceCatalog? expected, IReferenceCatalog? actual)
    {
        if (expected is null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        EqualJson(expected.Pack, actual.Pack);
        EqualJson(expected.Dataset, actual.Dataset);
        EqualJson(expected.Categories, actual.Categories);
        EqualJson(expected.SortKeys, actual.SortKeys);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var record in ReferenceCatalogReader.ReadBounded(expected, new(), expected.Count))
            EqualJson(record, actual.Find(record.Key));
        Assert.Null(actual.Find("unknown-record"));
        foreach (var sort in new ReferenceSortKey?[] { null, ReferenceSortKey.Key, ReferenceSortKey.Name, ReferenceSortKey.Category })
        foreach (var descending in new[] { false, true })
        foreach (var query in new[]
        {
            new ReferenceQuery { Offset = -1, Limit = 0 },
            new ReferenceQuery { Offset = 1, Limit = ReferenceQuery.MaxLimit + 1 },
            new ReferenceQuery { Text = " a ", Limit = 3 },
            new ReferenceQuery { Category = expected.Categories.FirstOrDefault(), Offset = 2, Limit = 5 },
        }) EqualJson(expected.Search(query with { SortBy = sort, Descending = descending }),
            actual.Search(query with { SortBy = sort, Descending = descending }));
    }

    internal static void EqualJson<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
