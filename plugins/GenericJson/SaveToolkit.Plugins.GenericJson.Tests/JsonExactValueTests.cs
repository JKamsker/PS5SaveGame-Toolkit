using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonExactValueTests
{
    [Theory]
    [InlineData("-0")]
    [InlineData("170141183460469231731687303715884105728")]
    [InlineData("-170141183460469231731687303715884105728")]
    [InlineData("0.123456789012345678901234567890123456789")]
    [InlineData("1e1000000")]
    [InlineData("1e-1000000")]
    [InlineData("1.230000E+0002")]
    public void ExactLexemesSurviveWireEditReopenAndUndo(string lexeme)
    {
        const string source = "\uFEFF[ 2.000e+00, \"002\" ]\n";
        using var document = Open(source);
        var field = JsonEditingTests.Fields(document)[0];
        var edited = JsonEditingTests.Number(lexeme);
        var wire = JsonSerializer.Serialize(edited);
        Assert.Contains("exactNumber", wire, StringComparison.Ordinal);
        var received = JsonSerializer.Deserialize<FieldValue>(wire);
        Assert.Equal(edited, received);
        Assert.True(document.ApplyEdits([new(field.ValueId, received)]).Succeeded);
        var bytes = document.Serialize()[0].Content.ToArray();
        Assert.Equal(Encoding.UTF8.GetBytes(source.Replace("2.000e+00", lexeme, StringComparison.Ordinal)), bytes);
        using var reopened = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.sav", bytes));
        Assert.Equal(edited, JsonEditingTests.Fields(reopened)[0].Value);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void NullMissingEmptyAndNumericLookingStringRemainDistinct()
    {
        const string source = "{ \"null\":null, \"empty\":\"\", \"text\":\"-0\", \"n\":-0 }";
        using var document = Open(source);
        var fields = JsonEditingTests.Fields(document);
        Assert.Equal(4, fields.Count);
        Assert.True(fields[0].Value.IsNull);
        Assert.Equal("", fields[1].Value.AsUnion().Value.AsString());
        Assert.Equal("string", fields[2].Value.AsUnion().Type);
        Assert.Equal("number", fields[3].Value.AsUnion().Type);
        var nullable = Assert.IsType<NullableSpec>(fields[0].Spec);
        Assert.IsType<UnionSpec>(nullable.Inner);
        Assert.True(document.ApplyEdits([new(fields[0].ValueId, JsonEditingTests.Text("")), new(fields[1].ValueId, FieldValue.Null)]).Succeeded);
        Assert.Equal("{ \"null\":\"\", \"empty\":null, \"text\":\"-0\", \"n\":-0 }", Encoding.UTF8.GetString(document.Serialize()[0].Content.Span));
        Assert.True(document.ApplyEdits(fields.Take(2).Select(f => new ValueEdit(f.ValueId, f.Value)).ToArray()).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void ForgedUnionTypesAndInvalidUnicodeRejectWholeBatch()
    {
        const string source = "[1,\"safe\"]";
        using var document = Open(source);
        var fields = JsonEditingTests.Fields(document);
        foreach (var invalid in new[] {
            FieldValue.Union("string", FieldValue.ExactNumber("2")),
            FieldValue.Union("unknown", FieldValue.Of("x")),
            FieldValue.Union("number", FieldValue.Of("2")),
            JsonEditingTests.Text("\uD800"),
            FieldValue.Of(double.PositiveInfinity),
        })
        {
            Assert.False(document.ApplyEdits([new(fields[0].ValueId, JsonEditingTests.Number("3")), new(fields[1].ValueId, invalid)]).Succeeded);
            Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
        }
    }

    [Fact]
    public void BeyondProtocolResourceLimitsStayVerbatimReadOnly()
    {
        var source = "[1e1000000001," + new string('9', ExactJsonNumber.HardMaxLength + 1) + "]";
        using var document = Open(source);
        var fields = JsonEditingTests.Fields(document);
        Assert.Equal(2, fields.Count);
        Assert.All(fields, f => Assert.True(f.Spec.ReadOnly));
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    private static ISaveDocument Open(string source) => new GenericJsonPlugin().Open(
        MemorySaveSet.SingleFile("data.sav", Encoding.UTF8.GetBytes(source)));
}
