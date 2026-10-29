using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Gives protocol test documents a real imported file without involving API hosting.</summary>
internal sealed class MutationSessionFixture : IDisposable
{
    public MutationSessionFixture(ISaveDocument document, DocumentSessionOptions? options = null,
        Func<ISaveDocument>? openOriginal = null)
    {
        var set = SaveSetImporter.Import(
            [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))], TestZips.TempDir());
        Session = new DocumentSession("mutation", set.AllDiskFiles.Single(), new CounterPlugin(), document,
            options, openOriginal);
    }

    public DocumentSession Session { get; }
    public void Dispose() => Session.Dispose();
}
