using System.Reflection;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedPackSnapshotTests(ITestOutputHelper output)
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
    public void CoreCatalogsAndAssets_MatchPerPackSnapshots()
    {
        using var stream = typeof(EmbeddedPackSnapshotTests).Assembly.GetManifestResourceStream(PackSnapshotBaseline.ResourceName)!;
        using var reader = new StreamReader(stream);
        var baseline = PackSnapshotBaseline.Read(reader);
        var errors = PackSnapshotBaseline.Differences(baseline, CurrentHashes());
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact(Explicit = true)]
    public void PrintCurrentPackHashes()
    {
        // Opt-in output only: never rewrite the reviewed baseline, even when it differs.
        foreach (var (id, hash) in CurrentHashes()) output.WriteLine($"{id} {hash}");
    }

    private static SortedDictionary<string, string> CurrentHashes()
    {
        var plugins = Plugins();
        Assert.NotEmpty(plugins);
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var assembly in plugins.Select(p => p.GetType().Assembly).Distinct())
        foreach (var (_, manifest) in Manifests(assembly))
        {
            var id = manifest.GetProperty("id").GetString()!;
            var pack = store.Find(id);
            Assert.True(pack is not null, $"Pack '{id}' was not loaded by Core.");
            Assert.True(hashes.TryAdd(id, PackSnapshotBaseline.Hash(pack, manifest)), $"Duplicate embedded pack id '{id}'.");
        }

        Assert.NotEmpty(hashes);
        return hashes;
    }
}
