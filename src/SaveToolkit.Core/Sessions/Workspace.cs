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
    private bool _idleRetired;
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
        => WithOwnershipLock(operation, CancellationToken.None);

    internal T WithOwnershipLock<T>(Func<T> operation, CancellationToken cancellationToken)
        => OperationCancellation.Locked(_openGate, () =>
        {
            ObjectDisposedException.ThrowIf(_disposed || _idleRetired, this);
            return operation();
        }, cancellationToken);

    public IReadOnlyList<DocumentGroupOffer> ProposeGroups(string fileId, string pluginId) =>
        WithOwnershipLock(() => _groups.Propose(fileId, pluginId));

    public DocumentGroupSession OpenGroup(string proposalToken, string? releaseCleanDocumentId = null, DocumentSessionOptions? options = null)
        => OpenGroup(proposalToken, out _, releaseCleanDocumentId, options);

    /// <summary>Prepares the initial response before publishing group ownership or releasing a clean member.</summary>
    public DocumentGroupSession OpenGroup(string proposalToken, out DocumentViewSnapshot view,
        string? releaseCleanDocumentId = null, DocumentSessionOptions? options = null)
    {
        lock (_openGate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _idleRetired, this);
            var group = _groups.Open(proposalToken, $"doc{Interlocked.Increment(ref _documentCounter)}", releaseCleanDocumentId, options, out view);
            _documents[group.Session.Id] = group.Session;
            _references[group.Session.Id] = group.References;
            return group;
        }
    }

    internal bool ReleaseCleanDocument(string documentId)
    {
        // Open, group replacement and CloseClean already hold the ownership gate.
        return _documents.TryGetValue(documentId, out var document) && document.TryCloseClean(() =>
        {
            _documents.TryRemove(documentId, out _);
            _references.TryRemove(documentId, out _);
            _groups.Remove(documentId);
        });
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
        => Open(fileId, pluginId, out _);

    /// <summary>Captures the initial response before publishing a candidate or replacing its clean predecessor.</summary>
    public DocumentSession Open(string fileId, string? pluginId, out DocumentViewSnapshot view)
    {
        var file = Set.FindById(fileId) ?? throw new KeyNotFoundException($"Unknown file '{fileId}'.");
        var plugin = pluginId is null
            ? Probe(file).FirstOrDefault()?.Plugin ?? throw new SaveFormatException("No plugin can open this file.")
            : _catalog.Find(pluginId) ?? throw new KeyNotFoundException($"Unknown plugin '{pluginId}'.");

        lock (_openGate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _idleRetired, this);
            _groups.ThrowIfOwned(file.RelativePath);
            var existing = _documents.Values.FirstOrDefault(d => d.File.Id == fileId);
            if (existing is not null)
            {
                if (existing.Plugin == plugin)
                {
                    view = existing.CaptureView();
                    return existing;
                }

                if (existing.State.IsDirty)
                {
                    throw new InvalidOperationException("This file has unsaved edits in another plugin. Close that document first.");
                }
            }

            // Reference packs are pinned per document: packs imported later only affect documents opened later.
            var references = _packs?.Pin(plugin, file.Directory.TitleId) ?? ReferenceContext.None;
            var document = plugin.Open(file, references);
            DocumentSession? session = null;
            try
            {
                session = new DocumentSession($"doc{Interlocked.Increment(ref _documentCounter)}", file, plugin, document, references: references);
                view = session.CaptureView();
                if (existing is not null && !ReleaseCleanDocument(existing.Id))
                    throw new InvalidOperationException("This file acquired unsaved edits while opening another plugin. No replacement was opened.");
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                SessionOpening.DisposeFailed(session is null ? document : session, failure);
                throw;
            }
            _references[session.Id] = references;
            _documents[session.Id] = session;
            return session;
        }
    }

    /// <summary>Closes only a clean document; concurrent committed edits retain all ownership and references.</summary>
    public bool CloseClean(string documentId)
    {
        // Match open/group replacement and idle retirement: ownership before session state.
        lock (_openGate)
        {
            if (!_documents.ContainsKey(documentId)) return false;
            try
            {
                if (ReleaseCleanDocument(documentId)) return true;
            }
            catch (Exception cleanup) when (cleanup is not (OutOfMemoryException or OperationCanceledException))
            {
                // TryCloseClean releases an already-disposed owner even if plugin disposal
                // fails. Do not report that committed close as a retained-state 409 conflict.
                throw new AggregateException("The clean document was closed, but cleanup failed.", cleanup);
            }
            throw new InvalidOperationException("This document has unsaved edits. Undo them before closing the document, or export your work and close the dump to discard them.");
        }
    }

    /// <summary>Explicitly discards a document, including dirty work. Used for workspace disposal, not HTTP clean close.</summary>
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

    /// <summary>Atomically retires a clean, idle workspace without calling plugin code.</summary>
    internal bool TryRetireClean(Func<bool> unregister)
    {
        // Lock order: workspace ownership -> all session gates -> store registration.
        // The store never waits for workspace/session locks while holding its gate.
        // Nonblocking attempts also skip reentrant/in-flight operations, even without a lease.
        if (_openGate.IsHeldByCurrentThread || !_openGate.TryEnter()) return false;
        DocumentSession[] sessions = [];
        var entered = 0;
        try
        {
            if (_idleRetired) return false;
            sessions = [.. _documents.Values];
            foreach (var session in sessions)
            {
                if (!session.TryEnterCleanState()) return false;
                entered++;
            }
            if (!unregister()) return false;
            _idleRetired = true;
            // Reject callers already holding raw session references before releasing gates.
            // Actual plugin disposal and blob deletion run outside the eviction decision.
            foreach (var session in sessions) session.RetireCleanState();
            return true;
        }
        finally
        {
            for (var index = entered - 1; index >= 0; index--) sessions[index].ExitCleanState();
            _openGate.Exit();
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
