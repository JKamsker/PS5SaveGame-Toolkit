using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>The singletons the ASP.NET host registers in DI, owned by one <see cref="LocalApiHost"/>.</summary>
internal sealed record LocalServices(
    PluginCatalog Catalog,
    WorkspaceStore Workspaces,
    ReferencePackStore Packs,
    PresetLibrary PresetLibrary,
    string Version)
{
    /// <summary>Created on first use; throws (like the Api's DI resolution) while the library is unreadable.</summary>
    public PresetStore Presets => PresetLibrary.Store;

    public PresetTransferService Transfers => PresetLibrary.Transfers;
}

/// <summary>A route-bound workspace retained by a lease for the rest of the request.</summary>
internal sealed record WorkspaceRef(Workspace Value);

/// <summary>A route-bound open document and its workspace.</summary>
internal sealed record DocumentRef(Workspace Workspace, DocumentSession Value);
