using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceTransactionTests
{
    [Theory]
    [InlineData("root", false)]
    [InlineData("expand", false)]
    [InlineData("query", false)]
    [InlineData("envelope", false)]
    [InlineData("root", true)]
    [InlineData("expand", true)]
    [InlineData("query", true)]
    public async Task FailedOutputPublishesNoBindingsMetadataOrExpansionAndPreservesPriorState(string operation, bool annotationConflict)
    {
        var plugin = new FieldReferenceTransactionPlugin();
        using var fixture = new ApiContractFixture(plugin);
        var (_, url) = await fixture.OpenAsync();
        var document = Assert.Single(plugin.Opened);
        using var established = await fixture.Client.GetAsync(url + "/nodes?id=good", ApiContractFixture.Ct);
        established.EnsureSuccessStatusCode();
        Assert.Equal(1, document.GoodExpansions);
        document.AnnotationConflict = annotationConflict;
        document.OversizedEnvelope = operation == "envelope";
        document.FaultActive = true;
        document.FailRoot = operation == "root";
        var suffix = operation switch { "expand" => "/nodes?id=bad", "query" or "envelope" => "/collections?collectionId=items&limit=1", _ => "" };
        using var failed = await fixture.Client.GetAsync(url + suffix, ApiContractFixture.Ct);
        var problem = await ApiProblemContractTests.AssertProblemAsync(failed,
            annotationConflict ? HttpStatusCode.Conflict : HttpStatusCode.RequestEntityTooLarge);
        if (operation == "envelope") Assert.Contains("collection page", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        document.FailRoot = false;
        document.FaultActive = false;
        document.OversizedEnvelope = false;

        // A failed presentation must not make newly encountered IDs writable or remember a metadata
        // registration that would later retire this previously view-only canonical field.
        var hidden = await Edit(fixture, url, "hidden", "6", HttpStatusCode.UnprocessableEntity);
        Assert.False(hidden.GetProperty("state").GetProperty("isDirty").GetBoolean());
        Assert.Equal(0, hidden.GetProperty("state").GetProperty("revision").GetInt64());
        Assert.False(hidden.GetProperty("state").GetProperty("canUndo").GetBoolean());
        Assert.Equal(0, document.HiddenWrites);
        using var action = await fixture.Client.PostAsJsonAsync(url + "/actions", new { actionId = "hidden-action" }, ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, action.StatusCode);
        Assert.Equal(0, document.HiddenActions);
        using var lazy = await fixture.Client.GetAsync(url + "/nodes?id=hidden-lazy", ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.NotFound, lazy.StatusCode);
        await Edit(fixture, url, "visible", "2", HttpStatusCode.OK);
        using var undo = await fixture.Client.PostAsync(url + "/undo", null, ApiContractFixture.Ct);
        undo.EnsureSuccessStatusCode();
        using var current = await fixture.Client.GetAsync(url, ApiContractFixture.Ct);
        current.EnsureSuccessStatusCode(); // Conflicting learned annotations must not poison the prior reference.
        var view = (await current.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct)).GetProperty("view");
        Assert.Equal("Canonical caution", view.GetProperty("children")[1].GetProperty("warning").GetString());

        using var rebuild = await fixture.Client.PostAsJsonAsync(url + "/actions", new { actionId = "rebuild" }, ApiContractFixture.Ct);
        rebuild.EnsureSuccessStatusCode();
        var result = await rebuild.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        Assert.Empty(result.GetProperty("warnings").EnumerateArray());
        Assert.Equal(2, document.GoodExpansions); // The previously successful expansion remains remembered.
        Assert.Equal(operation == "expand" ? 1 : 0, document.BadExpansions);
        await Edit(fixture, url, "hidden", "6", HttpStatusCode.UnprocessableEntity);
        await Edit(fixture, url, "metadata-only", "4", HttpStatusCode.UnprocessableEntity);
        await Edit(fixture, url, "established", "3", HttpStatusCode.OK);
        Assert.Equal(0, document.HiddenWrites);
    }

    private static async Task<JsonElement> Edit(ApiContractFixture fixture, string url, string id, string value, HttpStatusCode expected)
    {
        using var response = await fixture.Client.PostAsJsonAsync(url + "/edits",
            new { edits = new[] { new { valueId = id, value } } }, ApiContractFixture.Ct);
        Assert.Equal(expected, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        if (expected != HttpStatusCode.OK) Assert.False(result.TryGetProperty("stale", out var stale) && stale.GetBoolean());
        return result;
    }
}
