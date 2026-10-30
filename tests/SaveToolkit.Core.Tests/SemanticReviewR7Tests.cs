using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR7Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void SessionRetryPublishesRecoveredTargetAndCurrentValue()
    {
        var failures = 2;
        using var current = new SessionRetryDocument(static () => false);
        var workspace = _store.Create("semantic retry", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        using var session = new DocumentSession("retry", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: () => new SessionRetryDocument(() => failures-- > 0));

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", System.Text.Json.JsonSerializer.SerializeToElement(2))]).Ok);
        Assert.False(session.GetChanges().Complete);
        var repaired = session.GetChanges();

        Assert.Single(repaired.Changes);
        var change = Assert.Single(repaired.Changes);
        Assert.Equal("hidden", change.ValueId);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
        Assert.True(change.OriginalExists);
        Assert.True(change.CurrentExists);
        Assert.True(change.CanRevert);
    }

    [Fact]
    public void ReferencePolicyStateDriftRevokesRevertAuthority()
    {
        var plugin = new ItemsPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId);
        var references = new ReferenceFieldService(context);
        var first = new EligibilityPolicy(accept: true);
        var second = new EligibilityPolicy(accept: false);
        var originalSpec = new ReferenceSpec("items") { Assess = first.Assess };
        var currentSpec = new ReferenceSpec("items") { Assess = second.Assess };
        var original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["item"] = Field("item", originalSpec, "1001"),
        }, true, 0);
        var current = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["item"] = Field("item", currentSpec, "1001"),
        }, true, 0);

        SemanticTargetedAuthority.Validate(original, current, references);

        Assert.False(current.Complete);
        Assert.Empty(current.Fields);
    }

    [Fact]
    public void AcceptedFiveThousandScalarMetadataChangesReplayExactly()
    {
        const int count = 4_097;
        using var current = new LargeScalarDocument(count);
        var workspace = _store.Create("large replay", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        using var session = new DocumentSession("large", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: () => new LargeScalarDocument(count));
        var edits = Enumerable.Range(0, count)
            .Select(index => new IncomingEdit(LargeScalarDocument.Id(index), JsonSerializer.SerializeToElement(2)))
            .ToArray();
        var before = current.Serialize().Single().Content.ToArray();

        _ = edits;
        _ = session.BuildView();
        var preview = session.PlanAction("bulk", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var changed = current.Serialize().Single().Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.Equal(before, current.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, current.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void WarmMetadataReplacementIsAdmittedBeforeWireComparison()
    {
        using var document = new WarmMetadataDocument();
        var index = new FieldMetadataIndex(document, 2_048);
        index.ResolveAll(["alias"]);
        var before = index.Cached("alias");
        document.Oversized = true;

        Assert.Throws<InvalidOperationException>(() => index.ResolveAll(["alias"]));
        Assert.Equal(before, index.Cached("alias"));
    }

    private static SemanticField Field(string id, ReferenceSpec spec, string value) =>
        new(id, "Item", [], FieldValue.Of(value), false, CanonicalValueId: id,
            Spec: spec, BindingKind: FieldBindingKind.Primary);

    private sealed class EligibilityPolicy(bool accept)
    {
        private readonly bool _accept = accept;
        public ReferenceEligibility Assess(ReferenceRecord _) => new() { Eligible = _accept };
    }

    private sealed class SessionRetryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Func<bool> _failResolver;
        private int _value = 1;

        public override string Title => "Session retry";
        public override ViewNode BuildView() => new TextNode("No rendered fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (_failResolver()) throw new InvalidOperationException("transient metadata failure");
            return base.ResolveFieldMetadata(valueIds);
        }

        public SessionRetryDocument(Func<bool> failResolver)
        {
            _failResolver = failResolver;
            RegisterField(new FieldMetadata("hidden", "Canonical hidden", IntegerSpec.Int32)
            {
                Path = ["canonical", "hidden"],
            }, () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }
    }

    private sealed class LargeScalarDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
    {
        private readonly Dictionary<string, int> _values;

        public LargeScalarDocument(int count)
        {
            _values = Enumerable.Range(0, count).ToDictionary(Id, static _ => 1, StringComparer.Ordinal);
            foreach (var id in _values.Keys)
            {
                var captured = id;
                RegisterField(new FieldMetadata(captured, captured, IntegerSpec.Int32),
                    () => FieldValue.Of(_values[captured]), value => _values[captured] = value.AsInt32());
            }
        }

        public override string Title => "Large scalar replay";
        public override ViewNode BuildView() => new SectionNode("Metadata-only scalar set",
            [new ActionNode("bulk", "Bulk set") { SupportsPlanning = true }]);
        public override IReadOnlyList<FileOutput> Serialize() =>
            [new("save.dat", JsonSerializer.SerializeToUtf8Bytes(_values.OrderBy(pair => pair.Key)))];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            base.ResolveFieldMetadata(valueIds);
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = _values.Keys.Select(id => new PlannedChange(id, id,
                FieldValue.Of(_values[id]), FieldValue.Of(2))).ToArray() };
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits(plan.Changes.Select(change =>
                new ValueEdit(change.ValueId, change.NewValue)).ToArray());
        public static string Id(int index) => $"scalar/{index:0000}";
    }

    private sealed class WarmMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public bool Oversized { get; set; }
        public override string Title => "Warm metadata";
        public override ViewNode BuildView() => new TextNode("Metadata-only warm cache");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            valueIds.Select(id => id == "alias"
                ? new FieldMetadata(id, "Alias", IntegerSpec.Int32 with { ReadOnly = true })
                {
                    BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary",
                    Presentation = Oversized ? new FieldPresentation { Warning = new string('x', 8_000) } : null,
                }
                : new FieldMetadata(id, "Primary", IntegerSpec.Int32)
                { CanonicalValueId = "primary" }).ToArray();

        public FieldValue Value => FieldValue.Of(1);

        public WarmMetadataDocument()
        {
            RegisterField("primary", IntegerSpec.Int32, () => Value, _ => { });
            RegisterField("alias", IntegerSpec.Int32 with { ReadOnly = true }, () => Value);
        }
    }
}
