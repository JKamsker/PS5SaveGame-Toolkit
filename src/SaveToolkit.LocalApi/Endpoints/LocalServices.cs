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
    PresetStore Presets,
    PresetTransferService Transfers,
    string Version);

/// <summary>A route-bound workspace retained by a lease for the rest of the request.</summary>
internal sealed record WorkspaceRef(Workspace Value);

/// <summary>A route-bound open document and its workspace.</summary>
internal sealed record DocumentRef(Workspace Workspace, DocumentSession Value);
