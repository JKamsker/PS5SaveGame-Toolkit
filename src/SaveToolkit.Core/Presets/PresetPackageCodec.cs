using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>Finite, strict JSON transfer boundary. JSON numbers remain JsonElement lexemes.</summary>
public static class PresetPackageCodec
{
    public const string SchemaVersion = "1";
    public const string Producer = "PS5 Save Game Toolkit";
    public const int MaxBytes = 512 * 1024;
    public const int MaxPresets = PresetTransferLimits.MaxRecords;
    public const int MaxDepth = 16;
    private const int MaxTextLength = 4096;
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        MaxDepth = MaxDepth,
        WriteIndented = true,
    };

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, long? length, CancellationToken cancellationToken, int maximumBytes = MaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, MaxBytes);
        if (length > maximumBytes) throw TooLarge();
        // Fixed allocation before parsing, including requests without Content-Length.
        var buffer = new byte[maximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) return buffer.AsSpan(0, count).ToArray();
            count += read;
        }
        throw TooLarge();
    }

    public static PresetPackage Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) throw TooLarge();
        try
        {
            using var json = JsonDocument.Parse(bytes, new() { MaxDepth = MaxDepth, AllowDuplicateProperties = false });
            var root = json.RootElement;
            RequireFields(root, "schemaVersion", "provenance", "presets");
            if (root.GetProperty("schemaVersion").GetString() != SchemaVersion)
                throw Invalid("unsupportedPresetPackage", "This package version is not supported. Export with a compatible toolkit version.");
            var records = root.GetProperty("presets");
            if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() is < 1 or > MaxPresets)
                throw Invalid("presetPackageCount", "Choose a package containing 1–256 presets.");
            // Check required envelope fields before constructor defaults can widen compatibility.
            foreach (var record in records.EnumerateArray())
            {
                RequireFields(record, "schemaVersion", "id", "name", "compatibility", "actions", "createdAt", "updatedAt");
                RequireFields(record.GetProperty("compatibility"), "pluginId", "pluginVersion", "titleIds", "gameBuild",
                    "formatVariant", "referencePackIdentity", "requiresExplicitInstanceResolution", "presetKind",
                    "schemaFingerprint", "fileRole", "capabilitySchemaVersion");
                var actions = record.GetProperty("actions");
                if (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() is < 1 or > 32)
                    throw Invalid("presetPackageActions", "Each preset must contain 1–32 semantic actions.");
                var totalInstances = 0;
                foreach (var action in actions.EnumerateArray())
                {
                    RequireFields(action, "actionId", "parameters", "instances", "scopeKind");
                    var parameters = action.GetProperty("parameters");
                    var instances = action.GetProperty("instances");
                    if (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Count() > 128
                        || instances.ValueKind != JsonValueKind.Array || instances.GetArrayLength() > 256)
                        throw Invalid("presetPackageFields", "An action exceeds the parameter or semantic-target limit.");
                    totalInstances += instances.GetArrayLength();
                    if (totalInstances > PresetContractValidation.MaxSemanticInstances)
                        throw Invalid("presetPackageFields", "A preset exceeds the aggregate limit of 256 semantic targets across all actions.");
                }
            }
            var package = root.Deserialize<PresetPackage>(JsonOptions)!;
            if (package.Provenance is null || package.Provenance.ExportedAt == default)
                throw Invalid("presetPackageProvenance", "The package needs valid toolkit provenance and an export timestamp.");
            Text(package.Provenance.Producer, "producer", 100);
            foreach (var preset in package.Presets) Validate(preset);
            if (package.Presets.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != package.Presets.Count)
                throw Invalid("duplicatePackageId", "The package repeats a preset ID. Export each preset only once.");
            return package;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new PresetTransferException("invalidPresetPackage", "The package is malformed, too deeply nested, or contains unknown fields. Choose a toolkit preset package.");
        }
    }

    public static byte[] Encode(IReadOnlyList<ToolkitPreset> presets)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PresetPackage(SchemaVersion,
            new(Producer, DateTimeOffset.UtcNow), presets), JsonOptions);
        _ = Decode(bytes); // Export never creates a package that cannot be imported.
        return bytes;
    }

    private static void Validate(ToolkitPreset preset)
    {
        if (preset is null) throw Invalid("invalidPresetPackage", "A preset record is missing.");
        if (preset.SchemaVersion != PresetStore.CurrentSchemaVersion)
            throw Invalid("unsupportedPresetSchema", "A preset uses an unsupported schema version.");
        Text(preset.Id, "preset ID", PresetTransferLimits.MaxPresetIdLength);
        Text(preset.Name, "preset name", 100);
        if (preset.CreatedAt == default || preset.UpdatedAt < preset.CreatedAt)
            throw Invalid("presetPackageTimestamp", "Preset timestamps are missing or out of order.");
        var compatibility = preset.Compatibility;
        Text(compatibility.PluginId, "plugin ID", 128);
        Text(compatibility.PluginVersion, "plugin version", 128);
        Text(compatibility.PresetKind, "preset kind", 128);
        if (compatibility.TitleIds is null || compatibility.TitleIds.Count is < 1 or > 32)
            throw Invalid("presetPackageTitles", "Each preset needs 1–32 explicit title identifiers.");
        foreach (var title in compatibility.TitleIds) Text(title, "title ID", 128);
        foreach (var boundary in new[] { compatibility.GameBuild, compatibility.FormatVariant, compatibility.ReferencePackIdentity,
            compatibility.SchemaFingerprint, compatibility.FileRole, compatibility.CapabilitySchemaVersion })
            if (boundary is not null) Text(boundary, "compatibility boundary", MaxTextLength);
        foreach (var action in preset.Actions)
        {
            Text(action.ActionId, "action ID", 256);
            if (action.ScopeKind is not (PresetScopeKinds.Document or PresetScopeKinds.AllCopies or PresetScopeKinds.AllEligible or PresetScopeKinds.Selected))
                throw Invalid("presetPackageScope", "A preset contains an unsupported semantic scope.");
            if (action.Parameters is null || action.Parameters.Count > 128 || action.Instances is null || action.Instances.Count > 256)
                throw Invalid("presetPackageFields", "An action exceeds the parameter or semantic-target limit.");
            foreach (var pair in action.Parameters)
            {
                Text(pair.Key, "parameter ID", 256);
                ValidateValue(pair.Value);
            }
            foreach (var instance in action.Instances)
            {
                if (instance is null) throw Invalid("presetPackageInstance", "A semantic target is missing.");
                Text(instance.Kind, "semantic target kind", 128);
                Text(instance.StableId, "semantic target ID", PresetContractValidation.MaxKeyLength);
                if (instance.Label is not null) Text(instance.Label, "semantic target label", 256);
                if (instance.Role is not null) Text(instance.Role, "semantic target role", 256);
                if (instance.Path is not null) Text(instance.Path, "semantic target path", 1024);
                if (instance.Build is not null) Text(instance.Build, "semantic target build", 256);
                if (instance.Provenance is not null) Text(instance.Provenance, "semantic target provenance", MaxTextLength);
            }
        }
    }

    private static void ValidateValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= MaxTextLength) return;
        if (value.ValueKind == JsonValueKind.Number && value.GetRawText().Length <= MaxTextLength) return;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null) return;
        // Tagged exact/union values are validated structurally, without converting numbers to doubles.
        if (value.ValueKind == JsonValueKind.Object && value.GetRawText().Length <= MaxTextLength)
        {
            _ = value.Deserialize<SaveToolkit.Sdk.Values.FieldValue>(JsonOptions);
            return;
        }
        throw Invalid("presetPackageParameter", "A parameter is not a supported bounded typed field value.");
    }

    private static void RequireFields(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object || fields.Any(field => !element.TryGetProperty(field, out _)))
            throw Invalid("presetPackageMissingField", "The package is missing required envelope fields. Re-export it with a current toolkit.");
    }

    private static void Text(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw Invalid("presetPackageField", $"The {label} must contain 1–{maximum} characters without control characters.");
    }

    private static PresetTransferException Invalid(string code, string message) => new(code, message);
    private static PresetTransferException TooLarge() => new("presetPackageTooLarge", "Choose a preset package no larger than 512 KiB.", 413);
}
