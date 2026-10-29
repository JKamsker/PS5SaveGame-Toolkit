using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class ApiProblemContractTests(ApiContractFixture fixture) : IClassFixture<ApiContractFixture>
{
    [Theory]
    [InlineData("GET", "/api/workspaces/missing", 404)]
    [InlineData("DELETE", "/api/workspaces/missing", 404)]
    [InlineData("DELETE", "/api/reference-packs/missing", 404)]
    [InlineData("POST", "/api/plugins", 405)]
    [InlineData("GET", "/api/no-such-endpoint", 404)]
    public async Task Routing_and_missing_resources_have_problem_metadata(string method, string path, int status)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await fixture.Client.SendAsync(request, ApiContractFixture.Ct);
        await AssertProblemAsync(response, (HttpStatusCode)status);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("PUT")]
    public async Task Unsupported_methods_are_405_even_when_the_spa_fallback_accepts_the_method(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/api/plugins", UriKind.Relative));
        using var response = await fixture.Client.SendAsync(request, ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("GET", response.Content.Headers.Allow);
        if (method != "HEAD") await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("edits", "{}")]
    [InlineData("edits", "{\"edits\":null}")]
    [InlineData("edits", "{\"edits\":[null]}")]
    [InlineData("edits", "{\"edits\":[{\"valueId\":null,\"value\":3}]}")]
    [InlineData("edits", "{\"edits\":[{\"valueId\":\"counter\"}]}")]
    [InlineData("edits", "{\"edits\":[],\"revision\":-1}")]
    [InlineData("actions", "{}")]
    [InlineData("actions", "{\"actionId\":null}")]
    [InlineData("actions/plan", "{}")]
    [InlineData("actions/apply", "{\"planId\":\"x\"}")]
    [InlineData("actions/apply", "{\"planId\":null,\"revision\":0}")]
    [InlineData("changes/revert", "{\"valueId\":\"counter\"}")]
    [InlineData("changes/revert", "{\"valueId\":null,\"revision\":0}")]
    public async Task Invalid_request_shape_is_400_without_mutation(string path, string json)
    {
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{document}/{path}", json);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var current = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
        Assert.Equal(0, current.GetProperty("state").GetProperty("revision").GetInt64());
    }

    [Theory]
    [InlineData("{", 400)]
    [InlineData("{\"scope\":999}", 400)]
    [InlineData("{}", 422)]
    public async Task Export_failures_have_problem_metadata(string json, int status)
    {
        var (workspace, _) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{workspace}/export", json);
        await AssertProblemAsync(response, (HttpStatusCode)status);
    }

    [Fact]
    public async Task Semantic_errors_preserve_structured_edit_and_preview_bodies()
    {
        var (_, document) = await fixture.OpenAsync();
        using var edit = await fixture.PostAsync($"{document}/edits",
            "{\"edits\":[{\"valueId\":\"counter\",\"value\":1001}],\"revision\":0}");
        var body = await AssertProblemAsync(edit, HttpStatusCode.UnprocessableEntity);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        Assert.Equal(0, body.GetProperty("state").GetProperty("revision").GetInt64());

        using var preview = await fixture.PostAsync($"{document}/actions/plan", "{\"actionId\":\"missing\"}");
        body = await AssertProblemAsync(preview, HttpStatusCode.UnprocessableEntity);
        Assert.NotEmpty(body.GetProperty("plan").GetProperty("errors").EnumerateArray());
        Assert.Equal(0, body.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task Explicit_document_404_has_the_same_error_contract()
    {
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.Client.GetAsync(new Uri($"{document}/assets?id=missing", UriKind.Relative), ApiContractFixture.Ct);
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/workspaces")]
    [InlineData("/api/reference-packs")]
    public async Task Malformed_multipart_envelope_is_400(string path)
    {
        using var content = new ByteArrayContent([1]);
        content.Headers.ContentType = new("multipart/form-data");
        using var response = await fixture.Client.PostAsync(new Uri(path, UriKind.Relative), content, ApiContractFixture.Ct);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Wrong_json_content_type_is_415()
    {
        var (workspace, _) = await fixture.OpenAsync();
        using var content = new StringContent("{}");
        using var response = await fixture.Client.PostAsync(new Uri($"{workspace}/export", UriKind.Relative), content, ApiContractFixture.Ct);
        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"proposalToken\":null}")]
    public async Task Invalid_group_confirmation_shape_is_400(string json)
    {
        var (workspace, _) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{workspace}/document-groups", json);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    internal static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal("about:blank", body.GetProperty("type").GetString());
        Assert.Equal(Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase((int)status),
            body.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.Equal(body.GetProperty("detail").GetString(), body.GetProperty("error").GetString());
        return body;
    }
}
