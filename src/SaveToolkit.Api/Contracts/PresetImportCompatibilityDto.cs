namespace SaveToolkit.Api.Contracts;

/// <summary>Informational document context for a staged import. Apply remains plugin-authoritative.</summary>
public sealed record PresetImportCompatibilityDto(int Index, PresetCompatibilitySummaryDto Compatibility);
