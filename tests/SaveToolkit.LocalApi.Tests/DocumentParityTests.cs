using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi.Tests.Parity;
using static SaveToolkit.LocalApi.Tests.Parity.Scenarios;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Document view, edit, history, revision and binding-diagnostic parity.</summary>
public sealed class DocumentParityTests : IDisposable
{
    private readonly ParityHosts _hosts = new();

    [Fact]
    public Task Edits_history_and_revisions_match() => _hosts.AssertParityAsync(async t =>
    {
        var (_, doc) = await CounterAsync(t);
        await t.GetAsync(doc);
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"9"}],"revision":0}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"10"}],"revision":0}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"666"}]}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"5000"}]}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"missing","value":"1"}]}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"name","value":"héros ✓"}]}""", ifMatch: "\"2\"");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"flag","value":true}]}""", ifMatch: "\"1\"");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"flag","value":true}]}""", ifMatch: "W/\"2\", \"3\"");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"flag","value":true}]}""", ifMatch: "\"3\"");
        foreach (var header in IfMatchHeaders)
            await t.PostJsonAsync(doc + "/undo", "", ifMatch: header);
        await t.PostJsonAsync(doc + "/undo", """{"revision":1}""");
        await t.PostJsonAsync(doc + "/undo", """{"revision":4}""");
        await t.PostJsonAsync(doc + "/undo", "");
        await t.PostJsonAsync(doc + "/redo", "{}");
        await t.PostJsonAsync(doc + "/redo", "null");
        await t.PostJsonAsync(doc + "/actions", """{"actionId":"max","parameters":{"value":"12"}}""");
        await t.PostJsonAsync(doc + "/actions", """{"actionId":"nope"}""");
        await t.GetAsync(doc + "/changes");
        await t.PostJsonAsync(doc + "/changes/revert", """{"valueId":"counter","revision":99}""");
        await t.PostJsonAsync(doc + "/changes/revert", """{"valueId":"counter"}""");
        await t.GetAsync(doc);
        await t.DeleteAsync(doc);
    });

    [Fact]
    public Task Binding_diagnostics_match() => _hosts.AssertParityAsync(async t =>
    {
        var (ws, doc) = await CounterAsync(t);
        await t.PostJsonAsync(doc + "/edits", "{not json");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"9"}]}""", contentType: "text/plain");
        await t.SendAsync(HttpMethod.Post, doc + "/edits", Raw("""{"edits":[]}""", null));
        await t.PostJsonAsync(doc + "/edits", """{"edits":[]}""", contentType: "application/problem+json");
        await t.SendAsync(HttpMethod.Post, doc + "/edits");
        await t.PostJsonAsync(doc + "/edits", "null");
        await t.PostJsonAsync(doc + "/edits", "{}");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":" "}]}""");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[],"revision":-1}""");
        await t.PostJsonAsync(doc + "/edits", "﻿{\"edits\":[]}");
        await t.PostJsonAsync(doc + "/actions", """{"actionId":""}""");
        await t.PostJsonAsync(doc + "/actions/apply", """{"planId":"p"}""");
        await t.PostJsonAsync(doc + "/undo", "[1]");
        await t.SendAsync(HttpMethod.Post, doc + "/undo", Raw("{}", "text/plain"));
        await t.GetAsync(doc + "/nodes");
        await t.GetAsync(doc + "/nodes?id=");
        await t.GetAsync(doc + "/nodes?id=missing");
        await t.GetAsync(doc + "/blobs?id=b");
        await t.GetAsync(doc + "/blobs?id=b&offset=x&count=1");
        await t.GetAsync(doc + "/blobs?id=b&offset=-1&count=1");
        await t.GetAsync(doc + "/blobs?id=b&offset=0&count=1");
        await t.GetAsync(doc + "/blobs?offset=1,2&count=1");
        await t.GetAsync(doc + "/assets");
        await t.GetAsync(doc + "/assets?id=none");
        await t.GetAsync(doc + "/collections");
        await t.GetAsync(doc + "/collections?collectionId=missing");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&limit=abc");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&limit=");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&sortBy=Bogus");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&sortBy=99");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&descending=maybe");
        await t.GetAsync(doc + "/reference-fields/candidates?valueId=counter&revision=1.5");
        await t.GetAsync(doc + "/reference-fields/record?valueId=counter");
        await t.GetAsync($"{ws}/files/f0/group-proposals");
        await t.GetAsync($"{ws}/files/f0/group-proposals?pluginId=test.counter");
        await t.PostJsonAsync($"{ws}/document-groups", """{"proposalToken":""}""");
        await t.PostJsonAsync($"{ws}/document-groups", """{"proposalToken":"forged"}""");
        await t.PostJsonAsync($"{ws}/files/f0/open", """{"pluginId":"test.missing"}""");
        await t.PostJsonAsync($"{ws}/files/f0/open", "{", contentType: "application/json");
        await t.SendAsync(HttpMethod.Post, $"{ws}/files/f0/open", Raw("{}", "text/plain"));
        await t.GetAsync(doc.Replace("/documents/", "/documents/x", StringComparison.Ordinal));
    });

    [Fact]
    public Task Export_matches() => _hosts.AssertParityAsync(async t =>
    {
        var (ws, doc) = await CounterAsync(t);
        await t.PostJsonAsync(ws + "/export/plan", "{}");
        await t.PostJsonAsync(ws + "/export", "{}");
        await t.PostJsonAsync(ws + "/export", """{"scope":"Everything"}""");
        await t.PostJsonAsync(ws + "/export", """{"scope":42}""");
        await t.PostJsonAsync(ws + "/export", """{"scope":"Nope"}""");
        await t.SendAsync(HttpMethod.Post, ws + "/export");
        await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"9"}]}""");
        await t.PostJsonAsync(ws + "/export/plan", """{"mirrorBackups":false}""");
        await t.PostJsonAsync(ws + "/export", "{}");
        await t.DeleteAsync(doc);
        await t.GetAsync(ws);
    });

    [Fact]
    public Task Collections_match() => _hosts.AssertParityAsync(async t =>
    {
        var workspace = await ImportAsync(t, null, ("q.dat", QueryablePlugin.Create()));
        var doc = await OpenAsync(t, workspace);
        foreach (var query in CollectionQueries)
        {
            await t.GetAsync(doc + "/collections/items" + query);
            await t.GetAsync(doc + "/collections?collectionId=items" + (query.Length > 0 ? "&" + query[1..] : string.Empty));
        }

        foreach (var encoded in EncodedIds) await t.GetAsync(doc + "/collections/" + encoded);
        await t.GetAsync(doc + "/collections?collectionId=a%2Fb");
        await t.PostJsonAsync(doc + "/actions/plan", """{"actionId":"increment","scope":{"collectionId":"items","rowKeys":["1","2"]}}""");
    });

    private static readonly string[] IfMatchHeaders = ["*", "x", "\"4\" junk", "*, \"4\"", " , \"4\" ,", "W/\"4\"", "\"3\", \"4\""];

    [Fact]
    public async Task Oversized_collection_pages_match()
    {
        var plugins = new List<QueryablePlugin>();
        using var hosts = new ParityHosts(() =>
        {
            var plugin = new QueryablePlugin();
            plugins.Add(plugin);
            return [plugin];
        });
        await hosts.AssertParityAsync(async t =>
        {
            var workspace = await ImportAsync(t, null, ("q.dat", QueryablePlugin.Create()));
            var doc = await OpenAsync(t, workspace);
            foreach (var plugin in plugins.Where(p => p.LastOpened is not null))
            {
                plugin.LastOpened!.TransformPage = page => page with
                {
                    Rows = [.. page.Rows.Select(row => row with
                    {
                        Cells = new Dictionary<string, SaveToolkit.Sdk.Views.ViewNode>(row.Cells)
                        {
                            ["note"] = new SaveToolkit.Sdk.Views.TextNode(new string('x', 2_200_000)),
                        },
                    })],
                };
            }

            await t.GetAsync(doc + "/collections/items?limit=1");
            await t.GetAsync(doc);
        });
    }

    private static readonly string[] EncodedIds =
        ["a%2Fb", "a%2fb%2F", "a%20b%C3%A9", "%252F", "%25", "%C3", "%FF%FE", "%E2%9C%93", "x%2", "%41%42", "a+b", "%3Fq%3D1%23"];

    private static readonly string[] CollectionQueries =
    [
            "", "?limit=3", "?offset=5&limit=2&sort=value&descending=true", "?q=00001&limit=2", "?text=Item%20000002",
            "?filters=%7B%22name%22%3A%2200004%22%7D&limit=2", "?filter.value=7", "?Filter.Name=00009&filter.name=000099&limit=1",
            "?cursor=10&limit=2", "?cursor=", "?cursor=1&offset=1", "?limit=0", "?limit=201", "?limit=x", "?offset=-1",
            "?descending=yes", "?includeEmpty=true", "?revision=abc", "?revision=99", "?revision=0", "?filters=%5B%5D",
            "?filters=%7B%22a%22%3Anull%7D", "?filter.=1", "?filter.value=", "?limit=2&limit=3", "?q=a+b",
    ];

    public void Dispose() => _hosts.Dispose();
}
