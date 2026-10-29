using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>Retains the host's exception contract around the shared pure SDK decoder.</summary>
internal static class ReferencePackLoader
{
    public static LoadedReferencePack Load(IPackFileSource source, ReferencePackOrigin origin, ReferencePackLimits limits, ReferenceStringPool? strings = null)
    {
        try
        {
            return new LoadedReferencePack(PackDecoder.Load(source, origin, limits.ToEmbedded(), strings));
        }
        catch (EmbeddedReferencePackException ex)
        {
            throw ex.InnerException is { } inner
                ? new ReferencePackException(ex.Message, inner)
                : new ReferencePackException(ex.Message);
        }
    }
}
