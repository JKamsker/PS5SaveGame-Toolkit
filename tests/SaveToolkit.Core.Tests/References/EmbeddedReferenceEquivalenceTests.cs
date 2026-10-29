using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceEquivalenceTests
{
    [Fact]
    public void EveryEmbeddedPack_MatchesCoreDecoding_RecordByRecordAndAssetByAsset()
    {
        var plugins = EmbeddedPackSnapshotTests.Plugins();
        Assert.NotEmpty(plugins);
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        Assert.NotEmpty(store.Packs);
        foreach (var assembly in plugins.Select(p => p.GetType().Assembly).Distinct())
        foreach (var (name, manifest) in EmbeddedPackSnapshotTests.Manifests(assembly))
        {
            var packId = manifest.GetProperty("id").GetString()!;
            var pack = store.Find(packId);
            Assert.True(pack is not null, $"Pack '{packId}' was not loaded by Core.");
            var sdk = EmbeddedReferenceContext.Load(new SinglePackResourceAssembly(assembly, name));
            var core = new ReferenceContext([pack], ["en"]);
            foreach (var dataset in pack.Info.Datasets.Where(d => d.Kind == ReferenceDatasetKind.Records))
                CompareCatalog(pack.Catalog(dataset.Id), sdk.Catalog(dataset.Id, pack.Info.GameBuilds.FirstOrDefault()),
                    $"Pack '{packId}', catalog '{dataset.Id}'");
            CompareAssets(core, sdk, manifest, packId);
            EqualJson(core.Packs, sdk.Packs, $"Pack '{packId}' metadata");
        }
    }

    [Fact]
    public void EveryManifestResourcePack_MatchesCoreRecordsQueriesAssetsAndSelection()
    {
        var plugins = EmbeddedPackSnapshotTests.Plugins();
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        var visitedPacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in plugins.Select(p => p.GetType().Assembly).Distinct())
        foreach (var (_, manifest) in EmbeddedPackSnapshotTests.Manifests(assembly))
        {
            var packId = manifest.GetProperty("id").GetString()!;
            var pack = store.Find(packId)!;
            var location = $"Pack '{packId}' selection";
            Assert.True(visitedPacks.Add(packId), $"Pack '{packId}' was visited more than once.");
            foreach (var pluginId in pack.Info.PluginIds)
            foreach (var title in pack.Info.TitleIds.Count == 0 ? new string?[] { null, "PPSA99999" } : pack.Info.TitleIds.Cast<string?>())
            {
                var sdk = EmbeddedReferenceContext.Load(assembly, pluginId, title);
                var expected = new ReferenceContext(store.Packs.Where(p => p.AppliesTo(pluginId, title)).ToArray(), ["en"]);
                EqualJson(expected.Packs, sdk.Packs, location);
                foreach (var dataset in pack.Info.Datasets.Where(d => d.Kind == ReferenceDatasetKind.Records))
                foreach (var build in new string?[] { null, "unknown-build" }.Concat(pack.Info.GameBuilds))
                    CompareCatalog(expected.Catalog(dataset.Id, build), sdk.Catalog(dataset.Id, build),
                        $"{location}, plugin '{pluginId}', title '{title}', catalog '{dataset.Id}', build '{build}'");

                CompareAssets(expected, sdk, manifest, packId);
                Assert.Null(sdk.Catalog("missing-catalog"));
                Assert.Null(sdk.GetAsset("icons/unqualified.png"));
                Assert.Null(sdk.AssetProvenance("missing:icon.png"));
            }
        }
        using var stream = typeof(EmbeddedReferenceEquivalenceTests).Assembly.GetManifestResourceStream(PackSnapshotBaseline.ResourceName)!;
        using var reader = new StreamReader(stream);
        var pinnedPacks = PackSnapshotBaseline.Read(reader).Keys;
        Assert.True(visitedPacks.SetEquals(pinnedPacks),
            $"Visited embedded packs differ from {PackSnapshotBaseline.FilePath}. " +
            $"Not visited: {string.Join(", ", pinnedPacks.Except(visitedPacks, StringComparer.Ordinal))}. " +
            $"Not pinned: {string.Join(", ", visitedPacks.Except(pinnedPacks, StringComparer.Ordinal))}. " +
            $"Print current values: {PackSnapshotBaseline.PrintCommand}");
    }

    internal static void CompareCatalog(IReferenceCatalog? expected, IReferenceCatalog? actual, string location)
    {
        if (expected is null) { Assert.True(actual is null, $"{location}: SDK-03 returned an unexpected catalog."); return; }
        Assert.True(actual is not null, $"{location}: SDK-03 did not return Core's catalog.");
        var coreRecords = ReferenceCatalogReader.ReadBounded(expected, new(), expected.Count);
        var sdkRecords = ReferenceCatalogReader.ReadBounded(actual, new(), actual.Count);
        for (var index = 0; index < Math.Max(coreRecords.Count, sdkRecords.Count); index++)
        {
            var coreRecord = coreRecords.ElementAtOrDefault(index);
            var sdkRecord = sdkRecords.ElementAtOrDefault(index);
            var recordLocation = $"{location}, record #{index} (Core key '{coreRecord?.Key}', SDK-03 key '{sdkRecord?.Key}')";
            EqualJson(coreRecord, sdkRecord, recordLocation);
            EqualJson(coreRecord, actual.Find(coreRecord!.Key), $"{recordLocation} Find");
        }
        EqualJson(expected.Count, actual.Count, $"{location} count");
        EqualJson(expected.Pack, actual.Pack, $"{location} pack metadata");
        EqualJson(expected.Dataset, actual.Dataset, $"{location} dataset metadata");
        EqualJson(expected.Categories, actual.Categories, $"{location} categories");
        EqualJson(expected.SortKeys, actual.SortKeys, $"{location} sort keys");
        Assert.True(actual.Find("unknown-record") is null, $"{location}: SDK-03 returned an unknown record.");
        foreach (var sort in new ReferenceSortKey?[] { null, ReferenceSortKey.Key, ReferenceSortKey.Name, ReferenceSortKey.Category })
        foreach (var descending in new[] { false, true })
        foreach (var query in new[]
        {
            new ReferenceQuery { Offset = -1, Limit = 0 },
            new ReferenceQuery { Offset = 1, Limit = ReferenceQuery.MaxLimit + 1 },
            new ReferenceQuery { Text = " a ", Limit = 3 },
            new ReferenceQuery { Category = expected.Categories.FirstOrDefault(), Offset = 2, Limit = 5 },
        })
        {
            var request = query with { SortBy = sort, Descending = descending };
            EqualJson(expected.Search(request), actual.Search(request), $"{location} query {JsonSerializer.Serialize(request)}");
        }
    }

    internal static void CompareAssets(IReferenceContext core, IReferenceContext sdk, JsonElement manifest, string packId)
    {
        foreach (var dataset in manifest.GetProperty("datasets").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "assets"))
        foreach (var file in dataset.GetProperty("files").EnumerateArray())
        {
            var id = packId + ":" + file.GetProperty("path").GetString();
            var location = $"Pack '{packId}', asset '{id}'";
            var expected = core.GetAsset(id);
            var actual = sdk.GetAsset(id);
            Assert.True(expected is not null, $"{location}: Core did not return the asset.");
            Assert.True(actual is not null, $"{location}: SDK-03 did not return the asset.");
            EqualJson(expected.ContentType, actual.ContentType, $"{location} content type");
            Assert.True(expected.Content.Span.SequenceEqual(actual.Content.Span), $"{location}: content bytes differ.");
            EqualJson(core.AssetProvenance(id), sdk.AssetProvenance(id), $"{location} provenance");
        }
    }

    internal static void EqualJson<T>(T expected, T actual, string location = "Reference comparison")
    {
        var expectedJson = JsonSerializer.Serialize(expected);
        var actualJson = JsonSerializer.Serialize(actual);
        Assert.True(expectedJson == actualJson, $"{location} differs.\nCore: {expectedJson}\nSDK-03: {actualJson}");
    }
}
