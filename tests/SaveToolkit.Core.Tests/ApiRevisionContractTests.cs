using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class ApiRevisionContractTests(ApiContractFixture fixture) : IClassFixture<ApiContractFixture>
{
    private const string Edit = "{\"edits\":[{\"valueId\":\"counter\",\"value\":6}],\"revision\":0}";

    [Fact]
    public async Task Revision_etags_and_history_preconditions_preserve_state()
    {
        var (_, document) = await fixture.OpenAsync();
        using var initial = await fixture.Client.GetAsync(new Uri(document, UriKind.Relative), ApiContractFixture.Ct);
        Assert.Equal("\"0\"", initial.Headers.ETag?.Tag);
        using var edited = await fixture.PostAsync($"{document}/edits", Edit, "\"0\"");
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("\"1\"", edited.Headers.ETag?.Tag);

        using var staleHeader = await fixture.PostAsync($"{document}/undo", "{}", "\"0\"");
        var body = await ApiProblemContractTests.AssertProblemAsync(staleHeader, HttpStatusCode.PreconditionFailed);
        Assert.Equal("stale", body.GetProperty("code").GetString());
        using var staleBody = await fixture.PostAsync($"{document}/undo", "{\"revision\":0}");
        body = await ApiProblemContractTests.AssertProblemAsync(staleBody, HttpStatusCode.Conflict);
        Assert.True(body.GetProperty("stale").GetBoolean());
        Assert.Equal(1, body.GetProperty("state").GetProperty("revision").GetInt64());

        using var undone = await fixture.PostAsync($"{document}/undo", "{\"revision\":1}", "\"1\"");
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal("\"2\"", undone.Headers.ETag?.Tag);
        using var staleRedo = await fixture.PostAsync($"{document}/redo", "{\"revision\":1}");
        await ApiProblemContractTests.AssertProblemAsync(staleRedo, HttpStatusCode.Conflict);
        using var redone = await fixture.PostAsync($"{document}/redo", "{\"revision\":2}");
        Assert.Equal(HttpStatusCode.OK, redone.StatusCode);
        Assert.Equal("\"3\"", redone.Headers.ETag?.Tag);
    }

    [Theory]
    [InlineData("edits", Edit)]
    [InlineData("actions", "{\"actionId\":\"toggle-hidden\"}")]
    [InlineData("actions/plan", "{\"actionId\":\"max\"}")]
    [InlineData("actions/apply", "{\"planId\":\"missing\",\"revision\":0}")]
    [InlineData("changes/revert", "{\"valueId\":\"counter\",\"revision\":0}")]
    public async Task Every_document_operation_honors_if_match(string route, string json)
    {
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{document}/{route}", json, "\"99\"");
        await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);
        var current = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
        Assert.Equal(0, current.GetProperty("state").GetProperty("revision").GetInt64());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("*, \"0\"")]
    [InlineData("\"unclosed")]
    public async Task Unsupported_revision_etags_are_400(string tag)
    {
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{document}/edits", Edit, tag);
        await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("W/\"0\"", 412)]
    [InlineData("\"-1\"", 412)]
    [InlineData("\"unrelated\"", 412)]
    [InlineData("\"99\", \"0\"", 200)]
    [InlineData("*", 200)]
    public async Task If_match_uses_strong_comparison_and_supports_lists_and_existence(string tag, int status)
    {
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.PostAsync($"{document}/edits", Edit, tag);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Fact]
    public async Task Competing_edits_with_same_if_match_only_apply_once()
    {
        var (_, document) = await fixture.OpenAsync();
        const string edit = "{\"edits\":[{\"valueId\":\"flag\",\"value\":true}]}";
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => fixture.PostAsync($"{document}/edits", edit, "\"0\"")));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.PreconditionFailed));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Competing_history_requests_with_same_revision_only_apply_once()
    {
        var (_, document) = await fixture.OpenAsync();
        using var edited = await fixture.PostAsync($"{document}/edits", Edit);
        var requests = Enumerable.Range(0, 8).Select(_ => fixture.PostAsync($"{document}/undo", "{\"revision\":1}"));
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var current = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
            Assert.Equal(2, current.GetProperty("state").GetProperty("revision").GetInt64());
            Assert.True(current.GetProperty("state").GetProperty("canRedo").GetBoolean());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
