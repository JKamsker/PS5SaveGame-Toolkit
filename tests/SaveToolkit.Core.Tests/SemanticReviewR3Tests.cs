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

    [Fact]
    public void ContextAdmissionChargesAliasProjectionMultiplicityAndRetainsValidPrefix()
    {
        var change = new SemanticChange("primary", "Primary", ["primary"], FieldValue.Of(1),
            FieldValue.Of(2), true, true, true) { CanonicalValueId = "canonical", Aliases = [
                new SemanticProjection("alias-a", "Alias A", ["alias-a"]),
                new SemanticProjection("alias-b", "Alias B", ["alias-b"]),
            ] };
        var context = new SemanticMemberContext("character", "profile.dat", "Group", "fixture");
        var oneProjection = SemanticContextBudget.TryEstimate(context, out var contextBytes)
            ? contextBytes * 2 - 1 : throw new InvalidOperationException();

        var result = SemanticMemberContextResolver.Resolve(_ => new Dictionary<string, SemanticMemberContext>
        {
            ["canonical"] = context,
        }, [change], 0, oneProjection, CancellationToken.None);

        Assert.Empty(result.Contexts);
        Assert.Equal(1, result.OmittedCount);
        Assert.True(result.AcceptedBytes <= oneProjection);
        Assert.Contains("incomplete", result.Diagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextAdmissionRetainsValidPrefixAndCountsMalformedIdentityOnce()
    {
        var changes = new[]
        {
            new SemanticChange("first", "First", ["first"], FieldValue.Of(1), FieldValue.Of(2), true, true, true)
            { CanonicalValueId = "first" },
            new SemanticChange("second", "Second", ["second"], FieldValue.Of(1), FieldValue.Of(2), true, true, true)
            { CanonicalValueId = "second" },
        };
        var result = SemanticMemberContextResolver.Resolve(_ => new Dictionary<string, SemanticMemberContext>
        {
            ["first"] = new("character", "profile.dat"),
            ["second"] = new("character", new string('x', SemanticContextBudget.MaxStringLength + 1)),
        }, changes, 0, 4096, CancellationToken.None);

        Assert.Contains("first", result.Contexts.Keys);
        Assert.DoesNotContain("second", result.Contexts.Keys);
        Assert.Equal(1, result.OmittedCount);
    }

    [Fact]
    public void CurrentAliasMetadataWinsAndExplicitEmptyPresentationClearsHistoricalFallback()
    {
        var original = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true),
            Field("alias", 1, "canonical", FieldBindingKind.Derived, metadata: true,
                label: "Old alias", path: ["old"], presentation: new FieldPresentation { Warning = "old" }));
        var current = Snapshot(
            Field("primary", 1, "canonical", FieldBindingKind.Primary, metadata: true),
            Field("alias", 2, "canonical", FieldBindingKind.Derived, metadata: true,
                label: "Current alias", path: ["current"], presentation: null));

        var change = Assert.Single(SemanticSnapshotComparison.Compare(original.Fields, true, current.Fields, true,
            new TestDocument(), new Dictionary<string, SemanticMemberContext>(), CancellationToken.None));
        var alias = Assert.Single(change.Aliases!);
        Assert.Equal("Current alias", alias.Label);
        Assert.Equal(["current"], alias.Path);
        Assert.Null(alias.Presentation);
    }

    private static SemanticSnapshot Snapshot(params SemanticField[] fields) =>
        new(fields.ToDictionary(field => field.ValueId, StringComparer.Ordinal), true, 100);

    private static SemanticField Field(string id, int value, string? canonical, FieldBindingKind binding,
        bool metadata, string? label = null, IReadOnlyList<string>? path = null,
        FieldPresentation? presentation = null) => new(id, label ?? id, (path ?? [id]).ToArray(), FieldValue.Of(value),
        binding == FieldBindingKind.Derived, CanonicalValueId: canonical, BindingKind: binding,
        Spec: IntegerSpec.Int32, Presentation: presentation, MetadataPublished: metadata);

    private sealed class TestDocument : BoundSaveDocument
    {
        public override string Title => "R3";
        public override ViewNode BuildView() => new TextNode("R3");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
