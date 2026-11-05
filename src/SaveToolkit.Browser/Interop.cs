using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;

namespace SaveToolkit.Browser;

/// <summary>The JavaScript surface used by the web worker (web/src/runtime/worker.ts).</summary>
public static partial class Interop
{
    /// <summary>Loads plugins, creates the API host and returns boot info as JSON.</summary>
    /// <param name="optionsJson">JSON <c>{ "dataRoot": "...", "persistentRoot": "..." }</c>; either may be null.</param>
    [JSExport]
    public static string Initialize(string optionsJson)
    {
        var options = JsonSerializer.Deserialize(optionsJson, WireJsonContext.Default.WireInitOptions) ?? new WireInitOptions(null, null);
        return JsonSerializer.Serialize(BrowserHost.Initialize(options), WireJsonContext.Default.WireBootInfo);
    }

    /// <summary>
    /// Dispatches one HTTP-shaped request and returns a response handle for <see cref="TakeResponse"/>.
    /// (Source-generated interop cannot return <c>Task&lt;byte[]&gt;</c>, so the frame is fetched separately.)
    /// </summary>
    /// <param name="method">HTTP method, e.g. <c>GET</c>.</param>
    /// <param name="url">Path and query, e.g. <c>/api/plugins</c>, percent-encoded as the browser sends it.</param>
    /// <param name="headersJson">JSON array of <c>[name, value]</c> pairs.</param>
    /// <param name="body">Raw request body (empty for none).</param>
    [JSExport]
    public static async Task<int> DispatchAsync(string method, string url, string headersJson, byte[] body)
    {
        var frame = await BrowserHost.DispatchAsync(method, url, headersJson, body);
        return PendingResponses.Add(frame);
    }

    /// <summary>Removes and returns a framed response (see <see cref="WireFormat"/>) for a handle from <see cref="DispatchAsync"/>.</summary>
    [JSExport]
    public static byte[] TakeResponse(int handle) => PendingResponses.Take(handle);
}
