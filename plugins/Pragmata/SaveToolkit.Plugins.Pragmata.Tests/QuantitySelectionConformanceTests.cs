using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QuantitySelectionConformanceTests
{
    private const int OpeningQuantity = 10;
    private const int ReducedQuantity = 1;
    private const int TargetQuantity = 17;
    // Traversing the 251-row fixture also expands each Raw record and its ancestors.
    private const int MaximumLazyExpansions = 300;

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(251, false)]
    [InlineData(251, true)]
    public void StagedSelectionHasOnlyKnownSelectedRowEffectsAndPureDeterministicPlans(int count, bool restore)
    {
        var original = ImportedQuantityTests.Create(Enumerable.Repeat(OpeningQuantity, count).ToArray());
        using var model = new PragmataDocument("data000.bin", original);
        var quantities = ImportedQuantityTests.Quantities(model);
        var rows = model.Layout.Items.Select(QuantityActions.RowKey).ToArray();
        var selected = new[] { 0, count - 1 };
        var fixture = new SelectionScopeCase(restore ? PragmataViews.RestoreQuantityAction : PragmataViews.QuantityAction,
            InventoryCollections.CollectionId, selected.Select(index => rows[index]).ToArray())
        {
            Parameters = restore ? new Dictionary<string, FieldValue>()
                : new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(TargetQuantity) },
            Stage = document =>
            {
                var ids = quantities.Select(node => node.Path).ToArray();
                foreach (var batch in ids.Chunk(FieldMetadataLimits.MaxBatchSize))
                    _ = ((IFieldMetadataDocument)document).ResolveFieldMetadata(batch);
                var staged = document.ApplyEdits(ids.Select(id => new ValueEdit(id, FieldValue.Of(ReducedQuantity))).ToArray());
                if (!staged.Succeeded) throw new InvalidOperationException("Selection fixture staging was rejected.");
            },
            // Model identities establish ownership of selected AND unselected quantities; cells do not.
            RowOwnedValueIds = rows.Select((row, index) => (row, id: quantities[index].Path))
                .ToDictionary(entry => entry.row, entry => (IReadOnlyList<string>)[entry.id], StringComparer.Ordinal),
            ExpectedChangedValueIds = selected.Select(index => quantities[index].Path).ToArray(),
        };
        var result = SelectionScopeFixture.Evaluate(() => new PragmataDocument("data000.bin", original), fixture,
            new SelectionScopeLimits { MaxLazyExpansions = MaximumLazyExpansions });
        switch (result.Status)
        {
            case SelectionScopeStatus.Passed:
                break;
            case SelectionScopeStatus.NotEvaluated:
                Assert.Fail("Selection case was not evaluated: " + string.Join("; ", result.Diagnostics));
                break;
            case SelectionScopeStatus.Failed:
                Assert.Fail("Selection case failed: " + string.Join("; ", result.Diagnostics));
                break;
            default:
                Assert.Fail("Unknown selection conformance status.");
                break;
        }
    }
}
