using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>A validated, fully loaded pack. Immutable and shared by every document that pins it.</summary>
public sealed class LoadedReferencePack
{
    internal LoadedReferencePack(DecodedReferencePack decoded) => Decoded = decoded;

    internal DecodedReferencePack Decoded { get; }

    public ReferencePackInfo Info => Decoded.Info;
    public string Id => Info.Id;
    public IReferenceCatalog? Catalog(string catalogId) => Decoded.Catalog(catalogId);

    /// <summary>Asset by pack-relative path.</summary>
    internal DecodedAsset? Asset(string path) => Decoded.Asset(path);

    /// <summary>True when the pack applies to the given plugin and title.</summary>
    public bool AppliesTo(string pluginId, string? titleId) => Decoded.AppliesTo(pluginId, titleId);
}
