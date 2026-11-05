using SaveToolkit.LocalApi.Tests.Parity;
using static SaveToolkit.LocalApi.Tests.Parity.Scenarios;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Action review, preset library, document preset and preset transfer parity.</summary>
public sealed class PresetParityTests : IDisposable
{
    private const string Refill = """
        {"name":"Refill ✓","compatibility":{"pluginId":"test.planning","pluginVersion":"1.0.0","titleIds":["PPSA99999"]},
         "actions":[{"actionId":"refill","parameters":{"value":7}}]}
        """;

    private readonly ParityHosts _hosts = new();

    private static async Task<string> PlanningAsync(Transcript t)
    {
        var workspace = await ImportAsync(t, null, ("synthetic.zip", Zip(("PPSA99999/sdimg_SAVE/save.dat", [0]))));
        return await OpenAsync(t, workspace, body: """{"pluginId":"test.planning"}""");
    }

    [Fact]
    public Task Action_review_matches() => _hosts.AssertParityAsync(async t =>
    {
        var doc = await PlanningAsync(t);
        await t.GetAsync(doc + "/nodes?id=items");
        var plan = await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"refill","scope":{"rowKeys":["a"]},"revision":0}""");
        var planId = plan.Json.GetProperty("planId").GetString();
        await t.GetAsync(doc + "/changes");
        await t.PostJsonAsync(doc + "/actions/apply", $$"""{"planId":"{{planId}}","revision":0}""", ifMatch: "\"0\"");
        await t.PostJsonAsync(doc + "/actions/apply", $$"""{"planId":"{{planId}}","revision":0}""");
        await t.GetAsync(doc + "/changes");
        await t.PostJsonAsync(doc + "/changes/revert", """{"valueId":"item/a","revision":1}""");
        await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"refill","revision":0}""");
        await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"refill","scope":{"rowKeys":["a","a"]}}""");
        await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"remove","scope":{"rowKeys":["zz"]}}""");
        var remove = await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"remove"}""");
        await t.PostJsonAsync(doc + "/actions/apply", $$"""{"planId":"{{remove.Json.GetProperty("planId").GetString()}}","revision":2}""");
        await t.PostJsonAsync(doc + "/undo", """{"revision":3}""");
        await t.PostJsonAsync(doc + "/actions", """{"actionId":"legacy"}""");
        await t.GetAsync(doc);
        await t.GetAsync(doc + "/download");
    });

    [Fact]
    public Task Presets_match() => _hosts.AssertParityAsync(async t =>
    {
        var doc = await PlanningAsync(t);
        await t.GetAsync("/api/presets");
        var created = await t.PostJsonAsync("/api/presets", Refill);
        var id = created.Json.GetProperty("id").GetString();
        await t.PostJsonAsync("/api/presets", """{"name":" ","compatibility":null,"actions":[]}""");
        await t.PostJsonAsync("/api/presets", """{"name":"x","compatibility":{"pluginId":"p","pluginVersion":"1","titleIds":["T"]},"actions":[]}""");
        await t.PostJsonAsync("/api/presets", "{\"name\":\"" + new string('x', 600 * 1024) + "\"}");
        await t.SendAsync(HttpMethod.Patch, "/api/presets/" + id, Raw("""{"name":"Renamed"}""", "application/json"));
        await t.SendAsync(HttpMethod.Patch, "/api/presets/missing", Raw("""{"name":"Renamed"}""", "application/json"));
        await t.SendAsync(HttpMethod.Patch, "/api/presets/" + id, Raw("""{"name":""}""", "application/json"));
        await t.GetAsync(doc + "/presets");
        var preview = await t.PostJsonAsync($"{doc}/presets/{id}/preview", """{"revision":0}""");
        var planId = preview.Json.GetProperty("preview").GetProperty("planId").GetString();
        await t.PostJsonAsync($"{doc}/presets/{id}/apply", $$"""{"planId":"{{planId}}","revision":0}""", ifMatch: "\"0\"");
        await t.PostJsonAsync($"{doc}/presets/{id}/apply", $$"""{"planId":"{{planId}}","revision":0}""");
        await t.PostJsonAsync($"{doc}/presets/missing/preview", """{"revision":1}""");
        await t.PostJsonAsync($"{doc}/presets/{id}/preview", """{"revision":-1}""");
        await t.PostJsonAsync($"{doc}/presets/capture", """{"name":"Captured","actionId":"refill","parameters":{"value":3}}""");
        await t.PostJsonAsync($"{doc}/presets/capture", """{"name":"","actionId":"refill"}""");
        await t.PostJsonAsync($"{doc}/presets/capture", """{"name":"Bad scope","actionId":"refill","scopeKind":"everything"}""");
        await t.PostJsonAsync("/api/presets/reload", "");
        await t.GetAsync("/api/presets");
        await t.DeleteAsync("/api/presets/" + id);
        await t.DeleteAsync("/api/presets/" + id);
        await t.GetAsync(doc + "/presets");
    });

    [Fact]
    public Task Preset_transfer_matches() => _hosts.AssertParityAsync(async t =>
    {
        var doc = await PlanningAsync(t);
        var id = (await t.PostJsonAsync("/api/presets", Refill)).Json.GetProperty("id").GetString();
        var export = await t.PostJsonAsync("/api/presets/transfer/export", $$"""{"ids":["{{id}}"]}""");
        await t.PostJsonAsync("/api/presets/transfer/export", """{"ids":["missing"]}""");
        await t.PostJsonAsync("/api/presets/transfer/export", """{"ids":[],"extra":1}""");
        await t.PostJsonAsync("/api/presets/transfer/export", "not json");
        await t.SendAsync(HttpMethod.Post, "/api/presets/transfer/export", Raw("{}", "text/plain"));
        await t.SendAsync(HttpMethod.Post, "/api/presets/transfer/preview", Raw("{\"schemaVersion\":\"9\"}", null));
        var preview = await t.SendAsync(HttpMethod.Post, "/api/presets/transfer/preview", new ByteArrayContent(export.Body)
        {
            Headers = { ContentType = new("application/json") },
        });
        var token = preview.Json.GetProperty("token").GetString();
        await t.PostJsonAsync(doc + "/presets/transfer/compatibility", $$"""{"token":"{{token}}"}""");
        await t.PostJsonAsync(doc + "/presets/transfer/compatibility", """{"token":"missing"}""");
        var resolved = await t.PostJsonAsync("/api/presets/transfer/resolve", $$"""{"token":"{{token}}","choices":[{"index":0,"decision":"keepBoth"}]}""");
        var reviewed = resolved.Json.GetProperty("token").GetString();
        await t.PostJsonAsync("/api/presets/transfer/commit", $$"""{"token":"{{reviewed}}"}""");
        await t.PostJsonAsync("/api/presets/transfer/commit", $$"""{"token":"{{reviewed}}"}""");
        await t.DeleteAsync("/api/presets/transfer/previews/unknown");
        await t.SendAsync(HttpMethod.Post, "/api/presets/transfer/resolve", Raw(new string(' ', 70 * 1024), "application/json"));
        await t.GetAsync("/api/presets");
    });

    public void Dispose() => _hosts.Dispose();
}
