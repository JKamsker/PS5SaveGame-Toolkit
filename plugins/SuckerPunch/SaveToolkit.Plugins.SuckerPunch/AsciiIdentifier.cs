namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>The shared byte alphabet for named records and scanned identifier evidence.</summary>
internal static class AsciiIdentifier
{
    public static bool IsValid(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (!(value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
                >= (byte)'0' and <= (byte)'9' or (byte)'_'))
            {
                return false;
            }
        }
        return true;
    }
}
