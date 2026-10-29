using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceBatchApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const int BatchLimit = 256;
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.UseSetting("Toolkit:PackRoot", TestZips.TempDir());
        b.ConfigureTestServices(s => s.AddSingleton(new PluginCatalog([new BatchPlugin()])));
    }).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> OpenAsync()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", ItemsPlugin.Create(1)))), "files", "items.zip");
        var imported = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct);
        imported.EnsureSuccessStatusCode();
        var ws = await imported.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string root = $"/api/workspaces/{ws.GetProperty("id").GetString()}";
        string? file = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await _client.PostAsJsonAsync($"{root}/files/{file}/open", new { }, Ct);
        opened.EnsureSuccessStatusCode();
        var doc = await opened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return $"{root}/documents/{doc.GetProperty("id").GetString()}";
    }

    [Fact]
    public async Task EveryPair_UsesItsOwnFieldCatalogAndPolicy_InOrder_IncludingDuplicatesAndUnknowns()
    {
        string doc = await OpenAsync();
        object[] pairs = [
            new { fieldId = "allowed", recordKey = "2001" },
            new { fieldId = "blocked", recordKey = "2001" },
            new { fieldId = "other-catalog", recordKey = "2001" },
            new { fieldId = "missing", recordKey = "2001" },
            new { fieldId = "allowed", recordKey = "2001" },
            new { fieldId = "parameter", recordKey = "2001", actionId = "choose" },
            new { fieldId = "allowed", recordKey = "unknown/#? " },
            new { fieldId = "plain", recordKey = "2001" },
            new { fieldId = "parameter", recordKey = "2001", actionId = "missing" },
        ];
        var response = await _client.PostAsJsonAsync($"{doc}/reference-fields/records", new { pairs, revision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, body.GetProperty("revision").GetInt64());
        var results = body.GetProperty("results");
        Assert.Equal(pairs.Length, results.GetArrayLength());
        Assert.True(results[0].GetProperty("candidate").GetProperty("eligibility").GetProperty("selectable").GetBoolean());
        Assert.False(results[1].GetProperty("candidate").GetProperty("eligibility").GetProperty("selectable").GetBoolean());
        Assert.False(results[2].GetProperty("candidate").TryGetProperty("label", out _));
        Assert.Equal(404, results[3].GetProperty("status").GetInt32());
        Assert.False(results[3].TryGetProperty("candidate", out _));
        Assert.Equal(results[0].GetRawText(), results[4].GetRawText());
        Assert.Equal("choose", results[5].GetProperty("actionId").GetString());
        Assert.False(results[5].GetProperty("candidate").GetProperty("eligibility").GetProperty("selectable").GetBoolean());
        Assert.Equal("unknown/#? ", results[6].GetProperty("candidate").GetProperty("rawId").GetString());
        Assert.Equal(404, results[7].GetProperty("status").GetInt32());
        Assert.Equal(404, results[8].GetProperty("status").GetInt32());

        var single = await _client.GetFromJsonAsync<JsonElement>($"{doc}/reference-fields/record?valueId=blocked&rawId=2001", Ct);
        Assert.Equal(single.GetRawText(), results[1].GetProperty("candidate").GetRawText());
    }

    [Theory]
    [InlineData("{}", 400)]
    [InlineData("{\"pairs\":null}", 400)]
    [InlineData("{\"pairs\":[null]}", 400)]
    [InlineData("{\"pairs\":[{}]}", 400)]
    [InlineData("{\"pairs\":{}}", 400)]
    [InlineData("{\"pairs\":[{\"fieldId\":\"allowed\",\"recordKey\":42}]}", 400)]
    [InlineData("{\"pairs\":[{\"fieldId\":\"allowed\",\"recordKey\":\"2001\",\"actionId\":\" \"}]}", 400)]
    [InlineData("{\"pairs\":[{\"fieldId\":\"allowed\",\"recordKey\":null}]}", 400)]
    [InlineData("{\"pairs\":[{\"fieldId\":\"\",\"recordKey\":\"1\"}]}", 400)]
    [InlineData("{\"pairs\":[],\"revision\":-1}", 400)]
    [InlineData("{\"pairs\":[],\"revision\":99}", 409)]
    [InlineData("{\"pairs\":[{\"fieldId\":\"missing\",\"recordKey\":\"2001\"}],\"revision\":99}", 409)]
    public async Task MalformedAndStaleRequests_ReturnProblems(string json, int status)
    {
        string doc = await OpenAsync();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(new Uri($"{doc}/reference-fields/records", UriKind.Relative), content, Ct);
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(problem.GetProperty("detail").GetString(), problem.GetProperty("error").GetString());
        if (status == 409) Assert.Equal("stale", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BoundIsInclusive_EmptyIsValid_AndHeadersStillApply()
    {
        string doc = await OpenAsync();
        foreach (int count in new[] { 0, BatchLimit, BatchLimit + 1 })
        {
            var pairs = Enumerable.Repeat(new { fieldId = "allowed", recordKey = "" }, count).ToArray();
            var response = await _client.PostAsJsonAsync($"{doc}/reference-fields/records", new { pairs }, Ct);
            Assert.Equal(count > BatchLimit ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            if (count <= BatchLimit) Assert.Equal(count, body.GetProperty("results").GetArrayLength());
            else Assert.Contains(BatchLimit.ToString(), body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{doc}/reference-fields/records")
        {
            Content = JsonContent.Create(new { pairs = Array.Empty<object>() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await _client.SendAsync(request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"{doc}-missing/reference-fields/records", new { pairs = Array.Empty<object>() }, Ct)).StatusCode);
    }

    private sealed class BatchPlugin : IGamePlugin
    {
        private readonly ItemsPlugin _items = new();
        public PluginInfo Info => _items.Info;
        public ProbeResult Probe(ISaveFile file) => _items.Probe(file);
        public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);
        public ISaveDocument Open(ISaveFile file, IReferenceContext references)
        {
            _ = references.Catalog("items", "1.01");
            return new BatchDocument();
        }
    }

    private sealed class BatchDocument : BoundSaveDocument
    {
        public override string Title => "Batch policies";
        private static ReferenceSpec Blocked => new("items") { Assess = _ => new ReferenceEligibility { Eligible = false, Reasons = ["Blocked here."] } };
        public override ViewNode BuildView() => new SectionNode("Fields", [
            new FieldNode("allowed", "Allowed", new ReferenceSpec("items"), FieldValue.Of("2001")),
            new FieldNode("blocked", "Blocked", Blocked, FieldValue.Of("2001")),
            new FieldNode("other-catalog", "Other catalog", new ReferenceSpec("missing"), FieldValue.Of("2001")),
            new FieldNode("plain", "Plain", new TextSpec(), FieldValue.Of("2001")),
            new ActionNode("choose", "Choose") { Parameters = [new FieldNode("parameter", "Parameter", Blocked, FieldValue.Of("2001"))] },
        ]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
