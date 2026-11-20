using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Bounded reading of preset-transfer bodies; every host reads them without a media-type check.</summary>
internal static class PresetTransferControl
{
    public const int ControlRequestLimit = PresetTransferLimits.MaxControlBytes;

    /// <summary>Deliberately no save/title/path input in the download filename.</summary>
    public const string ExportFileName = "Toolkit presets.toolkit-presets.json";

    private static readonly JsonSerializerOptions ControlJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Rejects a declared body length above <paramref name="limit"/> before anything is read.</summary>
    public static void CheckDeclaredLength(long? contentLength, int limit)
    {
        if (contentLength > limit)
            throw new PresetTransferException("presetTransferRequestTooLarge", $"The request exceeds the {limit / 1024} KiB limit.", 413);
    }

    public static Task<byte[]> ReadPackageAsync(Stream body, long? contentLength, CancellationToken ct) =>
        PresetPackageCodec.ReadBoundedAsync(body, contentLength, ct);

    public static async Task<T> ReadControlAsync<T>(Stream body, long? contentLength, CancellationToken ct)
    {
        // The same streaming guard also covers chunked requests before deserialization.
        byte[] bytes;
        try { bytes = await PresetPackageCodec.ReadBoundedAsync(body, contentLength, ct, ControlRequestLimit); }
        catch (PresetTransferException ex) when (ex.StatusCode == 413)
        {
            throw new PresetTransferException("presetTransferRequestTooLarge", "The transfer control request exceeds its finite byte limit.", 413);
        }
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, ControlJson) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new PresetTransferException("invalidPresetTransferRequest", "The preset selection or import decisions are malformed.", 400);
        }
    }
}
