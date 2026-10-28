using System.Collections.Concurrent;
using System.Security.Cryptography;
using SaveToolkit.Core.Plugins;
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
    private readonly ConcurrentDictionary<string, Workspace> _workspaces = new(StringComparer.Ordinal);
    private readonly WorkspaceStoreOptions _options;

    public WorkspaceStore(PluginCatalog catalog, WorkspaceStoreOptions options)
    {
        Catalog = catalog;
        _options = options;
        if (Directory.Exists(options.DataRoot))
        {
            Directory.Delete(options.DataRoot, recursive: true);
        }

        Directory.CreateDirectory(options.DataRoot);
    }

    public PluginCatalog Catalog { get; }

    public IReadOnlyCollection<Workspace> All => [.. _workspaces.Values.OrderByDescending(w => w.CreatedAt)];

    public Workspace Create(string name, IEnumerable<ImportSource> sources)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var root = Path.Combine(_options.DataRoot, id);
        try
        {
            var set = SaveSetImporter.Import(sources, root, _options.Limits);
            var workspace = new Workspace(id, name, set, Catalog);
            _workspaces[id] = workspace;
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
        var ws = _workspaces.GetValueOrDefault(id);
        ws?.Touch();
        return ws;
    }

    public bool Remove(string id)
    {
        if (!_workspaces.TryRemove(id, out var ws))
        {
            return false;
        }

        ws.Dispose();
        TryDelete(ws.Set.StorageRoot);
        return true;
    }

    /// <summary>Drops workspaces idle longer than the configured timeout.</summary>
    public int Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - _options.IdleTimeout;
        var stale = _workspaces.Values.Where(w => w.LastAccess < cutoff).Select(w => w.Id).ToList();
        return stale.Count(Remove);
    }

    public void Dispose()
    {
        foreach (var id in _workspaces.Keys)
        {
            Remove(id);
        }
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
