using System.Text.RegularExpressions;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftReadOnlyViews(
    DdlSections sections,
    IReferenceCatalog? weapons,
    IReferenceCatalog? schema,
    Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
{
    internal IReadOnlyList<ViewNode> Loadout()
    {
        if (!sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign)) return [];
        var fields = new List<ViewNode>
        {
            new TextNode("Quick-select assignments are stored shortcuts. They do not prove weapon ownership, level or ammunition. Display-name joins are candidates; unresolved paths are retained.") { Tone = Tone.Warning },
        };
        foreach (var direction in new[] { "Up", "Right", "Down", "Left" })
        {
            var name = $"QuickSelectWeapon{direction}";
            if (!campaign.TryGetValue(name, out var field) || field.Values.Count != 1) continue;
            var raw = field.Values[0].AsString();
            var record = weapons?.Find(raw);
            fields.Add(bind($"loadout/{name}", $"{direction} · {record?.Name ?? "Unresolved weapon path"}",
                new ReferenceSpec("rift-weapons") { AllowCustom = true }, FieldValue.Of(raw)) with
            {
                Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
                Description = $"Stored path: {raw}. {record?.Description} {record?.AttributionText ?? "No pinned catalog record; raw path only."}",
                Warning = "Read-only: path metadata, ownership dependencies and console write acceptance are unproven.",
            });
        }
        return fields;
    }

    internal IReadOnlyList<ViewNode> Preferences()
    {
        if (!sections.Roots.TryGetValue(DdlSections.PreferencesTag, out var preferences)) return [];
        return [new TextNode("Stored preferences only. Numeric menu choices and units remain uncalibrated; no PC enum ordinals are substituted.") { Tone = Tone.Info },
            .. preferences.Values.Where(f => f.Values.Count == 1).GroupBy(f => PreferenceGroup(f.Name))
                .Select(group => (ViewNode)new SectionNode(group.Key, group.Select(f => (ViewNode)Scalar("preferences", f)).ToArray()))];
    }

    internal IReadOnlyList<ViewNode> CampaignDetails()
    {
        if (!sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign)) return [];
        string[] names = ["CharacterId", "CurrentLevel", "CurrentCheckpoint", "CurrentCheckpointId", "GameStage", "GameFlags",
            "BoltMultiplier", "BoltMultiplierKillsLeft", "FirstOmegaWeaponPurchased", "RuinTokensCollected",
            "ZerpStonesCollected", "VanityPickupsCollected", "TutorialRaritaniumAwarded"];
        return names.Where(campaign.ContainsKey).Select(n => campaign[n]).Where(f => f.Values.Count == 1)
            .Select(f => (ViewNode)Scalar("campaign", f)).ToArray();
    }

    private FieldNode Scalar(string path, DdlField field) =>
        bind($"{path}/{field.Name}", Regex.Replace(field.Name, "(?<=[a-z])(?=[A-Z])", " "), field.Spec, field.Values[0]) with
        {
            Confidence = Confidence.Verified,
            Description = $"Stored field {field.Name} ({field.Kind}); name/hash verified. " +
                (schema?.Find(field.Name)?.AttributionText ?? DdlSections.Attribution),
            Warning = "Verified storage identity only; menu labels, units, location and gameplay effects are not established.",
        };

    private static string PreferenceGroup(string name)
    {
        if (name.Contains("Volume", StringComparison.Ordinal) || name.Contains("Audio", StringComparison.Ordinal) ||
            name.Contains("Speaker", StringComparison.Ordinal) || name.Contains("Listening", StringComparison.Ordinal)) return "Audio";
        if (name.Contains("Subtitle", StringComparison.Ordinal) || name.Contains("Language", StringComparison.Ordinal)) return "Language and subtitles";
        if (name.Contains("Accessibility", StringComparison.Ordinal) || name.Contains("Shader", StringComparison.Ordinal) ||
            name.Contains("Contrast", StringComparison.Ordinal) || name.Contains("Assist", StringComparison.Ordinal)) return "Accessibility";
        if (name.Contains("Invert", StringComparison.Ordinal) || name.Contains("Trigger", StringComparison.Ordinal) ||
            name.Contains("Toggle", StringComparison.Ordinal) || name.Contains("Sensitivity", StringComparison.Ordinal)) return "Controls";
        return "Display and other preferences";
    }
}
