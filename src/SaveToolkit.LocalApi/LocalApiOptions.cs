using SaveToolkit.Core.Plugins;

namespace SaveToolkit.LocalApi;

/// <summary>Configuration of an in-process API host; defaults mirror the ASP.NET host's <c>Toolkit</c> options.</summary>
public sealed class LocalApiOptions
{
    /// <summary>Root directory for workspaces, presets and reference packs (MEMFS in the browser).</summary>
    public required string DataRoot { get; init; }

    /// <summary>Plugins to serve; null discovers every loaded plugin assembly.</summary>
    public PluginCatalog? Catalog { get; init; }

    /// <summary>Reported by <c>GET /api/version</c>.</summary>
    public string Version { get; init; } = "0.0.0-local";

    /// <summary>Imported workspace blobs. Wiped when the host starts. Default: <c>{DataRoot}/workspaces</c>.</summary>
    public string? WorkspaceRoot { get; init; }

    /// <summary>User-imported reference packs. Default: <c>{DataRoot}/reference-packs</c>.</summary>
    public string? PackRoot { get; init; }

    /// <summary>Saved presets. Default: <c>{DataRoot}/presets</c>.</summary>
    public string? PresetRoot { get; init; }

    /// <summary>Comma-separated reference-pack locale preferences; order and duplicates are kept.</summary>
    public string Locales { get; init; } = "en";

    /// <summary>Automatic expiry of clean, idle workspaces (dirty edits are retained).</summary>
    public TimeSpan WorkspaceIdleTimeout { get; init; } = TimeSpan.FromHours(6);

    /// <summary>How often idle workspaces are swept; null disables the background sweep.</summary>
    public TimeSpan? SweepInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Clock for the background sweep.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Receives unexpected (500) request failures and sweep cleanup failures, e.g. for console logging.</summary>
    public Action<string, Exception>? OnError { get; init; }

    internal string ResolvedWorkspaceRoot => Resolve(WorkspaceRoot, "workspaces");

    internal string ResolvedPackRoot => Resolve(PackRoot, "reference-packs");

    internal string ResolvedPresetRoot => Resolve(PresetRoot, "presets");

    private string Resolve(string? configured, string child)
    {
        if (string.IsNullOrWhiteSpace(DataRoot)) throw new ArgumentException("DataRoot must be a valid, non-whitespace directory path.");
        return string.IsNullOrEmpty(configured) ? Path.Combine(DataRoot, child) : configured;
    }
}
