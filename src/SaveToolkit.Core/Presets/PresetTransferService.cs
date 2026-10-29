using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>Bounded host-only transfer workflow. No document/session service is used.</summary>
public sealed class PresetTransferService(PresetStore store, TimeProvider? timeProvider = null)
{
    private const int MaxPendingPreviews = 8;
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, PendingImport> _pending = new(StringComparer.Ordinal);

    public byte[] Export(PresetExportRequest request)
    {
        if (request.Ids is null || request.Ids.Count is < 1 or > PresetPackageCodec.MaxPresets
            || request.Ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128) || request.Ids.Distinct(StringComparer.Ordinal).Count() != request.Ids.Count)
            throw new PresetTransferException("invalidPresetSelection", "Select 1–256 distinct presets to export.", 400);
        var snapshot = store.Snapshot();
        var selected = request.Ids.Select(id => snapshot.Presets.FirstOrDefault(p => p.Id == id)
            ?? throw new PresetTransferException("stalePresetExport", "A selected preset was removed. Refresh the library and select again.", 409)).ToArray();
        try { return PresetPackageCodec.Encode(selected); }
        catch (PresetTransferException ex) when (ex.StatusCode == 413)
        {
            throw new PresetTransferException("presetExportTooLarge", "This selection exceeds the 512 KiB package limit. Export fewer presets.", 413);
        }
    }

    public PresetImportPreview Preview(ReadOnlyMemory<byte> bytes)
    {
        var package = PresetPackageCodec.Decode(bytes);
        lock (_gate)
        {
            var snapshot = store.Snapshot();
            var items = package.Presets.Select((preset, index) => new PresetImportItem(index, preset.Name,
                preset.Compatibility, preset.Actions.Count, snapshot.Presets
                    .Where(existing => existing.Id == preset.Id || SameName(existing.Name, preset.Name))
                    .Select(existing => new PresetImportTarget(existing.Id, existing.Name)).ToArray(),
                package.Presets.Count(other => SameName(other.Name, preset.Name)) > 1)).ToArray();
            return Add(new(package, snapshot, items, null, _time.GetUtcNow() + PreviewLifetime));
        }
    }

    public PresetImportPreview Resolve(PresetImportResolution request)
    {
        lock (_gate)
        {
            var pending = Find(request.Token);
            CheckCurrent(pending);
            if (request.Choices is null || request.Choices.Count != pending.Items.Count || request.Choices.Any(c => c is null)
                || request.Choices.Select(c => c.Index).Distinct().Count() != request.Choices.Count)
                throw new PresetTransferException("presetImportChoices", "Choose Keep both or an explicit replacement for every imported preset.", 400);
            var library = pending.Snapshot.Presets.ToList();
            var items = new List<PresetImportItem>();
            var replacementIds = new HashSet<string>(StringComparer.Ordinal);
            var incomingIds = new HashSet<string>(pending.Package.Presets.Select(p => p.Id), StringComparer.Ordinal);
            foreach (var item in pending.Items)
            {
                var choice = request.Choices.SingleOrDefault(c => c.Index == item.Index)
                    ?? throw new PresetTransferException("presetImportChoices", "A preset decision is missing.", 400);
                var incoming = pending.Package.Presets[item.Index];
                ToolkitPreset result;
                string? replaces = null;
                if (choice.Decision == "replace")
                {
                    var target = item.Conflicts.SingleOrDefault(c => c.Id == choice.ReplaceId)
                        ?? throw new PresetTransferException("presetImportTarget", "Choose the specific existing preset to replace.", 400);
                    if (!replacementIds.Add(target.Id))
                        throw new PresetTransferException("presetImportTarget", "Two imported presets cannot replace the same existing preset.");
                    library.RemoveAll(p => p.Id == target.Id);
                    // An ID conflict with another record must not silently replace that second record.
                    result = incoming with { Id = target.Id };
                    replaces = target.Name;
                }
                else if (choice.Decision == "keepBoth" && choice.ReplaceId is null)
                {
                    var id = incoming.Id;
                    if (library.Any(p => p.Id == id))
                        do { id = Guid.NewGuid().ToString("N"); } while (incomingIds.Contains(id) || library.Any(p => p.Id == id));
                    result = incoming with { Id = id };
                }
                else throw new PresetTransferException("presetImportChoices", "Choose Keep both or Replace existing, or cancel the import.", 400);
                result = result with { Name = UniqueName(result.Name, library) };
                library.Add(result);
                items.Add(item with { ResultingName = result.Name, ReplacesName = replaces });
            }
            if (library.Count > PresetPackageCodec.MaxPresets)
                throw new PresetTransferException("presetLibraryFull", "The resulting library exceeds 256 presets. Remove presets or import fewer records.");
            // Verify whole-file storage size before offering confirmation, without changing the store.
            store.ValidateImportLibrary(library);
            return Add(pending with { Items = items, ResolvedLibrary = library });
        }
    }

    public PresetImportResult Commit(PresetImportCommit request)
    {
        lock (_gate)
        {
            var pending = Find(request.Token);
            if (pending.ResolvedLibrary is null)
                throw new PresetTransferException("presetImportNotReviewed", "Review the resulting preset names and replacements before confirming.", 409);
            store.ImportLibrary(pending.Snapshot.Identity, pending.ResolvedLibrary);
            _pending.Remove(request.Token);
            return new(pending.Items.Select(item => new PresetImportTarget(
                pending.ResolvedLibrary.Single(p => p.Name == item.ResultingName).Id, item.ResultingName!)).ToArray());
        }
    }

    /// <summary>Detached semantic payloads for informational host compatibility checks. Never applies a preset.</summary>
    public IReadOnlyList<ToolkitPreset> GetPreviewPresets(string token)
    {
        lock (_gate) return Array.AsReadOnly(Find(token).Package.Presets.Select(PresetStore.ClonePreset).ToArray());
    }

    public void Cancel(string token) { lock (_gate) _pending.Remove(token); }

    private PresetImportPreview Add(PendingImport pending)
    {
        foreach (var key in _pending.Where(p => p.Value.ExpiresAt <= _time.GetUtcNow()).Select(p => p.Key).ToArray()) _pending.Remove(key);
        if (_pending.Count >= MaxPendingPreviews)
            throw new PresetTransferException("presetImportBusy", "Too many import previews are open. Close another preview or retry in ten minutes.", 503);
        var token = Guid.NewGuid().ToString("N");
        _pending.Add(token, pending);
        var publicItems = Array.AsReadOnly(pending.Items.Select(item => item with
        {
            Compatibility = item.Compatibility with { TitleIds = Array.AsReadOnly(item.Compatibility.TitleIds.ToArray()) },
            Conflicts = Array.AsReadOnly(item.Conflicts.ToArray()),
        }).ToArray());
        return new(token, pending.Package.SchemaVersion, pending.Package.Provenance, publicItems,
            pending.ResolvedLibrary is not null, pending.ExpiresAt);
    }

    private PendingImport Find(string? token) => token is not null && _pending.TryGetValue(token, out var pending)
        && pending.ExpiresAt > _time.GetUtcNow() ? pending
        : throw new PresetTransferException("stalePresetImport", "This import preview expired or was already used. Preview the file again.", 409);

    private void CheckCurrent(PendingImport pending)
    {
        if (store.Snapshot().Identity != pending.Snapshot.Identity)
            throw new PresetTransferException("stalePresetImport", "The preset library changed. Preview the file again.", 409);
    }

    private static bool SameName(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string UniqueName(string name, List<ToolkitPreset> library)
    {
        name = name.Trim();
        if (!library.Any(p => SameName(p.Name, name))) return name;
        for (var number = 2; number <= PresetPackageCodec.MaxPresets + 1; number++)
        {
            var suffix = $" ({number})";
            var length = Math.Min(name.Length, 100 - suffix.Length);
            if (char.IsHighSurrogate(name[length - 1])) length--;
            var candidate = name[..length] + suffix;
            if (!library.Any(p => SameName(p.Name, candidate))) return candidate;
        }
        throw new PresetTransferException("presetImportNames", "No distinct preset name is available.");
    }

    private sealed record PendingImport(PresetPackage Package, PresetLibrarySnapshot Snapshot,
        IReadOnlyList<PresetImportItem> Items, IReadOnlyList<ToolkitPreset>? ResolvedLibrary, DateTimeOffset ExpiresAt);
}
