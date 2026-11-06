using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.LocalApi.Tests.Parity;

/// <summary>Shared scenario steps; each registers the ids it learns so transcripts normalize them.</summary>
internal static class Scenarios
{
    public static byte[] Zip(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static async Task<JsonElement> ImportAsync(Transcript t, string? name, params (string FileName, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (fileName, content) in files) form.Add(new ByteArrayContent(content), "files", fileName);
        if (name is not null) form.Add(new StringContent(name), "name");
        var response = await t.SendAsync(HttpMethod.Post, "/api/workspaces", form);
        Assert.Equal(200, response.Status);
        var workspace = response.Json;
        t.Alias(workspace.GetProperty("id").GetString(), "<ws>");
        var directory = 0;
        var file = 0;
        foreach (var title in workspace.GetProperty("titles").EnumerateArray())
            foreach (var dir in title.GetProperty("directories").EnumerateArray())
            {
                t.Alias(dir.GetProperty("id").GetString(), $"<dir{directory++}>");
                foreach (var entry in dir.GetProperty("files").EnumerateArray()) t.Alias(entry.GetProperty("id").GetString(), $"<file{file++}>");
            }

        return workspace;
    }

    public static string WorkspaceUrl(JsonElement workspace) => $"/api/workspaces/{workspace.GetProperty("id").GetString()}";

    public static string FirstFileId(JsonElement workspace) =>
        workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString()!;

    public static async Task<string> OpenAsync(Transcript t, JsonElement workspace, string? fileId = null, string body = "{}", string alias = "<doc>")
    {
        var url = WorkspaceUrl(workspace);
        var response = await t.PostJsonAsync($"{url}/files/{fileId ?? FirstFileId(workspace)}/open", body);
        Assert.Equal(200, response.Status);
        var id = response.Json.GetProperty("id").GetString();
        t.Alias(id, alias);
        return $"{url}/documents/{id}";
    }

    /// <summary>Imports one counter save and opens it; returns the workspace and document URLs.</summary>
    public static async Task<(string Workspace, string Document)> CounterAsync(Transcript t, string fileName = "保存.dat")
    {
        var workspace = await ImportAsync(t, "A dump / test", (fileName, CounterPlugin.Create(5)));
        return (WorkspaceUrl(workspace), await OpenAsync(t, workspace));
    }

    public static HttpContent Raw(string text, string? contentType)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        if (contentType is not null) content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }
}
