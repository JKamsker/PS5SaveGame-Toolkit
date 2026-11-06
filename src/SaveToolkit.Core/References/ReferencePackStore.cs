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
/// <remarks>
/// Construction only discovers packs. Each pack is validated in full the first time an operation needs
/// it (a pin for its plugin, a lookup of its id, the listing or <see cref="LoadErrors"/>), so results and
/// diagnostics are the same as validating everything up front. <see cref="ValidateNext"/> lets a host
/// spend idle time on the remaining packs. Thread-safe.
/// </remarks>
public sealed class ReferencePackStore
{
    // "pack-" prefix: ids like "con" or "nul" would be Windows device names on their own.
    private const string FilePrefix = "pack-";
    private const string PackExtension = ".zip";

    private readonly Lock _gate = new();
    private readonly EmbeddedPackSet _embedded;
    private ImmutableList<ReferencePackLoadError> _storedErrors = [];
    private volatile ImmutableList<LoadedReferencePack>? _userPacks;

    public ReferencePackStore(IEnumerable<IGamePlugin> plugins, ReferencePackOptions? options = null)
        : this(options, plugins.Select(p => p.GetType().Assembly))
    {
    }

    internal ReferencePackStore(ReferencePackOptions? options, IEnumerable<Assembly> assemblies)
    {
        Options = options ?? new ReferencePackOptions();
        _embedded = new EmbeddedPackSet(assemblies, Options.Limits);
    }

    public ReferencePackOptions Options { get; }

    /// <summary>All loaded packs, embedded first, each group ordered by id.</summary>
    public IReadOnlyList<LoadedReferencePack> Packs => Sorted(_embedded.All.Packs.Concat(UserPacks));

    /// <summary>Packs that were found but rejected (bad hash, bad manifest, ...).</summary>
    public IReadOnlyList<ReferencePackLoadError> LoadErrors
    {
        get
        {
            var embedded = _embedded.All.Errors;
            _ = UserPacks;
            lock (_gate)
            {
                return [.. embedded, .. _storedErrors];
            }
        }
    }

    public LoadedReferencePack? Find(string packId) => _embedded.Find(packId) ?? UserPacks.FirstOrDefault(p => p.Id == packId);

    /// <summary>Immutable reference context for a document opened by <paramref name="plugin"/> for <paramref name="titleId"/>.</summary>
    public ReferenceContext Pin(IGamePlugin plugin, string? titleId)
    {
        var pluginId = plugin.Info.Id;
        var packs = Sorted(_embedded.ForPlugin(pluginId).Concat(UserPacks).Where(p => p.AppliesTo(pluginId, titleId)));
        return packs.Count == 0 ? ReferenceContext.None : new ReferenceContext(packs, Options.PreferredLocales);
    }

    /// <summary>
    /// Validates one embedded pack (or the stored user packs) that no operation has needed yet, so a host
    /// can finish startup work between requests. Returns false once nothing is left. Never changes results.
    /// </summary>
    public bool ValidateNext()
    {
        if (_embedded.ValidateNext()) return true;
        if (_userPacks is not null) return false;
        _ = UserPacks;
        return true;
    }

    /// <summary>User packs (stored ones are validated on first need), ordered by id.</summary>
    private ImmutableList<LoadedReferencePack> UserPacks
    {
        get
        {
            if (_userPacks is { } packs) return packs;
            lock (_gate)
            {
                return _userPacks ??= LoadStored();
            }
        }
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

        if (_embedded.Find(pack.Id) is not null)
        {
            throw new ReferencePackException($"Pack id '{pack.Id}' is already used by a plugin's built-in pack. Give the pack another id.");
        }

        lock (_gate)
        {
            // Stored packs must be known before this archive is written next to them.
            var current = UserPacks;
            Persist(pack.Id, archive);
            _userPacks = Sorted(current.RemoveAll(p => p.Id == pack.Id).Add(pack));
        }

        return pack;
    }

    /// <summary>Removes a user pack. Documents that pinned it keep their copy until they close.</summary>
    public bool Remove(string packId)
    {
        if (_embedded.Find(packId) is not null)
        {
            throw new InvalidOperationException("Built-in packs ship with their plugin and cannot be removed.");
        }

        lock (_gate)
        {
            var pack = UserPacks.FirstOrDefault(p => p.Id == packId);
            if (pack is null)
            {
                return false;
            }

            if (Options.StorageRoot is { } root && PackPaths.IsValidPackId(packId))
            {
                File.Delete(Path.Combine(root, FileName(packId)));
            }

            _userPacks = UserPacks.Remove(pack);
            return true;
        }
    }

    /// <summary>Validates the packs kept in <see cref="ReferencePackOptions.StorageRoot"/>; called once, under the gate.</summary>
    private ImmutableList<LoadedReferencePack> LoadStored()
    {
        var loaded = new List<LoadedReferencePack>();
        var errors = new List<ReferencePackLoadError>();
        if (Options.StorageRoot is { } root && Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, FilePrefix + "*" + PackExtension).Where(f => f.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                var outcome = PackOutcome.Capture(() => LoadStoredFile(file, name));
                if (outcome.Pack is not { } pack)
                    errors.Add(new ReferencePackLoadError(name, outcome.Error!));
                else if (loaded.Any(p => p.Id == pack.Id) || _embedded.Find(pack.Id) is not null)
                    errors.Add(new ReferencePackLoadError(name, PackOutcome.Duplicate(pack.Id)));
                else
                    loaded.Add(pack);
            }
        }

        _storedErrors = [.. errors];
        return Sorted(loaded);
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

    private void Persist(string packId, ReadOnlyMemory<byte> archive)
    {
        if (Options.StorageRoot is not { } root)
        {
            return;
        }

        Directory.CreateDirectory(root);
        var target = Path.Combine(root, FileName(packId));
        var temp = target + ".tmp";
        try
        {
            File.WriteAllBytes(temp, archive.Span);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            // A failed write/rename must not retain a complete unregistered archive.
            File.Delete(temp);
        }
    }

    private static string FileName(string packId) => FilePrefix + packId + PackExtension;

    private static ImmutableList<LoadedReferencePack> Sorted(IEnumerable<LoadedReferencePack> packs) =>
    [
        .. packs
            .OrderBy(p => p.Info.Origin == ReferencePackOrigin.Embedded ? 0 : 1)
            .ThenBy(p => p.Id, StringComparer.Ordinal),
    ];
}
