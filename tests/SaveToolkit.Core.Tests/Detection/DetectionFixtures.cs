using System.Reflection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.Detection;

internal sealed record DetectionFixture(IGamePlugin Owner, ISaveFile File, string Name);

internal sealed class DetectionFixtures
{
    public PluginCatalog Catalog { get; } = PluginCatalog.Discover();
    public IReadOnlyList<ConformanceFixture> Conformance { get; }

    public DetectionFixtures()
    {
        var fixtures = new List<ConformanceFixture>();
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "SaveToolkit.Plugins.*.Tests.dll"))
        {
            var assembly = Assembly.Load(AssemblyName.GetAssemblyName(path));
            foreach (var type in assembly.GetTypes().Where(type => !type.IsAbstract))
            {
                if (type.BaseType is not { IsGenericType: true } parent
                    || parent.GetGenericTypeDefinition() != typeof(PluginConformanceTests<>)) continue;
                var pluginType = parent.GetGenericArguments()[0];
                var plugin = Assert.Single(Catalog.Plugins, plugin => plugin.GetType() == pluginType);
                fixtures.Add(new(plugin, Activator.CreateInstance(type)!, parent));
            }
        }

        Conformance = fixtures.OrderBy(fixture => fixture.Plugin.Info.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(Catalog.Plugins.Where(plugin => plugin is not RawFilePlugin)
            .Select(plugin => plugin.Info.Id).Order(StringComparer.Ordinal),
            Conformance.Select(fixture => fixture.Plugin.Info.Id));
    }

    public IEnumerable<DetectionFixture> Synthetic()
    {
        foreach (var fixture in Conformance)
        {
            var seeds = fixture.SyntheticSeeds.ToArray();
            Assert.NotEmpty(seeds);
            foreach (var seed in seeds)
                yield return new(fixture.Plugin, seed, $"{fixture.Plugin.Info.Id}/{seed.FileName}");
        }

        // Documented overlap policy: a recognized Dungeons character is more specific than
        // GenericJson. Keep this priority case available even without its private sample.
        yield return new(Catalog.Find("format.json-xor")!,
            MemorySaveSet.SingleFile("plain-character.json",
                "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472},\"CharacterSaveV1\":{}}"u8.ToArray(),
                "PPSA16064"), "format.json-xor/plain-character.json");
    }

    public sealed class ConformanceFixture(IGamePlugin plugin, object instance, Type contract)
    {
        public IGamePlugin Plugin => plugin;
        public IReadOnlyList<string> Titles => Read<IReadOnlyList<string>>("SampleTitleIds");
        public IEnumerable<ISaveFile> SyntheticSeeds => Read<IEnumerable<ISaveFile>>("MutationFuzzSyntheticSeeds");

        // The existing test classes are sealed and their fixture hooks are protected. Read only
        // these two known virtual properties rather than copying builders or changing plugin APIs.
        // This reflection is confined to test-fixture integration, never production discovery.
        private T Read<T>(string name) => (T)contract.GetProperty(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    }

    public static DiskSaveSet Import(ISaveFile seed, string root) => SaveSetImporter.Import(
        seed.Directory.SaveSet.Directories.SelectMany(directory => directory.Files.Concat(directory.SystemFiles))
            .Select(file => new ImportSource(file.RelativePath, file.OpenRead)), root);

    public static IEnumerable<(string Name, ISaveFile File)> Mutations(ISaveFile file)
    {
        const int HeaderBytes = 64;
        var original = file.ReadAllBytes();
        foreach (var length in new[] { 0, 1, 4, 8, 16, 32, HeaderBytes, original.Length / 2, original.Length - 1 }
            .Where(length => length >= 0 && length < original.Length).Distinct())
            yield return ($"truncate-{length}", Replace(file, original.AsMemory(0, length)));

        for (var offset = 0; offset < Math.Min(HeaderBytes, original.Length); offset++)
        {
            var changed = original.ToArray();
            changed[offset] ^= (byte)(1 << (offset % 8));
            yield return ($"header-bit-{offset}", Replace(file, changed));
        }
    }

    private static ISaveFile Replace(ISaveFile file, ReadOnlyMemory<byte> bytes) =>
        ((ISaveSet)new OverlaySaveSet(file.Directory.SaveSet,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal) { [file.RelativePath] = bytes }))
            .FindFile(file.RelativePath)!;
}
