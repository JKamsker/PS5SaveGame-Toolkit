using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Presets;

/// <summary>A portable host package, containing semantic presets rather than save documents.</summary>
public sealed record PresetPackage(string SchemaVersion, PresetPackageProvenance Provenance,
    IReadOnlyList<ToolkitPreset> Presets);

/// <summary>Public toolkit provenance only; local paths and session identities are never recorded.</summary>
public sealed record PresetPackageProvenance(string Producer, DateTimeOffset ExportedAt);

public sealed record PresetLibrarySnapshot(string Identity, IReadOnlyList<ToolkitPreset> Presets);
public sealed record PresetImportTarget(string Id, string Name);
public sealed record PresetImportItem(int Index, string Name, PresetCompatibility Compatibility,
    int ActionCount, IReadOnlyList<PresetImportTarget> Conflicts, bool RepeatedPackageName = false, string? ResultingName = null,
    string? ReplacesName = null);
public sealed record PresetImportPreview(string Token, string SchemaVersion, PresetPackageProvenance Provenance,
    IReadOnlyList<PresetImportItem> Items, bool ReadyToCommit, DateTimeOffset ExpiresAt);
public sealed record PresetImportChoice(int Index, string Decision, string? ReplaceId = null);
public sealed record PresetImportResolution(string Token, IReadOnlyList<PresetImportChoice> Choices);
public sealed record PresetImportCommit(string Token);
public sealed record PresetImportResult(IReadOnlyList<PresetImportTarget> Imported);
public sealed record PresetExportRequest(IReadOnlyList<string> Ids);

/// <summary>A stable, actionable transfer diagnostic, independent from save-edit failures.</summary>
public sealed class PresetTransferException(string code, string message, int statusCode = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
