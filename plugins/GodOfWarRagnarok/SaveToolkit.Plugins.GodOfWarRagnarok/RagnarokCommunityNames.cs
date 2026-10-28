using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Direct callers receive the same attributed identities without requiring a Core pack store.</summary>
internal static class RagnarokCommunityNames
{
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> Records =
        RagnarokReferenceLoader.Load("gow-ragnarok.community.en");

    public static ReferenceRecord? Find(string key) => Records.GetValueOrDefault(key);

}
