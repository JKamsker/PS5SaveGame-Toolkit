using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ApiDownloadContractTests(ApiContractFixture fixture) : IClassFixture<ApiContractFixture>
{
    [Fact]
    public async Task Downloads_preserve_bytes_content_type_and_unicode_filename_before_and_after_edit()
    {
        var (_, document) = await fixture.OpenAsync();
        await AssertDownloadAsync(document, CounterPlugin.Create(5));
        using var edit = await fixture.PostAsync($"{document}/edits",
            "{\"edits\":[{\"valueId\":\"counter\",\"value\":6}],\"revision\":0}");
        edit.EnsureSuccessStatusCode();
        await AssertDownloadAsync(document, CounterPlugin.Create(6));
    }

    [Theory]
    [InlineData("ChangedFiles")]
    [InlineData("ChangedDirectories")]
    [InlineData("Everything")]
    public async Task Repeated_export_and_plan_preserve_state_and_entry_contents(string scope)
    {
        var (workspace, document) = await fixture.OpenAsync();
        using var edit = await fixture.PostAsync($"{document}/edits",
            "{\"edits\":[{\"valueId\":\"counter\",\"value\":6}],\"revision\":0}");
        edit.EnsureSuccessStatusCode();
        var before = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
        using var options = JsonDocument.Parse(JsonSerializer.Serialize(new { scope, mirrorBackups = true }));
        string request = options.RootElement.GetRawText();
        using var planBefore = await fixture.PostAsync($"{workspace}/export/plan", request);
        planBefore.EnsureSuccessStatusCode();
        string plan = await planBefore.Content.ReadAsStringAsync(ApiContractFixture.Ct);
        string path = JsonSerializer.Deserialize<JsonElement>(plan).GetProperty("files")[0].GetProperty("relativePath").GetString()!;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await fixture.PostAsync($"{workspace}/export", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("A_dump___test-edited.zip", response.Content.Headers.ContentDisposition?.FileNameStar);
            using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(ApiContractFixture.Ct));
            var entry = Assert.Single(zip.Entries);
            Assert.Equal(path, entry.FullName);
            using var content = new MemoryStream();
            await using var stream = entry.Open();
            await stream.CopyToAsync(content, ApiContractFixture.Ct);
            Assert.Equal(CounterPlugin.Create(6), content.ToArray());
        }

        using var planAfter = await fixture.PostAsync($"{workspace}/export/plan", request);
        Assert.Equal(plan, await planAfter.Content.ReadAsStringAsync(ApiContractFixture.Ct));
        var after = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using var undo = await fixture.PostAsync($"{document}/undo", "{\"revision\":1}");
        undo.EnsureSuccessStatusCode();
        await AssertDownloadAsync(document, CounterPlugin.Create(5));
    }

    private async Task AssertDownloadAsync(string document, byte[] expected)
    {
        using var response = await fixture.Client.GetAsync(new Uri($"{document}/download", UriKind.Relative), ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("保存.dat", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync(ApiContractFixture.Ct));
    }
}
