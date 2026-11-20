using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SaveToolkit.LocalApi.Http;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>
/// Byte-level form bodies, FormOptions limits, 4 KiB buffer edges and seeded mutations, each compared with
/// ASP.NET Core's FormFeature through <see cref="FormOracle"/>; plus the JSON body charset handling.
/// </summary>
public sealed class FormReaderDifferentialTests
{
    private const string Multipart = "multipart/form-data; boundary=b";
    private const string FileCd = "Content-Disposition: form-data; name=\"f\"; filename=\"x\"";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>One text section named <c>t</c> with optional extra headers and a raw value.</summary>
    private static byte[] TextSection(string headers, params byte[][] value) =>
        [.. Utf8("--b\r\nContent-Disposition: form-data; name=\"t\"\r\n" + headers + "\r\n"), .. value.SelectMany(v => v), .. Utf8("\r\n--b--")];

    private static byte[] Charset(string charset, params byte[][] value) =>
        TextSection("Content-Type: text/plain; charset=" + charset + "\r\n", value);

    public static TheoryData<string, string, byte[]> ByteBodies => new()
    {
        { "utf16-bom", Multipart, TextSection("", [0xFF, 0xFE], Encoding.Unicode.GetBytes("Hi")) },
        { "utf16be-bom", Multipart, TextSection("", [0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes("Hi")) },
        { "utf32-bom", Multipart, TextSection("", [0xFF, 0xFE, 0, 0], Encoding.UTF32.GetBytes("Hi")) },
        { "utf32be-bom", Multipart, TextSection("", [0, 0, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes("Hi")) },
        { "utf8-bom", Multipart, TextSection("", [0xEF, 0xBB, 0xBF], Utf8("é")) },
        { "only-ff-fe", Multipart, TextSection("", [0xFF, 0xFE]) },
        { "ff-fe-00", Multipart, TextSection("", [0xFF, 0xFE, 0]) },
        { "only-ef-bb", Multipart, TextSection("", [0xEF, 0xBB]) },
        { "truncated-utf8", Multipart, TextSection("", Utf8("a"), [0xC3]) },
        { "invalid-utf8", Multipart, TextSection("", [0xFF, 0x41, 0x80]) },
        { "utf16-no-bom", Multipart, Charset("utf-16", Encoding.Unicode.GetBytes("Hi")) },
        { "utf16-utf32-bom", Multipart, Charset("utf-16", [0xFF, 0xFE, 0, 0], Encoding.Unicode.GetBytes("Hi")) },
        { "utf16-utf8-bom", Multipart, Charset("utf-16", [0xEF, 0xBB, 0xBF], Utf8("Hi")) },
        { "utf16-odd", Multipart, Charset("UTF-16LE", Encoding.Unicode.GetBytes("Hi"), [0x41]) },
        { "utf32-partial-bom", Multipart, Charset("utf-32", [0xFF, 0xFE]) },
        { "utf16be-declared", Multipart, Charset("utf-16BE", Encoding.BigEndianUnicode.GetBytes("Hé")) },
        { "latin1", Multipart, Charset("iso-8859-1", [0xE9, 0xFF]) },
        { "ascii", Multipart, Charset("ascii", [0xE9, 0x41]) },
        { "windows-1252", Multipart, Charset("windows-1252", [0x80, 0xE9]) },
        { "utf-8-bom-declared", Multipart, Charset("utf-8", [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF], Utf8("x")) },
        { "dup-content-type", Multipart, TextSection("Content-Type: text/plain; charset=iso-8859-1\r\nContent-Type: text/plain\r\n", [0xE9]) },
        { "header-invalid-utf8", Multipart, [.. Utf8("--b\r\nX-"), 0xFF, .. Utf8(": 1\r\n" + FileCd + "\r\n\r\nv\r\n--b--")] },
        { "boundary-line-invalid-utf8", Multipart, [.. Utf8("--b"), 0xC2, 0xA0, .. Utf8("--\r\n" + FileCd + "\r\n\r\nv\r\n--b--")] },
        { "boundary-nbsp-final", Multipart, [.. Utf8("--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--"), 0xC2, 0xA0] },
        { "urlenc-utf16", "application/x-www-form-urlencoded; charset=utf-16", Encoding.Unicode.GetBytes("a=1&b=%41+c&&d") },
        { "urlenc-utf16-misaligned", "application/x-www-form-urlencoded; charset=utf-16", Encoding.Unicode.GetBytes("a=♸\u0000x&b=㴀") },
        { "urlenc-latin1", "application/x-www-form-urlencoded; charset=iso-8859-1", [.. Utf8("n=%E9&m="), 0xE9] },
        { "urlenc-raw-invalid", "application/x-www-form-urlencoded", [.. Utf8("n="), 0xFF, 0xC3, .. Utf8("&%C3"), 0xA9] },
    };

    [Theory]
    [MemberData(nameof(ByteBodies))]
    public Task Byte_bodies_match(string label, string contentType, byte[] body) => FormOracle.AssertSameAsync(label, contentType, body);

    public static TheoryData<string, string, string, int, long> LimitBodies => new()
    {
        { "sections-at-limit", Multipart, "--b\r\n" + FileCd + "\r\n\r\n1\r\n--b\r\nContent-Disposition: attachment\r\n\r\n2\r\n--b--", 2, long.MaxValue },
        { "ignored-section-counts", Multipart, "--b\r\n" + FileCd + "\r\n\r\n1\r\n--b\r\nContent-Disposition: attachment\r\n\r\n2\r\n--b--", 1, long.MaxValue },
        { "values-at-limit", "application/x-www-form-urlencoded", "a&b", 2, long.MaxValue },
        { "values-over-limit", "application/x-www-form-urlencoded", "a&&b", 2, long.MaxValue },
        { "section-at-body-limit", Multipart, "--b\r\n" + FileCd + "\r\n\r\n0123456789\r\n--b--", 10, 10 },
        { "section-over-body-limit", Multipart, "--b\r\n" + FileCd + "\r\n\r\n0123456789A\r\n--b--", 10, 10 },
        { "value-over-body-limit", Multipart, "--b\r\nContent-Disposition: form-data; name=\"n\"\r\n\r\n0123456789A\r\n--b--", 10, 10 },
        { "preamble-not-body-limited", Multipart, "0123456789ABCDEF--b\r\n" + FileCd + "\r\n\r\n0\r\n--b--", 10, 10 },
    };

    [Theory]
    [MemberData(nameof(LimitBodies))]
    public Task Limits_match(string label, string contentType, string body, int valueCountLimit, long bodyLengthLimit) =>
        FormOracle.AssertSameAsync(label, contentType, Utf8(body), valueCountLimit, bodyLengthLimit);

    /// <summary>Delimiters, partial delimiters and boundary lines around the 4 KiB buffer refills and 1 KiB text reads.</summary>
    [Fact]
    public async Task Buffer_edges_match()
    {
        var failures = new List<string>();
        foreach (var size in Enumerable.Range(4070, 40).Concat(Enumerable.Range(1010, 30)).Concat(Enumerable.Range(8170, 30)))
        {
            foreach (var (label, body) in EdgeBodies(size))
            {
                var expected = await FormOracle.AspNetAsync(Multipart, body, 100, long.MaxValue);
                var actual = FormOracle.Local(Multipart, body, 100, long.MaxValue);
                if (expected != actual) failures.Add($"[{label} {size}]\n aspnet: {expected}\n local:  {actual}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    private static IEnumerable<(string, byte[])> EdgeBodies(int size)
    {
        var filler = new string('x', size);
        yield return ("file", Utf8("--b\r\n" + FileCd + "\r\n\r\n" + filler + "\r\n--b--"));
        yield return ("value", Utf8("--b\r\nContent-Disposition: form-data; name=\"v\"\r\n\r\n" + filler + "\r\n--b--"));
        yield return ("preamble", Utf8(filler + "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--"));
        yield return ("partial", Utf8("--b\r\n" + FileCd + "\r\n\r\n" + filler + "\r\n--c\r\n-\r\n--b--"));
        yield return ("remainder", Utf8("--b\r\n" + FileCd + "\r\n\r\n" + filler[..(size - 60)] + "\r\n--b" + new string(' ', 70) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--"));
        yield return ("end", Utf8("--b\r\n" + FileCd + "\r\n\r\n" + filler + "\r\n--b"));
        yield return ("utf16", [.. Utf8("--b\r\nContent-Disposition: form-data; name=\"v\"\r\n\r\n"), 0xFF, 0xFE,
            .. Encoding.Unicode.GetBytes(filler[..(size / 2)]), .. Utf8("\r\n--b--")]);
    }

    private static readonly string[] Lines =
    [
        "--b", "--b--", "--b ", "--b\t--", "--bX", "--b -- ", "--b-", "--", "-", "--c", "\r", "\n", "\r\n", "",
        FileCd, "Content-Disposition: form-data; name=\"n\"", "Content-Disposition: form-data; name=\"n\"; filename*=UTF-8''%41",
        "Content-Disposition: attachment", "Content-Disposition :x", ":x", "X: 1", "content-type: text/plain; charset=utf-16",
        "Content-Type: text/plain; charset=iso-8859-1", "x", "é", "﻿", "￿", "v\r\n--b",
    ];

    /// <summary>Seeded mutations of multipart and url-encoded bodies (a fixed corpus: failures are reproducible).</summary>
    [Fact]
    public async Task Seeded_mutations_match()
    {
        var random = new Random(20261106);
        var failures = new List<string>();
        for (var i = 0; i < 1500; i++)
        {
            var (contentType, body) = i % 4 == 3 ? UrlEncoded(random) : MultipartBody(random);
            var limit = random.Next(1, 8);
            var expected = await FormOracle.AspNetAsync(contentType, body, limit, 4096);
            var actual = FormOracle.Local(contentType, body, limit, 4096);
            if (expected != actual)
                failures.Add($"[#{i}] {contentType} {Convert.ToBase64String(body)}\n aspnet: {expected}\n local:  {actual}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    private static (string, byte[]) MultipartBody(Random random)
    {
        var text = new StringBuilder();
        var parts = random.Next(1, 14);
        for (var i = 0; i < parts; i++)
        {
            text.Append(Lines[random.Next(Lines.Length)]);
            text.Append(random.Next(5) switch { 0 => "", 1 => "\n", _ => "\r\n" });
            if (random.Next(12) == 0) text.Append('y', random.Next(4000, 4200));
            if (random.Next(20) == 0) text.Append(' ', random.Next(90, 110));
        }

        var start = random.Next(4) == 0 ? "" : "--b\r\n";
        var boundary = random.Next(10) switch { 0 => "\"b\"", 1 => "\" b\"", _ => "b" };
        return ("multipart/form-data; boundary=" + boundary, Utf8(start + text));
    }

    private static readonly string[] Tokens = ["a", "=", "&", "%", "%0", "%00", "%C3", "%A9", "%E2%82%AC", "+", "%2B", "é", "%ZZ", "%F0%9F%98%80", "%ED%A0%80", "%c3%a9", "%26", "%3D"];

    private static (string, byte[]) UrlEncoded(Random random)
    {
        var text = string.Concat(Enumerable.Range(0, random.Next(0, 16)).Select(_ => Tokens[random.Next(Tokens.Length)]));
        var charset = random.Next(6) switch { 0 => "; charset=iso-8859-1", 1 => "; charset=utf-16", 2 => "; charset=us-ascii", _ => "" };
        var encoding = charset.EndsWith("utf-16", StringComparison.Ordinal) ? Encoding.Unicode : Encoding.UTF8;
        return ("application/x-www-form-urlencoded" + charset, encoding.GetBytes(text));
    }

    public static TheoryData<string, byte[]> JsonBodies => new()
    {
        { "application/json", Utf8("{\"a\":\"é\"}") },
        { "application/json; charset=utf-8", [0xEF, 0xBB, 0xBF, .. Utf8("{\"a\":1}")] },
        { "application/json; charset=UTF8", Utf8("{\"a\":\"é\"}") },
        { "application/json; charset=\"utf-8\"", Utf8("{}") },
        { "application/json; charset=bogus", Utf8("{}") },
        { "application/json; charset=windows-1252", Utf8("{}") },
        { "application/json; charset=utf-7", Utf8("{}") },
        { "application/json; charset", Utf8("{\"a\":1}") },
        { "application/json; charset=utf-16", Encoding.Unicode.GetBytes("{\"a\":\"é\"}") },
        { "application/json; charset=utf-16", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("{\"a\":1}")] },
        { "application/json; charset=utf-16", [0xFE, 0xFF, .. Encoding.Unicode.GetBytes("{\"a\":1}")] },
        { "application/json; charset=utf-16", [.. Encoding.Unicode.GetBytes("{\"a\":\"x\"}"), 0x20] },
        { "application/json; charset=unicodeFFFE", Encoding.BigEndianUnicode.GetBytes("{\"a\":1}") },
        { "application/json; charset=utf-32", Encoding.UTF32.GetBytes("{\"a\":1}") },
        { "application/json; charset=iso-8859-1", Encoding.Latin1.GetBytes("{\"a\":\"é\"}") },
        { "application/json; charset=us-ascii", Encoding.Latin1.GetBytes("{\"a\":\"é\"}") },
        { "application/json; charset=latin1", [0xEF, 0xBB, 0xBF, .. Utf8("{}")] },
        { "application/problem+json; charset=utf-16", Encoding.Unicode.GetBytes("[1]") },
        { "application/json", [0xC3, 0x28] },
        { "application/json; charset=iso-8859-1", [.. Utf8("\""), 0xC3, 0x28, .. Utf8("\"")] },
    };

    /// <summary><c>ReadFromJsonAsync</c> against <see cref="Charsets.JsonUtf8"/> plus the host's BOM-tolerant parse.</summary>
    [Theory]
    [MemberData(nameof(JsonBodies))]
    public async Task Json_charsets_match(string contentType, byte[] body)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        string expected;
        try { expected = (await context.Request.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetRawText(); }
        catch (JsonException) { expected = "JsonException"; }
        catch (InvalidOperationException exception) { expected = exception.Message; }

        string actual;
        try
        {
            var span = Charsets.JsonUtf8(MediaType.Parse(contentType)!, body).Span;
            if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) span = span[3..];
            actual = JsonSerializer.Deserialize<JsonElement>(span).GetRawText();
        }
        catch (JsonException) { actual = "JsonException"; }
        catch (InvalidOperationException exception) { actual = exception.Message; }

        Assert.Equal(expected, actual);
    }
}
