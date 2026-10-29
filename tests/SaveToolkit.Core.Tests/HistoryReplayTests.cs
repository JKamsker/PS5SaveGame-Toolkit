using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class HistoryReplayTests : IDisposable
{
    private readonly ReplayPlugin _plugin = new();
    private readonly WorkspaceStore _store;
    private readonly DocumentSession _session;

    public HistoryReplayTests()
    {
        _store = new(new PluginCatalog([_plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var workspace = _store.Create("replay", [new ImportSource("data.bin", () => new MemoryStream([0, 0]))]);
        _session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, _plugin.Info.Id);
        _session.BuildView();
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public void MixedGroup_ReplaysInOrder_AndSwapsStructuralTokensForRepeatedUndoRedo()
    {
        CreateGroup();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _plugin.Live.Calls.Clear();
            var undo = _session.Undo();
            Assert.True(undo.Ok);
            Assert.Equal(["edit:1", "restore:0:1", "edit:0"], _plugin.Live.Calls);
            Assert.Equal(["value", "value"], undo.Changes.Select(c => c.ValueId));
            Assert.Equal(["assigned 1", "assigned 0"], undo.Warnings);
            Assert.True(undo.StructureChanged);
            Assert.False(undo.State.IsDirty);
            Assert.True(undo.State.CanRedo);
            Assert.Equal(new byte[] { 0, 0 }, _plugin.Live.Bytes);

            _plugin.Live.Calls.Clear();
            var redo = _session.Redo();
            Assert.True(redo.Ok);
            Assert.Equal(["edit:1", "restore:1:1", "edit:2"], _plugin.Live.Calls);
            Assert.Equal(["assigned 1", "assigned 2"], redo.Warnings);
            Assert.True(redo.State.CanUndo);
            Assert.False(redo.State.CanRedo);
            Assert.Equal(new byte[] { 1, 2 }, _plugin.Live.Bytes);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedReplay_CompensatesCompletedSteps_AndKeepsHistoryUnlessCompensationFails(bool redo, bool failCompensation)
    {
        CreateGroup();
        if (redo) Assert.True(_session.Undo().Ok);
        var before = _session.State;
        var bytes = _plugin.Live.Bytes;
        _plugin.Live.Calls.Clear();
        _plugin.Live.Failures.Add(redo ? "restore:1:1" : "restore:0:1");
        if (failCompensation) _plugin.Live.Failures.Add(redo ? "edit:0" : "edit:2");

        var result = redo ? _session.Redo() : _session.Undo();

        Assert.False(result.Ok);
        string[] expectedCalls = redo
            ? ["edit:1", "restore:1:1", "edit:0"]
            : ["edit:1", "restore:0:1", "edit:2"];
        Assert.Equal(expectedCalls, _plugin.Live.Calls);
        Assert.Equal("Plugin error: restore rejected", Assert.Single(result.Errors).Message);
        Assert.Empty(result.Changes);
        Assert.True(result.StructureChanged);
        Assert.Equal(before.Revision + 1, result.State.Revision);
        if (failCompensation)
        {
            Assert.False(result.State.CanUndo);
            Assert.False(result.State.CanRedo);
            Assert.True(result.State.IsDirty);
            Assert.Equal(["The step failed part-way and could not be fully reverted; undo history was cleared."], result.Warnings);
            Assert.Equal(new byte[] { redo ? (byte)0 : (byte)1, 1 }, _plugin.Live.Bytes);
        }
        else
        {
            Assert.Equal(bytes, _plugin.Live.Bytes);
            Assert.Equal(before.CanUndo, result.State.CanUndo);
            Assert.Equal(before.CanRedo, result.State.CanRedo);
            Assert.Empty(result.Warnings);
            _plugin.Live.Failures.Clear();
            Assert.True((redo ? _session.Redo() : _session.Undo()).Ok);
        }
    }

    private void CreateGroup()
    {
        var result = _session.RunGroup(session =>
        {
            Assert.True(Edit(1).Ok);
            Assert.True(session.InvokeAction("advance", new Dictionary<string, JsonElement>()).Ok);
            return Edit(2);
        });
        Assert.True(result.Ok);
    }

    private SessionEditResult Edit(int value) => _session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(value))]);

    private sealed class ReplayPlugin : IGamePlugin
    {
        public ReplayDocument Live { get; private set; } = null!;
        public PluginInfo Info { get; } = new("test.replay", "Replay", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title);
        public ISaveDocument Open(ISaveFile file)
        {
            var document = new ReplayDocument(file.RelativePath);
            Live ??= document; // Original-value review opens an independent document later.
            return document;
        }
    }

    private sealed class ReplayDocument(string path) : BoundSaveDocument
    {
        private int _phase;
        private int _value;
        public List<string> Calls { get; } = [];
        public HashSet<string> Failures { get; } = new(StringComparer.Ordinal);
        public byte[] Bytes => [(byte)_phase, (byte)_value];
        public override string Title => "Replay";
        public override ViewNode BuildView() => new SectionNode(Title, [
            Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(_value), v => _value = v.AsInt32()),
            new ActionNode("advance", "Advance"),
        ]);
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, Bytes)];

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            var call = $"edit:{edits.Single().Value.AsInt32()}";
            Calls.Add(call);
            return Failures.Contains(call) ? EditOutcome.Rejected("value", "edit rejected")
                : base.ApplyEdits(edits) with { Warnings = [$"assigned {edits.Single().Value.AsInt32()}"] };
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            Structural(() => _phase++);

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_phase, _value), 8);
        protected override void LoadSnapshot(DocumentMemento snapshot)
        {
            var state = ((SnapshotMemento<(int Phase, int Value)>)snapshot).State;
            (_phase, _value) = state;
        }

        public override DocumentMemento RestoreMemento(DocumentMemento memento)
        {
            var state = ((SnapshotMemento<(int Phase, int Value)>)memento).State;
            var call = $"restore:{state.Phase}:{state.Value}";
            Calls.Add(call);
            if (Failures.Contains(call)) throw new InvalidOperationException("restore rejected");
            return base.RestoreMemento(memento);
        }
    }
}
