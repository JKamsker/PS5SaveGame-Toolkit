using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR8Tests
{
    [Fact]
    public void NestedReferenceAuthorityUsesPinnedCatalogForNullableAndUnion()
    {
        var plugin = new ItemsPlugin();
        var references = new ReferenceFieldService(new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId));
        var policy = new NestedPolicy(accept: true);
        var nullable = new NullableSpec(new ReferenceSpec("items") { Assess = policy.Assess });
        var union = new UnionSpec([new UnionOption("item", "Item", new ReferenceSpec("items")
        {
            Assess = policy.Assess,
        })]);

        foreach (var (id, spec, value) in new[]
        {
            ("nullable", (FieldSpec)nullable, FieldValue.Of("1001")),
            ("union", (FieldSpec)union, FieldValue.Union("item", FieldValue.Of("1001"))),
        })
        {
            var original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
            {
                [id] = Field(id, spec, value),
            }, true, 0);
            var current = original.Clone();

            SemanticTargetedAuthority.Validate(original, current, references);

            Assert.True(current.Complete);
            Assert.Contains(id, current.Fields.Keys);
        }
    }

    [Fact]
    public void NestedReferencePolicyDriftRevokesAuthorityWithPinnedCatalog()
    {
        var plugin = new ItemsPlugin();
        var references = new ReferenceFieldService(new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId));
        var originalPolicy = new NestedPolicy(accept: true);
        var currentPolicy = new NestedPolicy(accept: false);
        var originalSpec = new NullableSpec(new ReferenceSpec("items") { Assess = originalPolicy.Assess });
        var currentSpec = new NullableSpec(new ReferenceSpec("items") { Assess = currentPolicy.Assess });
        var original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["nested"] = Field("nested", originalSpec, FieldValue.Of("1001")),
        }, true, 0);
        var current = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["nested"] = Field("nested", currentSpec, FieldValue.Of("1001")),
        }, true, 0);

        SemanticTargetedAuthority.Validate(original, current, references);

        Assert.False(current.Complete);
        Assert.Empty(current.Fields);
    }

    [Fact]
    public void RecoveredMetadataRetainsEvidenceWhenTargetSetIsFull()
    {
        const int retained = 4_095;
        using var original = new CapacityDocument(retained);
        using var current = new CapacityDocument(retained);
        var review = new SemanticReview(new());
        var retainedIds = Enumerable.Range(0, retained).Select(CapacityDocument.Id).ToArray();

        review.CaptureTargetedOriginals(() => original, retainedIds, CancellationToken.None);
        original.FailA = true;
        review.CaptureTargetedOriginals(() => original, ["A"], CancellationToken.None);
        review.CaptureTargetedOriginals(() => original, ["B"], CancellationToken.None);
        original.FailA = false;
        current.SetValue("A", 2);

        var log = review.Query(() => original, current, _ => { }, _ => { }, 1, CancellationToken.None);

        Assert.False(log.Complete);
        var change = Assert.Single(log.Changes, change => change.ValueId == "A");
        Assert.True(change.OriginalExists);
        Assert.True(change.CurrentExists);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
        Assert.DoesNotContain("A", review.CaptureState().TargetedIds);
        Assert.NotEmpty(review.CaptureState().CaptureDiagnostics);
    }

    [Fact]
    public void OriginalMetadataOpenFailureCanRetryWithoutAffectedIds()
    {
        using var document = new RetryDocument();
        var snapshot = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            ["value"] = Field("value", IntegerSpec.Int32, FieldValue.Of(1)),
        }, true, 0);
        var fail = true;
        var capturing = false;

        var failed = SemanticOriginalMetadata.Enrich(() =>
        {
            if (fail) throw new InvalidOperationException("transient open failure");
            return document;
        }, snapshot, 16 * 1024 * 1024, [], forceAttempt: true, ref capturing, CancellationToken.None);

        Assert.False(failed.Applied);
        fail = false;
        var repaired = SemanticOriginalMetadata.Enrich(() => document, snapshot, 16 * 1024 * 1024, [],
            forceAttempt: true, ref capturing, CancellationToken.None);

        Assert.True(repaired.Applied);
        Assert.True(SemanticOriginalMetadata.HasMetadata(snapshot.Fields["value"]));
    }

    private static SemanticField Field(string id, FieldSpec spec, FieldValue value) =>
        new(id, id, [], value, spec.ReadOnly, CanonicalValueId: id, Spec: spec,
            BindingKind: FieldBindingKind.Primary);

    private sealed class NestedPolicy(bool accept)
    {
        private readonly bool _accept = accept;
        public ReferenceEligibility Assess(ReferenceRecord _) => new() { Eligible = _accept };
    }

    private sealed class RetryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public RetryDocument()
        {
            RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32 with { ReadOnly = true }),
                () => FieldValue.Of(1));
        }

        public override string Title => "Retry document";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }

    private sealed class CapacityDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Dictionary<string, int> _values;
        public bool FailA { get; set; }

        public CapacityDocument(int count)
        {
            _values = Enumerable.Range(0, count).ToDictionary(Id, static _ => 1, StringComparer.Ordinal);
            _values["A"] = 1;
            _values["B"] = 1;
            foreach (var id in _values.Keys)
            {
                var captured = id;
                RegisterField(captured, IntegerSpec.Int32, () => FieldValue.Of(_values[captured]),
                    value => _values[captured] = value.AsInt32());
            }
        }

        public override string Title => "Capacity document";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public void SetValue(string id, int value) => _values[id] = value;

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (FailA && valueIds.Contains("A", StringComparer.Ordinal))
                throw new InvalidOperationException("A is temporarily unavailable");
            return valueIds.Select(id => new FieldMetadata(id, id, IntegerSpec.Int32)
            {
                Path = ["canonical", id],
            }).ToArray();
        }

        public static string Id(int index) => $"field/{index:0000}";
    }
}
