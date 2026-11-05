using SaveToolkit.Api.Contracts;

// Namespace retained from the ASP.NET host so existing callers and tests keep resolving it.
namespace SaveToolkit.Api.Infrastructure;

/// <summary>Bounded preset JSON envelopes, enforced before request binding by every host.</summary>
internal static class PresetRequestBodyLimits
{
    public const int CreateBytes = 512 * 1024;
    public const int CaptureBytes = 256 * 1024;
    public const int PreviewBytes = 256 * 1024;
    public const int ApplyBytes = 64 * 1024;
    public const int RenameBytes = 16 * 1024;

    public static ErrorDto Rejection(int maxBytes) =>
        new($"Preset request body exceeds the {maxBytes} byte limit.", "presetPayloadTooLarge");
}
