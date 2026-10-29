using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

public sealed record PresetStoreOptions
{
    public required string StorageRoot { get; init; }
    public int MaxPresets { get; init; } = 256;
    public int MaxPayloadBytes { get; init; } = 512 * 1024;
}

public sealed record PresetCompatibilityCheck(bool Compatible, string? Message = null, string? Code = null)
{
    public static PresetCompatibilityCheck Success { get; } = new(true);
}

/// <summary>Durable, host-owned JSON storage for toolkit presets. Save-set exports never read this store.</summary>
public sealed class PresetStore
{
    public const string CurrentSchemaVersion = "1";
    private const string FileName = "presets.json";
    private readonly PresetStoreOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, ToolkitPreset> _presets = new(StringComparer.Ordinal);
    private string _generation = Guid.NewGuid().ToString("N");

    public PresetStore(PresetStoreOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StorageRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPresets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPayloadBytes);
        _options = options;
        Directory.CreateDirectory(options.StorageRoot);
        Load();
    }

    public IReadOnlyList<ToolkitPreset> List()
    {
        lock (_gate) return [.. _presets.Values.OrderByDescending(p => p.UpdatedAt).Select(ClonePreset)];
    }

    public ToolkitPreset? Find(string id)
    {
        lock (_gate) return _presets.TryGetValue(id, out var preset) ? ClonePreset(preset) : null;
    }

    /// <summary>Snapshot bound to successful CRUD and the current durable file identity.</summary>
    public PresetLibrarySnapshot Snapshot()
    {
        lock (_gate) return new(StorageIdentity(), List());
    }

    /// <summary>Publishes a reviewed whole-library transaction with one durable write.</summary>
    public void ImportLibrary(string expectedIdentity, IReadOnlyList<ToolkitPreset> records)
    {
        lock (_gate)
        {
            if (!string.Equals(expectedIdentity, StorageIdentity(), StringComparison.Ordinal))
                throw new PresetTransferException("stalePresetImport", "The preset library changed. Preview the package again before importing.", 409);
            ValidateImportLibrary(records);
            var staged = new Dictionary<string, ToolkitPreset>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                ValidateName(record.Name);
                ValidatePayload(record.Compatibility, record.Actions);
                if (!staged.TryAdd(record.Id, ClonePreset(record)))
                    throw new PresetTransferException("duplicatePresetId", "The import would repeat a preset ID.");
            }
            // Persist before publication: a failed file replacement cannot mutate the live graph.
            Persist(staged.Values);
            _presets.Clear();
            foreach (var pair in staged) _presets.Add(pair.Key, pair.Value);
        }
    }

    public void ValidateImportLibrary(IReadOnlyList<ToolkitPreset> records)
    {
        if (records.Count > _options.MaxPresets)
            throw new PresetTransferException("presetLibraryFull", "The resulting library exceeds the preset limit. Remove presets or import fewer records.");
        if (JsonSerializer.SerializeToUtf8Bytes(records.OrderBy(p => p.Id), JsonOptions).Length > _options.MaxPayloadBytes)
            throw new PresetTransferException("presetLibraryFull", "The resulting preset library exceeds its 512 KiB storage limit. Import fewer presets.");
    }

    private string StorageIdentity()
    {
        try
        {
            var path = Path.Combine(_options.StorageRoot, FileName);
            return _generation + ":" + PresetStorageIdentity.Read(path, _options.MaxPayloadBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }

    /// <summary>Stable fingerprint of the reviewed preset payload, excluding mutable timestamps.</summary>
    public static string PayloadFingerprint(ToolkitPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { preset.SchemaVersion, preset.Name, preset.Compatibility, preset.Actions }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public ToolkitPreset Create(string name, PresetCompatibility compatibility, IReadOnlyList<PresetAction> actions)
    {
        ValidateName(name);
        ValidatePayload(compatibility, actions);
        lock (_gate)
        {
            if (_presets.Count >= _options.MaxPresets) throw new InvalidOperationException("The preset limit has been reached.");
            var now = DateTimeOffset.UtcNow;
            var preset = ClonePreset(new ToolkitPreset(CurrentSchemaVersion, Guid.NewGuid().ToString("N"), name.Trim(), compatibility,
                CloneActions(actions), now, now));
            _presets.Add(preset.Id, preset);
            try { Persist(); }
            catch { _presets.Remove(preset.Id); throw; }
            return ClonePreset(preset);
        }
    }

    public ToolkitPreset Rename(string id, string name)
    {
        ValidateName(name);
        lock (_gate)
        {
            var current = FindRequired(id);
            var updated = current with { Name = name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
            _presets[id] = updated;
            try { Persist(); }
            catch { _presets[id] = current; throw; }
            return ClonePreset(updated);
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            if (!_presets.TryGetValue(id, out var removed)) return false;
            _presets.Remove(id);
            try { Persist(); }
            catch
            {
                // Keep the in-memory view consistent with the durable file after a failed write.
                _presets[id] = removed;
                throw;
            }
            return true;
        }
    }

    public static PresetCompatibilityCheck CheckCompatibility(ToolkitPreset preset, DocumentSession document)
    {
        if (!string.Equals(preset.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            return new(false, $"Preset schema '{preset.SchemaVersion}' is not supported.", "schemaMismatch");
        if (!string.Equals(preset.Compatibility.PluginId, document.Plugin.Info.Id, StringComparison.Ordinal))
            return new(false, "This preset belongs to a different plugin.", "pluginUnavailable");
        if (!string.Equals(preset.Compatibility.PluginVersion, document.Plugin.Info.Version, StringComparison.Ordinal))
            return new(false, "This preset was created for a different plugin version.", "pluginUnavailable");
        if (!preset.Compatibility.TitleIds.Contains(document.File.Directory.TitleId, StringComparer.OrdinalIgnoreCase))
            return new(false, "This preset belongs to a different title.", "titleMismatch");
        var capability = document.PresetCapability;
        var build = capability?.GameBuild;
        if (preset.Compatibility.GameBuild is { } expected
            && !string.Equals(expected, build, StringComparison.Ordinal))
            return new(false, build is null ? "The current game build is unknown." : "This preset belongs to a different game build.", "buildMismatch");
        if (!string.Equals(preset.Compatibility.PresetKind, capability?.PresetKind ?? "toolkit.action-parameters", StringComparison.Ordinal))
            return new(false, "This preset kind is not supported by the current document.", "schemaMismatch");
        if (capability is not null)
        {
            if (MissingOrDifferent(preset.Compatibility.GameBuild, build, "game build", "buildMismatch") is { } buildCheck)
                return buildCheck;
            if (MissingOrDifferent(preset.Compatibility.FormatVariant, capability.FormatVariant, "save variant", "schemaMismatch") is { } variantCheck)
                return variantCheck;
            if (MissingOrDifferent(preset.Compatibility.ReferencePackIdentity, capability.ReferencePackIdentity, "reference pack", "referencePackMismatch") is { } referenceCheck)
                return referenceCheck;
            if (MissingOrDifferent(preset.Compatibility.SchemaFingerprint, capability.SchemaFingerprint, "schema", "schemaMismatch") is { } schemaCheck)
                return schemaCheck;
            if (MissingOrDifferent(preset.Compatibility.FileRole, capability.FileRole, "save-file role", "schemaMismatch") is { } roleCheck)
                return roleCheck;
            if (MissingOrDifferent(preset.Compatibility.CapabilitySchemaVersion, capability.SchemaVersion, "preset capability schema", "schemaMismatch") is { } capabilitySchemaCheck)
                return capabilitySchemaCheck;
        }
        return PresetCompatibilityCheck.Success;

        static PresetCompatibilityCheck? MissingOrDifferent(string? expected, string? actual, string label, string code)
        {
            if (actual is null) return expected is null ? null : new(false, $"The current {label} is unknown.", code);
            if (expected is null) return new(false, $"This preset is missing the required {label} boundary.", code);
            return string.Equals(expected, actual, StringComparison.Ordinal)
                ? null : new(false, $"This preset belongs to a different {label}.", code);
        }
    }

    private ToolkitPreset FindRequired(string id) => _presets.TryGetValue(id, out var preset)
        ? preset : throw new KeyNotFoundException("Preset not found.");

    private void Load()
    {
        var path = Path.Combine(_options.StorageRoot, FileName);
        if (!File.Exists(path)) return;
        try
        {
            var fileLength = new FileInfo(path).Length;
            if (fileLength > _options.MaxPayloadBytes)
                throw new InvalidDataException("Preset storage exceeds the configured size limit.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength > _options.MaxPayloadBytes)
                throw new InvalidDataException("Preset storage exceeds the configured size limit.");
            var records = JsonSerializer.Deserialize<List<ToolkitPreset>>(bytes, JsonOptions) ?? [];
            if (records.Count > _options.MaxPresets) throw new InvalidDataException("Preset storage exceeds the configured limit.");
            foreach (var preset in records)
            {
                if (preset.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(preset.Id)) continue;
                ValidatePayload(preset.Compatibility, preset.Actions);
                _presets[preset.Id] = ClonePreset(preset);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Preset storage is not valid JSON.", ex);
        }
    }

    private void Persist(IEnumerable<ToolkitPreset>? records = null)
    {
        var path = Path.Combine(_options.StorageRoot, FileName);
        var temp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes((records ?? _presets.Values).OrderBy(p => p.Id), JsonOptions);
        if (bytes.LongLength > _options.MaxPayloadBytes)
            throw new InvalidOperationException("Preset storage exceeds the configured size limit.");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            _generation = Guid.NewGuid().ToString("N");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }

    private void ValidatePayload(PresetCompatibility compatibility, IReadOnlyList<PresetAction> actions)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        ArgumentNullException.ThrowIfNull(actions);
        if (string.IsNullOrWhiteSpace(compatibility.PluginId) || string.IsNullOrWhiteSpace(compatibility.PluginVersion)
            || compatibility.TitleIds is null || compatibility.TitleIds.Count == 0 || compatibility.TitleIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Preset compatibility must include plugin and title identifiers.");
        if (actions.Count is < 1 or > 32) throw new ArgumentException("A preset must contain between one and 32 actions.");
        if (actions.Any(action => action is null || string.IsNullOrWhiteSpace(action.ActionId) || action.Parameters is null || action.Parameters.Count > 128
            || string.IsNullOrWhiteSpace(action.ScopeKind)))
            throw new ArgumentException("Preset actions must have an id and at most 128 parameters.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { compatibility, actions }, JsonOptions);
        if (bytes.Length > _options.MaxPayloadBytes) throw new ArgumentException("Preset payload is too large.");
    }

    private static IReadOnlyList<PresetAction> CloneActions(IReadOnlyList<PresetAction> actions) =>
        Array.AsReadOnly(actions.Select(CloneAction).ToArray());

    private static PresetAction CloneAction(PresetAction action) => action with
    {
        Parameters = new ReadOnlyDictionary<string, JsonElement>(
            action.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)),
        Instances = Array.AsReadOnly((action.Instances ?? []).Select(instance => instance with { }).ToArray()),
    };

    private static ToolkitPreset ClonePreset(ToolkitPreset preset) => preset with
    {
        Compatibility = preset.Compatibility with
        {
            TitleIds = Array.AsReadOnly(preset.Compatibility.TitleIds.ToArray()),
        },
        Actions = CloneActions(preset.Actions),
    };

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new ArgumentException("Preset name must contain 1–100 characters.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}
