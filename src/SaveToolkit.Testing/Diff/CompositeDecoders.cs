using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>Uses the first decoder that accepts the file (e.g. plugins first, then JSON).</summary>
public sealed class FirstMatchDecoder(IReadOnlyList<ISaveDecoder> decoders) : ISaveDecoder
{
    public string Id => string.Join("|", decoders.Select(d => d.Id));

    public bool CanDecode(ISaveFile file) => decoders.Any(d => d.CanDecode(file));

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits) =>
        (decoders.FirstOrDefault(d => d.CanDecode(file)) ?? throw new InvalidOperationException($"No decoder accepts {file.RelativePath}."))
            .Decode(file, limits);
}

/// <summary>Probes every plugin per file and decodes through the best match above the raw fallback.</summary>
public sealed class ProbingPluginDecoder(IEnumerable<IGamePlugin> plugins) : ISaveDecoder
{
    private readonly IReadOnlyList<PluginViewDecoder> _decoders = [.. plugins.Select(p => new PluginViewDecoder(p))];

    public string Id => "plugins";

    public bool CanDecode(ISaveFile file) => Best(file) is not null;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits) =>
        (Best(file) ?? throw new InvalidOperationException($"No plugin claims {file.RelativePath}.")).Decode(file, limits);

    private PluginViewDecoder? Best(ISaveFile file) => _decoders
        .Select(d => (Decoder: d, Score: SafeScore(d.Plugin, file)))
        .Where(m => m.Score > ProbeScore.Fallback)
        .OrderByDescending(m => m.Score)
        .ThenBy(m => m.Decoder.Id, StringComparer.Ordinal)
        .Select(m => m.Decoder)
        .FirstOrDefault();

    private static int SafeScore(IGamePlugin plugin, ISaveFile file)
    {
        try
        {
            return plugin.Probe(file).Score;
        }
        catch (Exception)
        {
            // Mirrors PluginCatalog: a plugin that throws while probing is "no match".
            return ProbeScore.None;
        }
    }
}
