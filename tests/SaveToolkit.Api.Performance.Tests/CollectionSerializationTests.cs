using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class CollectionSerializationTests
{
    private const long RejectionAllocationLimit = 8 * 1024 * 1024;

    [Fact]
    public void RejectedPageDoesNotAllocateItsEntireJsonBody()
    {
        var cells = Enumerable.Range(0, 32).ToDictionary(i => $"c{i}", _ => (ViewNode)new TextNode(new string('x', 4096)));
        var rows = Enumerable.Range(0, 200).Select(i => new TableRow($"row{i}", cells)).ToArray();
        var page = Page(rows);
        _ = CollectionPageResponse.Create(page);

        var allocated = AllocationProbe.Measure(() => CollectionPageResponse.Create(page), out var response);

        var error = Assert.IsType<JsonHttpResult<ErrorDto>>(response);
        Assert.Equal(413, error.StatusCode);
        Assert.Equal("The collection page exceeds the response size limit.", error.Value?.Error);
        allocated.AssertLessThan(RejectionAllocationLimit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ExactByteLimitIsPreserved(int difference)
    {
        var overhead = JsonSerializer.SerializeToUtf8Bytes(TextPage(""), ToolkitJson.Options).Length;
        var page = TextPage(new string('x', CollectionPageResponse.MaximumBytes - overhead + difference));
        var response = CollectionPageResponse.Create(page);

        if (difference > 0)
        {
            Assert.Equal(413, Assert.IsType<JsonHttpResult<ErrorDto>>(response).StatusCode);
            return;
        }

        var file = Assert.IsType<FileContentHttpResult>(response);
        Assert.Equal("application/json", file.ContentType);
        Assert.Equal(CollectionPageResponse.MaximumBytes + difference, file.FileContents.Length);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(page, ToolkitJson.Options), file.FileContents.ToArray());
    }

    [Fact]
    public void EscapedUnicodeAndPolymorphicCellsKeepTheirWireShape()
    {
        var page = TextPage("quotes: \"\\\n; Unicode: ä 🎮 <>&");
        var file = Assert.IsType<FileContentHttpResult>(CollectionPageResponse.Create(page));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(page, ToolkitJson.Options), file.FileContents.ToArray());
    }

    private static QueryablePageDto TextPage(string text) => Page([new TableRow("row", new Dictionary<string, ViewNode>
    {
        ["text"] = new TextNode(text),
    })]);

    private static QueryablePageDto Page(IReadOnlyList<TableRow> rows) =>
        new("items", [], rows, rows.Count, rows.Count, 0, rows.Count, null, 7);
}
