using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class SessionIntegrationTests
{
    [Fact]
    public void FrameworkCanUndoAndRedoDerivedClockValues()
    {
        using var document = EditingTests.Gameplay();
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var session = new DocumentSession("clock", file, new PragmataPlugin(), document);
        _ = session.BuildView();
        var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
        var old = document.Read(playtime).AsUInt64();
        var result = session.ApplyEdits([new IncomingEdit(playtime.Path, JsonSerializer.SerializeToElement((old + 1).ToString(CultureInfo.InvariantCulture)))]);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Changes.Count);
        var modified = session.Serialize().Single().Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void FrameworkCanUndoAndRedoBulkInventoryAction()
    {
        using var document = EditingTests.Gameplay();
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var session = new DocumentSession("inventory", file, new PragmataPlugin(), document);
        _ = session.BuildView();
        var preview = session.PlanAction(Views.PragmataViews.QuantityAction,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("17") });
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        var result = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(result.Ok);
        Assert.True(session.State.CanUndo);
        var modified = session.Serialize().Single().Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void FrameworkCanUndoAndRedoAnEditExposedThroughBothViews()
    {
        using var document = EditingTests.Gameplay();
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        using var session = new DocumentSession("raw", file, new PragmataPlugin(), document);
        var view = (TabsNode)session.BuildView();
        var target = EditingTests.Quantity(document);
        IReadOnlyList<ViewNode> nodes = view.Tabs.Single(t => t.Title == "Raw").Children;
        var ancestors = new Stack<SaveNode>();
        for (var node = target.Parent; node is not null; node = node.Parent) ancestors.Push(node);
        foreach (var ancestor in ancestors)
        {
            var section = nodes.OfType<SectionNode>().Single(s => s.LazyId!.StartsWith($"tree/{ancestor.Path}/", StringComparison.Ordinal));
            nodes = session.Expand(section.LazyId!);
        }
        var raw = nodes.OfType<FieldNode>().Single(f => f.ValueId == target.Path);
        var result = session.ApplyEdits([new IncomingEdit(raw.ValueId, JsonSerializer.SerializeToElement("9999"))]);
        Assert.True(result.Ok);
        Assert.Single(result.Changes);
        var modified = session.Serialize().Single().Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
    }
}
