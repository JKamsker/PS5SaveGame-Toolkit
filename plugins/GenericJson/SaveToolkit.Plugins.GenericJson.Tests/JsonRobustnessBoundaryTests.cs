using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonRobustnessBoundaryTests
{
    private const int MaxDepth = 64;
    private const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxNodes = 250_000;
    private static readonly string[] DuplicateValues = ["1", "2", "3", "4", "5"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepthBoundaryIsLosslessAndExcessiveDepthRejects(bool objects)
    {
        JsonCorruptionTests.RoundTrip(Nested(MaxDepth, objects));
        foreach (var depth in new[] { MaxDepth + 1, 10_000 })
        {
            var error = JsonCorruptionTests.Reject(Nested(depth, objects));
            Assert.Contains("maximum depth: 64", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NodeLimitAcceptsExactBoundaryAndRejectsNextValue()
    {
        // Root array counts as one value; property names and closing delimiters do not.
        var valid = "[" + string.Join(',', Enumerable.Repeat("0", MaxNodes - 1)) + "]";
        JsonCorruptionTests.RoundTrip(Encoding.UTF8.GetBytes(valid));
        var error = JsonCorruptionTests.Reject(Encoding.UTF8.GetBytes(valid[..^1] + ",0]"));
        Assert.Contains("250,000 value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ByteLimitIncludesBomAndTrailingWhitespace()
    {
        var bytes = new byte[MaxBytes];
        bytes.AsSpan().Fill((byte)' ');
        "\uFEFF{}"u8.CopyTo(bytes);
        JsonCorruptionTests.RoundTrip(bytes);
        var error = JsonCorruptionTests.Reject([.. bytes, (byte)' ']);
        Assert.Contains("16 MiB", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("fraction")]
    [InlineData("exponent")]
    public void HugeValidNumbersStayReadOnlyAndOtherFieldsRemainEditable(string kind)
    {
        var digits = new string('9', 100_000);
        var number = kind switch { "integer" => digits, "fraction" => "0." + digits, _ => "1e+" + digits };
        var source = "[" + number + ",false]";
        var file = MemorySaveSet.SingleFile("huge.json", Encoding.UTF8.GetBytes(source));
        var plugin = new GenericJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        var fields = JsonEditingTests.Fields(document);
        Assert.True(fields[0].Spec.ReadOnly);
        Assert.Equal(number, fields[0].Value.AsString());
        Assert.Contains("preserved verbatim", fields[0].Warning, StringComparison.Ordinal);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("json-tree", new());
        Assert.Equal(3, page.TotalCount);
        Assert.False(document.ApplyEdits([new(fields[0].ValueId, JsonEditingTests.Number("0"))]).Succeeded);
        Assert.True(document.ApplyEdits([new(fields[1].ValueId, FieldValue.Union("boolean", FieldValue.Of(true)))]).Succeeded);
        var changed = Encoding.UTF8.GetBytes("[" + number + ",true]");
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
        JsonCorruptionTests.RoundTrip(changed);
        Assert.True(document.ApplyEdits([new(fields[1].ValueId, fields[1].Value)]).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EscapedDuplicateNamesKeepDistinctOccurrencesAndEditOnlyAddressedToken()
    {
        const string source = "\uFEFF{\"a\":1,\"\\u0061\":2,\"a\":3,\"nested\":{\"a\":4,\"a\":5}}\n";
        var bytes = Encoding.UTF8.GetBytes(source);
        var file = MemorySaveSet.SingleFile("duplicates.json", bytes);
        var plugin = new GenericJsonPlugin();
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        var fields = JsonEditingTests.Fields(document);
        Assert.Equal(5, fields.Count);
        Assert.Equal(5, fields.Select(f => f.ValueId).Distinct().Count());
        Assert.Equal(DuplicateValues, fields.Select(f => f.Value.AsUnion().Value.AsExactNumber()));
        Assert.True(document.ApplyEdits([new(fields[1].ValueId, JsonEditingTests.Number("20"))]).Succeeded);
        var expected = Encoding.UTF8.GetBytes(source.Replace(":2,", ":20,", StringComparison.Ordinal));
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
        JsonCorruptionTests.RoundTrip(expected);
        Assert.True(document.ApplyEdits([new(fields[1].ValueId, fields[1].Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Nested(int depth, bool objects) => Encoding.UTF8.GetBytes(objects
        ? string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "0" + new string('}', depth)
        : new string('[', depth) + "0" + new string(']', depth));
}
