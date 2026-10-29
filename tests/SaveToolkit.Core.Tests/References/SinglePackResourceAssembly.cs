using System.Reflection;

namespace SaveToolkit.Core.Tests.References;

// Forward real resource streams for one pack so catalog selection cannot hide another pack's rows.
// This uses the same Assembly test-double seam as EmbeddedReferenceRejectionTests.ResourceAssembly.
internal sealed class SinglePackResourceAssembly : Assembly
{
    private const string ManifestFileName = "pack.json";
    private readonly Assembly _source;
    private readonly HashSet<string> _names;

    internal SinglePackResourceAssembly(Assembly source, string manifestName)
    {
        _source = source;
        var prefix = manifestName.Replace('\\', '/')[..^ManifestFileName.Length];
        _names = source.GetManifestResourceNames()
            .Where(name => name.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
    }

    public override AssemblyName GetName() => _source.GetName();
    public override string[] GetManifestResourceNames() => [.. _names];
    public override Stream? GetManifestResourceStream(string name) =>
        _names.Contains(name) ? _source.GetManifestResourceStream(name) : null;
}
