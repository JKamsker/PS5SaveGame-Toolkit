using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Startup-only API configuration, mapped to the existing domain options.</summary>
internal sealed class ToolkitOptions
{
    internal const string SectionName = "Toolkit";
    private const string ApplicationDirectoryName = "SaveToolkit";
    private const string DefaultLocales = "en";

    public string DataRoot { get; set; } = DefaultDataRoot;
    public string PackRoot { get; set; } = DefaultPackRoot;
    /// <summary>Comma-separated preferences; parsing retains order and duplicates.</summary>
    public string Locales { get; set; } = DefaultLocales;

    private static string DefaultDataRoot => Path.Combine(Path.GetTempPath(), ApplicationDirectoryName, "workspaces");
    private static string DefaultPackRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationDirectoryName, "reference-packs");

    internal void ApplyDefaults()
    {
        // Empty settings historically select the same defaults as absent settings.
        if (string.IsNullOrEmpty(DataRoot)) DataRoot = DefaultDataRoot;
        if (string.IsNullOrEmpty(PackRoot)) PackRoot = DefaultPackRoot;
        if (string.IsNullOrEmpty(Locales)) Locales = DefaultLocales;
    }

    internal WorkspaceStoreOptions ToWorkspaceOptions() => new() { DataRoot = DataRoot };

    internal ReferencePackOptions ToPackOptions() => new()
    {
        StorageRoot = PackRoot,
        PreferredLocales = Locales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };
}
