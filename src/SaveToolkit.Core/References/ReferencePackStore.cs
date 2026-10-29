using System.Collections.Immutable;
using System.Reflection;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

public sealed record ReferencePackOptions
{
    /// <summary>Folder for imported user packs (kept across restarts). Null keeps user packs in memory only.</summary>
    public string? StorageRoot { get; init; }

    /// <summary>Locales to prefer, best first (BCP-47).</summary>
    public IReadOnlyList<string> PreferredLocales { get; init; } = ["en"];

    public ReferencePackLimits Limits { get; init; } = new();
}

/// <param name="Origin">Assembly/resource folder or file name of the pack.</param>
public sealed record ReferencePackLoadError(string Origin, string Message);

/// <summary>
/// Cache of validated packs: packs embedded in plugin assemblies plus packs the user imported.
/// Documents pin a snapshot at open time via <see cref="Pin"/>. Plugins never see this type.
/// </summary>
public sealed class ReferencePackStore
{
    // "pack-" prefix: ids like "con" or "nul" would be Windows device names on their own.
    private const string FilePrefix = "pack-";
    private const string PackExtension = ".zip";

    private readonly Lock _gate = new();
    private readonly List<ReferencePackLoadError> _errors = [];
    private volatile ImmutableList<LoadedReferencePack> _packs = [];

    public ReferencePackStore(IEnumerable<IGamePlugin> plugins, ReferencePackOptions? options = null)
        : this(options, plugins.Select(p => p.GetType().Assembly))
    {
    }

    internal ReferencePackStore(ReferencePackOptions? options, IEnumerable<Assembly> assemblies)
    {
        Options = options ?? new ReferencePackOptions();
        LoadEmbedded(assemblies);
        LoadStored();
    }

    public ReferencePackOptions Options { get; }

    /// <summary>All loaded packs, embedded first, each group ordered by id.</summary>
    public IReadOnlyList<LoadedReferencePack> Packs => _packs;

    /// <summary>Packs that were found but rejected (bad hash, bad manifest, ...).</summary>
    public IReadOnlyList<ReferencePackLoadError> LoadErrors
    {
        get
        {
            lock (_gate)
            {
                return [.. _errors];
            }
        }
    }

    public LoadedReferencePack? Find(string packId) => _packs.FirstOrDefault(p => p.Id == packId);

    /// <summary>Immutable reference context for a document opened by <paramref name="plugin"/> for <paramref name="titleId"/>.</summary>
    public ReferenceContext Pin(IGamePlugin plugin, string? titleId)
    {
        var packs = _packs.Where(p => p.AppliesTo(plugin.Info.Id, titleId)).ToList();
        return packs.Count == 0 ? ReferenceContext.None : new ReferenceContext(packs, Options.PreferredLocales);
    }

    /// <summary>
    /// Validates a pack archive and adds it, replacing an earlier user pack with the same id. Embedded
    /// pack ids cannot be overridden. Throws <see cref="ReferencePackException"/> when invalid.
    /// </summary>
    public LoadedReferencePack Import(ReadOnlyMemory<byte> archive, string fileName)
    {
        if (archive.Length > Options.Limits.MaxArchiveBytes)
        {
            throw new ReferencePackException($"'{fileName}' is larger than the limit of {Options.Limits.MaxArchiveBytes:N0} bytes.");
        }

        LoadedReferencePack pack;
        using (var source = new ZipPackSource(archive, $"'{fileName}'", Options.Limits))
        {
            pack = ReferencePackLoader.Load(source, ReferencePackOrigin.User, Options.Limits);
        }

        lock (_gate)
        {
            if (_packs.Any(p => p.Id == pack.Id && p.Info.Origin == ReferencePackOrigin.Embedded))
            {
                throw new ReferencePackException($"Pack id '{pack.Id}' is already used by a plugin's built-in pack. Give the pack another id.");
            }

            Persist(pack.Id, archive);
            _packs = Sorted(_packs.RemoveAll(p => p.Id == pack.Id).Add(pack));
        }

        return pack;
    }

