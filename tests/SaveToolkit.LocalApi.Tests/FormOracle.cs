using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SaveToolkit.LocalApi.Http;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>
/// Reads one form body with ASP.NET Core's <c>FormFeature</c> and with <see cref="MultipartForm"/> and
/// describes both outcomes (exception type and message, or every file and value) for an exact comparison.
/// </summary>
internal static class FormOracle
{
    public static async Task AssertSameAsync(string label, string contentType, byte[] body, int valueCountLimit = 100_000,
        long bodyLengthLimit = long.MaxValue)
    {
        var expected = await AspNetAsync(contentType, body, valueCountLimit, bodyLengthLimit);
        var actual = Local(contentType, body, valueCountLimit, bodyLengthLimit);
        Assert.True(expected == actual, $"[{label}] {contentType}\n aspnet: {expected}\n local:  {actual}");
    }

    public static async Task<string> AspNetAsync(string contentType, byte[] body, int valueCountLimit, long bodyLengthLimit)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        var options = new FormOptions { ValueCountLimit = valueCountLimit, MultipartBodyLengthLimit = bodyLengthLimit };
        try
        {
            var form = await new FormFeature(context.Request, options).ReadFormAsync(TestContext.Current.CancellationToken);
            var files = new List<string>();
            foreach (var file in form.Files)
            {
                using var buffer = new MemoryStream();
                await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                files.Add(File(file.Name, file.FileName, buffer.ToArray()));
            }

            var values = form.Select(p => (p.Key, (IEnumerable<string>)p.Value.Select(v => v ?? "<null>").ToList()));
            return Describe(files, values);
        }
#pragma warning disable CA1031 // Any failure is part of the observed behaviour.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Failure(exception);
        }
    }

    public static string Local(string contentType, byte[] body, int valueCountLimit, long bodyLengthLimit)
    {
        try
        {
            var media = MediaType.Parse(contentType) ?? throw new InvalidOperationException("unparsable content type");
            var form = MultipartForm.Read(media, body, bodyLengthLimit, valueCountLimit, TestContext.Current.CancellationToken);
            var files = form.Files.Select(f => File(f.Name, f.FileName, f.Content.ToArray())).ToList();
            // FormCollection groups values by key (first spelling wins), keeping their order.
            var values = form.Values.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.First().Key, g.Select(v => v.Value)));
            return Describe(files, values);
        }
#pragma warning disable CA1031 // Any failure is part of the observed behaviour.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Failure(exception);
        }
    }

    private static string Failure(Exception exception) => $"THROW {exception.GetType().Name}: {exception.Message}";

    private static string File(string name, string fileName, byte[] content) =>
        $"file({Show(name)},{Show(fileName)},{content.Length}#{Hash(content)})";

    private static string Describe(List<string> files, IEnumerable<(string Key, IEnumerable<string> Values)> values) =>
        string.Join(" | ", files) + " || " + string.Join(" | ", values.OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => Show(v.Key) + "=" + string.Join(",", v.Values.Select(Show))));

    /// <summary>A readable, escaped prefix plus the length and a hash of the whole string.</summary>
    private static string Show(string value)
    {
        var escaped = new StringBuilder();
        foreach (var c in value.Length > 40 ? value[..40] : value)
            escaped.Append(c is < ' ' or > '~' ? "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) : c);
        return value.Length > 40 ? $"{escaped}…[{value.Length}#{Hash(Encoding.Unicode.GetBytes(value))}]" : $"'{escaped}'";
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 4));
}
