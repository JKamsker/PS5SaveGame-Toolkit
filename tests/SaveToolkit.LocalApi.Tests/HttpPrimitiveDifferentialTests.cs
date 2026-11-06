using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SaveToolkit.LocalApi.Http;
using AspNetReasonPhrases = Microsoft.AspNetCore.WebUtilities.ReasonPhrases;
using LocalContentDisposition = SaveToolkit.LocalApi.Http.ContentDisposition;
using LocalReasonPhrases = SaveToolkit.LocalApi.Http.ReasonPhrases;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Compares the dependency-free HTTP primitives with the ASP.NET Core implementations they replace.</summary>
public sealed class HttpPrimitiveDifferentialTests
{
    public static TheoryData<string> FileNames => new()
    {
        "save.dat", "保存.dat", "a b.dat", "x\"y\\z.dat", "emoji 😀.bin", "semi;colon,comma.txt", "tab\tname", "percent%2F*'.zip",
        "Toolkit presets.toolkit-presets.json", "ünïcödé-ascii_mix~!#$&+^`|.bin", "é́", "lone\ud800surrogate",
    };

    public static TheoryData<string> EntityTagLists => new()
    {
        "\"1\"", "W/\"1\"", "w/\"1\"", "*", "\"1\", \"2\"", " , \"1\" ,", "\"1\" junk", "*, \"1\"", "", "x", "W/ \"1\"", "W/",
        "\"a\\\"b\"", "\"unterminated", "\"1\",,\"2\"", "\"1\"\t,\t\"2\"", "**", "\"1\"\"2\"", " ", ",", " , ",
    };

    public static TheoryData<string> ContentTypes => new()
    {
        "<none>", "", "application/json", "Application/JSON; charset=utf-8", "application/problem+json", "text/json", "text/x+json",
        "application/json;", "application/json; charset=\"utf-8\"", "multipart/form-data; boundary=x", "application/x-www-form-urlencoded",
        "text/plain", "application/jsonx", "application /json", "json", "application/json; =x", "MULTIPART/FORM-DATA",
        "application/json; charset", "application/json; charset=", "application/json; charset=\"\"", "application/json ; charset=utf-16",
        "application/json; charset=\"open", "application/json; charset= ; a=1", "multipart/form-data; boundary= ", "application/json; a=;b",
    };

    public static TheoryData<string> Queries => new()
    {
        "", "?", "?a=1&b=2", "?a=1&A=2&a=3", "?filter.x=1&Filter.X=2", "?=v&k", "?a+b=c+d&%41=%2F%zz", "?x=1&&y=2", "?q=%E2%9C%93&q=",
        "?k1=1&k2=1&k3=1&k4=1&k5=1&k6=1&k7=1&k8=1&k9=1&k10=1&k11=1&K1=2&k12=1&K12=2",
    };

    public static TheoryData<string> Dispositions => new()
    {
        "form-data; name=\"files\"; filename=\"a.zip\"", "form-data; name=files; filename=a.zip", "form-data; name=\"name\"",
        "form-data; name=\"f\"; filename=\"x.dat\"; filename*=UTF-8''%E4%BF%9D.dat", "form-data; name=\"f\"; filename=\"=?utf-8?B?5L+d5a2Y?=\"",
        "form-data; name=\"f\"; filename*=bogus''x", "form-data; name=\"f\"; filename=\"\"", "attachment; filename=x", "form-data;", "form-data",
        "form-data; name=\"a\\\"b\"", "form-data; name=\"f\"; filename*=utf-8'en'%41%42", "bad header value",
        "form-data; name", "form-data; name=", "form-data; name=; filename=x", "form-data; filename*=", "form-data; name=\"open",
    };

