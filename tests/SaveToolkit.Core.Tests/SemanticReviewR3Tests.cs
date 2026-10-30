using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR3Tests
{
    [Fact]
    public void IncompleteAliasMetadataDoesNotInventAddedOrRemovedProjection()
    {
        var original = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true),
            Field("alias", 1, "canonical", FieldBindingKind.Derived, metadata: true));
        var current = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true));

        var changes = SemanticSnapshotComparison.Compare(original.Fields, true, current.Fields, false,
            new TestDocument(), new Dictionary<string, SemanticMemberContext>(), CancellationToken.None);

        Assert.Empty(changes);
    }

    [Fact]
    public void AliasOnlyValueChangeProducesOneReadOnlyCanonicalRow()
    {
        var original = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true),
            Field("alias", 1, "canonical", FieldBindingKind.Derived, metadata: true));
        var current = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true),
            Field("alias", 2, "canonical", FieldBindingKind.Derived, metadata: true));

        var change = Assert.Single(SemanticSnapshotComparison.Compare(original.Fields, true, current.Fields, true,
            new TestDocument(), new Dictionary<string, SemanticMemberContext>(), CancellationToken.None));

        Assert.Equal("primary", change.ValueId);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("1", change.CurrentValue.ToString());
        Assert.False(change.CanRevert);
        Assert.Contains(change.Aliases!, alias => alias.ValueId == "alias" && alias.Changed);
        Assert.Contains("read-only projection", change.RevertReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedMemberContextIsOmittedWithBoundedDiagnostic()
    {
        var change = new SemanticChange("primary", "Primary", ["primary"], FieldValue.Of(1), FieldValue.Of(2), true, true, true)
        { CanonicalValueId = "canonical" };
        var result = SemanticMemberContextResolver.Resolve(_ => new Dictionary<string, SemanticMemberContext>
        {
            ["canonical"] = new("derived", new string('x', SemanticContextBudget.MaxStringLength + 1))
        }, [change], 0, 4096, CancellationToken.None);

        Assert.Empty(result.Contexts);
        Assert.Equal(1, result.OmittedCount);
        Assert.Contains("incomplete", result.Diagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    private static SemanticSnapshot Snapshot(params SemanticField[] fields) =>
        new(fields.ToDictionary(field => field.ValueId, StringComparer.Ordinal), true, 100);

    private static SemanticField Field(string id, int value, string? canonical, FieldBindingKind binding,
        bool metadata) => new(id, id, [id], FieldValue.Of(value), binding == FieldBindingKind.Derived,
        CanonicalValueId: canonical, BindingKind: binding, Spec: IntegerSpec.Int32, MetadataPublished: metadata);

    private sealed class TestDocument : BoundSaveDocument
    {
        public override string Title => "R3";
        public override ViewNode BuildView() => new TextNode("R3");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
