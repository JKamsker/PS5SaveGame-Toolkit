using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed record CharacterProfile(bool TestedVersion, bool Offline)
{
    internal const string Format = "FCharacterSaveV1";
    internal const string TestedSoftVersion = "2";
    internal const string TestedFormatHash = "3777917472";

    public static CharacterProfile? Read(JsonTokenTree tree)
    {
        var root = tree.Nodes[0];
        var meta = root.Member("SerializeMeta");
        var hero = root.Member("CharacterSaveV1");
        if (meta?.Kind != System.Text.Json.JsonTokenType.StartObject ||
            meta.Member("HardFormat")?.Current != FieldValue.Of(Format) ||
            hero?.Kind != System.Text.Json.JsonTokenType.StartObject) return null;
        var tested = meta.Member("SoftVersion")?.Current == FieldValue.ExactNumber(TestedSoftVersion)
            && meta.Member("FormatHash")?.Current == FieldValue.ExactNumber(TestedFormatHash);
        return new(tested, hero.Member("MetaData")?.Member("IsOnline")?.Current == FieldValue.Of(false));
    }

    public string Warning => !TestedVersion
        ? "Untested character SoftVersion/FormatHash: read-only. Only PS5 SoftVersion 2 / FormatHash 3777917472 has sample evidence."
        : !Offline ? "Online or unknown character mode: read-only. No online-save compatibility has been established."
        : "PS5 character read view only. Gameplay and structural writes require controlled captures and console reload tests.";
}
