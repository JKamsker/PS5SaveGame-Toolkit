using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class TextCapacitySessionTests
{
    [Fact]
    public void EditsAndActionParametersAreValidatedBeforePluginMutationAndUndoPreservesBytes()
    {
        var plugin = new CounterPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        var workspace = store.Create("text", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        var file = workspace.Set.AllDiskFiles.Single();
        using var document = new CaptionDocument(file.RelativePath);
        using var session = new DocumentSession("text", file, plugin, document, openOriginal: () => new CaptionDocument(file.RelativePath));
        _ = session.BuildView();
        var original = document.Serialize().Single().Content.ToArray();
        Assert.Empty(session.Serialize());
        Assert.False(session.ApplyEdits([new IncomingEdit("caption", JsonSerializer.SerializeToElement("🎮a"))]).Ok);
        Assert.False(session.InvokeAction("direct", Parameters("🎮a")).Ok);
        Assert.Null(session.PlanAction("planned", Parameters("🎮a")).PlanId);
        Assert.Equal(0, document.ActionCalls);
        Assert.False(session.State.IsDirty);

        var preview = session.PlanAction("planned", Parameters("🎮"));
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal("🎮", Encoding.UTF8.GetString(session.Serialize().Single().Content.Span));
        Assert.Equal(1, document.ActionCalls);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize());
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal("🎮", Encoding.UTF8.GetString(session.Serialize().Single().Content.Span));
    }

    private static Dictionary<string, JsonElement> Parameters(string text) => new() { ["text"] = JsonSerializer.SerializeToElement(text) };

    private sealed class CaptionDocument(string path) : BoundSaveDocument, IActionPlanningDocument
    {
        private static readonly TextSpec Spec = new() { MaxUtf8Bytes = 4 };
        private string _caption = "old";
        public int ActionCalls { get; private set; }
        public override string Title => "Synthetic caption";
        public override bool CanEdit => true;
        public override ViewNode BuildView() => new SectionNode("Caption", [
            Field("caption", "Caption", Spec, () => FieldValue.Of(_caption), value => _caption = value.AsString()),
            new ActionNode("direct", "Direct") { Parameters = [Parameter()] },
            new ActionNode("planned", "Planned") { Parameters = [Parameter()], SupportsPlanning = true },
        ]);
        private static FieldNode Parameter() => new("text", "Text", Spec, FieldValue.Of("old"));
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = [new("caption", "Caption", FieldValue.Of(_caption), parameters["text"])] };
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            ActionCalls++;
            return ApplyEdits([new("caption", parameters["text"])]);
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            ActionCalls++;
            return new() { Changes = SetValues([new("caption", parameters["text"])]) };
        }
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, Encoding.UTF8.GetBytes(_caption))];
    }
}
