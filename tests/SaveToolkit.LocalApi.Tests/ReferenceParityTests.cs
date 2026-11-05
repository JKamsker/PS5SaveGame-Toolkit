using System.Text;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi.Tests.Parity;
using static SaveToolkit.LocalApi.Tests.Parity.Scenarios;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Reference pack, catalog, field reference and reference asset parity.</summary>
public sealed class ReferenceParityTests : IDisposable
{
    private readonly ParityHosts _hosts = new();

    [Fact]
    public Task References_match() => _hosts.AssertParityAsync(async t =>
    {
        var workspace = await ImportAsync(t, null, ("items.zip",
            Zip(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", ItemsPlugin.Create(1, (1001, 3), (2001, 1), (9999, 2))))));
        var doc = await OpenAsync(t, workspace);
        await t.GetAsync(doc);
        await t.GetAsync(doc + "/sources");
        foreach (var query in Queries) await t.GetAsync(doc + "/references/items" + query);
        await t.GetAsync(doc + "/references/missing");
        await t.GetAsync(doc + "/references/items/record?key=1001");
        await t.GetAsync(doc + "/references/items/record?key=nope");
        await t.GetAsync(doc + "/references/items/record");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=items/0/id&q=pot&limit=2");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=items/0/id&revision=0&offset=1");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=items/0/id&revision=5");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=replacement&actionId=replace-first");
        await t.GetAsync(doc + "/reference-fields/record?valueId=items/0/id&rawId=2001");
        await t.GetAsync(doc + "/reference-fields/record?valueId=items/0/id&rawId=31337");
        await t.PostJsonAsync(doc + "/reference-fields/records",
            """{"pairs":[{"fieldId":"items/0/id","recordKey":"1001"},{"fieldId":"nope","recordKey":"1"},{"fieldId":"replacement","recordKey":"2001","actionId":"replace-first"}],"revision":0}""");
        await t.PostJsonAsync(doc + "/reference-fields/records", """{"pairs":[],"revision":3}""");
        await t.PostJsonAsync(doc + "/reference-fields/records", """{"pairs":null}""");
        await t.PostJsonAsync(doc + "/reference-fields/records", """{"pairs":[{"fieldId":"","recordKey":"1"}]}""");
        var many = string.Join(",", Enumerable.Repeat("""{"fieldId":"a","recordKey":"1"}""", 257));
        await t.PostJsonAsync(doc + "/reference-fields/records", "{\"pairs\":[" + many + "]}");
        var view = await t.GetAsync(doc);
        var asset = FindAssetId(view.Text);
        await t.GetAsync(doc + "/reference-assets?id=" + Uri.EscapeDataString(asset ?? "none"));
        await t.GetAsync(doc + "/reference-assets?id=none");
        await t.GetAsync(doc + "/assets?id=" + Uri.EscapeDataString(asset ?? "none"));
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"items/0/id","value":"2001"}]}""");
        await t.PostJsonAsync(doc + "/actions", """{"actionId":"replace-first","parameters":{"replacement":"1001"}}""");
        await t.GetAsync(doc);
    });

    [Fact]
    public Task Reference_packs_match() => _hosts.AssertParityAsync(async t =>
    {
        await t.GetAsync("/api/reference-packs");
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", Raw("{}", "application/json"));
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", new MultipartFormDataContent());
        var two = new MultipartFormDataContent
        {
            { new ByteArrayContent([1]), "files", "a.zip" },
            { new ByteArrayContent([2]), "files", "b.zip" },
        };
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", two);
        var broken = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("not a zip")), "files", "pack.zip" } };
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", broken);
        var crossSite = new MultipartFormDataContent { { new ByteArrayContent([1]), "files", "pack.zip" } };
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", crossSite, null, ("Sec-Fetch-Site", "cross-site"));
        await t.SendAsync(HttpMethod.Delete, "/api/reference-packs/test.items.en", null, null, ("Origin", "http://evil.example"));
        await t.SendAsync(HttpMethod.Delete, "/api/reference-packs/test.items.en", null, null, ("Origin", "http://localhost"));
        await t.DeleteAsync("/api/reference-packs/test.items.en");
        await t.DeleteAsync("/api/reference-packs/missing");
        var pack = new MultipartFormDataContent { { new ByteArrayContent(PackBuilder.WithItems().Zip()), "pack", "user items.zip" } };
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", pack, null, ("Origin", "http://localhost"), ("Sec-Fetch-Site", "same-origin"));
        var again = new MultipartFormDataContent { { new ByteArrayContent(PackBuilder.WithItems().Zip()), "pack", "user items.zip" } };
        await t.SendAsync(HttpMethod.Post, "/api/reference-packs", again);
        await t.GetAsync("/api/reference-packs");
        await t.DeleteAsync("/api/reference-packs/user.items");
        await t.GetAsync("/api/reference-packs");
    });

    private static readonly string[] Queries =
    [
        "", "?q=pot", "?category=weapon", "?category=", "?sortBy=Name&descending=true", "?sortBy=name", "?sortBy=7",
        "?offset=-5&limit=1000", "?offset=1&limit=0", "?limit=x", "?descending=", "?q=%E2%9C%93",
    ];

    private static string? FindAssetId(string json)
    {
        const string marker = "\"assetId\":\"";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        return json[start..json.IndexOf('"', start)];
    }

    public void Dispose() => _hosts.Dispose();
}
