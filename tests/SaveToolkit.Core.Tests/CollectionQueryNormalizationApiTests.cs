using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class CollectionQueryNormalizationApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir())).CreateClient();

    [Theory]
    [InlineData("filters={\"type\":\"\"}")]
    [InlineData("filter.type=")]
    [InlineData("filters={\"type\":\"number\"}&filter.type=")]
    [InlineData("filter.type=number&filter.type=")]
    [InlineData("q=&text=no-match&sort=")]
    [InlineData("text=&sort=")]
    public async Task ClearedFiltersSearchAndSort_RestoreAllRows(string query)
    {
        var url = await OpenAsync();
        var filtered = await GetPageAsync($"{url}?filter.type=number");
        Assert.Equal(1, filtered.GetProperty("filteredCount").GetInt64());
        var page = await GetPageAsync($"{url}?{query}");
        Assert.Equal(4, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(4, page.GetProperty("filteredCount").GetInt64());
        Assert.Equal(4, page.GetProperty("rows").GetArrayLength());
    }

    [Theory]
    [InlineData("filters={\"type\":\"\",\"state\":\"unchanged\"}", 4)]
    [InlineData("filters={\"type\":\"\",\"state\":\"modified\"}", 0)]
    [InlineData("filters={\"type\":\"number\"}&filter.type=", 4)]
    [InlineData("filters={\"type\":\"\"}&filter.type=number", 1)]
    [InlineData("filter.type=%20", 0)]
    public async Task Normalization_PreservesOtherFiltersOverridesAndLiteralWhitespace(string query, int count)
    {
        var page = await GetPageAsync($"{await OpenAsync()}?{query}");
        Assert.Equal(count, page.GetProperty("filteredCount").GetInt64());
    }

    [Theory]
    [InlineData("filters={\"type\":null}")]
    [InlineData("filter.=number")]
    [InlineData("filter.unknown=number")]
    [InlineData("sort=unknown")]
    [InlineData("cursor=")]
    public async Task InvalidQueries_AreStillRejected(string query)
    {
        using var response = await _client.GetAsync($"{await OpenAsync()}?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("offset=bad&limit=201", "Offset and limit must be integers.")]
    [InlineData("offset=-1&limit=201", "Page size must be between 1 and 200.")]
    [InlineData("offset=-1&descending=bad", "Offset cannot be negative.")]
    [InlineData("descending=bad&filters=null", "Descending, includeEmpty and revision must be valid values.")]
    [InlineData("revision=bad&filters=null", "Descending, includeEmpty and revision must be valid values.")]
    [InlineData("filters=null&cursor=", "Filters must be a JSON object of string values.")]
    [InlineData("cursor=", "Collection query exceeds the protocol limits.")]
    public async Task TransportValidation_PreservesProblemDetailsAndDiagnosticOrder(string query, string diagnostic)
    {
        using var response = await _client.GetAsync($"{await OpenAsync()}?{query}", TestContext.Current.CancellationToken);
        var problem = await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(diagnostic, problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(128, 200)]
    [InlineData(129, 400)]
    public async Task ClearedFilterKeys_StillRespectTheDocumentedLengthBound(int length, int status)
    {
        string key = new('x', length);
        using var response = await _client.GetAsync($"{await OpenAsync()}?filter.{key}=", TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (status == 400)
        {
            var problem = await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal("Collection query exceeds the protocol limits.", problem.GetProperty("detail").GetString());
        }
    }

    private async Task<string> OpenAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("{\"number\":1,\"label\":\"Potion\",\"blank\":\"\"}")),
            "files", "save.json");
        using var upload = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceUrl = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        using var opened = await _client.PostAsJsonAsync($"{workspaceUrl}/files/{file.GetProperty("id").GetString()}/open",
            new { pluginId = "format.json" }, ct);
        opened.EnsureSuccessStatusCode();
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        return $"{workspaceUrl}/documents/{document.GetProperty("id").GetString()}/collections/json-tree";
    }

    private async Task<JsonElement> GetPageAsync(string url)
    {
        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
