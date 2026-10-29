using System.Security.Cryptography;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Sessions;

public sealed record WorkspaceStoreOptions
{
    /// <summary>Folder for imported blobs; wiped on startup.</summary>
    public required string DataRoot { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromHours(6);

    public ImportLimits Limits { get; init; } = new();
}

/// <summary>In-memory registry of workspaces with on-disk blob storage. Single-user, local tool.</summary>
public sealed class WorkspaceStore : IDisposable
{
    private readonly Dictionary<string, Registration> _workspaces = new(StringComparer.Ordinal);
    private readonly WorkspaceStoreOptions _options;
    private readonly Lock _lifetimeGate = new();
    private bool _disposed;

    public WorkspaceStore(PluginCatalog catalog, WorkspaceStoreOptions options, ReferencePackStore? packs = null)
    {
        Catalog = catalog;
        Packs = packs ?? new ReferencePackStore(catalog.Plugins);
        _options = options;
        if (Directory.Exists(options.DataRoot))
        {
            Directory.Delete(options.DataRoot, recursive: true);
        }

        Directory.CreateDirectory(options.DataRoot);
    }

    public PluginCatalog Catalog { get; }

    /// <summary>Reference packs pinned to documents when they open.</summary>
    public ReferencePackStore Packs { get; }

    public IReadOnlyCollection<Workspace> All
    {
        get { lock (_lifetimeGate) return [.. _workspaces.Values.Select(r => r.Workspace).OrderByDescending(w => w.CreatedAt)]; }
    }

    public Workspace Create(string name, IEnumerable<ImportSource> sources)
    {
        lock (_lifetimeGate) ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var root = Path.Combine(_options.DataRoot, id);
        try
        {
            var set = SaveSetImporter.Import(sources, root, _options.Limits);
            var workspace = new Workspace(id, name, set, Catalog, Packs);
            lock (_lifetimeGate)
            {
                if (_disposed)
                {
                    workspace.Dispose();
                    throw new ObjectDisposedException(nameof(WorkspaceStore));
                }
                _workspaces[id] = new Registration(workspace);
            }
            return workspace;
        }
        catch
        {
            TryDelete(root);
            throw;
        }
    }

    public Workspace? Get(string id)
    {
        lock (_lifetimeGate)
        {
            var workspace = _workspaces.GetValueOrDefault(id)?.Workspace;
            workspace?.Touch();
            return workspace;
        }
    }

    /// <summary>Retains a workspace and its imported blobs until the operation or response finishes.</summary>
    public WorkspaceLease? Acquire(string id)
    {
        lock (_lifetimeGate)
        {
            if (!_workspaces.TryGetValue(id, out var registration)) return null;
            registration.Workspace.Touch();
            registration.Leases++;
            return new WorkspaceLease(registration.Workspace, () => Release(registration));
        }
    }

    public bool Remove(string id) => Remove(id, idleCutoff: null);

    private bool Remove(string id, DateTimeOffset? idleCutoff)
    {
        Registration registration;
        bool cleanup;
        lock (_lifetimeGate)
        {
            if (!_workspaces.TryGetValue(id, out registration!)) return false;
            if (idleCutoff is { } cutoff && (registration.Leases > 0 || registration.Workspace.LastAccess >= cutoff)) return false;
            _workspaces.Remove(id);
            registration.Removed = true;
            cleanup = registration.Leases == 0;
        }
        if (cleanup) Cleanup(registration.Workspace);
        return true;
    }

    /// <summary>Drops workspaces idle longer than the configured timeout.</summary>
    public int Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - _options.IdleTimeout;
        string[] ids;
        lock (_lifetimeGate) ids = [.. _workspaces.Keys];
        var removed = 0;
        SessionCleanup.RunAll(ids, id => { if (Remove(id, cutoff)) removed++; });
        return removed;
    }

    public void Dispose()
    {
        string[] ids;
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;
            ids = [.. _workspaces.Keys];
        }
        SessionCleanup.RunAll(ids, id => Remove(id));
    }

    private void Release(Registration registration)
    {
        bool cleanup;
        lock (_lifetimeGate)
        {
            registration.Leases--;
            registration.Workspace.Touch();
            cleanup = registration.Removed && registration.Leases == 0;
        }
        if (cleanup) Cleanup(registration.Workspace);
    }

    private static void Cleanup(Workspace workspace)
    {
        try { workspace.Dispose(); }
        finally { TryDelete(workspace.Set.StorageRoot); }
    }

    private sealed class Registration(Workspace workspace)
    {
        public Workspace Workspace { get; } = workspace;
        public int Leases { get; set; }
        public bool Removed { get; set; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; the folder is wiped on next start anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
