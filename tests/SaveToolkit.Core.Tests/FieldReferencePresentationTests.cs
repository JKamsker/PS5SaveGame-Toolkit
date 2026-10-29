using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferencePresentationTests
{
    [Theory]
    [InlineData(false, "warning")]
    [InlineData(false, "confidence")]
    [InlineData(false, "description")]
    [InlineData(true, "warning")]
    [InlineData(true, "confidence")]
    [InlineData(true, "description")]
    public void ContradictoryLoadedAnnotationsCannotOverwriteCanonicalEvidence(bool metadataAware, string annotation)
    {
        using var document = new FieldReferenceDocument { PublishPresentation = false };
        var root = Assert.IsType<SectionNode>(document.BuildView());
        var canonical = Assert.IsType<FieldNode>(root.Children[^1]);
        var index = metadataAware ? new ViewIndex(document) : new ViewIndex();
        index.Add(canonical);
        index.Add(canonical with { Label = "Another caption" });
        var conflicting = annotation switch
        {
            "warning" => canonical with { Warning = null },
            "confidence" => canonical with { Confidence = Confidence.Verified },
            _ => canonical with { Description = null },
        };
        Assert.Equal(canonical.Value, conflicting.Value);
        Assert.Same(canonical.Spec, conflicting.Spec);
        index.Add(conflicting);
        Assert.Same(conflicting, index.ResolveFieldReferences(document, conflicting)); // Legacy non-reference rendering stays compatible.
        Assert.Contains("annotations conflict", Assert.Throws<InvalidOperationException>(() =>
            index.ResolveFieldReferences(document, new FieldReferenceNode("quantity"))).Message, StringComparison.Ordinal);
        Assert.Equal(0, document.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Confidence.Unknown)]
    [InlineData(Confidence.Inferred)]
    public void ExplicitCanonicalConfidenceIsNeverPromoted(Confidence? confidence)
    {
        using var fixture = new FieldReferenceSessionFixture(loaded: false);
        fixture.Document.AnnotationOverride = new() { Confidence = confidence, Warning = "Canonical caution" };
        var projected = Assert.IsType<FieldNode>(Assert.IsType<SectionNode>(fixture.Session.BuildView()).Children[0]);
        Assert.Equal(confidence, projected.Confidence);
        Assert.Equal("Canonical caution", projected.Warning);
    }

    [Fact]
    public void ExplicitMetadataCannotSilentlyDiscardLearnedCanonicalWarnings()
    {
        using var fixture = new FieldReferenceSessionFixture();
        fixture.Document.AnnotationOverride = new();
        Assert.Throws<InvalidOperationException>(() => fixture.Session.BuildView());
        Assert.Equal(0, fixture.Document.Writes);
    }

    [Fact]
    public void CanonicalAnnotationsUseTheSamePublicStringConfidenceWireAsFields()
    {
        var presentation = new FieldPresentation { Confidence = Confidence.Inferred, Warning = "Preserve warning", Description = "Attribution" };
        var json = System.Text.Json.JsonSerializer.Serialize(presentation);
        Assert.Contains("\"Confidence\":\"Inferred\"", json, StringComparison.Ordinal);
        Assert.Equal(presentation, System.Text.Json.JsonSerializer.Deserialize<FieldPresentation>(json));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnspecifiedAnnotationsRequireLearnedCanonicalFieldBeforeRenderingAnEditor(bool loaded)
    {
        using var fixture = new FieldReferenceSessionFixture(loaded);
        fixture.Document.PublishPresentation = false;
        var root = Assert.IsType<SectionNode>(fixture.Session.BuildView());
        if (!loaded) Assert.IsType<FieldReferenceNode>(root.Children[0]);
        else
        {
            var projection = Assert.IsType<FieldNode>(root.Children[0]);
            Assert.Equal(Confidence.Inferred, projection.Confidence);
            Assert.Equal("Unverified gameplay meaning.", projection.Warning);
        }
        Assert.Equal(0, fixture.Document.Writes);
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Fact]
    public void ExplicitCompleteAnnotationsPreserveUnloadedEvidenceWarningsAndReadOnlyDerivedState()
    {
        using var fixture = new FieldReferenceSessionFixture(loaded: false);
        var root = Assert.IsType<SectionNode>(fixture.Session.BuildView());
        var quantity = Assert.IsType<FieldNode>(root.Children[0]);
        Assert.Equal(Confidence.Inferred, quantity.Confidence);
        Assert.Equal("Unverified gameplay meaning.", quantity.Warning);
        Assert.Equal("Canonical explanation\nContext", quantity.Description);
        var total = Assert.IsType<FieldNode>(root.Children[2]);
        Assert.True(total.Spec.ReadOnly);
        Assert.Null(total.Confidence); // Explicit empty presentation preserves absence of a confidence label.
        Assert.Equal(0, fixture.Document.Expansions);
    }

    [Fact]
    public void PartiallyRegisteredDocumentsStillResolveLoadedViewOnlyFields()
    {
        using var document = new MetadataDocument();
        var index = new ViewIndex(document);
        var root = Assert.IsType<SectionNode>(document.BuildView());
        var reference = new FieldReferenceNode("visible");
        index.Add(root);
        index.Add(reference);
        var field = Assert.IsType<FieldNode>(index.ResolveFieldReferences(document, reference));
        Assert.Equal("visible", field.ValueId);
        Assert.Equal(1, field.Value.AsInt32());
    }

    [Fact]
    public void LegacyViewSizeAndNodeIdentityRemainUnchangedWhenThereAreNoReferences()
    {
        using var document = new FieldReferenceDocument();
        var root = new SectionNode("Legacy large view", Enumerable.Range(0, 100_001)
            .Select(index => (ViewNode)SectionNode.Lazy("Item", "lazy/" + index)).ToArray());
        var index = new ViewIndex(document);
        index.Add(root);
        Assert.Same(root, index.ResolveFieldReferences(document, root));
    }
}
