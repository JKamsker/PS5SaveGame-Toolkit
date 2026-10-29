using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QuantityBoundaryTests
{
    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, int.MinValue)]
    [InlineData(int.MaxValue, -1)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void SelectedBoundaryTargetsReportEveryChangePreserveOtherBytesAndUndoAliases(int target, int negativeSource)
    {
        var container = new DsssContainer(SyntheticInventory.Create(4));
        var plaintext = container.Plaintext.ToArray();
        var layout = new SaveLayout(plaintext);
        var sources = new[] { target, negativeSource, target == 0 ? int.MaxValue : 0, 3 };
        for (var index = 0; index < sources.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(layout.Items[index].Find(KnownHashes.Quantity)!.Offset), sources[index]);
        var original = container.Rebuild(plaintext);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("quantity-boundaries", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var quantities = document.Layout.Items.Select(item => item.Find(KnownHashes.Quantity)!).ToArray();
        var raw = ExpandRaw(session, quantities[1]);
        var scope = new ActionScope(document.Layout.Items.Take(3).Select(item => $"inventory/record-at-{item.Offset:x}").ToArray());
        var parameters = Parameters(target);
        var preview = session.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        var skipped = Assert.Single(preview.Plan.SkippedRows);
        Assert.Equal(scope.RowKeys![0], skipped.RowKey);
        Assert.Contains("already equals", skipped.Reason, StringComparison.Ordinal);
        var expected = new[]
        {
            (Id: quantities[1].Path, Old: negativeSource, New: target, Derived: false),
            (Id: quantities[2].Path, Old: sources[2], New: target, Derived: false),
            (Id: raw.ValueId, Old: negativeSource, New: target, Derived: true),
        }.OrderBy(change => change.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, preview.Plan.Changes.Select(change =>
            (change.ValueId, change.OldValue.AsInt32(), change.NewValue.AsInt32(), change.Derived))
            .OrderBy(change => change.ValueId, StringComparer.Ordinal));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.CanUndo);
        var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok);
        Assert.Equal(expected.Select(change => (change.Id, change.Old, change.New)), applied.Changes.Select(change =>
            (change.ValueId, change.OldValue.AsInt32(), change.NewValue.AsInt32())).OrderBy(change => change.ValueId, StringComparer.Ordinal));
        Assert.NotEmpty(applied.Warnings);
        var expectedPlaintext = plaintext.ToArray();
        foreach (var index in new[] { 1, 2 })
            BinaryPrimitives.WriteInt32LittleEndian(expectedPlaintext.AsSpan(quantities[index].Offset), target);
        var modified = session.Serialize().Single().Content.ToArray();
        Assert.Equal(expectedPlaintext, new DsssContainer(modified).Plaintext);
        Assert.Equal(container.Rebuild(expectedPlaintext), modified);
        Assert.Equal(target, document.GetValue(raw.ValueId).AsInt32());
        using var reopened = new PragmataDocument(fixture.File.RelativePath, modified);
        Assert.Equal(new[] { target, target, target, sources[3] }, reopened.Layout.Items.Select(item =>
            reopened.Read(item.Find(KnownHashes.Quantity)!).AsInt32()));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(negativeSource, document.GetValue(raw.ValueId).AsInt32());
        Assert.Empty(session.Serialize());
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    public void EmptyInventoryEmptySelectionAndAlreadyEqualRowsRemainByteIdentical(int count, bool emptySelection)
    {
        var original = SyntheticInventory.Create(count);
        using var document = new PragmataDocument("data000.bin", original);
        _ = document.BuildView();
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(0L) };
        var scope = emptySelection ? new ActionScope([]) : new ActionScope();
        var plan = document.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(count == 1 ? 1 : 0, plan.SkippedRows.Count);
        if (count == 1)
            Assert.Equal($"inventory/record-at-{document.Layout.Items[0].Offset:x}", plan.SkippedRows[0].RowKey);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        var outcome = document.ApplyActionPlan(PragmataViews.QuantityAction, parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MissingExtraOrWrongKindParametersRejectWithoutChangingSyntheticBytes()
    {
        var original = SyntheticInventory.Create(2);
        using var document = new PragmataDocument("data000.bin", original);
        _ = document.BuildView();
        IReadOnlyDictionary<string, FieldValue>[] invalid =
        [
            new Dictionary<string, FieldValue>(),
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1L), ["extra"] = FieldValue.Of(1L) },
            new Dictionary<string, FieldValue> { ["other"] = FieldValue.Of(1L) },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1.0) },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Null },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(-1L) },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of((long)int.MaxValue + 1) },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of("17") },
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(true) },
        ];
        foreach (var parameters in invalid)
        {
            var scope = new ActionScope();
            var plan = document.PlanAction(PragmataViews.QuantityAction, parameters, scope);
            Assert.NotEmpty(plan.Errors);
            Assert.Empty(plan.Changes);
            Assert.False(document.ApplyActionPlan(PragmataViews.QuantityAction, parameters, scope, plan).Succeeded);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        }
    }

    private static Dictionary<string, JsonElement> Parameters(int target) => new()
        { ["quantity"] = JsonSerializer.SerializeToElement(target.ToString(CultureInfo.InvariantCulture)) };

    private static FieldNode ExpandRaw(DocumentSession session, SaveNode target)
    {
        IReadOnlyList<ViewNode> nodes = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Raw").Children;
        var ancestors = new Stack<SaveNode>();
        for (var node = target.Parent; node is not null; node = node.Parent) ancestors.Push(node);
        foreach (var ancestor in ancestors)
            nodes = session.Expand(nodes.OfType<SectionNode>().Single(section =>
                section.LazyId!.StartsWith($"tree/{ancestor.Path}/", StringComparison.Ordinal)).LazyId!);
        return nodes.OfType<FieldNode>().Single(field => field.ValueId == $"raw/{target.Path}");
    }
}
