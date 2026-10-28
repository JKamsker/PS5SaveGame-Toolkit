using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSessionValueTests
{
    [Fact]
    public void HostWireValidationAndHistoryPreserveExactNumbersAndTypes()
    {
        const string source = "\uFEFF[ -0, null, \"\\u0041\" ]\n";
        using var fixture = new JsonSessionFixture(source);
        var fields = fixture.Fields();
        var exact = JsonEditingTests.Number("0.12345678901234567890123456789e-1000000");
        Assert.True(fixture.Edit(fields[0].ValueId, exact).Ok);
        Assert.True(fixture.Edit(fields[1].ValueId, JsonEditingTests.Text("")).Ok);
        Assert.True(fixture.Edit(fields[2].ValueId, FieldValue.Null).Ok);
        Assert.Equal(exact, fixture.Document.GetValue(fields[0].ValueId));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(exact, fixture.Document.GetValue(fields[0].ValueId));
    }

    [Theory]
    [InlineData("{\"kind\":\"union\",\"type\":\"number\",\"value\":{\"kind\":\"exactNumber\",\"text\":\"01\"}}")]
    [InlineData("{\"kind\":\"union\",\"type\":\"unknown\",\"value\":null}")]
    public void HostRejectsMalformedAndLossyNumericWireValues(string json)
    {
        using var fixture = new JsonSessionFixture("[1]");
        var id = fixture.Fields()[0].ValueId;
        Assert.False(fixture.Session.ApplyEdits([new(id, JsonSerializer.Deserialize<JsonElement>(json))], 0).Ok);
        Assert.Equal("[1]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void HostReadsNumericWireLexemeWithoutMachineNumberConversion()
    {
        using var fixture = new JsonSessionFixture("[1]");
        var id = fixture.Fields()[0].ValueId;
        var wire = JsonSerializer.Deserialize<JsonElement>("{\"kind\":\"union\",\"type\":\"number\",\"value\":0.12345678901234567890123456789}");
        Assert.True(fixture.Session.ApplyEdits([new(id, wire)], 0).Ok);
        Assert.Equal("[0.12345678901234567890123456789]", Encoding.UTF8.GetString(fixture.Bytes));
    }
}
