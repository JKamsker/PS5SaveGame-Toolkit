using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataRegistrationTests
{
    [Fact]
    public void SnapshotRetainsExecutableReferencePolicyAndDetectsPolicyDrift()
    {
        using var document = new PublishedDocument();
        Func<ReferenceRecord, ReferenceEligibility> assess = _ => new() { Eligible = false };
        document.Publish(new FieldMetadata("value", "Reference", new NullableSpec(new ReferenceSpec("items") { Assess = assess })));
        var index = new ViewIndex(document);
        var original = index.Metadata("value")!;
        var reference = Assert.IsType<ReferenceSpec>(Assert.IsType<NullableSpec>(original.Spec).Inner);
        Assert.Same(assess, reference.Assess);
        document.Publish(new FieldMetadata("value", "Reference", new NullableSpec(new ReferenceSpec("items")
            { Assess = _ => new() { Eligible = true } })));
        Assert.False(FieldMetadataPlanning.Agree([original], [index.Metadata("value")!]));
    }

    [Fact]
    public void RegistrationAndResolutionDoNotRenderOrReadValues()
    {
        using var document = new PublishedDocument();
        document.Publish(new FieldMetadata("value", "Value", IntegerSpec.Int32));
        Assert.Single(document.ResolveFieldMetadata(["value"]));
        Assert.Empty(document.ResolveFieldMetadata(["missing"]));
        Assert.Equal(0, document.Reads);
    }

    [Fact]
    public void SeparatelyAllocatedNestedSpecsAgreeAndCanonicalSpecWins()
    {
        using var document = new PublishedDocument();
        var spec = new EnumSpec([new("a", "A")]);
        document.Publish(new FieldMetadata("value", "Canonical", spec));
        var index = new ViewIndex(document);
        var registered = index.Spec("value");
        index.Add(new FieldNode("value", "Projection", new EnumSpec([new("a", "A")]), FieldValue.Of("a")));
        Assert.Same(registered, index.Spec("value"));
        Assert.Throws<InvalidOperationException>(() => index.Add(new FieldNode("value", "Projection",
            new EnumSpec([new("a", "Different label")]), FieldValue.Of("a"))));
    }

    [Fact]
    public void ViewFirstThenConflictingRegistrationIsRejected()
    {
        using var document = new PublishedDocument();
        var index = new ViewIndex(document);
        index.Add(new FieldNode("value", "Value", IntegerSpec.Int32, FieldValue.Of(1)));
        document.Publish(new FieldMetadata("value", "Value", new IntegerSpec(0, 10)));
        Assert.Throws<InvalidOperationException>(() => index.Spec("value"));
    }

    [Fact]
    public void PreviewMetadataCopiesNestedOptionsAndNavigation()
    {
        using var document = new PublishedDocument();
        var options = new List<EnumOption> { new("a", "Original") };
        var path = new List<string> { "section:Original" };
        document.Publish(new FieldMetadata("value", "Value", new EnumSpec(options)) { Path = path });
        var index = new ViewIndex(document);
        var snapshot = index.Metadata("value")!;
        options[0] = new("a", "Changed");
        path[0] = "section:Changed";
        Assert.Equal("Original", Assert.IsType<EnumSpec>(snapshot.Spec).Options[0].Label);
        Assert.Equal("section:Original", Assert.Single(snapshot.Path!));
        Assert.False(FieldMetadataPlanning.Agree([snapshot], [index.Metadata("value")!]));
    }

    [Fact]
    public void PresentationAnnotationsParticipateInAgreementAndUseStableWireValues()
    {
        using var document = new PublishedDocument();
        document.Publish(new FieldMetadata("value", "Value", IntegerSpec.Int32)
        {
            Presentation = new FieldPresentation { Confidence = Confidence.Verified, Warning = "Check source." },
        });
        var index = new ViewIndex(document);
        var snapshot = index.Metadata("value")!;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        Assert.Equal("Verified", json.RootElement.GetProperty("Presentation").GetProperty("Confidence").GetString());

        document.Publish(new FieldMetadata("value", "Value", IntegerSpec.Int32)
        {
            Presentation = new FieldPresentation { Confidence = Confidence.Verified, Warning = "Changed." },
        });
        Assert.False(FieldMetadataPlanning.Agree([snapshot], [index.Metadata("value")!]));
    }

    [Fact]
    public void ReplacingStorageWithConflictingSpecCannotPublishStaleMetadata()
    {
        using var document = new PublishedDocument();
        document.Publish(new FieldMetadata("value", "Value", IntegerSpec.Int32));
        document.ReplaceStorage();
        Assert.Throws<InvalidOperationException>(() => document.ResolveFieldMetadata(["value"]));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void PublicationRejectsMissingPrimarySetterOrWritableDerived(bool derived, bool readOnly, bool setter)
    {
        using var document = new PublishedDocument();
        Assert.Throws<ArgumentException>(() => document.Publish(new FieldMetadata("value", "Value",
            IntegerSpec.Int32 with { ReadOnly = readOnly })
            { BindingKind = derived ? FieldBindingKind.Derived : FieldBindingKind.Primary }, setter));
    }

    private sealed class PublishedDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int Reads { get; private set; }
        public override string Title => "Published";
        public override ViewNode BuildView() => throw new InvalidOperationException("Must not render.");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public void Publish(FieldMetadata metadata, bool setter = true) =>
            RegisterField(metadata, () => { Reads++; return FieldValue.Of("a"); }, setter ? _ => { } : null);
        public void ReplaceStorage() => RegisterField("value", new BoolSpec(), () => FieldValue.Of(true), _ => { });
    }
}
