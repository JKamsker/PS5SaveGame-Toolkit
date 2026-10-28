using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Adapter that opens a plugin document and walks eager, lazy and bounded queryable views.
/// Fields retain their value ids as paths. Plugins already promise value ids that are unique and
/// stable, and well-behaved ones derive them from record identity (<c>inventory/2227368435/quantity</c>),
/// so no plugin changes are needed. The document is only read, never edited or serialised.
/// </summary>
public sealed class PluginViewDecoder(IGamePlugin plugin, int lazyExpansionBudget = 10_000) : ISaveDecoder
{
    public IGamePlugin Plugin => plugin;

    public string Id => plugin.Info.Id;

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            return plugin.Probe(file).Score > ProbeScore.Fallback;
        }
        catch (Exception)
        {
            // Probe must not throw; a plugin that does simply does not decode this file.
            return false;
        }
    }

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException($"File is {file.Length} bytes; the decode limit is {limits.MaxFileBytes}.");
        }

        using var document = plugin.Open(file);
        var builder = new SemanticTreeBuilder(Id, limits);
        new PluginViewTraversal(document, builder, lazyExpansionBudget).Walk();
        return builder.Build();
    }
}
