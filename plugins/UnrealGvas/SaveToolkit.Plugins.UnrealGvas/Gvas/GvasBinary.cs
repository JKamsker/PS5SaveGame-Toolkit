using System.Runtime.ExceptionServices;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>GVAS limits, encoding policy and legacy diagnostics around the shared binary helpers.</summary>
internal static class GvasBinary
{
    internal const string Context = "GVAS";
    internal static readonly FStringLimits Limits = new(2 * GvasFile.MaxStringLength, GvasFile.MaxStringLength)
    {
        MaxUtf8Bytes = GvasFile.MaxStringLength,
    };

    public static string String(this ref BoundedBinaryCursor reader) => FStringCodec.Read(ref reader, Limits);

    public static string ReadString(BinaryReader reader)
    {
        try { return FStringCodec.Read(reader, Limits, Context); }
        // The header reader historically distinguishes a short prefix/non-seekable body from
        // a seekable body's declared-length failure. Let its existing EOF handler keep doing so.
        catch (SaveFormatException ex) when (ex.InnerException is EndOfStreamException)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    public static byte[] EncodeString(string value, bool wide)
    {
        if (value.Length >= GvasFile.MaxStringLength || value.Contains('\0'))
            throw new ArgumentException("String is too long or contains NUL.", nameof(value));
        try { return FStringCodec.Encode(value, wide || value.Any(c => c > 127), Limits); }
        catch (ArgumentException ex) when (ex.InnerException is EncoderFallbackException)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    public static SaveFormatException LegacyError(SaveFormatException error)
    {
        // Translate only SDK diagnostics; property/layout errors already carry their GVAS wording.
        var message = error.Message switch
        {
            "GVAS: FString length exceeds its byte or UTF-16 code-unit limit." => "GVAS string exceeds the supported length limit.",
            "GVAS: FString is missing its NUL terminator." => "GVAS string is missing its NUL terminator.",
            "GVAS: invalid FString encoding." => "Invalid GVAS string encoding.",
            "GVAS: truncated FString body." => "Truncated GVAS string.",
            var text when text.StartsWith("GVAS: truncated data", StringComparison.Ordinal) => text.Replace("GVAS: truncated data", "Truncated GVAS data", StringComparison.Ordinal),
            var text when text.StartsWith("GVAS: invalid count", StringComparison.Ordinal) => text.Replace("GVAS: invalid count", "Invalid GVAS count", StringComparison.Ordinal),
            _ => error.Message,
        };
        return error.InnerException is { } inner ? new(message, inner) : new(message);
    }
}
