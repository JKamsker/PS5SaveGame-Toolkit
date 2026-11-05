using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
    public async Task Multipart_forms_match(string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        var feature = new FormFeature(context.Request, new FormOptions { ValueCountLimit = 100_000 });
        IFormCollection? expected = null;
        try { expected = await feature.ReadFormAsync(TestContext.Current.CancellationToken); }
        catch (Exception exception) when (exception is InvalidDataException or IOException) { }

        FormData? actual = null;
        try { actual = MultipartForm.Read(MediaType.Parse(contentType)!, bytes, long.MaxValue, 100_000, TestContext.Current.CancellationToken); }
        catch (Exception exception) when (exception is InvalidDataException or IOException) { }

        Assert.Equal(expected is null, actual is null);
        if (expected is null || actual is null) return;
        Assert.Equal(expected.Files.Select(f => (f.Name, f.FileName, f.Length)), actual.Files.Select(f => (f.Name, f.FileName, f.Length)));
        Assert.Equal(expected.SelectMany(p => p.Value.Select(v => (p.Key, v))).OrderBy(p => p.Key, StringComparer.Ordinal),
            actual.Values.Select(p => (p.Key, (string?)p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    public static TheoryData<string, string> MultipartBodies => new()
    {
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a.zip\"\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=\"b\"", "pre\r\n--b \t\r\nContent-Disposition: form-data; name=\"name\"\r\n\r\nNamed\r\n--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"x\"\r\nContent-Type: text/plain\r\n\r\nline1\r\nline2\r\n--b--\r\nepilogue" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"\"\r\n\r\nvalue\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nbad header\r\n\r\nabc\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"a\"\r\n\r\nabc" },
        { "multipart/form-data", "--b\r\n\r\n" },
        { "multipart/form-data; boundary=b", "garbage" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Type: text/plain\r\n\r\nno disposition\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: attachment; filename=\"x\"\r\n\r\nignored\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n﻿bom\r\n--b\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n2\r\n--b--" },
        { "multipart/form-data; boundary=b", "--b--" },
        { "application/x-www-form-urlencoded", "name=x&files=y" },
    };
}
