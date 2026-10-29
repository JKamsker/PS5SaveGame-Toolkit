using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ActionPlansSessionTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    [Fact]
    public void HelperBindsUnopenedDerivedWrites_AndCoreUndoesAndRedoesOneGroup()
    {
        var plugin = new HelperPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/helper.dat", new byte[] { 1, 2 }));
        var workspace = store.Create("helpers", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        var document = plugin.LastOpened!;
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("a"));
        var empty = session.PlanAction("fill", NoParameters, new([]));
        Assert.Empty(empty.Plan.Changes);
        Assert.True(session.ApplyActionPlan(empty.PlanId!, empty.Revision).Ok);
        Assert.False(session.State.CanUndo);

        var preview = session.PlanAction("fill", NoParameters);
        Assert.NotNull(preview.PlanId);
        Assert.True(preview.Plan.Changes[1].Derived);
        Assert.Equal(new byte[] { 1, 2 }, document.Serialize()[0].Content.ToArray());
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.True(applied.Ok);
        Assert.Equal(new byte[] { 10, 20 }, document.Serialize()[0].Content.ToArray());
        Assert.True(session.State.CanUndo);
        Assert.True(session.Undo().Ok);
        Assert.Equal(new byte[] { 1, 2 }, document.Serialize()[0].Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(new byte[] { 10, 20 }, document.Serialize()[0].Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
    }

    private sealed class HelperPlugin : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.sdk-helper", "SDK helper fixture", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Fixture");
        public HelperDocument? LastOpened { get; private set; }
        public ISaveDocument Open(ISaveFile file) => LastOpened = new HelperDocument(file.RelativePath, file.ReadAllBytes());
    }

    private sealed class HelperDocument(string path, byte[] bytes) : BoundSaveDocument, IActionPlanningDocument
    {
        private const string ActionId = "fill";
        private static readonly string[] Keys = ["a", "b"];
        public override string Title => "Helper fixture";
        public override ViewNode BuildView() => new SectionNode("Fixture",
            [SectionNode.Lazy("Values", "values"), new ActionNode(ActionId, "Fill") { SupportsPlanning = true }]);
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "values" ? Bind() : base.ExpandNode(lazyId);
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, bytes.ToArray())];

        private FieldNode[] Bind() => Keys.Select((key, index) => Field(key, key, IntegerSpec.UInt8,
            () => FieldValue.Of((long)bytes[index]), value => bytes[index] = checked((byte)value.AsUInt32()))).ToArray();

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            if (actionId != ActionId || parameters.Count != 0 || scope.RowKeys?.Any(key => !Keys.Contains(key, StringComparer.Ordinal)) == true)
                return new() { Errors = [new(actionId, "Invalid fixture request")] };
            return new()
            {
                Changes = (scope.RowKeys ?? Keys).Select(key =>
                {
                    var index = Array.IndexOf(Keys, key);
                    return new PlannedChange(key, key, FieldValue.Of((long)bytes[index]), FieldValue.Of(index == 0 ? 10L : 20L))
                    { Derived = index == 1 };
                }).ToArray(),
                Warnings = ["Fixture planning warning"],
            };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
            ActionPlans.ApplyScalar(plan, () => PlanAction(actionId, parameters, scope), new(actionId, "Preview changed"),
                fresh => { _ = Bind(); }, fresh => fresh.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(),
                ApplyEdits, ActionPlanWarningPolicy.Merge);
    }
}
