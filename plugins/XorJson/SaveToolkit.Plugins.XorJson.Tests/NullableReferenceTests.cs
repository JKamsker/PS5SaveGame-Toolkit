using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class NullableReferenceTests
{
    private const string Original = "{\"ItemData\":{\"TypeTag\":null},\"untouched\":1.2300E+02}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogStringRetainsTheExplicitUnionSpec(bool xor)
    {
        using var document = Open(xor);
        var field = XorJsonTests.Fields(document).Single(field => field.Label == "TypeTag");
        var value = FieldValue.Union("string", FieldValue.Of("SW.Item.Sword"));
        Assert.True(document.ApplyEdits([new(field.ValueId, value)]).Succeeded);

        var current = XorJsonTests.Fields(document).Single(candidate => candidate.ValueId == field.ValueId);
        Assert.IsType<UnionSpec>(Assert.IsType<NullableSpec>(current.Spec).Inner);
        Assert.True(FieldValueCoercer.Normalize(current.Value, current.Spec).Ok);
        Assert.Equal(Confidence.Inferred, current.Confidence);
        Assert.Contains("console", current.Warning!, StringComparison.OrdinalIgnoreCase);
        var replacement = FieldValue.Union("string", FieldValue.Of("SW.Item.Bow"));
        Assert.True(FieldValueCoercer.Normalize(replacement, current.Spec).Ok);
        Assert.True(document.ApplyEdits([new(field.ValueId, replacement)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(Original.Replace("null", "\"SW.Item.Bow\"", StringComparison.Ordinal), xor),
            document.Serialize().Single().Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Null)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(Original, xor), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogStringCanReturnToAnotherPublishedScalarType(bool xor)
    {
        using var document = Open(xor);
        var field = XorJsonTests.Fields(document).Single(field => field.Label == "TypeTag");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Union("string", FieldValue.Of("SW.Item.Sword")))]).Succeeded);
        var result = document.ApplyEdits([new(field.ValueId, FieldValue.Union("boolean", FieldValue.Of(true)))]);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(XorJsonTests.Encode(Original.Replace("null", "true", StringComparison.Ordinal), xor),
            document.Serialize().Single().Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Null)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(Original, xor), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstStringAssignmentStillChecksCatalogEligibility(bool xor)
    {
        using var document = Open(xor);
        var field = XorJsonTests.Fields(document).Single(field => field.Label == "TypeTag");
        var unknown = FieldValue.Union("string", FieldValue.Of("SW.Item.UnknownFuture"));
        Assert.False(document.ApplyEdits([new(field.ValueId, unknown)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(Original, xor), document.Serialize().Single().Content.ToArray());
    }

    private static ISaveDocument Open(bool xor)
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        return plugin.Open(MemorySaveSet.SingleFile("data.sav", XorJsonTests.Encode(Original, xor), "PPSA16064"), references);
    }
}
