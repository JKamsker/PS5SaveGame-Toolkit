using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class GroupExportCancellationTests
{
    [Fact]
    public void Fingerprint_hash_stops_after_cancelled_read()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingReadStream(new byte[1024 * 1024], cancellation);

        Assert.ThrowsAny<OperationCanceledException>(() => OperationCancellation.HashSha256(stream, cancellation.Token));

        Assert.Equal(1, stream.Reads);
        Assert.True(stream.Position < stream.Length);
    }

    [Fact]
    public void Fingerprint_hash_preserves_existing_sha256_format()
    {
        byte[] bytes = [1, 2, 3];
        using var stream = new MemoryStream(bytes);

        Assert.Equal(WorkspaceExporter.Sha256(bytes), OperationCancellation.HashSha256(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_group_verification_keeps_edits_and_releases_locks()
    {
        using var cancellation = new CancellationTokenSource();
        var plugin = new CallbackGroupPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var archive = TestZips.Build(
            ("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10)),
            ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20)));
        var workspace = store.Create("group", [new ImportSource("dump.zip", () => new MemoryStream(archive))]);
        var file = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, plugin.Info.Id)).Token);
        _ = group.Session.BuildView();
        Assert.True(group.Session.ApplyEdits([new IncomingEdit("character/balance", JsonSerializer.SerializeToElement("11"))]).Ok);
        var state = group.Session.State;
        plugin.OnValidate = cancellation.Cancel;

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExporter.Plan(workspace, new(), cancellation.Token));

        plugin.OnValidate = null;
        Assert.Equal(state, group.Session.State);
        var retry = await TestWorker.Run(() => WorkspaceExporter.Plan(workspace, new()), TestContext.Current.CancellationToken);
        Assert.Equal(2, retry.Overrides.Count);
        Assert.True(group.Session.Undo().Ok);
        Assert.False(group.Session.State.IsDirty);
    }

    private sealed class CallbackGroupPlugin : IGamePlugin, IDocumentGroupPlugin
    {
        private readonly GroupPlugin _inner = new();
        public Action? OnValidate { get; set; }
        public PluginInfo Info => _inner.Info;
        public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
        public ISaveDocument Open(ISaveFile file) => _inner.Open(file);
        public ISaveDocument Open(ISaveFile file, IReferenceContext references) => _inner.Open(file, references);
        public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => _inner.ProposeGroups(anchor);
        public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references) => _inner.OpenGroup(input, references);
        public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references)
        {
            OnValidate?.Invoke();
            return _inner.ValidateGroup(input, references);
        }
    }

    private sealed class CancellingReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }
        public override int Read(Span<byte> buffer)
        {
            Reads++;
            var count = base.Read(buffer);
            cancellation.Cancel();
            return count;
        }
    }
}
