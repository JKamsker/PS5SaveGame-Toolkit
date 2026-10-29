using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Benchmarks;

internal static class StartupHttpCycle
{
    public static async Task RunAsync(HttpClient client, byte[] zip)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "synthetic.zip");
        using var imported = await client.PostAsync("/api/workspaces", form);
        imported.EnsureSuccessStatusCode();
        var workspace = JsonSerializer.Deserialize<JsonElement>(await imported.Content.ReadAsStringAsync());
        var url = "/api/workspaces/" + workspace.GetProperty("id").GetString();
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        using var opened = await client.PostAsJsonAsync(url + "/files/" + file.GetProperty("id").GetString() + "/open",
            new { pluginId = new SaveToolkit.Plugins.GenericJson.GenericJsonPlugin().Info.Id });
        opened.EnsureSuccessStatusCode();
        var document = JsonSerializer.Deserialize<JsonElement>(await opened.Content.ReadAsStringAsync());
        using var closed = await client.DeleteAsync(url + "/documents/" + document.GetProperty("id").GetString());
        closed.EnsureSuccessStatusCode();
        using var removed = await client.DeleteAsync(url);
        removed.EnsureSuccessStatusCode();
    }
}
