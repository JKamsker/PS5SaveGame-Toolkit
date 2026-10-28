using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

internal sealed class JsonSessionFixture : IDisposable
{
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "SaveToolkit.GenericJson.Tests", Guid.NewGuid().ToString("N"));

    public JsonSessionFixture(string source, DocumentSessionOptions? options = null)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var set = SaveSetImporter.Import([new ImportSource("PPSA00000/sdimg_fixture/data.json", () => new MemoryStream(bytes))], _storage);
        var file = set.AllDiskFiles.Single();
        var plugin = new GenericJsonPlugin();
        Document = plugin.Open(file);
        Session = new DocumentSession("fixture", file, plugin, Document, options);
    }

    public ISaveDocument Document { get; }
    public DocumentSession Session { get; }
    public byte[] Bytes => Document.Serialize()[0].Content.ToArray();
    public SessionEditResult Edit(string id, FieldValue value, long? revision = null) =>
        Session.ApplyEdits([new IncomingEdit(id, JsonSerializer.SerializeToElement(value))], revision ?? Session.State.Revision);

    public IReadOnlyList<FieldNode> Fields()
    {
        var fields = new List<FieldNode>();
        Visit(Session.BuildView());
        return fields;
        void Visit(ViewNode node)
        {
            switch (node)
            {
                case FieldNode field: fields.Add(field); break;
                case TabsNode tabs:
                    foreach (var child in tabs.Tabs.SelectMany(t => t.Children)) Visit(child);
                    break;
                case SectionNode section:
                    foreach (var child in section.LazyId is { } id ? Session.Expand(id) : section.Children) Visit(child);
                    break;
            }
        }
    }

    public void Dispose()
    {
        Session.Dispose();
        Directory.Delete(_storage, recursive: true);
    }
}
