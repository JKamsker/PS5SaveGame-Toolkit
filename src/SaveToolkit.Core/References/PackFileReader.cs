namespace SaveToolkit.Core.References;

/// <summary>Bounded ZIP reads with the host's existing rejection type.</summary>
internal static class PackFileReader
{
    public static byte[] ReadBounded(Stream stream, long maxBytes, string what)
    {
        try
        {
            return Sdk.References.PackFileReader.ReadBounded(stream, maxBytes, what);
        }
        catch (Sdk.References.EmbeddedReferencePackException ex)
        {
            throw ex.InnerException is { } inner
                ? new ReferencePackException(ex.Message, inner)
                : new ReferencePackException(ex.Message);
        }
    }
}
