using System.Collections.Concurrent;
using System.Reflection;

namespace SaveToolkit.Core.Tests.References;

/// <summary>Counts immutable embedded-resource reads without compiler-generated test assemblies.</summary>
internal sealed class CountingPackResourceAssembly(IReadOnlyDictionary<string, byte[]> files) : Assembly
{
    private readonly ConcurrentDictionary<string, int> _reads = new(StringComparer.Ordinal);

    internal Action<string>? BeforeRead { get; set; }
    internal int Reads(string path) => _reads.GetValueOrDefault(path);
    public override AssemblyName GetName() => new("CountedSyntheticReferences");
    public override string[] GetManifestResourceNames() => [.. files.Keys];

    public override Stream? GetManifestResourceStream(string name)
    {
        if (!files.TryGetValue(name, out var bytes)) return null;
        _reads.AddOrUpdate(name, 1, (_, count) => count + 1);
        BeforeRead?.Invoke(name);
        return new MemoryStream(bytes, writable: false);
    }

    internal static CountingPackResourceAssembly Create(params PackBuilder[] packs)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pack in packs)
        foreach (var (path, content) in EmbeddedReferenceRejectionTests.Resources(pack).Files)
            files.Add(path.Replace("refpacks/fixture/", $"refpacks/{pack.Id}/", StringComparison.Ordinal), content);
        return new(files);
    }
}
