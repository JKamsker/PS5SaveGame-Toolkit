using System.Reflection;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>
/// The packs embedded in plugin assemblies, each validated in full (manifest, every hash, record and
/// image) the first time something needs it rather than all at startup. Validating every pack still
/// happens exactly once, and the packs, duplicate-id winners and diagnostics are the same as validating
/// every pack eagerly in discovery order. Thread-safe.
/// </summary>
internal sealed class EmbeddedPackSet
{
    private readonly Slot[] _slots;
    private readonly Lock _gate = new();
    private volatile EmbeddedPacks? _all;
    private int _cursor;

    public EmbeddedPackSet(IEnumerable<Assembly> pluginAssemblies, ReferencePackLimits limits)
    {
        var strings = new ReferenceStringPool();
        _slots =
        [
            .. pluginAssemblies.Distinct().OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
                .SelectMany(EmbeddedPackSource.Discover)
                .Select(source => new Slot(source, limits, strings)),
        ];
    }

    /// <summary>Every accepted pack in discovery order plus every rejection; validates whatever is still pending.</summary>
    public EmbeddedPacks All
    {
        get
        {
            if (_all is { } all) return all;
            lock (_gate)
            {
                return _all ??= Fold();
            }
        }
    }

    /// <summary>The accepted pack with this id: the first copy in discovery order that passes validation.</summary>
    public LoadedReferencePack? Find(string packId)
    {
        // Only copies whose manifest names this id can win. Validating them in order keeps the
        // eager "first valid copy wins" rule without touching unrelated packs.
        foreach (var slot in _slots)
        {
            if (slot.MayHaveId(packId) && slot.Outcome.Pack is { } pack && pack.Id == packId) return pack;
        }

        return null;
    }

    /// <summary>Accepted packs that list <paramref name="pluginId"/>, in discovery order.</summary>
    public IEnumerable<LoadedReferencePack> ForPlugin(string pluginId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in _slots)
        {
            if (!slot.MayListPlugin(pluginId)) continue;
            // Each id has one winner; later copies of a resolved id are skipped without validating them.
            if ((slot.KnownId ?? slot.Outcome.Pack?.Id) is not { } id || !ids.Add(id)) continue;
            if (Find(id) is { } pack && pack.Info.PluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
                yield return pack;
        }
    }

    /// <summary>Validates the next pack nothing has needed yet. False when every pack is validated.</summary>
    public bool ValidateNext()
    {
        while (true)
        {
            var index = Volatile.Read(ref _cursor);
            if (index >= _slots.Length) return false;
            if (Interlocked.CompareExchange(ref _cursor, index + 1, index) != index) continue;
            if (_slots[index].IsValidated) continue;
            _ = _slots[index].Outcome;
            return true;
        }
    }

    private EmbeddedPacks Fold()
    {
        var packs = new List<LoadedReferencePack>();
        var errors = new List<ReferencePackLoadError>();
        foreach (var slot in _slots)
        {
            var outcome = slot.Outcome;
            if (outcome.Pack is not { } pack)
                errors.Add(new ReferencePackLoadError(slot.Origin, outcome.Error!));
            else if (packs.Any(p => p.Id == pack.Id))
                errors.Add(new ReferencePackLoadError(slot.Origin, PackOutcome.Duplicate(pack.Id)));
            else
                packs.Add(pack);
        }

        return new EmbeddedPacks(packs, errors);
    }

    /// <summary>One embedded pack folder: its validation result, computed once, and a cheap manifest hint.</summary>
    private sealed class Slot(EmbeddedPackSource source, ReferencePackLimits limits, ReferenceStringPool strings)
    {
        private readonly Lock _gate = new();
        private volatile PackOutcome? _outcome;
        private volatile ManifestHint? _hint;

        public string Origin => source.Origin;

        public bool IsValidated => _outcome is not null;

        public PackOutcome Outcome
        {
            get
            {
                if (_outcome is { } outcome) return outcome;
                lock (_gate)
                {
                    // Not cached on OutOfMemoryException (rethrown, as eager loading did), so a later need retries.
                    return _outcome ??= PackOutcome.Capture(() =>
                        ReferencePackLoader.Load(source, ReferencePackOrigin.Embedded, limits, strings));
                }
            }
        }

        /// <summary>The id of the validated pack, or the id its manifest declares; null when neither is known.</summary>
        public string? KnownId => _outcome is { } outcome ? outcome.Pack?.Id : Hint.Id;

        /// <summary>False only when this folder cannot produce a pack with that id.</summary>
        public bool MayHaveId(string packId) => _outcome is { } outcome
            ? outcome.Pack?.Id == packId
            : Hint.Id is not { } id || id == packId;

        /// <summary>False only when this folder cannot produce a pack listing that plugin.</summary>
        public bool MayListPlugin(string pluginId) => _outcome is { } outcome
            ? outcome.Pack?.Info.PluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase) == true
            : Hint.Plugins is not { } plugins || plugins.Contains(pluginId, StringComparer.OrdinalIgnoreCase);

        private ManifestHint Hint => _hint ??= ManifestHint.Read(source, limits);
    }

    /// <summary>
    /// The id and plugins a folder's manifest declares, used only to skip folders that cannot match. A
    /// folder whose manifest cannot be read gets no hint and is validated whenever it might matter.
    /// </summary>
    private sealed record ManifestHint(string? Id, IReadOnlyList<string>? Plugins)
    {
        private static readonly ManifestHint None = new(null, null);

        public static ManifestHint Read(EmbeddedPackSource source, ReferencePackLimits limits)
        {
            try
            {
                if (source.Read(PackManifest.FileName, limits.MaxManifestBytes) is not { } bytes) return None;
                var manifest = PackJson.Parse<PackManifest>(bytes, PackManifest.FileName);
                return new ManifestHint(manifest.Id, manifest.Plugins is { } plugins ? [.. plugins] : null);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Full validation reports the real problem; the hint only narrows the search.
                return None;
            }
        }
    }
}

/// <summary>Accepted embedded packs (discovery order) and rejections, as eager loading produced them.</summary>
internal sealed record EmbeddedPacks(IReadOnlyList<LoadedReferencePack> Packs, IReadOnlyList<ReferencePackLoadError> Errors);

/// <summary>The result of validating one pack: the pack, or the message recorded for its rejection.</summary>
internal sealed record PackOutcome(LoadedReferencePack? Pack, string? Error)
{
    public static string Duplicate(string packId) => $"Pack id '{packId}' is already loaded; this copy was skipped.";

    public static PackOutcome Capture(Func<LoadedReferencePack> load)
    {
        try
        {
            return new PackOutcome(load(), null);
        }
        catch (ReferencePackException ex)
        {
            return new PackOutcome(null, ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // One unreadable pack must never stop the app from starting.
            return new PackOutcome(null, $"Could not be read: {ex.Message}");
        }
    }
}
