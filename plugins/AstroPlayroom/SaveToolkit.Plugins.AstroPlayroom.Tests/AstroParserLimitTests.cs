using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroParserLimitTests
{
    private const string Prefix = "\r\n<PreInstall>";
    private const string Suffix = "</PreInstall>";
    private const int MaximumDepth = 64;
    private const int MaximumElements = 4096;
    private const int MaximumValues = 4096;
    private const int MaximumRawPageEntries = 65;

    [Theory]
    [InlineData(MaximumDepth, true)]
    [InlineData(MaximumDepth + 1, false)]
    public void DepthLimitIncludesTheRoot(int depth, bool accepted)
    {
        var text = Prefix + string.Concat(Enumerable.Repeat("<N>", depth - 1)) + "value" +
            string.Concat(Enumerable.Repeat("</N>", depth - 1)) + Suffix;
        Check(text, accepted, depth, 1, "ASTRO tree exceeds the supported depth or element count.");
    }

    [Theory]
    [InlineData(MaximumElements, true)]
    [InlineData(MaximumElements + 1, false)]
    public void ElementLimitIncludesEmptyElementsAndTheRoot(int elements, bool accepted)
    {
        var text = Prefix + string.Concat(Enumerable.Repeat("<N/>", elements - 1)) + Suffix;
        Check(text, accepted, elements, 0, "ASTRO tree exceeds the supported depth or element count.");
    }

    [Theory]
    [InlineData(MaximumValues, true)]
    [InlineData(MaximumValues + 1, false)]
    public void ValueLimitCountsAttributes(int values, bool accepted)
    {
        var attributes = string.Concat(Enumerable.Range(0, values).Select(i => $" a{i}='x'"));
        Check(Prefix + "<N" + attributes + "/>" + Suffix, accepted, 2, MaximumValues,
            "ASTRO tree exceeds the supported value count.");
    }

    [Theory]
    [InlineData(MaximumValues, true)]
    [InlineData(MaximumValues + 1, false)]
    public void ValueLimitCountsMixedTextSegments(int values, bool accepted)
    {
        // Comments split text tokens without adding elements or attributes.
        var text = Prefix + string.Concat(Enumerable.Repeat("x<!--split-->", values)) + Suffix;
        Check(text, accepted, 1, MaximumValues, "ASTRO tree exceeds the supported value count.");
    }

    private static void Check(string text, bool accepted, int expectedElements, int expectedValues, string diagnostic)
    {
        var bytes = AstroTestData.Bytes(text);
        if (!accepted)
        {
            var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(bytes));
            Assert.StartsWith(diagnostic, error.Message, StringComparison.Ordinal);
            Assert.Contains("(byte 0x", error.Message, StringComparison.Ordinal);
            return;
        }

        using var document = AstroTestData.Open(bytes);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var schema = Assert.IsType<SectionNode>(Assert.Single(Assert.Single(tabs.Tabs, tab => tab.Title == "Schema").Children));
        var stats = Assert.Single(schema.Children.OfType<StatsNode>());
        Assert.Equal(expectedElements.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(stats.Items, stat => stat.Label == "Elements").Value);
        Assert.Equal(expectedValues, RawValueCount(document));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    private static int RawValueCount(ISaveDocument document)
    {
        // Traverse Raw only: the Schema table legitimately adds over 12,000 cells for
        // 4,095 top-level empty elements, exceeding the general-purpose test walk's budget.
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var raw = Assert.Single(tabs.Tabs, tab => tab.Title == "Raw");
        var queue = new Queue<ViewNode>(raw.Children);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var fields = new HashSet<string>(StringComparer.Ordinal);
        while (queue.TryDequeue(out var node))
        {
            if (node is FieldNode field) Assert.True(fields.Add(field.ValueId));
            if (node is not SectionNode { LazyId: { } id }) continue;
            Assert.True(expanded.Add(id), "Raw traversal repeated a lazy page.");
            Assert.True(expanded.Count <= MaximumElements + MaximumValues);
            var children = document.ExpandNode(id);
            Assert.True(children.Count <= MaximumRawPageEntries);
            foreach (var child in children) queue.Enqueue(child);
        }

        return fields.Count;
    }
}
