using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

internal sealed class JsonSessionFixture : IDisposable
{
    private readonly string _storage;
    private bool _disposed;

    public JsonSessionFixture(string source, DocumentSessionOptions? options = null, string title = "PPSA00000", string fileName = "data.json", bool useReferences = false,
        string? storageDirectory = null)
    {
        _storage = storageDirectory ?? Path.Combine(TestScratch.ProcessFolder, "fixture-" + Guid.NewGuid().ToString("N"));
        ISaveDocument? document = null;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            var set = SaveSetImporter.Import([new ImportSource($"{title}/sdimg_fixture/{fileName}", () => new MemoryStream(bytes))], _storage);
            var file = set.AllDiskFiles.Single();
            var plugin = new GenericJsonPlugin();
            var references = useReferences ? new ReferencePackStore([plugin]).Pin(plugin, title) : ReferenceContext.None;
            Document = document = plugin.Open(file, references);
            Session = new DocumentSession("fixture", file, plugin, Document, options, references: references);
        }
        catch
        {
            try { document?.Dispose(); }
            finally { DeleteStorage(); }
            throw;
        }
    }

    public ISaveDocument Document { get; }
    public DocumentSession Session { get; }
    public byte[] Bytes => Document.Serialize()[0].Content.ToArray();
    public SessionEditResult Edit(string id, FieldValue value, long? revision = null) =>
        Session.ApplyEdits([new IncomingEdit(id, JsonSerializer.SerializeToElement(value))], revision ?? Session.State.Revision);

    public IReadOnlyList<FieldNode> Fields() => JsonTestViews.Fields(Session.BuildView(), id => Session.Expand(id));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Session.Dispose(); }
        finally { DeleteStorage(); }
    }

    private void DeleteStorage()
    {
        if (Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
    }
}
