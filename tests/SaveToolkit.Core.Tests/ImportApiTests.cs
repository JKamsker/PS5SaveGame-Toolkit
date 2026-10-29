using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportApiTests : IDisposable
{
    private readonly string _root = TestZips.TempDir();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ImportApiTests()
    {
        _factory = new IsolatedApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production").UseSetting("Toolkit:DataRoot", _root);
            builder.ConfigureServices(services =>
            {
                var catalog = new PluginCatalog([]);
                services.AddSingleton(catalog);
                services.AddSingleton(new WorkspaceStore(catalog, new WorkspaceStoreOptions
                {
                    DataRoot = _root, Limits = new ImportLimits { MaxEntries = 5, MaxTotalBytes = 1024 },
                }));
            });
        });
        _client = _factory.CreateClient();
    }

    [Theory]
    [InlineData("not-zip")]
    [InlineData("empty-file")]
    [InlineData("empty-zip")]
    [InlineData("directory-only")]
    [InlineData("truncated")]
    [InlineData("crc")]
    [InlineData("compression")]
    [InlineData("aes-method")]
    [InlineData("long-name")]
    [InlineData("size")]
    [InlineData("count")]
    public async Task Failed_upload_returns_a_clear_error_and_leaves_no_workspace(string failure)
    {
        byte[] archive = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3]));
        string expected = "broken.zip";
        int central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        switch (failure)
        {
            case "not-zip": archive = "This is not a ZIP file."u8.ToArray(); break;
            case "empty-file": archive = []; break;
            case "empty-zip": archive = TestZips.Build(); expected = "No save files"; break;
            case "directory-only": archive = TestZips.Build(("PPSA00001/sdimg_SAVE/", [])); expected = "No save files"; break;
            case "truncated": archive = archive[..^12]; break;
            case "crc": archive[central + 16] ^= 1; break;
            case "compression": BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(central + 10), 12); break;
            case "aes-method": BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(central + 10), 99); break;
            case "long-name": archive = TestZips.Build((new string('x', 256), [1])); expected = "unsupported"; break;
            case "size": archive = TestZips.Build(("data.bin", new byte[1025])); expected = "exceeds"; break;
            case "count": archive = TestZips.Build(Enumerable.Range(0, 6).Select(i => ($"{i}.bin", Array.Empty<byte>())).ToArray()); expected = "Too many files"; break;
        }
        using var form = new MultipartFormDataContent();
        if (failure is not ("empty-zip" or "directory-only"))
            form.Add(new ByteArrayContent([9]), "files", "earlier.bin");
        form.Add(new ByteArrayContent(archive), "files", "broken.zip");
        using var response = await PostAsync(form);

        await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, expected);
        Assert.Empty(_factory.Services.GetRequiredService<WorkspaceStore>().All);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));

        using var retry = new MultipartFormDataContent();
        retry.Add(new ByteArrayContent([7]), "files", "retry.bin");
        using var retried = await PostAsync(retry);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var store = _factory.Services.GetRequiredService<WorkspaceStore>();
        Assert.Equal([7], Assert.Single(Assert.Single(Assert.Single(store.All).Set.Directories).Files).ReadAllBytes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loose_files_and_zip_uploads_preserve_nested_unicode_paths_and_empty_files(bool zipped)
    {
        var entries = new (string Path, byte[] Content)[]
        {
            ("./PPSA00001/SAVE/sub/保存🎮.bin", [1, 2]),
            ("./PPSA00001/save/empty.bin", []),
        };
        using var form = new MultipartFormDataContent();
        if (zipped) form.Add(new ByteArrayContent(TestZips.Build(entries)), "files", "工具.ZIP");
        else foreach (var (path, bytes) in entries) form.Add(new ByteArrayContent(bytes), "files", path);

        using var response = await PostAsync(form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var directory = Assert.Single(Assert.Single(body.GetProperty("titles").EnumerateArray()).GetProperty("directories").EnumerateArray());
        var files = directory.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => f.GetProperty("path").GetString() == "sub/保存🎮.bin" && f.GetProperty("length").GetInt64() == 2);
        Assert.Contains(files, f => f.GetProperty("path").GetString() == "empty.bin" && f.GetProperty("length").GetInt64() == 0);
    }

    [Fact]
    public async Task Truncated_multipart_body_returns_an_upload_error()
    {
        using var content = new StringContent("--upload\r\nContent-Disposition: form-data; name=\"files\"; filename=\"data.bin\"\r\n\r\npartial",
            Encoding.UTF8);
        content.Headers.ContentType = new("multipart/form-data");
        content.Headers.ContentType.Parameters.Add(new("boundary", "upload"));

        using var response = await PostAsync(content);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "upload");
        Assert.Empty(_factory.Services.GetRequiredService<WorkspaceStore>().All);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_files_have_a_useful_bad_request(bool multipart)
    {
        using HttpContent content = multipart ? new MultipartFormDataContent() : new StringContent("not a form");
        if (content is MultipartFormDataContent form) form.Add(new StringContent("empty selection"), "name");
        using var response = await PostAsync(content);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, multipart ? "No files" : "multipart/form-data");
    }

    private Task<HttpResponseMessage> PostAsync(HttpContent content) =>
        _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string expected)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Contains(expected, body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
