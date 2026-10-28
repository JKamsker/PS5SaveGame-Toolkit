using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

public sealed class PlanningPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.planning", "Planning fixture", "1.0.0");
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Planning fixture");
    public ISaveDocument Open(ISaveFile file) => LastOpened = new PlanningDocument(file.RelativePath);
    public PlanningDocument? LastOpened { get; private set; }

    public sealed class PlanningDocument(string path) : BoundSaveDocument, IActionPlanningDocument
    {
        private Dictionary<string, long> _items = new() { ["a"] = 2, ["b"] = 4 };
        public override string Title => "Inventory";
        public bool ReadOnly { get; set; }
        public bool RejectApply { get; set; }
        public bool DifferentPlan { get; set; }
        public bool OversizedPlan { get; set; }
        public bool ThrowDuringApply { get; set; }
        public int ApplyCount { get; private set; }
        public override bool CanEdit => !ReadOnly;

        public override ViewNode BuildView() => new SectionNode("Save",
        [
            new SectionNode("Inventory", []) { LazyId = "items", Collapsed = true },
            Field("total", "Total quantity", IntegerSpec.Int32, () => FieldValue.Of(_items.Values.Sum())),
            new ActionNode("refill", "Refill selected")
            {
                SupportsPlanning = true,
                Parameters = [new FieldNode("value", "Target quantity", IntegerSpec.Int32 with { Min = 0, Max = 100 }, FieldValue.Of(10L))],
            },
            new ActionNode("remove", "Remove selected") { SupportsPlanning = true },
            new ActionNode("legacy", "Legacy action"),
        ]);

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "items"
            ? [new TableNode([new TableColumn("quantity", "Quantity")], _items.Select(pair => new TableRow(pair.Key,
                new Dictionary<string, ViewNode> { ["quantity"] = Field("item/" + pair.Key, pair.Key + " quantity",
                    IntegerSpec.Int32, () => FieldValue.Of(_items[pair.Key]), value => _items[pair.Key] = value.AsInt64()) })).ToArray())]
            : base.ExpandNode(lazyId);

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            var rows = scope.RowKeys ?? _items.Keys.ToArray();
            var errors = rows.Where(row => !_items.ContainsKey(row)).Select(row => new FieldError(row, "Unknown row.")).ToArray();
            if (errors.Length > 0) return new ActionPlan { Errors = errors };
            var target = DifferentPlan ? 9L : parameters.GetValueOrDefault("value", FieldValue.Of(10L)).AsInt64();
            var changes = rows.Select(row => new PlannedChange("item/" + row, row + " quantity", FieldValue.Of(_items[row]),
                actionId == "remove" ? FieldValue.Null : FieldValue.Of(target)) { NewExists = actionId != "remove" }).ToList();
            if (rows.Count > 0)
            {
                var total = _items.Values.Sum();
                var after = actionId == "remove" ? total - rows.Sum(row => _items[row]) : total + rows.Sum(row => target - _items[row]);
                changes.Add(new PlannedChange("total", "Total quantity", FieldValue.Of(total), FieldValue.Of(after)) { Derived = true });
            }

            return new ActionPlan { Changes = changes, Warnings = [OversizedPlan ? new string('x', 4 * 1024 * 1024) : "Fixture warning"], SkippedRows = [new SkippedActionRow("locked", "Locked row")] };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            ApplyCount++;
            if (RejectApply) return EditOutcome.Rejected(actionId, "Precondition failed.");
            _ = ExpandNode("items"); // Bind targeted lazy values before replaying the planned edits.
            if (actionId == "remove")
            {
                var result = Structural(() =>
                {
                    foreach (var row in scope.RowKeys ?? _items.Keys.ToArray()) _items.Remove(row);
                    if (ThrowDuringApply) throw new InvalidOperationException("Fixture structural failure.");
                });
                return new EditOutcome { StructureChanged = true, Revert = result.Revert };
            }

            return ApplyEdits(plan.Changes.Where(c => !c.Derived).Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray());
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            new() { Changes = SetValues([new ValueEdit("item/a", FieldValue.Of(3L))]) };

        protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
        {
            var total = _items.Values.Sum();
            return applied.Count == 0 ? [] : [new ValueChange("total", FieldValue.Of(total - applied.Sum(c => c.NewValue.AsInt64() - c.OldValue.AsInt64())), FieldValue.Of(total))];
        }

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(new Dictionary<string, long>(_items), 64);
        protected override void LoadSnapshot(DocumentMemento snapshot) => _items = new Dictionary<string, long>(((SnapshotMemento<Dictionary<string, long>>)snapshot).State);
        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(path, System.Text.Encoding.UTF8.GetBytes(string.Join(",", _items.Select(p => p.Key + "=" + p.Value))))];
    }
}
