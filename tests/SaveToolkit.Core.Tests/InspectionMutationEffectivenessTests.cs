using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class InspectionMutationEffectivenessTests
{
    private static readonly Dictionary<string, FieldValue> NoParameters = [];

    [Fact]
    public void SystemFileInspectionsCanReopenTheirReadOnlyOverlay()
    {
        const string path = "PPSA00001/sdimg_SAVE/sce_sys/icon0.png";
        var set = SaveSetImporter.Import([new ImportSource(path, () => new MemoryStream([1]))], TestZips.TempDir());
        var file = Assert.Single(set.AllDiskFiles);
        FileOutput[] outputs = [new(path, new byte[] { 1 })];
        using var live = new SnapshotDocument(outputs);
        var plugin = new InspectionPlugin(() => new SnapshotDocument(outputs));
        var result = InspectionActionRunner.Run(plugin, file, live, ReferenceContext.None, [], "inspect", NoParameters);
        Assert.Empty(result.Changes);
        Assert.Equal(new byte[] { 1 }, file.ReadAllBytes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySerializedFileMustMatchBeforeAndAfterInspection(bool corruptOnOpen)
    {
        using var fixture = new MutationSessionFixture(new SnapshotDocument([]));
        var path = fixture.Session.File.RelativePath;
        FileOutput[] outputs = [new(path, new byte[] { 1 }), new("second.dat", new byte[] { 2 })];
        using var live = new SnapshotDocument(outputs);
        SnapshotDocument? reopened = null;
        var plugin = new InspectionPlugin(() => reopened = new SnapshotDocument(
            corruptOnOpen ? [outputs[0], outputs[1] with { Content = new byte[] { 9 } }] : outputs)
            { CorruptOnAction = !corruptOnOpen });

        Assert.Throws<InvalidOperationException>(() => InspectionActionRunner.Run(plugin,
            fixture.Session.File, live, ReferenceContext.None, [], "inspect", NoParameters));
        Assert.Equal(new byte[] { 2 }, live.Serialize()[1].Content.ToArray());
        Assert.Equal(corruptOnOpen ? 0 : 1, reopened!.Actions);
    }

    [Fact]
    public void ReopeningMustNeverReturnOrDisposeTheLiveDocument()
    {
        using var fixture = new MutationSessionFixture(new SnapshotDocument([]));
        using var live = new SnapshotDocument([new(fixture.Session.File.RelativePath, new byte[] { 1 })]);
        var plugin = new InspectionPlugin(() => live);
        Assert.Throws<InvalidOperationException>(() => InspectionActionRunner.Run(plugin,
            fixture.Session.File, live, ReferenceContext.None, [], "inspect", NoParameters));
        Assert.Equal(0, live.Disposals);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SnapshotBudgetUsesTheInclusiveSumOfAllOutputsBeforeReopening(int excess, bool accepted)
    {
        const int halfBudget = 128 * 1024 * 1024;
        var buffer = new byte[halfBudget + excess];
        using var fixture = new MutationSessionFixture(new SnapshotDocument([]));
        FileOutput[] outputs = [new(fixture.Session.File.RelativePath, buffer.AsMemory(0, halfBudget)), new("second.dat", buffer)];
        using var live = new SnapshotDocument(outputs);
        var opens = 0;
        var plugin = new InspectionPlugin(() => { opens++; return new SnapshotDocument(outputs); });

        Action inspect = () => InspectionActionRunner.Run(plugin,
            fixture.Session.File, live, ReferenceContext.None, [], "inspect", NoParameters);
        if (accepted) inspect();
        else Assert.Throws<InvalidOperationException>(inspect);
        Assert.Equal(accepted ? 1 : 0, opens);
    }

    private sealed class InspectionPlugin(Func<ISaveDocument> open) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.inspection-mutation", "Inspection mutation", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => open();
    }

    private sealed class SnapshotDocument(IReadOnlyList<FileOutput> outputs) : BoundSaveDocument
    {
        private IReadOnlyList<FileOutput> _outputs = outputs;
        public bool CorruptOnAction { get; init; }
        public int Disposals { get; private set; }
        public int Actions { get; private set; }
        public override string Title => "Inspection snapshot";
        public override ViewNode BuildView() => new SectionNode(Title, []);
        public override IReadOnlyList<FileOutput> Serialize() => _outputs;
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            Actions++;
            if (CorruptOnAction) _outputs = [_outputs[0], _outputs[1] with { Content = new byte[] { 9 } }];
            return new();
        }
        protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
    }
}
