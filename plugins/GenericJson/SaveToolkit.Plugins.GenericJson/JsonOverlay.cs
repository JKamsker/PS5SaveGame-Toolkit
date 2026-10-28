using System.Text.Json;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Conservative, read-only interpretations. No third-party name dictionary is embedded.</summary>
internal sealed record JsonOverlay(string Title, string Evidence, IReadOnlyList<JsonValueToken> Groups)
{
    internal const string Provenance = "Labels and values are read from this imported save; interpretation is inferred, not console validated.";

    public static JsonOverlay? Match(ISaveFile file, JsonTokenTree tree)
    {
        var root = tree.Nodes[0];
        if (root.Kind != JsonTokenType.StartObject) return null;
        var title = file.Directory.TitleId;
        if (title == "PPSA04027" && file.FileName == "settings.json"
            && Unique(root, "version")?.Current.ToString() == "136"
            && Unique(root, "data") is { Kind: JsonTokenType.StartArray } data
            && data.Children.All(g => g.Kind == JsonTokenType.StartObject
                && Unique(g, "group_name")?.Kind == JsonTokenType.String
                && Unique(g, "options")?.Kind == JsonTokenType.StartArray)
            && data.Children.Count <= 128)
        {
            return new("Cyberpunk settings (version 136)",
                "Configuration only; these are file-supplied groups/options, not sav.dat gameplay state.", data.Children);
        }

        if (title == "PPSA02343" && file.FileName == "ue4savegame.dpx.sav"
            && Unique(root, "Profile") is { Kind: JsonTokenType.StartObject } profile
            && profile.Children.All(n => n.Kind == JsonTokenType.String)
            && Unique(root, "CloudSettings") is not null && Unique(root, "LocalSettings") is not null)
        {
            var gameplay = profile.Children.Where(n => n.Label.StartsWith("Minigame.", StringComparison.Ordinal)
                || n.Label is "FurthestUnlockedChapter" or "LastSaveProgressPoint" or "LastSaveChapter").ToArray();
            if (gameplay.Length > 128) return null;
            return new("It Takes Two profile", "PPSA02343: observed nested string profile. Missing keys remain absent, not false. "
                + "Chapter/checkpoint dependencies and score meanings are unconfirmed. Identification: https://prosperopatches.com/PPSA02343", gameplay);
        }

        if (title == "PPSA07696" && file.FileName == "imgui_emu_option.json"
            && Unique(root, "options") is { Kind: JsonTokenType.StartObject } options
            && Unique(root, "settings") is { Kind: JsonTokenType.StartObject } settings)
        {
            return new("Ridge Racer emulator options", "Wrapper configuration only; DATA.BIN gameplay is separate. "
                + "Raw option names do not establish enum meanings, bounds or PS1 preset compatibility.", [options, settings]);
        }

        return null;
    }

    private static JsonValueToken? Unique(JsonValueToken parent, string name)
    {
        var matches = parent.Children.Where(n => n.Label == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
