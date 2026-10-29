using System.Text;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AstroRawPagingPerformanceTests
{
    private const string NodeId = "/PreInstall[0]/Unknown[0]";

    [Fact]
    public void LateRawPagesAllocateOnlyTheirVisibleValueNodes()
    {
        var text = new StringBuilder("\r\n<PreInstall><Unknown");
        for (var i = 0; i < 2048; i++) text.Append($" a{i}='v{i}'");
        text.Append("/></PreInstall>");
        var bytes = AstroTestData.Bytes(text.ToString());
        using var document = AstroTestData.Open(bytes);
        var allocated = AstroPerformanceData.Allocated(() => _ = document.ExpandNode(NodeId + "|31"));
        allocated.AssertInRange(0, 45_000);
        var page = document.ExpandNode(NodeId + "|31");
        Assert.Equal(64, page.Count);
        Assert.Equal(Enumerable.Range(1984, 64).Select(i => NodeId + $"/@a{i}"),
            page.Cast<FieldNode>().Select(field => field.ValueId));
        Assert.Equal(Enumerable.Range(1984, 64).Select(i => $"v{i}"),
            page.Cast<FieldNode>().Select(field => field.Value.AsString()));
        Assert.All(page.Cast<FieldNode>(), field => Assert.Equal(field.Value, document.GetValue(field.ValueId)));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(63, 65)]
    [InlineData(64, 64)]
    [InlineData(65, 64)]
    [InlineData(130, 65)]
    public void RawPagesRetainValueThenChildOrderingAndExactContinuation(int attributes, int children)
    {
        var text = new StringBuilder("\r\n<PreInstall><Unknown");
        for (var i = 0; i < attributes; i++) text.Append($" a{i}='v{i}'");
        text.Append('>');
        for (var i = 0; i < children; i++) text.Append($"<child{i}/>");
        text.Append("</Unknown></PreInstall>");
        using var document = AstroTestData.Open(AstroTestData.Bytes(text.ToString()));
        var expected = Enumerable.Range(0, attributes).Select(i => NodeId + $"/@a{i}")
            .Concat(Enumerable.Range(0, children).Select(i => NodeId + $"/child{i}[0]")).ToArray();
        for (var page = 0; page <= expected.Length / 64; page++)
        {
            var nodes = document.ExpandNode(NodeId + $"|{page}");
            var more = nodes.OfType<SectionNode>().Where(section => section.Title == "More…").ToArray();
            var actual = nodes.Where(node => node is not SectionNode { Title: "More…" }).Select(node => node switch
            {
                FieldNode field => field.ValueId,
                SectionNode section => section.LazyId,
                _ => throw new InvalidOperationException("Unexpected raw node.")
            });
            Assert.Equal(expected.Skip(page * 64).Take(64), actual);
            if ((page + 1) * 64 < expected.Length)
                Assert.Equal(NodeId + $"|{page + 1}", Assert.Single(more).LazyId);
            else Assert.Empty(more);
        }
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(NodeId + $"|{expected.Length / 64 + 1}"));
    }
}
