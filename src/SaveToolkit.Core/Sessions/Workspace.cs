using System.Collections.Concurrent;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
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
    private int _documentCounter;

    public Workspace(string id, string name, DiskSaveSet set, PluginCatalog catalog, ReferencePackStore? packs = null)
    {
        Id = id;
        Name = name;
        Set = set;
        _catalog = catalog;
        _packs = packs;
        CreatedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public string Id { get; }
    public string Name { get; }
    public DiskSaveSet Set { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastAccess { get; private set; }

    public IReadOnlyCollection<DocumentSession> Documents => [.. _documents.Values];

    public void Touch() => LastAccess = DateTimeOffset.UtcNow;

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
            var existing = _documents.Values.FirstOrDefault(d => d.File.Id == fileId);
            if (existing is not null)
            {
                if (existing.Plugin == plugin)
                {
                    return existing;
                }

                if (existing.State.IsDirty)
                {
                    throw new InvalidOperationException("This file has unsaved edits in another plugin. Close that document first.");
                }

                Close(existing.Id);
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
        if (!_documents.TryRemove(documentId, out var session))
        {
            return false;
        }

        _references.TryRemove(documentId, out _);
        session.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _documents.Keys)
        {
            Close(id);
        }
    }
}
