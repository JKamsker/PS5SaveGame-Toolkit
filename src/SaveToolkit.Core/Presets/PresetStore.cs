using System.Text.Json;
using System.Text.Json.Serialization;
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
        lock (_gate) return [.. _presets.Values.OrderByDescending(p => p.UpdatedAt)];
    }

    public ToolkitPreset? Find(string id)
    {
        lock (_gate) return _presets.GetValueOrDefault(id);
    }

    public ToolkitPreset Create(string name, PresetCompatibility compatibility, IReadOnlyList<PresetAction> actions)
    {
        ValidateName(name);
        ValidatePayload(compatibility, actions);
        lock (_gate)
        {
            if (_presets.Count >= _options.MaxPresets) throw new InvalidOperationException("The preset limit has been reached.");
            var now = DateTimeOffset.UtcNow;
            var preset = new ToolkitPreset(CurrentSchemaVersion, Guid.NewGuid().ToString("N"), name.Trim(), compatibility,
                CloneActions(actions), now, now);
            _presets.Add(preset.Id, preset);
            try { Persist(); }
            catch { _presets.Remove(preset.Id); throw; }
            return preset;
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
            return updated;
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
        var build = document.PresetDocument?.PresetCapability.GameBuild;
        if (preset.Compatibility.GameBuild is { } expected
            && !string.Equals(expected, build, StringComparison.Ordinal))
            return new(false, build is null ? "The current game build is unknown." : "This preset belongs to a different game build.", "buildMismatch");
        var capability = document.PresetDocument?.PresetCapability;
        if (preset.Compatibility.FormatVariant is { } variant
            && !string.Equals(variant, capability?.FormatVariant, StringComparison.Ordinal))
            return new(false, capability?.FormatVariant is null ? "The current save variant is unknown." : "This preset belongs to a different save variant.", "schemaMismatch");
        if (preset.Compatibility.ReferencePackIdentity is { } referencePack
            && !string.Equals(referencePack, capability?.ReferencePackIdentity, StringComparison.Ordinal))
            return new(false, capability?.ReferencePackIdentity is null ? "The required reference pack is unavailable." : "This preset requires a different reference pack.", "referencePackMismatch");
        if (!string.Equals(preset.Compatibility.PresetKind, capability?.PresetKind ?? "toolkit.action-parameters", StringComparison.Ordinal))
            return new(false, "This preset kind is not supported by the current document.", "schemaMismatch");
        if (preset.Compatibility.SchemaFingerprint is { } fingerprint
            && !string.Equals(fingerprint, capability?.SchemaFingerprint, StringComparison.Ordinal))
            return new(false, capability?.SchemaFingerprint is null ? "The current schema fingerprint is unknown." : "This preset belongs to a different schema.", "schemaMismatch");
        if (preset.Compatibility.FileRole is { } fileRole
            && !string.Equals(fileRole, capability?.FileRole, StringComparison.Ordinal))
            return new(false, "This preset targets a different save-file role.", "schemaMismatch");
        return PresetCompatibilityCheck.Success;
    }

    private ToolkitPreset FindRequired(string id) => _presets.TryGetValue(id, out var preset)
        ? preset : throw new KeyNotFoundException("Preset not found.");

    private void Load()
    {
        var path = Path.Combine(_options.StorageRoot, FileName);
        if (!File.Exists(path)) return;
        try
        {
            var records = JsonSerializer.Deserialize<List<ToolkitPreset>>(File.ReadAllBytes(path), JsonOptions) ?? [];
            if (records.Count > _options.MaxPresets) throw new InvalidDataException("Preset storage exceeds the configured limit.");
            foreach (var preset in records)
            {
                if (preset.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(preset.Id)) continue;
                ValidatePayload(preset.Compatibility, preset.Actions);
                _presets[preset.Id] = preset;
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Preset storage is not valid JSON.", ex);
        }
    }

    private void Persist()
    {
        var path = Path.Combine(_options.StorageRoot, FileName);
        var temp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_presets.Values.OrderBy(p => p.Id), JsonOptions);
        if (bytes.Length > _options.MaxPayloadBytes * _options.MaxPresets)
            throw new InvalidOperationException("Preset storage exceeds the configured size limit.");
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
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
        actions.Select(action => action with { Parameters = new Dictionary<string, JsonElement>(action.Parameters, StringComparer.Ordinal), Instances = [.. action.Instances ?? []] }).ToArray();

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
