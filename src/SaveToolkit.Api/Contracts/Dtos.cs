using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Contracts;

/// <summary><paramref name="Code"/> is a machine-readable reason, e.g. <c>"stale"</c> when the client must refetch the view.</summary>
public sealed record ErrorDto(string Error, string? Code = null);

public sealed record PluginDto(
    string Id,
    string Name,
    string Version,
    string? Description,
    PluginMaturity Maturity,
    IReadOnlyList<GameTitle> Games,
    IReadOnlyList<string> References)
{
    public bool SupportsGroups { get; init; }
}

public sealed record WorkspaceSummaryDto(string Id, string Name, DateTimeOffset CreatedAt, int DirectoryCount, int FileCount, IReadOnlyList<string> TitleIds);

public sealed record WorkspaceDto(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TitleDto> Titles,
    IReadOnlyList<DocumentSummaryDto> Documents);

/// <summary><paramref name="GameName"/> comes from a plugin that declares this title id, if any.</summary>
public sealed record TitleDto(string TitleId, string? GameName, IReadOnlyList<DirectoryDto> Directories);

public sealed record SfoDto(string? MainTitle, string? SubTitle, string? Detail, string? SaveDataDirectory);

public sealed record DirectoryDto(
    string Id,
    string Name,
    string RawName,
    bool IsBackup,
    int Generation,
    string? TwinId,
    bool TwinIdentical,
    bool HasIcon,
    SfoDto? Sfo,
    IReadOnlyList<FileDto> Files);

public sealed record PluginMatchDto(string PluginId, string PluginName, int Score, string? Label, PluginMaturity Maturity);

public sealed record FileDto(string Id, string Path, long Length, IReadOnlyList<PluginMatchDto> Matches, string? OpenDocumentId);

public sealed record DocumentSummaryDto(string Id, string FileId, string FilePath, string Title, string PluginId, bool IsDirty)
{
    public IReadOnlyList<GroupMemberInfo>? GroupMembers { get; init; }
}

public sealed record DocumentDto(
    string Id,
    string FileId,
    string FilePath,
    string Title,
    PluginDto Plugin,
    bool CanEdit,
    DocumentState State,
    ViewNode View)
{
    public IReadOnlyList<GroupMemberInfo>? GroupMembers { get; init; }
    public PresetCapability? PresetCapability { get; init; }
    public string? TitleId { get; init; }
}

public sealed record OpenRequest(string? PluginId);

public sealed record EditRequestItem(string ValueId, JsonElement Value);

/// <summary><paramref name="Revision"/> is the view revision the client is bound to; a mismatch is rejected as stale.</summary>
public sealed record EditRequest(IReadOnlyList<EditRequestItem> Edits, long? Revision = null);

public sealed record ActionRequest(string ActionId, Dictionary<string, JsonElement>? Parameters, long? Revision = null);

public sealed record ExportRequest(ExportScope Scope = ExportScope.ChangedDirectories, bool MirrorBackups = true);

public sealed record ExportPlanDto(IReadOnlyList<ExportedFile> Files, int ChangedCount);

public sealed record PresetDto(
    string SchemaVersion,
    string Id,
    string Name,
    PresetCompatibility Compatibility,
    IReadOnlyList<PresetAction> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PresetCompatibilitySummaryDto(
    bool Compatible,
    string? Code,
    string? Reason,
    bool ReadOnly,
    string PresetKind,
    string? SavedGameBuild,
    string? CurrentGameBuild,
    string? SavedFormatVariant,
    string? CurrentFormatVariant,
    string? SavedReferencePackIdentity,
    string? CurrentReferencePackIdentity,
    string? SavedSchemaFingerprint,
    string? CurrentSchemaFingerprint,
    string? SavedFileRole,
    string? CurrentFileRole,
    string? SavedCapabilitySchemaVersion,
    string? CurrentCapabilitySchemaVersion,
    IReadOnlyList<string> UnsupportedActions);

public sealed record DocumentPresetDto(PresetDto Preset, PresetCompatibilitySummaryDto Compatibility);

public sealed record CreatePresetRequest(
    string Name,
    PresetCompatibility Compatibility,
    IReadOnlyList<PresetAction> Actions);

public sealed record RenamePresetRequest(string Name);

public sealed record PresetPlanRequest(long? Revision = null, IReadOnlyDictionary<string, string>? Choices = null);

public sealed record CapturePresetRequest(
    string Name,
    string ActionId,
    Dictionary<string, JsonElement>? Parameters = null,
    string ScopeKind = "document",
    IReadOnlyList<string>? SelectedRowKeys = null,
    bool AllMatches = false);

public sealed record ApplyPresetRequest(string PlanId, long Revision);

public sealed record PresetPreviewDto(PresetDto Preset, ActionPreview Preview);
public sealed record PresetSemanticPreviewDto(PresetDto Preset, PresetActionPreview Preview);
