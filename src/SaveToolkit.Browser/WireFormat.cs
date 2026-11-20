using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Browser;

/// <summary>Response metadata that precedes the body in a framed response.</summary>
internal sealed record WireResponseHead(int Status, IReadOnlyList<string[]> Headers);

/// <summary>Boot information returned to the worker once the host is ready.</summary>
internal sealed record WireBootInfo(string Version, IReadOnlyList<string> Plugins, IReadOnlyList<string> FailedAssemblies);

/// <summary>Options the worker passes to <see cref="Interop.InitializeAsync"/>.</summary>
internal sealed record WireInitOptions(string? DataRoot, string? PersistentRoot);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WireResponseHead))]
[JsonSerializable(typeof(WireBootInfo))]
[JsonSerializable(typeof(WireInitOptions))]
[JsonSerializable(typeof(string[][]))]
internal sealed partial class WireJsonContext : JsonSerializerContext;

/// <summary>
/// JS &lt;-&gt; .NET framing. Requests arrive as separate arguments (method, URL, a JSON array of
/// <c>[name, value]</c> header pairs and the raw body). A response is one byte array:
/// a 4-byte little-endian length N, N bytes of UTF-8 JSON <see cref="WireResponseHead"/>, then the body.
/// </summary>
internal static class WireFormat
{
    public const int PrefixLength = sizeof(uint);

    public static IReadOnlyList<KeyValuePair<string, string>> ParseHeaders(string headersJson)
    {
        if (string.IsNullOrEmpty(headersJson)) return [];
        var pairs = JsonSerializer.Deserialize(headersJson, WireJsonContext.Default.StringArrayArray) ?? [];
        var headers = new List<KeyValuePair<string, string>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is not [var name, var value]) throw new FormatException("Each header must be a [name, value] pair.");
            headers.Add(new(name, value));
        }

        return headers;
    }

    public static byte[] Frame(int status, IEnumerable<KeyValuePair<string, string>> headers, ReadOnlySpan<byte> body)
    {
        var head = new WireResponseHead(status, [.. headers.Select(h => new[] { h.Key, h.Value })]);
        var headBytes = JsonSerializer.SerializeToUtf8Bytes(head, WireJsonContext.Default.WireResponseHead);
        var frame = new byte[PrefixLength + headBytes.Length + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)headBytes.Length);
        headBytes.CopyTo(frame.AsSpan(PrefixLength));
        body.CopyTo(frame.AsSpan(PrefixLength + headBytes.Length));
        return frame;
    }
}
