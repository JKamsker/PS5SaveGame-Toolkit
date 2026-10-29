using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ImportedQuantitySessionTests
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    [Fact]
    public void WholeQueryResolvesUnloadedBindingsHonorsExclusionsAndRestoresInOneUndoStep()
    {
        const int count = 251;
        var original = ImportedQuantityTests.Create(Enumerable.Repeat(10, count).ToArray());
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("restore-query", fixture.File, new PragmataPlugin(), document);
        var view = (TabsNode)session.BuildView();
        var quantities = ImportedQuantityTests.Quantities(document);
        var rawIds = quantities.Select(node => RawFieldNavigation.Find(view, node, session.Expand).ValueId).ToArray();
        Assert.True(session.ApplyEdits(rawIds.Select(id => Edit(id, 1)).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.All(quantities, quantity => Assert.True(document.HasPrimaryBinding(quantity)));
        var reduced = document.Serialize().Single().Content.ToArray();
        var excluded = QuantityActions.RowKey(document.Layout.Items[^1]);
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope(InventoryCollections.CollectionId,
                new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["quantity"] = "1" }, Limit = 7 },
                session.State.Revision) { ExcludedRowKeys = [excluded] },
        };
        Assert.False(session.InvokeAction(ImportedQuantityTests.ActionId, NoParameters, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var preview = session.PlanAction(ImportedQuantityTests.ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(count - 1, preview.Scope.RowKeys!.Count);
        Assert.DoesNotContain(excluded, preview.Scope.RowKeys);
        Assert.Equal(count - 1, preview.Plan.Changes.Count); // One canonical write per quantity.
        Assert.All(quantities, quantity => Assert.True(document.HasPrimaryBinding(quantity)));
        Assert.Equal(reduced, document.Serialize().Single().Content.ToArray()); // Cancel leaves bytes and history untouched.
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Redo().Ok);
        preview = session.PlanAction(ImportedQuantityTests.ActionId, NoParameters,
            scope with { QueryScope = scope.QueryScope! with { Revision = session.State.Revision } }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(1, document.Read(quantities[^1]).AsInt32());
        Assert.All(quantities.Take(count - 1), quantity => Assert.Equal(10, document.Read(quantity).AsInt32()));
        var restored = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        var expected = new DsssContainer(original).Plaintext.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(quantities[^1].Offset), 1);
        Assert.Equal(expected, new DsssContainer(restored).Plaintext);
        using var reopened = new PragmataDocument(fixture.File.RelativePath, restored);
        Assert.Equal(restored, reopened.Serialize().Single().Content.ToArray());
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray());
    }

    [Fact]
    public void ExplicitSelectionLeavesOtherReductionsUntouchedAndEmptySelectionRemainsANoOp()
    {
        var original = ImportedQuantityTests.Create(10, 20, 30);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("restore-selected", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var quantities = ImportedQuantityTests.Quantities(document);
        Assert.True(session.ApplyEdits(quantities.Select(node => Edit(node.Path, 1)).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var scope = new ActionScope([QuantityActions.RowKey(document.Layout.Items[1])]);
        var preview = session.PlanAction(ImportedQuantityTests.ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(20, Assert.Single(preview.Plan.Changes).NewValue.AsInt32());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        int[] expected = [1, 20, 1];
        Assert.Equal(expected, quantities.Select(node => document.Read(node).AsInt32()));
        var before = document.Serialize().Single().Content.ToArray();
        preview = session.PlanAction(ImportedQuantityTests.ActionId, NoParameters, new ActionScope([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok); // No-op application must not add a history entry.
        Assert.All(quantities, node => Assert.Equal(1, document.Read(node).AsInt32()));
    }

    internal static IncomingEdit Edit(string id, int value) =>
        new(id, JsonSerializer.SerializeToElement(value.ToString(CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoMatchesOrFullyExcludedQueryNeverFallsBackToDocumentScope(bool excludeAll)
    {
        var original = ImportedQuantityTests.Create(Enumerable.Repeat(10, 251).ToArray());
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("restore-empty-query", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope(InventoryCollections.CollectionId,
                new QueryableCollectionQuery { Text = excludeAll ? null : "no-such-item" }, session.State.Revision)
            {
                ExcludedRowKeys = excludeAll ? document.Layout.Items.Select(QuantityActions.RowKey).ToArray() : [],
            },
        };
        var preview = session.PlanAction(ImportedQuantityTests.ActionId, NoParameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Scope.RowKeys!);
        Assert.Empty(preview.Plan.Changes);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
