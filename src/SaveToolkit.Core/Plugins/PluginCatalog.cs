using System.Reflection;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Plugins;

public sealed record ProbeMatch(IGamePlugin Plugin, ProbeResult Result);

/// <summary>Holds plugin instances and ranks them per file.</summary>
public sealed class PluginCatalog
{
    private readonly Dictionary<string, IGamePlugin> _byId;

    public PluginCatalog(IEnumerable<IGamePlugin> plugins)
    {
        _byId = new Dictionary<string, IGamePlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            if (!_byId.TryAdd(plugin.Info.Id, plugin))
            {
                throw new InvalidOperationException($"Duplicate plugin id '{plugin.Info.Id}'.");
            }
        }
    }

    public IReadOnlyCollection<IGamePlugin> Plugins => _byId.Values;

    public IGamePlugin? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// All plugins that claim the file, best first. A plugin that throws while probing is treated as
    /// "no match" so one broken plugin cannot take the whole workspace down.
    /// </summary>
    public IReadOnlyList<ProbeMatch> Probe(ISaveFile file)
    {
        var matches = new List<ProbeMatch>();
        foreach (var plugin in _byId.Values)
        {
            ProbeResult result;
            try
            {
                result = plugin.Probe(file);
            }
            catch (Exception)
            {
                continue;
            }

            if (result.IsMatch)
            {
                matches.Add(new ProbeMatch(plugin, result));
            }
        }

        return [.. matches.OrderByDescending(m => m.Result.Score).ThenBy(m => m.Plugin.Info.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Built-in plugins plus every <c>SaveToolkit.Plugins.*.dll</c> next to the application, plus any
    /// already-loaded assembly with plugin types.
    /// </summary>
    public static PluginCatalog Discover(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        foreach (var dll in Directory.EnumerateFiles(baseDirectory, "SaveToolkit.Plugins.*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(dll);
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name)))
            {
                Assembly.Load(name);
            }
        }

        var plugins = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is { } n && (n.StartsWith("SaveToolkit.Plugins.", StringComparison.Ordinal) || n == "SaveToolkit.Core"))
            .SelectMany(SafeTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IGamePlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IGamePlugin)Activator.CreateInstance(t)!)
            .ToList();
        return new PluginCatalog(plugins);
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
