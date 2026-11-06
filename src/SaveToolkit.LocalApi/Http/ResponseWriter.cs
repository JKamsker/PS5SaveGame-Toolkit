using System.Text.Encodings.Web;
using System.Text.Json;
using SaveToolkit.Core;

namespace SaveToolkit.LocalApi.Http;

/// <summary>Renders a result to wire bytes and headers, like minimal-API result execution.</summary>
internal static class ResponseWriter
{
    /// <summary>
    /// ASP.NET's HTTP JSON options (<c>JsonOptions.DefaultSerializerOptions</c> configured by
    /// <c>ToolkitJson.Configure</c>): handler values use the relaxed encoder, while problem envelopes
    /// are written with <see cref="ToolkitJson.Options"/> directly.
    /// </summary>
    private static readonly JsonSerializerOptions HttpJson = ToolkitJson.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static LocalResponse Write(LocalResult result, IReadOnlyList<KeyValuePair<string, string>> extraHeaders)
    {
        var headers = new List<KeyValuePair<string, string>>(extraHeaders);
        byte[] body;
        switch (result)
        {
            case JsonResult json:
                headers.Add(new("Content-Type", json.ContentType));
                body = JsonSerializer.SerializeToUtf8Bytes<object?>(json.Value, json.IsProblem ? ToolkitJson.Options : HttpJson);
                break;
            case BytesResult bytes:
                headers.Add(new("Content-Type", bytes.ContentType));
                if (bytes.DownloadName is { Length: > 0 } name) headers.Add(new("Content-Disposition", ContentDisposition.Attachment(name)));
                body = bytes.Content.ToArray();
                break;
            default:
                body = [];
                break;
        }

        return new LocalResponse(result.Status, headers, body);
    }
}
