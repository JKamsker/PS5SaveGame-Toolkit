using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Catalog;

public enum TitleSupport
{
    /// <summary>No game plugin; files open in the raw inspector.</summary>
    None,

    /// <summary>A plugin shows the data but cannot write it.</summary>
    View,

    /// <summary>At least one plugin can edit this title.</summary>
    Edit,
}

public sealed record TitleInfo(string TitleId, string Name, TitleSupport Support, IReadOnlyList<string> PluginIds);

/// <summary>
/// Friendly names for PS5 title ids: everything plugins declare, plus identities confirmed by research
/// for titles no plugin supports yet (see docs/formats).
/// </summary>
public static class TitleCatalog
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PPSA01494"] = "Borderlands 4",
        ["PPSA01506"] = "Immortals Fenyx Rising",
        ["PPSA01651"] = "YouTube",
        ["PPSA01802"] = "Kena: Bridge of Spirits",
        ["PPSA01876"] = "Far Cry 6",
        ["PPSA01982"] = "DOOM Eternal",
        ["PPSA06251"] = "Star Ocean: The Divine Force",
        ["PPSA07696"] = "Ridge Racer 2 (PSP classic)",
        ["PPSA07821"] = "Overwatch 2",
    };

    public static IReadOnlyList<TitleInfo> Build(PluginCatalog plugins)
    {
        var byTitle = new Dictionary<string, (string Name, List<IGamePlugin> Plugins)>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins.Plugins)
        {
            foreach (var game in plugin.Info.Games)
            {
                if (!byTitle.TryGetValue(game.TitleId, out var entry))
                {
                    entry = (game.Name, []);
                    byTitle[game.TitleId] = entry;
                }

                entry.Plugins.Add(plugin);
            }
        }

        var titles = byTitle.Select(kv => new TitleInfo(
            kv.Key.ToUpperInvariant(),
            kv.Value.Name,
            kv.Value.Plugins.Any(p => p.Info.Maturity != PluginMaturity.ReadOnly) ? TitleSupport.Edit : TitleSupport.View,
            [.. kv.Value.Plugins.Select(p => p.Info.Id).Order(StringComparer.Ordinal)]));

        var unsupported = Known
            .Where(kv => !byTitle.ContainsKey(kv.Key))
            .Select(kv => new TitleInfo(kv.Key, kv.Value, TitleSupport.None, []));

        return [.. titles.Concat(unsupported).OrderBy(t => t.TitleId, StringComparer.Ordinal)];
    }
}