    [Fact]
    public void Reason_phrases_match()
    {
        for (var status = 100; status < 600; status++)
            Assert.Equal(AspNetReasonPhrases.GetReasonPhrase(status), LocalReasonPhrases.Get(status));
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void Attachment_headers_match(string fileName)
    {
        var expected = new ContentDispositionHeaderValue("attachment");
        expected.SetHttpFileName(fileName);
        Assert.Equal(expected.ToString(), LocalContentDisposition.Attachment(fileName));
    }

    [Theory]
    [MemberData(nameof(EntityTagLists))]
    public void Entity_tag_lists_match(string header)
    {
        var expectedOk = EntityTagHeaderValue.TryParseStrictList([header], out var expected);
        var actualOk = EntityTags.TryParseStrictList([header], out var actual);
        Assert.Equal(expectedOk, actualOk);
        if (!expectedOk) return;
        Assert.Equal(expected!.Select(t => (ReferenceEquals(t, EntityTagHeaderValue.Any), t.IsWeak, t.Tag.ToString())),
            actual.Select(t => (t.IsAny, t.IsWeak, t.IsAny ? "*" : t.Tag)));
    }

    [Theory]
    [MemberData(nameof(ContentTypes))]
    public void Json_and_form_content_types_match(string header)
    {
        var contentType = header == "<none>" ? null : header;
        var request = new DefaultHttpContext().Request;
        request.ContentType = contentType;
        var media = MediaType.Parse(contentType);
        Assert.Equal(request.HasJsonContentType(), media is { IsJson: true });
        Assert.Equal(request.HasFormContentType, media is { IsForm: true });
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void Query_strings_match(string query)
    {
        var request = new DefaultHttpContext().Request;
        request.QueryString = new QueryString(query);
        var expected = request.Query.Select(p => (p.Key, string.Join("|", p.Value.Select(v => v ?? "<null>"))));
        var actual = QueryValues.Parse(query).Entries.Select(p => (p.Key, string.Join("|", p.Value)));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Dispositions))]
    public void Form_section_dispositions_match(string header)
    {
        var expectedOk = ContentDispositionHeaderValue.TryParse(header, out var expected);
        var actual = LocalContentDisposition.Parse(header);
        Assert.Equal(expectedOk, actual is not null);
        if (!expectedOk) return;
        Assert.Equal(expected!.DispositionType.ToString(), actual!.DispositionType);
        Assert.Equal(expected.Name.Value, actual.Name);
        Assert.Equal(expected.FileName.Value, actual.FileName);
        Assert.Equal(expected.FileNameStar.Value, actual.FileNameStar);
        Assert.Equal(expected.IsFileDisposition(), actual.IsFile);
        Assert.Equal(expected.IsFormDisposition(), actual.IsFormValue);
    }

    [Theory]
    [MemberData(nameof(MultipartBodies))]
    public Task Multipart_forms_match(string contentType, string body) =>
        FormOracle.AssertSameAsync(body.Length > 60 ? body[..60] : body, contentType, Encoding.UTF8.GetBytes(body));

    private const string Cd = "Content-Disposition: form-data; name=";
    private const string FileCd = Cd + "\"f\"; filename=\"x\"";

