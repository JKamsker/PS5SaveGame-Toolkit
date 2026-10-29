using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataPagingTests
{
    private const int RowCount = 251;

    [Fact]
    public void PlanApplyUndoRedoAcrossUnqueriedPagesUsesBoundedMetadataAndExactBytes()
    {
        var plugin = new PlanningPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 2 }));
        var workspace = store.Create("metadata", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var document = new InventoryDocument();
        using var session = new DocumentSession("metadata", workspace.Set.AllDiskFiles.Single(), plugin, document,
            openOriginal: () => new InventoryDocument());
        _ = session.BuildView();
        var original = document.Serialize().Single().Content.ToArray();

        Assert.True(session.ApplyEdits([new IncomingEdit("item/250", JsonSerializer.SerializeToElement(5))]).Ok);
        Assert.True(session.Undo().Ok);
        var preview = session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.Equal(RowCount + 1, preview.Plan.Changes.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var changed = document.Serialize().Single().Content.ToArray();
        Assert.All(changed, value => Assert.Equal(7, value));
        Assert.Equal(RowCount * 7, document.GetValue("total").AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
        Assert.Contains(200, document.BatchSizes);
        Assert.All(document.BatchSizes, count => Assert.InRange(count, 1, 200));
        Assert.Equal(0, document.Queries);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unrequested")]
    [InlineData("writable-derived")]
    [InlineData("unbound")]
    public void MalformedDescriptorsAreRejected(string failure)
    {
        using var document = new InventoryDocument { Failure = failure };
        if (failure == "unbound") Assert.Throws<KeyNotFoundException>(() => new ViewIndex(document).Spec("item/0"));
        else Assert.Throws<InvalidOperationException>(() => new ViewIndex(document).Spec("item/0"));
        Assert.All(document.Serialize().Single().Content.ToArray(), value => Assert.Equal(2, value));
    }

    private sealed class InventoryDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument, IQueryableDocument
    {
        private readonly byte[] _values = Enumerable.Repeat((byte)2, RowCount).ToArray();
        private static readonly IntegerSpec QuantitySpec = new(0, 10);
        private static readonly QueryableColumn[] Columns = [new("quantity", "Quantity", QuantitySpec)];
        public List<int> BatchSizes { get; } = [];
        public string? Failure { get; init; }
        public int Queries { get; private set; }
        public override string Title => "Inventory";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", Title, Columns, RowCount)];
        public override ViewNode BuildView() => new SectionNode(Title,
            [new QueryableTableNode("items", Title, Columns, RowCount), new ActionNode("fill", "Fill") { SupportsPlanning = true }]);

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries++;
            throw new InvalidOperationException("Metadata discovery must not query a page.");
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            BatchSizes.Add(valueIds.Count);
            foreach (var id in valueIds.Where(_ => Failure != "unbound"))
            {
                if (id == "total")
                    RegisterField(new FieldMetadata(id, "Total", IntegerSpec.Int32 with { ReadOnly = true })
                        { BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of(_values.Sum(value => value)));
                else if (id.StartsWith("item/", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out var row)
                    && row >= 0 && row < RowCount)
                    RegisterField(new FieldMetadata(id, $"Quantity {row}", QuantitySpec),
                        () => FieldValue.Of(_values[row]), value => _values[row] = (byte)value.AsInt32());
            }
            var fields = base.ResolveFieldMetadata(valueIds);
            return Failure switch
            {
                "duplicate" => [fields[0], fields[0]],
                "unrequested" => [fields[0] with { ValueId = "other" }],
                "writable-derived" => [fields[0] with { BindingKind = FieldBindingKind.Derived }],
                "unbound" => [new FieldMetadata("item/0", "Missing binding", QuantitySpec)],
                _ => fields,
            };
        }

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new()
        {
            Changes = Enumerable.Range(0, RowCount).Select(row => new PlannedChange($"item/{row}", $"Quantity {row}",
                FieldValue.Of(_values[row]), FieldValue.Of(7))).Append(new PlannedChange("total", "Total",
                    FieldValue.Of(_values.Sum(value => value)), FieldValue.Of(RowCount * 7)) { Derived = true }).ToArray(),
        };

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits(plan.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());

        protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
        {
            var total = _values.Sum(value => value);
            return [new ValueChange("total", FieldValue.Of(total - applied.Sum(change =>
                change.NewValue.AsInt32() - change.OldValue.AsInt32())), FieldValue.Of(total))];
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput("save.dat", _values.ToArray())];
    }
}
