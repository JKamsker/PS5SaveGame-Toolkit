using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SessionOutputOwnershipTests
{
    [Theory]
    [InlineData("edit")]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("dispose")]
    public void BinaryResults_OwnTheirBytesAfterTheSessionLockIsReleased(string operation)
    {
        var plugin = new BufferPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("buffer ownership", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(Edit(session, 2).Ok);
        if (operation == "redo")
        {
            Assert.True(Edit(session, 3).Ok);
            Assert.True(session.Undo().Ok);
        }
        var output = Assert.Single(session.Serialize()).Content;
        var asset = Assert.IsType<AssetContent>(session.GetAsset("asset")).Content;
        var blob = session.ReadBlob("blob", 0, 1);

        switch (operation)
        {
            case "edit": Assert.True(Edit(session, 3).Ok); break;
            case "undo": Assert.True(session.Undo().Ok); break;
            case "redo": Assert.True(session.Redo().Ok); break;
            case "dispose": Assert.True(workspace.Close(session.Id)); break;
        }

        Assert.Equal(new byte[] { 2 }, output.ToArray());
        Assert.Equal(new byte[] { 2 }, asset.ToArray());
        Assert.Equal(new byte[] { 2 }, blob.ToArray());
    }

    private static SessionEditResult Edit(DocumentSession session, byte value) =>
        session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(value))]);

    private sealed class BufferPlugin : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.buffer", "Live buffer", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => new BufferDocument(file.RelativePath, file.ReadAllBytes());
    }

    // Returning live memory is permitted by the SDK; the session owns synchronization and lifetime.
    private sealed class BufferDocument(string path, byte[] bytes) : BoundSaveDocument
    {
        public override string Title => "Live buffer";
        public override ViewNode BuildView() => new SectionNode(Title,
        [
            Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(bytes[0]), v => bytes[0] = (byte)v.AsInt32()),
            new ImageNode("asset", "Live image"),
            new HexNode("blob", bytes.Length),
        ]);
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, bytes)];
        public override AssetContent? GetAsset(string assetId) => new("application/octet-stream", bytes);
        public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => bytes.AsMemory((int)offset, count);
        protected override void Dispose(bool disposing)
        {
            bytes.AsSpan().Clear();
            base.Dispose(disposing);
        }
    }
}
