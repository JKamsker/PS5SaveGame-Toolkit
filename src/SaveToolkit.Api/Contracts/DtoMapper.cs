using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Api.Contracts;

internal static class DtoMapper
{
    public static PluginDto ToDto(this IGamePlugin plugin) => new(
        plugin.Info.Id,
        plugin.Info.Name,
        plugin.Info.Version,
        plugin.Info.Description,
        plugin.Info.Maturity,
        plugin.Info.Games,
        plugin.Info.References) { SupportsGroups = plugin is IDocumentGroupPlugin };

    public static WorkspaceSummaryDto ToSummary(this Workspace ws) => new(
        ws.Id,
        ws.Name,
        ws.CreatedAt,
        ws.Set.Directories.Count,
        ws.Set.Directories.Sum(d => d.Files.Count),
        [.. ws.Set.Directories.Select(d => d.TitleId).Distinct(StringComparer.OrdinalIgnoreCase)]);

    public static WorkspaceDto ToDto(this Workspace ws, PluginCatalog catalog)
    {
        var openByFile = ws.Documents.ToDictionary(d => d.File.Id, d => d.Id, StringComparer.Ordinal);
        foreach (var group in ws.DocumentGroups)
            foreach (var member in group.Members) openByFile[member.FileId] = group.Session.Id;
        var titles = ws.Set.Directories
            .GroupBy(d => d.TitleId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TitleDto(
                g.Key,
                GameName(catalog, g.Key),
                [.. g.Cast<DiskSaveDirectory>().Select(d => ToDto(ws, d, openByFile))]))
            .ToList();
        return new WorkspaceDto(ws.Id, ws.Name, ws.CreatedAt, titles, [.. ws.Documents.Select(d =>
            d.ToSummary() with { GroupMembers = ws.DocumentGroups.FirstOrDefault(g => g.Session.Id == d.Id)?.Members })]);
    }

    public static DocumentSummaryDto ToSummary(this DocumentSession d) =>
        new(d.Id, d.File.Id, d.File.RelativePath, d.Title, d.Plugin.Info.Id, d.State.IsDirty);

    public static DocumentDto ToDto(this DocumentSession d, Workspace? workspace = null)
    {
        var snapshot = d.CaptureView();
        return new(d.Id, d.File.Id, d.File.RelativePath, snapshot.Title, d.Plugin.ToDto(), snapshot.CanEdit, snapshot.State, snapshot.View)
        {
            GroupMembers = workspace?.DocumentGroups.FirstOrDefault(g => g.Session.Id == d.Id)?.Members,
            PresetCapability = d.PresetCapability,
            TitleId = d.File.Directory.TitleId,
        };
    }

    public static PresetDto ToDto(this ToolkitPreset preset) => new(
        preset.SchemaVersion, preset.Id, preset.Name, preset.Compatibility, preset.Actions, preset.CreatedAt, preset.UpdatedAt);

    public static DocumentPresetDto ToDocumentDto(this ToolkitPreset preset, DocumentSession document)
    {
        var check = PresetStore.CheckCompatibility(preset, document);
        var capability = document.PresetCapability;
        var unsupported = capability is null
            ? []
            : preset.Actions.Where(action => capability.Actions.All(item => item.ActionId != action.ActionId
                || !item.SupportedScopes.Contains(action.ScopeKind, StringComparer.Ordinal)
                || !item.SupportsApply)).Select(action => action.ActionId).Distinct(StringComparer.Ordinal).ToArray();
        return new DocumentPresetDto(preset.ToDto(), new(
            check.Compatible,
            check.Code,
            check.Message,
            !document.CanEdit,
            preset.Compatibility.PresetKind,
            preset.Compatibility.GameBuild,
            capability?.GameBuild,
            preset.Compatibility.FormatVariant,
            capability?.FormatVariant,
            preset.Compatibility.ReferencePackIdentity,
            capability?.ReferencePackIdentity,
            preset.Compatibility.SchemaFingerprint,
            capability?.SchemaFingerprint,
            preset.Compatibility.FileRole,
            capability?.FileRole,
            preset.Compatibility.CapabilitySchemaVersion,
            capability?.SchemaVersion,
            unsupported));
    }

    private static DirectoryDto ToDto(Workspace ws, DiskSaveDirectory dir, Dictionary<string, string> openByFile)
    {
        var twin = ws.Set.Twin(dir);
        var identical = twin is not null
            && twin.Files.Count == dir.Files.Count
            && dir.Files.Cast<DiskSaveFile>().All(f => twin.Files.Cast<DiskSaveFile>().Any(t => t.PathInDirectory == f.PathInDirectory && t.Sha256 == f.Sha256));
        var sfo = dir.ParamSfo is { } p ? new SfoDto(p.MainTitle, p.SubTitle, p.Detail, p.SaveDataDirectory) : null;
        var files = dir.Files.Cast<DiskSaveFile>()
            .Select(f => new FileDto(
                f.Id,
                f.PathInDirectory,
                f.Length,
                [.. ws.Probe(f).Select(m => new PluginMatchDto(m.Plugin.Info.Id, m.Plugin.Info.Name, m.Result.Score, m.Result.Label, m.Plugin.Info.Maturity))],
                openByFile.GetValueOrDefault(f.Id)))
            .ToList();
        return new DirectoryDto(dir.Id, dir.Name, dir.RawName, dir.IsBackup, dir.Generation, twin?.Id, identical, Icon(dir) is not null, sfo, files);
    }

    public static ISaveFile? Icon(ISaveDirectory dir) =>
        dir.SystemFiles.FirstOrDefault(f => string.Equals(f.PathInDirectory, "sce_sys/icon0.png", StringComparison.OrdinalIgnoreCase));

    private static string? GameName(PluginCatalog catalog, string titleId) => catalog.Plugins
        .SelectMany(p => p.Info.Games)
        .FirstOrDefault(g => string.Equals(g.TitleId, titleId, StringComparison.OrdinalIgnoreCase))?.Name;
}
