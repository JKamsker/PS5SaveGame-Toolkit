using System.Net;
using System.Text;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi.Tests.Parity;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Request-envelope edge cases (body charsets, form syntax, method casing, path escapes) against both hosts.</summary>
public sealed class HttpEdgeParityTests : IDisposable
{
    private const string Preset =
        """{"name":"P","compatibility":{"pluginId":"test.planning","pluginVersion":"1.0.0","titleIds":["PPSA99999"]},"actions":[{"actionId":"refill","parameters":{"value":7}}]}""";

    private static readonly string[] Charsets = ["bogus", "\"utf-8\"", "windows-1252", "utf-7", ""];

    private static readonly string[] Methods = ["get", "Get", "GET", "head", "HEAD", "post", "PUT", "delete", "options", "patch", "FOO", "foo"];

    private static readonly string[] Paths =
    [
        "/api/%76ersion", "/%61pi/version", "/api/version%2F", "/api/workspaces/%C3", "/api/workspaces/%FF%FE", "/api/workspaces/%E2%82",
        "/api/workspaces/%ED%A0%80", "/api/workspaces/%C0%80", "/api/workspaces/a%2Fb", "/api/workspaces/%25", "/api/workspaces/%zz",
        "/api/workspaces/%F0%9F%98%80", "/api/workspaces/%E2%82%AC%2F%E2%82%AC", "/api/workspaces/x/../../api/version",
    ];

    private static readonly string[] FileIds =
    [
        "%66%30", "%C3", "%C3%A9", "%FF%FE", "%E2%82", "%E2%82x", "%ED%A0%80", "%C0%80", "%F4%90%80%80", "a%2Fb", "%2f", "%25", "%zz", "%",
        "%F0%9F%98%80", "%E2%82%AC%2F%E2%82%AC", "a+b", "%2e%2E", "%C3%28", "%E2%28%A1", "%F0%28%8C%BC", "%c3%a9%C3",
    ];

    private readonly ParityHosts _hosts = new();

    private static ByteArrayContent Bytes(byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return content;
    }

    /// <summary>A multipart upload: <paramref name="head"/> (LF line ends become CRLF), a counter save, the final boundary.</summary>
    private static ByteArrayContent Upload(string head, string boundary = "b", string? parameter = null) =>
        Bytes([.. Encoding.UTF8.GetBytes(head.Replace("\n", "\r\n", StringComparison.Ordinal)), .. CounterPlugin.Create(5),
            .. Encoding.UTF8.GetBytes("\r\n--" + boundary + "--")], "multipart/form-data; boundary=" + (parameter ?? boundary));

    private static async Task ImportAsync(Transcript t, HttpContent content)
    {
        var response = await t.SendAsync(HttpMethod.Post, "/api/workspaces", content);
        if (response.Status == 200) t.Alias(response.Json.GetProperty("id").GetString(), "<ws>");
    }

