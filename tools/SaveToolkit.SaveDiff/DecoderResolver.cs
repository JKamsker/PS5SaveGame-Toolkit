using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

/// <summary>Resolves CLI/fixture decoder ids using only explicitly registered catalogued plugins.</summary>
internal sealed class DecoderResolver
{
    private const int MaxDecoderIdLength = 128;
    private const string BuiltInKind = "built-in";
    private readonly Dictionary<string, ISaveDecoder> _decoders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DecoderDescription> _available = [];

    public DecoderResolver(IReadOnlyCollection<IGamePlugin> plugins)
    {
        Plugins = plugins;
        Register("none", BuiltInKind, null, null);
        Register("json", BuiltInKind, null, new JsonSaveDecoder());
        Register("auto", BuiltInKind, null, new FirstMatchDecoder([new ProbingPluginDecoder(plugins), new JsonSaveDecoder()]));
        foreach (var plugin in plugins.OrderBy(p => p.Info.Id, StringComparer.Ordinal))
        {
            Register(plugin.Info.Id, "view", plugin.Info.Id, new PluginViewDecoder(plugin));
        }

        foreach (var plugin in plugins.OrderBy(p => p.Info.Id, StringComparer.Ordinal))
        {
            if (plugin is not ISaveDecoderProvider provider) continue;
            foreach (var hook in provider.Decoders)
            {
                Register(hook.Id, "hook", plugin.Info.Id, hook as ISaveDecoder ?? new HookSaveDecoder(hook));
            }
        }
    }

    public static DecoderResolver Discover() => new(PluginCatalog.Discover().Plugins);

    public IReadOnlyCollection<IGamePlugin> Plugins { get; }

    public IReadOnlyList<DecoderDescription> Available => [.. _available.OrderBy(d => d.Id, StringComparer.Ordinal)];

    /// <param name="mode"><c>none</c>/null, <c>json</c>, <c>auto</c>, a plugin id or an explicit hook id.</param>
    public ISaveDecoder? Resolve(string? mode)
    {
        if (string.IsNullOrEmpty(mode) || string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase)) return null;
        return _decoders.TryGetValue(mode, out var decoder)
            ? decoder
            : throw new UsageException($"Unknown decoder '{mode}'. Run 'savediff list-decoders' for available ids.");
    }

    private void Register(string id, string kind, string? pluginId, ISaveDecoder? decoder)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxDecoderIdLength || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
        {
            throw new UsageException($"Invalid decoder id '{id}'; use 1–{MaxDecoderIdLength} ASCII letters, digits, '.', '_' or '-'.");
        }

        if (_available.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UsageException($"Duplicate decoder id '{id}' from plugin '{pluginId}'; hook ids must be unique and cannot replace built-in modes or plugin ids.");
        }

        _available.Add(new(id, kind, pluginId));
        if (decoder is not null) _decoders.Add(id, decoder);
    }
}

internal sealed record DecoderDescription(string Id, string Kind, string? PluginId);
