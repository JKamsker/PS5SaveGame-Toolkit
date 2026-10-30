using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceDescriptionTests
{
    public static TheoryData<bool, string?, string?, string?> Descriptions
    {
        get
        {
            var data = new TheoryData<bool, string?, string?, string?>();
            foreach (var loaded in new[] { true, false })
            {
                data.Add(loaded, null, null, null);
                data.Add(loaded, null, "", null);
                data.Add(loaded, null, "Context", "Context");
                data.Add(loaded, "", null, "");
                data.Add(loaded, "", "", "");
                data.Add(loaded, "", "Context", "Context");
                data.Add(loaded, "Canonical", null, "Canonical");
                data.Add(loaded, "Canonical", "", "Canonical");
                data.Add(loaded, "Canonical", "Context", "Canonical\nContext");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Descriptions))]
    public void CanonicalDescriptionIsExactUnlessContextIsAppended(bool loaded, string? canonical, string? context, string? expected)
    {
        using var document = new FieldReferenceDocument { AnnotationOverride = new() { Description = canonical } };
        _ = document.BuildView();
        var index = new ViewIndex(document);
        if (loaded) index.Add(new FieldNode("quantity", "Quantity", new IntegerSpec(0, 10), FieldValue.Of(2)) { Description = canonical });
        var reference = new FieldReferenceNode("quantity") { Description = context };
        index.Add(reference);
        var projected = Assert.IsType<FieldNode>(index.ResolveFieldReferences(document, reference));
        Assert.Equal(expected, projected.Description);
        var wire = JsonSerializer.SerializeToElement<ViewNode>(projected, ToolkitJson.Options);
        Assert.Equal(expected is not null, wire.TryGetProperty("description", out var description));
        if (expected is not null) Assert.Equal(expected, description.GetString());
        Assert.Null(projected.Confidence);
        Assert.Null(projected.Warning);
        Assert.Equal(2, projected.Value.AsInt32());
        Assert.Equal(0, document.Writes);
    }
}
