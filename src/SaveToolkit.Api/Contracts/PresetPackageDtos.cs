using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Api.Contracts;

/// <summary>Strict version-1 transfer graph. Legacy CRUD records retain their optional/default fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageDto(
    [property: RegularExpression("^1$")] string SchemaVersion,
    PresetPackageProvenanceDto Provenance,
    [property: MinLength(1), MaxLength(PresetTransferLimits.MaxRecords)] IReadOnlyList<PresetPackageRecordDto> Presets);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageProvenanceDto(
    [property: StringLength(100, MinimumLength = 1)] string Producer,
    DateTimeOffset ExportedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageRecordDto(
    [property: RegularExpression("^1$")] string SchemaVersion,
    [property: StringLength(PresetTransferLimits.MaxPresetIdLength, MinimumLength = 1)] string Id,
    [property: StringLength(100, MinimumLength = 1)] string Name,
    PresetPackageCompatibilityDto Compatibility,
    [property: MinLength(1), MaxLength(32)] IReadOnlyList<PresetPackageActionDto> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageCompatibilityDto(
    [property: StringLength(PresetContractValidation.MaxKeyLength, MinimumLength = 1)] string PluginId,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string PluginVersion,
    [property: MinLength(1), MaxLength(PresetContractValidation.MaxMapEntries)] IReadOnlyList<string> TitleIds,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? GameBuild,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? FormatVariant,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? ReferencePackIdentity,
    bool RequiresExplicitInstanceResolution,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string PresetKind,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? SchemaFingerprint,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? FileRole,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? CapabilitySchemaVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageActionDto(
    [property: StringLength(256, MinimumLength = 1)] string ActionId,
    IReadOnlyDictionary<string, JsonElement> Parameters,
    [property: MaxLength(256)] IReadOnlyList<PresetPackageInstanceDto> Instances,
    [property: RegularExpression("^(document|allCopies|allEligible|selected)$")] string ScopeKind);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresetPackageInstanceDto(
    [property: StringLength(PresetContractValidation.MaxKeyLength, MinimumLength = 1)] string Kind,
    [property: StringLength(256, MinimumLength = 1)] string StableId,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? Label = null,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? Role = null,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? Path = null,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? Build = null,
    [property: StringLength(PresetContractValidation.MaxMetadataLength, MinimumLength = 1)] string? Provenance = null);
