using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>Validates a complete replacement graph before any persistence, map or identity publication.</summary>
internal sealed class PresetLibraryStaging(PresetStoreOptions options, PresetLibraryStorage storage, Action<ToolkitPreset> validateRecord)
{
    internal Dictionary<string, ToolkitPreset> Stage(IReadOnlyList<ToolkitPreset> records)
    {
        if (records.Count > options.MaxPresets) throw new InvalidDataException("Preset storage exceeds the configured count limit.");
        var staged = new Dictionary<string, ToolkitPreset>(StringComparer.Ordinal);
        foreach (var preset in records)
        {
            if (preset is null) throw new InvalidDataException("Preset storage contains a null record.");
            if (preset.SchemaVersion != PresetStore.CurrentSchemaVersion)
                throw new InvalidDataException($"Preset schema '{preset.SchemaVersion}' is not supported.");
            if (string.IsNullOrWhiteSpace(preset.Id)) throw new InvalidDataException("Preset storage contains a blank id.");
            try { validateRecord(preset); }
            catch (ArgumentException ex) { throw new InvalidDataException("Preset storage contains a malformed record.", ex); }
            if (!staged.TryAdd(preset.Id, PresetStore.ClonePreset(preset)))
                throw new InvalidDataException($"Preset storage contains duplicate id '{preset.Id}'.");
        }
        _ = storage.Serialize(staged.Values);
        return staged;
    }
}
