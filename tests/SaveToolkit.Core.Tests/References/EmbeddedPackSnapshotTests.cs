using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedPackSnapshotTests
{
    internal static IReadOnlyList<IGamePlugin> Plugins() => [.. PluginCatalog.Discover().Plugins
        .Where(p => p.GetType().Assembly.GetName().Name!.StartsWith("SaveToolkit.Plugins.", StringComparison.Ordinal))];

    internal static IEnumerable<(string Name, JsonElement Manifest)> Manifests(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var path = name.Replace('\\', '/');
            if (!path.StartsWith("refpacks/", StringComparison.Ordinal) || !path.EndsWith("/pack.json", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var json = JsonDocument.Parse(stream);
            yield return (name, json.RootElement.Clone());
        }
    }

    [Fact]
    public void CoreCatalogsAndAssets_MatchPreExtractionSnapshot()
    {
        var plugins = Plugins();
        Assert.NotEmpty(plugins);
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        foreach (var assembly in plugins.Select(p => p.GetType().Assembly).Distinct().OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        foreach (var (_, manifest) in Manifests(assembly))
        {
            var pack = store.Find(manifest.GetProperty("id").GetString()!)!;
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(pack.Info));
            var context = new ReferenceContext([pack], ["en"]);
            foreach (var dataset in manifest.GetProperty("datasets").EnumerateArray())
            {
                if (dataset.GetProperty("kind").GetString() == "records")
                {
                    var catalog = pack.Catalog(dataset.GetProperty("id").GetString()!)!;
                    foreach (var record in ReferenceCatalogReader.ReadBounded(catalog, new(), catalog.Count))
                        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(record));
                }
                else
                {
                    foreach (var file in dataset.GetProperty("files").EnumerateArray())
                    {
                        var id = pack.Id + ":" + file.GetProperty("path").GetString();
                        var asset = context.GetAsset(id)!;
                        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(asset.ContentType));
                        hash.AppendData(asset.Content.Span);
                        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(context.AssetProvenance(id)));
                    }
                }
            }
            count++;
        }

        Assert.Equal(39, count);
        Assert.Equal("19ff03f234988519f8758116484ea69d63ac69e58e73baf1f148c770704dfd0b",
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
