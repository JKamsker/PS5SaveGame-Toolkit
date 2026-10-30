using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Composes the landed ownership provider with repeated-scalar session operations.</summary>
internal sealed class ScalarOutputFixture : IDisposable
{
    public const string Root = "PPSA99997/sdimg_SAVE/";
    public const string PrimaryPath = Root + "slot.dat";
    public const string SystemPath = Root + "sce_sys/param.sfo";
    private readonly WorkspaceStore _store;
    public SystemOutputOwnershipTests.OutputPlugin Plugin { get; } = new(true, true);
    public Workspace Workspace { get; }
    public DocumentSession Session { get; }

    public ScalarOutputFixture()
    {
        _store = new WorkspaceStore(new PluginCatalog([Plugin]), new() { DataRoot = TestZips.TempDir() });
        Workspace = _store.Create("scalar ownership", [new ImportSource("fixture.zip", () => new MemoryStream(
            TestZips.Build((PrimaryPath, [1]), (SystemPath, SystemOutputOwnershipTests.CreateSfo("Old")),
                (Root + "sibling.dat", [1]))))]);
        Session = Workspace.Open(Workspace.Set.AllDiskFiles.Single(file => file.FileName == "slot.dat").Id, Plugin.Info.Id);
    }

    public SessionEditResult Edit(int value) => Session.ApplyEdits([Incoming(value)]);
    public static IncomingEdit Incoming(int value) => new("value", JsonSerializer.SerializeToElement(value));
    public ISaveFile Source(string path) => ((ISaveSet)Workspace.Set).FindAnyFile(path)!;

    public Dictionary<string, ReadOnlyMemory<byte>> Verify(DocumentOutputCapture capture)
    {
        var outputs = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        DocumentOutputVerifier.AddOutputs(Workspace, Session, capture, outputs, CancellationToken.None);
        DocumentOutputVerifier.Verify(Workspace, [Session], outputs, CancellationToken.None);
        return outputs;
    }

    public void AssertDirtyBytes(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs, byte value)
    {
        Assert.Equal(2, outputs.Count);
        Assert.Equal(new[] { value }, outputs[PrimaryPath].ToArray());
        Assert.Equal(SystemOutputOwnershipTests.CreateSfo("New"), outputs[SystemPath].ToArray());
    }

    public void Dispose() => _store.Dispose();
}