    public static TheoryData<string, string> MultipartBodies => new()
    {
        // Review round 2: bare LF inside quoted part-header values, and a valueless filename* parameter.
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a\nb.dat\"\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"name\"\r\nContent-Type: text/plain; x=\"1\n2\"; charset=iso-8859-1\r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a.dat\"; filename*\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: attachment; name=\"files\"; filename*\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a.zip\"\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=\"b\"", "pre\r\n--b \t\r\nContent-Disposition: form-data; name=\"name\"\r\n\r\nNamed\r\n--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"x\"\r\nContent-Type: text/plain\r\n\r\nline1\r\nline2\r\n--b--\r\nepilogue" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"\"\r\n\r\nvalue\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nbad header\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"a\"\r\n\r\nabc" },
        { "multipart/form-data", "--b\r\n\r\n" },
        { "multipart/form-data; boundary=b", "garbage" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Type: text/plain\r\n\r\nno disposition\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: attachment; filename=\"x\"\r\n\r\nignored\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n\ufeffbom\r\n--b\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n2\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b--" },
        { "application/x-www-form-urlencoded", "name=x&files=y" },
        // Review r1: preamble without CRLF (data loss), lenient boundary lines, header syntax and limits.
        { "multipart/form-data; boundary=b", "xx--b\r\n" + Cd + "\"a\"\r\n\r\nv\r\n--b\r\n" + FileCd + "\r\n\r\nF\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"a\"\r\n\r\nv\r\n--bX\r\n" + Cd + "\"c\"\r\n\r\nw\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b --" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition : form-data; name=\"f\"; filename=\"x\"\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n:x\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + string.Concat(Enumerable.Repeat("X-A: 1\r\n", 17)) + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + string.Concat(Enumerable.Range(0, 16).Select(i => $"X-{i}: 1\r\n")) + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + string.Concat(Enumerable.Range(0, 15).Select(i => $"X-{i}: 1\r\n")) + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"" + new string('k', 3000) + "\"\r\n\r\nv\r\n--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"name\"\r\n\r\n" + new string('v', (4 * 1024 * 1024) + 1) + "\r\n--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"f\"; filename=\"a.dat\"; filename*=UTF-8''\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\n" + FileCd + "\n\nv\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"name\"\r\nContent-Type: text/plain; charset=iso-8859-1\r\n\r\n\u00e9\r\n--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=\" \"", "-- \r\n" + FileCd + "\r\n\r\nv\r\n-- --" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b\r\n" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--" },
        { "multipart/form-data; boundary=b", "--b" + new string(' ', 200) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b" + new string(' ', 99) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" + new string(' ', 98) },
        { "multipart/form-data; boundary=b", "--b" + new string(' ', 100) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"f\"; filename*=\"UTF-8''q.dat\"\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition:\r\ncontent-disposition: " + FileCd[21..] + "\r\n\r\nv\r\n--b\t--  \r\n" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Type: a\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset=utf-16\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset=\"iso-8859-1\"\r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset=bogus\r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset=utf-7\r\n\r\n+AGEA-\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset=us-ascii\r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain;charset=latin1 \r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"t\"\r\nContent-Type: text/plain; charset\r\n\r\n\u00e9\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + Cd + "\"a\"\r\n\r\n1\r\n--b\r\n" + Cd + "\"A\"\r\n\r\n2\r\n--b\r\n" + Cd + "a\r\n\r\n3\r\n--b--" },
        { "multipart/form-data; boundary=" + new string('b', 128), "--" + new string('b', 128) + "\r\n" + FileCd + "\r\n\r\nv\r\n--" + new string('b', 128) + "--" },
        { "multipart/form-data; boundary=" + new string('b', 129), "--" + new string('b', 129) + "\r\n" + FileCd + "\r\n\r\nv\r\n--" + new string('b', 129) + "--" },
        { "multipart/form-data; boundary=", "--\r\n\r\n" },
        { "multipart/form-data; boundary", "--\r\n\r\n" },
        { "multipart/form-data; boundary=\"\"", "--\r\n\r\n" },
        { "multipart/form-data; boundary= ", "-- \r\n\r\n" },
        { "multipart/form-data", "" },
        { "multipart/form-data; boundary=b", new string('p', 16384) + "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", new string('p', 16385) + "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--\r\n" + new string('e', 16382) },
        { "multipart/form-data; boundary=b", "--b\r\n" + FileCd + "\r\n\r\nv\r\n--b--\r\n" + new string('e', 16385) },
        { "multipart/form-data; boundary=b", "--b\r\nX: " + new string('h', 16384 - 3 - FileCd.Length) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nX: " + new string('h', 16385 - 3 - FileCd.Length) + "\r\n" + FileCd + "\r\n\r\nv\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nX: " + new string('h', 5000) + "\r\n" + FileCd + "\r\n\r\n" + new string('d', 9000) + "\r\n--b--" },
        // Url-encoded bodies: FormPipeReader limits, empty pairs and UrlDecoder's form decoding.
        { "application/x-www-form-urlencoded", "" },
        { "application/x-www-form-urlencoded", "&" },
        { "application/x-www-form-urlencoded", "&&a=1&" },
        { "application/x-www-form-urlencoded", "=" },
        { "application/x-www-form-urlencoded", "a&a=2&A=3&b" },
        { "application/x-www-form-urlencoded", "a=%00" },
        { "application/x-www-form-urlencoded", "%00=a" },
        { "application/x-www-form-urlencoded", "a=%C3%28&b=%E2%82&c=%F0%90%80&d=%ED%A0%80&e=%C0%80&f=%F4%90%80%80&g=%E2%82%AC%F0%9F%98%80" },
        { "application/x-www-form-urlencoded", "a=+%2B%20&b=%zz%&c=%&d=a%&e=%4&f=%c3%a9" },
        { "application/x-www-form-urlencoded", new string('k', 2048) + "=v&" + new string('j', 2048) },
        { "application/x-www-form-urlencoded", new string('k', 2049) },
        { "application/x-www-form-urlencoded", new string('k', 3000) + "=v" },
        { "application/x-www-form-urlencoded", "k=" + new string('v', 4 * 1024 * 1024) },
        { "application/x-www-form-urlencoded", "k=" + new string('v', (4 * 1024 * 1024) + 1) },
        { "application/x-www-form-urlencoded; charset=iso-8859-1", "n=%E9&m=\u00e9" },
        { "application/x-www-form-urlencoded; charset=us-ascii", "n=%C3%A9&m=\u00e9" },
        { "application/x-www-form-urlencoded; charset=\"utf-16\"", "n=%C3%A9" },
        { "application/x-www-form-urlencoded; charset=utf-7", "n=%C3%A9" },
    };
}
