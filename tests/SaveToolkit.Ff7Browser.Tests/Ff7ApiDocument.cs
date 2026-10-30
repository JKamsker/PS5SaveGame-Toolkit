using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

/// <summary>All probe/open gates run through multipart ZIP import and normal file open.</summary>
internal sealed class Ff7ApiDocument(Ff7ApiHost host, string workspace, string path, Ff7Binary fixture) : IAsyncDisposable
{
    public Ff7ApiHost Host { get; } = host;
    public string Workspace { get; } = workspace;
    public string Path { get; } = path;
    public Ff7Binary Fixture { get; } = fixture;

    public static async Task<Ff7ApiDocument> Open(Ff7ApiHost host, Ff7Binary fixture, string? fileName = null)
    {
        var (workspace, file, _) = await Import(host, fixture, fileName);
        var opened = await host.Send($"{workspace}/files/{file}/open", new { pluginId = "ff7r" });
        Assert.Equal(200, opened.Status);
        Assert.Equal("ff7r", opened.Body["plugin"]!["id"]!.GetValue<string>());
        Assert.Equal(fixture.TitleId, opened.Body["titleId"]!.GetValue<string>());
        return new(host, workspace, $"{workspace}/documents/{opened.Body["id"]!.GetValue<string>()}", fixture);
    }

    public static async Task<(string Workspace, string File, JsonObject Info)> Import(Ff7ApiHost host,
        Ff7Binary fixture, string? fileName = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(fixture.Zip()), "files", "authored-ff7.zip");
        using var imported = await host.Client.PostAsync("/api/workspaces", form, Ff7ApiHost.Ct);
        imported.EnsureSuccessStatusCode();
        var responseBytes = await imported.Content.ReadAsByteArrayAsync(Ff7ApiHost.Ct);
        await host.Retain("import.response.json", responseBytes);
        var info = JsonNode.Parse(responseBytes)!.AsObject();
        var files = info["titles"]![0]!["directories"]![0]!["files"]!.AsArray();
        var file = files.Single(node => node!["path"]!.GetValue<string>() == (fileName ?? fixture.GameplayName))!;
        return ($"/api/workspaces/{info["id"]!.GetValue<string>()}", file["id"]!.GetValue<string>(), file.AsObject());
    }

    public async Task<JsonObject> Capture(string action, object parameters, string scope = "document", string[]? rows = null)
    {
        var captured = await Host.Send(Path + "/presets/capture", new
        {
            name = "Authored " + Guid.NewGuid().ToString("N"), actionId = action, parameters, scopeKind = scope,
            selectedRowKeys = rows, allMatches = scope is "allCopies" or "allEligible",
        });
        Assert.Equal(200, captured.Status);
        return captured.Body;
    }

    public async Task<(int Status, JsonObject Body)> Preview(JsonObject preset, long revision = 0, object? choices = null) =>
        await Host.Send($"{Path}/presets/{preset["id"]!.GetValue<string>()}/preview", new { revision, choices });

    public async Task<(int Status, JsonObject Body)> Apply(JsonObject preset, JsonObject preview) =>
        await Host.Send($"{Path}/presets/{preset["id"]!.GetValue<string>()}/apply", new
        {
            planId = preview["preview"]!["planId"]!.GetValue<string>(),
            revision = preview["preview"]!["revision"]!.GetValue<long>(),
        });

    public async Task<Dictionary<string, byte[]>> Export()
    {
        using var response = await Host.Client.PostAsJsonAsync(Workspace + "/export", new { scope = "Everything", mirrorBackups = false }, Ff7ApiHost.Ct);
        response.EnsureSuccessStatusCode();
        var zipBytes = await response.Content.ReadAsByteArrayAsync(Ff7ApiHost.Ct);
        await Host.Retain("whole-export.zip", zipBytes);
        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        var members = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            using var output = new MemoryStream();
            await entry.Open().CopyToAsync(output, Ff7ApiHost.Ct);
            members.Add(entry.FullName, output.ToArray());
        }
        var manifest = members.ToDictionary(pair => pair.Key, pair => new { length = pair.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)) });
        await File.WriteAllTextAsync(System.IO.Path.Combine(Host.Root, "members-" + Guid.NewGuid().ToString("N") + ".json"),
            System.Text.Json.JsonSerializer.Serialize(manifest), Ff7ApiHost.Ct);
        return members;
    }

    public async Task AssertBytes(byte[] expected)
    {
        var members = await Export();
        Assert.Equal(2, members.Count);
        Assert.Equal(expected, members[Fixture.GameplayPath]);
        Assert.Equal(Fixture.Common, members[Fixture.CommonPath]);
        Assert.Equal(expected, await Host.Client.GetByteArrayAsync(Path + "/download", Ff7ApiHost.Ct));
    }

    public async Task<AcceptanceSnapshot> Snapshot() => new(
        (await Host.Get(Path))["state"]!.ToJsonString(), (await Host.Get("/api/presets")).ToJsonString(), await Export());

    public async Task Unchanged(AcceptanceSnapshot before)
    {
        var after = await Snapshot();
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.Library, after.Library);
        Assert.Equal(before.Members.Keys.Order(), after.Members.Keys.Order());
        foreach (var (name, bytes) in before.Members) Assert.Equal(bytes, after.Members[name]);
    }

    public async ValueTask DisposeAsync()
    {
        using var response = await Host.Client.DeleteAsync(Workspace, Ff7ApiHost.Ct);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed record AcceptanceSnapshot(string State, string Library, IReadOnlyDictionary<string, byte[]> Members);
