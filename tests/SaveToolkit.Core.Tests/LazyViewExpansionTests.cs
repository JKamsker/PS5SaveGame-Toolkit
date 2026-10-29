using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class LazyViewExpansionTests : IDisposable
{
    private readonly NestedPlugin _plugin = new();
    private readonly WorkspaceStore _store;
    private readonly Workspace _workspace;

    public LazyViewExpansionTests()
    {
        _store = new(new PluginCatalog([_plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        _workspace = _store.Create("nested", [new ImportSource("data.bin", () => new MemoryStream([7, 1]))]);
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Inspection_RebindsNestedSectionsInDependencyOrder_IgnoringAbsentAndRepeatedIds()
    {
        var file = _workspace.Set.AllDiskFiles.Single();
        using var live = _plugin.Open(file);
        var result = InspectionActionRunner.Run(_plugin, file, live, IReferenceContext.Empty,
            ["child", "missing", "parent", "parent"], "inspect", new Dictionary<string, FieldValue>());

        Assert.Equal("7", result.Message);
        Assert.Equal(["parent", "child"], _plugin.LastOpened!.Expanded);
        Assert.Equal(new byte[] { 7, 1 }, live.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void Inspection_PropagatesExpansionFailure_AndDisposesOnlyItsCopy()
    {
        var file = _workspace.Set.AllDiskFiles.Single();
        using var live = _plugin.Open(file);
        _plugin.FailExpansion = true;

        var error = Assert.Throws<InvalidOperationException>(() => InspectionActionRunner.Run(
            _plugin, file, live, IReferenceContext.Empty, ["parent", "child"], "inspect", new Dictionary<string, FieldValue>()));

        Assert.Equal("child expansion failed", error.Message);
        Assert.True(_plugin.LastOpened!.Disposed);
        Assert.False(((NestedDocument)live).Disposed);
    }

    [Fact]
    public void StructuralUndo_RemembersDisappearedNestedSections_AndRebindsTheirFields()
    {
        var session = _workspace.Open(_workspace.Set.AllDiskFiles.Single().Id, _plugin.Info.Id);
        session.BuildView();
        session.Expand("parent");
        session.Expand("child");
        Assert.True(session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(9))]).Ok);
        Assert.True(session.InvokeAction("toggle", new Dictionary<string, JsonElement>()).Ok);
        Assert.True(session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(10))]).Stale);

        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok); // Replayed without asking the client to expand either section again.
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        Assert.Equal(9, session.Serialize().Single().Content.Span[0]);
        Assert.True(session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Ok);
    }

    private sealed class NestedPlugin : IGamePlugin
    {
        public bool FailExpansion { get; set; }
        public NestedDocument? LastOpened { get; private set; }
        public PluginInfo Info { get; } = new("test.nested", "Nested", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title);
        public ISaveDocument Open(ISaveFile file) => LastOpened = new(file.RelativePath, file.ReadAllBytes(), FailExpansion);
    }

    private sealed class NestedDocument(string path, byte[] bytes, bool failExpansion) : BoundSaveDocument
    {
        private int _value = bytes[0];
        private bool _visible = bytes[1] != 0;
        private bool _parentExpanded;
        public List<string> Expanded { get; } = [];
        public bool Disposed { get; private set; }
        public override string Title => "Nested";

        public override ViewNode BuildView()
        {
            _parentExpanded = false;
            return new SectionNode(Title, [
                .. _visible ? new ViewNode[] { SectionNode.Lazy("Parent", "parent") } : [],
                new ActionNode("toggle", "Toggle"),
            ]);
        }

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            Expanded.Add(lazyId);
            if (lazyId == "parent" && _visible)
            {
                _parentExpanded = true;
                return [SectionNode.Lazy("Child", "child")];
            }
            if (lazyId == "child" && _parentExpanded)
            {
                if (failExpansion) throw new InvalidOperationException("child expansion failed");
                return [Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(_value), v => _value = v.AsInt32()),
                    new ActionNode("inspect", "Inspect") { Mutates = false }];
            }
            return base.ExpandNode(lazyId);
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            actionId == "inspect" ? new() { Message = GetValue("value").ToString() } : Structural(() => _visible = !_visible);

        public override IReadOnlyList<FileOutput> Serialize() => [new(path, new byte[] { (byte)_value, _visible ? (byte)1 : (byte)0 })];
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_value, _visible), 8);
        protected override void LoadSnapshot(DocumentMemento snapshot) => (_value, _visible) = ((SnapshotMemento<(int, bool)>)snapshot).State;
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
