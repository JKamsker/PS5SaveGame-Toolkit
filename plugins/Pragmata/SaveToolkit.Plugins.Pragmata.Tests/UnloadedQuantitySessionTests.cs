using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class UnloadedQuantitySessionTests
{
    private const int InventorySize = 251;
    private static readonly Dictionary<string, JsonElement> Parameters = new() { ["quantity"] = JsonSerializer.SerializeToElement("17") };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnloadedDocumentAndExplicitScopesPlanApplyReopenAndUndoRedoExactly(bool explicitSelection)
    {
        var original = SyntheticInventory.Create(InventorySize);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("unloaded-quantities", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView(); // F8 table only: no Inventory, Raw or evidence pages queried/expanded.
        var quantities = ImportedQuantityTests.Quantities(document);
        Assert.All(quantities, node => Assert.False(document.HasPrimaryBinding(node)));
        var selected = explicitSelection ? document.Layout.Items.Take(1).Concat(document.Layout.Items.TakeLast(1)).ToArray()
            : document.Layout.Items.ToArray();
        var scope = explicitSelection ? new ActionScope(selected.Select(QuantityActions.RowKey).ToArray()) : new ActionScope();
        var preview = session.PlanAction(PragmataViews.QuantityAction, Parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(selected.Length, preview.Plan.Changes.Count);
        Assert.All(preview.Plan.Changes, change => Assert.False(change.Derived));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var modified = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        var container = new DsssContainer(original);
        var expected = container.Plaintext.ToArray();
        foreach (var quantity in quantities.Where(node => preview.Plan.Changes.Any(change => change.ValueId == node.Path)))
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(quantity.Offset), 17);
        Assert.Equal(container.Rebuild(expected), modified);
        using var reopened = new PragmataDocument(fixture.File.RelativePath, modified);
        Assert.Equal(expected, new DsssContainer(reopened.Serialize().Single().Content.Span).Plaintext);
        Assert.Equal(modified, reopened.Serialize().Single().Content.ToArray());
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void DirectUnloadedEditValidatesBoundsAndUndoRedoUsesCanonicalBindings()
    {
        var original = SyntheticInventory.Create(InventorySize);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("unloaded-edit", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var quantity = ImportedQuantityTests.Quantities(document)[^1];
        Assert.False(document.HasPrimaryBinding(quantity));
        Assert.False(session.ApplyEdits([new(quantity.Path, JsonSerializer.SerializeToElement("2147483648"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.ApplyEdits([ImportedQuantitySessionTests.Edit(quantity.Path, 17)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var modified = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void EachChangedQuantityAppearsOnceInSemanticReview()
    {
        var original = SyntheticInventory.Create(InventorySize);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("canonical-review", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var scope = new ActionScope(document.Layout.Items.Skip(1).Select(QuantityActions.RowKey).ToArray());
        var preview = session.PlanAction(PragmataViews.QuantityAction, Parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var review = session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(review.Complete);
        Assert.Equal(250, review.Changes.Count);
        Assert.Equal(ImportedQuantityTests.Quantities(document).Skip(1).Select(node => node.Path).Order(StringComparer.Ordinal),
            review.Changes.Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.All(review.Changes, change => Assert.True(change.CanRevert));
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void UnloadedRestoreUsesImportedBaselineAndUndoRedoIsExact()
    {
        var original = ImportedQuantityTests.Create(Enumerable.Repeat(10, InventorySize).ToArray());
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("unloaded-restore", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var quantities = ImportedQuantityTests.Quantities(document);
        Assert.All(quantities, node => Assert.False(document.HasPrimaryBinding(node)));
        Assert.True(session.ApplyEdits(quantities.Select(node => ImportedQuantitySessionTests.Edit(node.Path, 1)).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        var preview = session.PlanAction(ImportedQuantityTests.ActionId, new Dictionary<string, JsonElement>(), new ActionScope(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(InventorySize, preview.Plan.Changes.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void RegisteredDerivedAliasesAreReportedButNeverReplayedOrDuplicatedInReview()
    {
        var original = SyntheticInventory.Create(InventorySize);
        using var fixture = new SyntheticSessionFile(original);
        using var document = new PragmataDocument(fixture.File.RelativePath, original);
        using var session = new DocumentSession("derived-history", fixture.File, new PragmataPlugin(), document);
        _ = session.BuildView();
        var quantity = ImportedQuantityTests.Quantities(document)[^1];
        string[] aliases = [$"raw/{quantity.Path}", $"inspect/value/{quantity.Path}"];
        var metadata = document.ResolveFieldMetadata(aliases);
        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, field =>
        {
            Assert.Equal(quantity.Path, field.CanonicalValueId);
            Assert.Equal(FieldBindingKind.Derived, field.BindingKind);
        });
        Assert.False(session.ApplyEdits([ImportedQuantitySessionTests.Edit(aliases[0], 17)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var scope = new ActionScope([QuantityActions.RowKey(document.Layout.Items[^1])]);
        var preview = session.PlanAction(PragmataViews.QuantityAction, Parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(quantity.Path, Assert.Single(preview.Plan.Changes, change => !change.Derived).ValueId);
        Assert.Equal(aliases, preview.Plan.Changes.Where(change => change.Derived).Select(change => change.ValueId));
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var modified = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        var review = session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(review.Complete);
        Assert.Equal(quantity.Path, Assert.Single(review.Changes).ValueId);
        Assert.True(session.Undo().Ok); // Replaying either derived alias would reject as read-only.
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
        Assert.All(aliases, id => Assert.Equal(FieldValue.Of(17), document.GetValue(id)));
    }
}
