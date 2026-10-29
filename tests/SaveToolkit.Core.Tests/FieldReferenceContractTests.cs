using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceContractTests
{
    [Fact]
    public void PublicWireRoundTripsOnlyTheTargetAndPresentation()
    {
        ViewNode node = new FieldReferenceNode("raw/opaque~雪") { Label = "Named field", Description = "Context" };
        var json = JsonSerializer.Serialize(node, ToolkitJson.Options);
        Assert.Equal("""{"type":"fieldReference","targetValueId":"raw/opaque~\u96EA","label":"Named field","description":"Context"}""", json);
        Assert.Equal(node, JsonSerializer.Deserialize<ViewNode>(json, ToolkitJson.Options));
        var element = JsonSerializer.SerializeToElement(node, ToolkitJson.Options);
        Assert.False(element.TryGetProperty("spec", out _));
        Assert.False(element.TryGetProperty("value", out _));
        Assert.False(element.TryGetProperty("valueId", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"targetValueId\":null}")]
    [InlineData("{\"targetValueId\":\" \"}")]
    [InlineData("{\"targetValueId\":\"id\",\"label\":\"\"}")]
    [InlineData("{\"targetValueId\":\"id\",\"spec\":{}}")]
    [InlineData("{\"targetValueId\":\"id\",\"readOnly\":false}")]
    [InlineData("{\"targetValueId\":\"id\",\"value\":7}")]
    public void MalformedOrPermissionBearingWireIsRejected(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FieldReferenceNode>(json, ToolkitJson.Options));

    [Theory]
    [InlineData("targetValueId", FieldReferenceNode.MaxTargetIdLength)]
    [InlineData("label", FieldReferenceNode.MaxPresentationLength)]
    [InlineData("description", FieldReferenceNode.MaxPresentationLength)]
    public void DeserializationAndIndexEnforceThePublishedStringBounds(string property, int maximum)
    {
        var properties = new Dictionary<string, string> { ["targetValueId"] = "id", [property] = new('x', maximum) };
        var valid = JsonSerializer.Deserialize<FieldReferenceNode>(JsonSerializer.Serialize(properties), ToolkitJson.Options)!;
        new ViewIndex().Add(valid);
        properties[property] += "x";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FieldReferenceNode>(JsonSerializer.Serialize(properties), ToolkitJson.Options));
        Assert.Throws<InvalidOperationException>(() => new ViewIndex().Add(valid with { Label = new('x', maximum + 1) }));
    }

    [Fact]
    public void LegacyLoadedFieldsResolveRegardlessOfOrderAndReferencesNeverPublishSpecs()
    {
        using var document = new MetadataDocument();
        var field = new FieldNode("quantity", "Loaded canonical", new IntegerSpec(0, 10), FieldValue.Of(-1))
            { Confidence = Confidence.Unknown, Warning = "Keep this warning" };
        var reference = new FieldReferenceNode("quantity") { Label = "Named" };
        var root = new SectionNode("Root", [reference, field, new FieldReferenceNode("missing")]);
        var index = new ViewIndex(); // Legacy, view-only discovery, despite the document's optional capability.
        index.Add(root);
        Assert.Single(index.Specs);
        Assert.False(index.KnowsBinding("missing"));
        var resolved = Assert.IsType<SectionNode>(index.ResolveFieldReferences(document, root));
        var projection = Assert.IsType<FieldNode>(resolved.Children[0]);
        Assert.Equal(2, projection.Value.AsInt32());
        Assert.Equal(field.Warning, projection.Warning);
        Assert.Same(field.Spec, projection.Spec);
        Assert.IsType<FieldReferenceNode>(resolved.Children[2]);
        Assert.Single(index.Specs);
        Assert.Throws<InvalidOperationException>(() => index.Add(new FieldReferenceNode(string.Empty)));
    }

    [Fact]
    public void MetadataResolutionBatchesDistinctTargetsAndIgnoresHiddenChildren()
    {
        using var document = new FieldReferenceDocument { ShowCanonical = false };
        var index = new ViewIndex(document);
        var repeated = Enumerable.Range(0, 1000).Select(_ => (ViewNode)new FieldReferenceNode("quantity"));
        var hidden = new SectionNode("Hidden", [new FieldReferenceNode("never-resolve")]) { LazyId = "hidden" };
        var root = new SectionNode("Root", [.. repeated, hidden]);
        index.Add(root);
        var projected = Assert.IsType<SectionNode>(index.ResolveFieldReferences(document, root));
        Assert.Equal(["quantity"], Assert.Single(document.MetadataRequests));
        var first = Assert.IsType<FieldNode>(projected.Children[0]);
        Assert.All(projected.Children.Take(1000), child => Assert.Same(first.Spec, Assert.IsType<FieldNode>(child).Spec));
        Assert.Same(hidden, projected.Children[^1]);
        Assert.Empty(index.Specs);
        Assert.Equal(0, document.Expansions);
        var cancelled = new CancellationToken(canceled: true);
        Assert.Throws<OperationCanceledException>(() => index.ResolveFieldReferences(document, root, cancelled));
    }

    [Fact]
    public void PresentationReferencesCannotChainOrIntroduceMutableBindingIdentities()
    {
        using var document = new FieldReferenceDocument();
        var index = new ViewIndex(document);
        var root = new SectionNode("Root", [new FieldReferenceNode("other-projection"), new FieldReferenceNode("quantity")]);
        index.Add(root);
        var projected = Assert.IsType<SectionNode>(index.ResolveFieldReferences(document, root));
        Assert.IsType<FieldReferenceNode>(projected.Children[0]);
        Assert.IsType<FieldNode>(projected.Children[1]);
        Assert.False(index.KnowsBinding("other-projection"));
    }
}