    [Fact]
    public Task Json_charsets_match() => _hosts.AssertParityAsync(async t =>
    {
        foreach (var charset in Charsets)
            await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.UTF8.GetBytes(Preset), "application/json; charset=" + charset));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.UTF8.GetBytes(Preset), "application/json; charset"));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.Unicode.GetBytes(Preset), "application/json; charset=utf-16"));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(Preset)], "application/json; charset=UTF-16"));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.Latin1.GetBytes(Preset.Replace("\"P\"", "\"Pé\"", StringComparison.Ordinal)), "application/json; charset=iso-8859-1"));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.UTF8.GetBytes(Preset.Replace("\"P\"", "\"Pé\"", StringComparison.Ordinal)), "application/json; charset=us-ascii"));
        await t.SendAsync(HttpMethod.Post, "/api/presets", Bytes(Encoding.UTF8.GetBytes("{"), "application/json; charset=utf-32"));
        await t.GetAsync("/api/presets");
    });

    [Fact]
    public Task Upload_envelopes_match() => _hosts.AssertParityAsync(async t =>
    {
        const string File = "Content-Disposition: form-data; name=\"files\"; filename=\"real.dat\"";
        await ImportAsync(t, Upload("--b\nContent-Disposition: form-data; name=\"files\"; filename=\"real.dat\"; filename*=UTF-8''\n\n"));
        await ImportAsync(t, Upload("preamble--b\nContent-Disposition: form-data; name=\"name\"\n\nNamed\n--b\n" + File + "\n\n"));
        await ImportAsync(t, Upload("--b\nContent-Disposition: form-data; name=\"" + new string('k', 3000) + "\"\n\nv\n--b\n" + File + "\n\n"));
        await ImportAsync(t, Upload("--b\nContent-Disposition: form-data; name=\"name\"\nContent-Type: text/plain; charset=iso-8859-1\n\né\n--b\n" + File + "\n\n"));
        await ImportAsync(t, Upload("--b\nContent-Disposition: form-data; name=\"name\"\n\nN\n--bjunk\n" + File + "\n\n"));
        await ImportAsync(t, Upload("-- \n" + File + "\n\n", " ", "\" \""));
        await ImportAsync(t, Upload("-- \n" + File + "\n\n", " "));
        await ImportAsync(t, Upload("--b\n" + File + "\n" + File + "\n\n"));
        await ImportAsync(t, Upload("--b\nContent-Disposition : form-data; name=\"files\"; filename=\"x\"\n\n"));
        await ImportAsync(t, Upload("--b\n:x\n" + File + "\n\n"));
        await ImportAsync(t, Upload("--b" + new string(' ', 120) + "\n" + File + "\n\n"));
        await ImportAsync(t, Bytes(Encoding.UTF8.GetBytes(new string('k', 3000) + "=v"), "application/x-www-form-urlencoded"));
        await ImportAsync(t, Bytes(Encoding.UTF8.GetBytes("k=" + new string('v', (4 * 1024 * 1024) + 1)), "application/x-www-form-urlencoded"));
        await ImportAsync(t, Bytes(Encoding.UTF8.GetBytes("name=%00"), "application/x-www-form-urlencoded"));
        await ImportAsync(t, Bytes([], "multipart/form-data"));
        await t.GetAsync("/api/workspaces");
    });

    /// <summary>
    /// TestServer canonicalizes known methods case-insensitively and routing compares methods
    /// case-insensitively, so lower-case methods behave like upper-case ones; unknown methods get a 405.
    /// </summary>
    [Fact]
    public Task Method_casing_matches() => _hosts.AssertParityAsync(async t =>
    {
        foreach (var method in Methods)
        {
            await t.SendAsync(new HttpMethod(method), "/api/version");
            await t.SendAsync(new HttpMethod(method), "/api/workspaces/missing");
        }
    });

    [Fact]
    public Task Path_escapes_match() => _hosts.AssertParityAsync(async t =>
    {
        foreach (var path in Paths)
        {
            await t.GetAsync(path);
            await t.DeleteAsync(path);
        }
    });

    /// <summary>Decoded route values are visible in "Unknown file" errors (and %66%30 opens file f0).</summary>
    [Fact]
    public Task Route_value_escapes_match() => _hosts.AssertParityAsync(async t =>
    {
        var workspace = await Parity.Scenarios.ImportAsync(t, null, ("x.dat", CounterPlugin.Create(5)));
        var id = workspace.GetProperty("id").GetString();
        foreach (var fileId in FileIds) await t.SendAsync(HttpMethod.Post, $"/api/workspaces/{id}/files/{fileId}/open");
    });

    /// <summary>
    /// An escaped NUL never reaches routing. TestServer's path decoder throws (a test-host artefact);
    /// Kestrel, the real host, answers a bare 400 without running the app, which the in-process host matches.
    /// </summary>
    [Theory]
    [InlineData("/api/workspaces/%00")]
    [InlineData("/api/workspaces/a%00b/files/x/open")]
    [InlineData("/api/workspaces/%C3%00")]
    public async Task Escaped_nul_paths_are_rejected_like_kestrel(string path)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _hosts.AspNet.GetAsync(new Uri(path, UriKind.Relative), Transcript.Ct));
        using var actual = await _hosts.Local.GetAsync(new Uri(path, UriKind.Relative), Transcript.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, actual.StatusCode);
        Assert.Empty(await actual.Content.ReadAsByteArrayAsync(Transcript.Ct));
    }

    public void Dispose() => _hosts.Dispose();
}
