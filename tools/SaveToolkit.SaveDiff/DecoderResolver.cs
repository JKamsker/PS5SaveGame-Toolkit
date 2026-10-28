using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

/// <summary>Maps <c>--semantic</c> / a fixture's <c>decoder</c> to an <see cref="ISaveDecoder"/>.</summary>
internal sealed class DecoderResolver(IReadOnlyCollection<IGamePlugin> plugins)
{
    public static DecoderResolver Discover() => new(PluginCatalog.Discover().Plugins);

    public IReadOnlyCollection<IGamePlugin> Plugins => plugins;

    /// <param name="mode"><c>none</c>/null, <c>json</c>, <c>auto</c> (best plugin, then JSON) or a plugin id.</param>
    public ISaveDecoder? Resolve(string? mode)
    {
        switch (mode)
        {
            case null or "" or "none":
                return null;
            case "json":
                return new JsonSaveDecoder();
            case "auto":
                return new FirstMatchDecoder([new ProbingPluginDecoder(plugins), new JsonSaveDecoder()]);
        }

        var plugin = plugins.FirstOrDefault(p => string.Equals(p.Info.Id, mode, StringComparison.OrdinalIgnoreCase));
        return plugin is not null
            ? new PluginViewDecoder(plugin)
            : throw new UsageException($"Unknown decoder '{mode}'. Use none, json, auto or a plugin id: {string.Join(", ", plugins.Select(p => p.Info.Id).Order(StringComparer.Ordinal))}.");
    }
}
