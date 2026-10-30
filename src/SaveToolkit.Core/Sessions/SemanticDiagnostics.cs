namespace SaveToolkit.Core.Sessions;

/// <summary>Bounds provider-controlled text retained by semantic review diagnostics.</summary>
internal static class SemanticDiagnostics
{
    internal const int MaxMessageChars = 512;

    public static string Exception(string prefix, Exception failure) =>
        Bound($"{prefix}: {Bound(failure.Message)}");

    public static string Bound(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length <= MaxMessageChars) return message;
        return message[..(MaxMessageChars - 1)] + '…';
    }
}
