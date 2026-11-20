using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Export;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Export request mapping and the download name of an exported workspace.</summary>
internal static class ExportNaming
{
    public const string NothingChangedMessage = "Nothing has been changed yet.";

    public static ExportOptions ToOptions(ExportRequest request) => new() { Scope = request.Scope, MirrorBackups = request.MirrorBackups };

    /// <summary>True when the export would contain no edits and the caller did not ask for everything.</summary>
    public static bool NothingToExport(ExportPlan plan, ExportRequest request) =>
        plan.Overrides.Count == 0 && request.Scope != ExportScope.Everything;

    public static string DownloadName(string workspaceName) => $"{SafeName(workspaceName)}-edited.zip";

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