    /// <summary>Removes a user pack. Documents that pinned it keep their copy until they close.</summary>
    public bool Remove(string packId)
    {
        lock (_gate)
        {
            var pack = Find(packId);
            if (pack is null)
            {
                return false;
            }

            if (pack.Info.Origin == ReferencePackOrigin.Embedded)
            {
                throw new InvalidOperationException("Built-in packs ship with their plugin and cannot be removed.");
            }

            if (Options.StorageRoot is { } root && PackPaths.IsValidPackId(packId))
            {
                File.Delete(Path.Combine(root, FileName(packId)));
            }

            _packs = _packs.Remove(pack);
            return true;
        }
    }

    private void LoadEmbedded(IEnumerable<Assembly> pluginAssemblies)
    {
        var assemblies = pluginAssemblies.Distinct().OrderBy(a => a.GetName().Name, StringComparer.Ordinal);
        var loaded = new List<LoadedReferencePack>();
        var strings = new ReferenceStringPool();
        foreach (var source in assemblies.SelectMany(EmbeddedSources))
        {
            TryLoad(source.Origin, () => ReferencePackLoader.Load(source, ReferencePackOrigin.Embedded, Options.Limits, strings), loaded);
        }

        _packs = Sorted(loaded);
    }

    private void LoadStored()
    {
        if (Options.StorageRoot is not { } root || !Directory.Exists(root))
        {
            return;
        }

        var loaded = new List<LoadedReferencePack>(_packs);
        foreach (var file in Directory.EnumerateFiles(root, FilePrefix + "*" + PackExtension).Where(f => f.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            TryLoad(name, () => LoadStoredFile(file, name), loaded);
        }

        _packs = Sorted(loaded);
    }

    private LoadedReferencePack LoadStoredFile(string file, string name)
    {
        if (new FileInfo(file).Length > Options.Limits.MaxArchiveBytes)
        {
            throw new ReferencePackException($"'{name}' is larger than the limit of {Options.Limits.MaxArchiveBytes:N0} bytes.");
        }

        using var source = new ZipPackSource(File.ReadAllBytes(file), $"'{name}'", Options.Limits);
        return ReferencePackLoader.Load(source, ReferencePackOrigin.User, Options.Limits);
    }

    private void TryLoad(string origin, Func<LoadedReferencePack> load, List<LoadedReferencePack> into)
    {
        try
        {
            var pack = load();
            if (into.Any(p => p.Id == pack.Id))
            {
                _errors.Add(new ReferencePackLoadError(origin, $"Pack id '{pack.Id}' is already loaded; this copy was skipped."));
                return;
            }

            into.Add(pack);
        }
        catch (ReferencePackException ex)
        {
            _errors.Add(new ReferencePackLoadError(origin, ex.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // One unreadable pack must never stop the app from starting.
            _errors.Add(new ReferencePackLoadError(origin, $"Could not be read: {ex.Message}"));
        }
    }

    private void Persist(string packId, ReadOnlyMemory<byte> archive)
    {
        if (Options.StorageRoot is not { } root)
        {
            return;
        }

        Directory.CreateDirectory(root);
        var target = Path.Combine(root, FileName(packId));
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, archive.ToArray());
        File.Move(temp, target, overwrite: true);
    }

    private static string FileName(string packId) => FilePrefix + packId + PackExtension;

    private static IEnumerable<IPackFileSource> EmbeddedSources(Assembly assembly) => EmbeddedPackSource.Discover(assembly);

    private static ImmutableList<LoadedReferencePack> Sorted(IEnumerable<LoadedReferencePack> packs) =>
    [
        .. packs
            .OrderBy(p => p.Info.Origin == ReferencePackOrigin.Embedded ? 0 : 1)
            .ThenBy(p => p.Id, StringComparer.Ordinal),
    ];
}
