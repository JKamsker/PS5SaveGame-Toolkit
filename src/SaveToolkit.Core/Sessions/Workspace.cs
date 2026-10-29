using System.Collections.Concurrent;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>An imported save set plus the documents opened from it.</summary>
public sealed class Workspace : IDisposable
{
    private readonly ConcurrentDictionary<string, DocumentSession> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ProbeMatch>> _probes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ReferenceContext> _references = new(StringComparer.Ordinal);
    private readonly PluginCatalog _catalog;
    private readonly ReferencePackStore? _packs;
    private readonly Lock _openGate = new();
    private readonly DocumentGroupRegistry _groups;
    private int _documentCounter;
    private bool _disposed;
    private long _lastAccessTicks;

    public Workspace(string id, string name, DiskSaveSet set, PluginCatalog catalog, ReferencePackStore? packs = null)
    {
        Id = id;
        Name = name;
        Set = set;
        _catalog = catalog;
        _packs = packs;
        _groups = new DocumentGroupRegistry(this, catalog, packs, _openGate);
        CreatedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public string Id { get; }
    public string Name { get; }
    public DiskSaveSet Set { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastAccess => new(Interlocked.Read(ref _lastAccessTicks), TimeSpan.Zero);

    public IReadOnlyCollection<DocumentSession> Documents => [.. _documents.Values];

    public IReadOnlyList<DocumentGroupSession> DocumentGroups => _groups.All;

    internal T WithOwnershipLock<T>(Func<T> operation)
    {
        lock (_openGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return operation();
        }
    }

    public IReadOnlyList<DocumentGroupOffer> ProposeGroups(string fileId, string pluginId) =>
        WithOwnershipLock(() => _groups.Propose(fileId, pluginId));

    public DocumentGroupSession OpenGroup(string proposalToken, string? releaseCleanDocumentId = null, DocumentSessionOptions? options = null)
    {
        lock (_openGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var group = _groups.Open(proposalToken, $"doc{Interlocked.Increment(ref _documentCounter)}", releaseCleanDocumentId, options);
            _documents[group.Session.Id] = group.Session;
            _references[group.Session.Id] = group.References;
            return group;
        }
    }

    internal bool ReleaseCleanDocument(string documentId)
    {
        if (!_documents.TryGetValue(documentId, out var document) || !document.TryCloseClean()) return false;
        _documents.TryRemove(documentId, out _);
        _references.TryRemove(documentId, out _);
        return true;
    }

    public void Touch() => Interlocked.Exchange(ref _lastAccessTicks, DateTimeOffset.UtcNow.Ticks);

    public IReadOnlyList<ProbeMatch> Probe(DiskSaveFile file) => _probes.GetOrAdd(file.Id, _ => _catalog.Probe(file));

    public DocumentSession? FindDocument(string documentId) => _documents.GetValueOrDefault(documentId);

    /// <summary>Reference packs pinned to an open document (empty when none apply).</summary>
    public ReferenceContext References(string documentId) => _references.GetValueOrDefault(documentId) ?? ReferenceContext.None;

    /// <summary>
    /// Opens <paramref name="fileId"/> with the given plugin (or the best probe match). Re-opening the same
    /// file with the same plugin returns the existing session; switching plugins requires the old one to be clean.
    /// </summary>
    public DocumentSession Open(string fileId, string? pluginId)
    {
        var file = Set.FindById(fileId) ?? throw new KeyNotFoundException($"Unknown file '{fileId}'.");
        var plugin = pluginId is null
            ? Probe(file).FirstOrDefault()?.Plugin ?? throw new SaveFormatException("No plugin can open this file.")
            : _catalog.Find(pluginId) ?? throw new KeyNotFoundException($"Unknown plugin '{pluginId}'.");

        lock (_openGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _groups.ThrowIfOwned(file.RelativePath);
            var existing = _documents.Values.FirstOrDefault(d => d.File.Id == fileId);
            if (existing is not null)
            {
                if (existing.Plugin == plugin)
                {
                    return existing;
                }

                if (!ReleaseCleanDocument(existing.Id))
                {
                    throw new InvalidOperationException("This file has unsaved edits in another plugin. Close that document first.");
                }
            }

            // Reference packs are pinned per document: packs imported later only affect documents opened later.
            var references = _packs?.Pin(plugin, file.Directory.TitleId) ?? ReferenceContext.None;
            var document = plugin.Open(file, references);
            var session = new DocumentSession($"doc{Interlocked.Increment(ref _documentCounter)}", file, plugin, document, references: references);
            _references[session.Id] = references;
            _documents[session.Id] = session;
            return session;
        }
    }

    public bool Close(string documentId)
    {
        lock (_openGate)
        {
            if (!_documents.TryRemove(documentId, out var session)) return false;
            _references.TryRemove(documentId, out _);
            try { session.Dispose(); }
            finally { _groups.Remove(documentId); }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_openGate)
        {
            if (_disposed) return;
            _disposed = true;
            SessionCleanup.RunAll(_documents.Keys, id => Close(id));
        }
    }
}
