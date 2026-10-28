using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>SaveInfo's sourced summary is deliberately separate from live LSMF character state.</summary>
internal static class PartyView
{
    private const int MaxSummaryBytes = 1024 * 1024;
    private const int MaxCharacters = 128;
    private const int MaxDisplayText = 2048;
    private const string CurrentXp = "Experience Points (Current level)";
    private const string TotalXp = "Experience Points (Total)";

    internal static IReadOnlyList<ViewNode> Build(IReadOnlyList<DocumentEntry> entries,
        Func<string, string, ResourceAttribute, FieldNode> bindAttribute,
        Func<string, string, FieldValue, FieldNode> bindSummary)
    {
        var nodes = new List<ViewNode>
        {
            new TextNode("Saved party summary. Level and XP are read-only metadata; live character stats are stored separately in NewAge.") { Tone = Tone.Info },
        };
        for (var e = 0; e < entries.Count; e++)
        {
            if (entries[e].Name != "meta.lsf" || entries[e].Resource is not { } resource) continue;
            for (var a = 0; a < resource.Attributes.Count; a++)
            {
                var attribute = resource.Attributes[a];
                if (attribute.Name != "LeaderName" || !AttributeDecoder.IsString(attribute.Type)) continue;
                if (attribute.Data.Length > ResourceExplorer.MaxInlineBytes)
                {
                    nodes.Add(new TextNode("LeaderName exceeds the inline preview limit; inspect its raw resource bytes."));
                    break;
                }

                nodes.Add(bindAttribute($"resource/{e}/attribute/{a}", $"LeaderName ({attribute.Type})", attribute) with
                {
                    Confidence = Confidence.Inferred,
                    Description = "Party leader display name in meta.lsf. Changing this metadata does not rename a live character.",
                });
                break; // One leader summary, even if malformed metadata repeats the attribute name many times.
            }
        }

        var summary = entries.FirstOrDefault(e => e.Name.Equals("SaveInfo.json", StringComparison.OrdinalIgnoreCase));
        if (summary is null || summary.Bytes.Length > MaxSummaryBytes)
        {
            nodes.Add(new TextNode("No supported SaveInfo.json party summary is available."));
        }
        else
        {
            try
            {
                using var json = JsonDocument.Parse(summary.Bytes, new JsonDocumentOptions { MaxDepth = 32 });
                var party = Property(Property(json.RootElement, "Active Party"), "Characters");
                if (party.ValueKind == JsonValueKind.Array)
                {
                    var rows = party.EnumerateArray().Take(MaxCharacters).Select((character, index) => new TableRow(
                        index.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
                        {
                            ["origin"] = Text(character, "Origin", index),
                            ["level"] = Number(character, "Level", index),
                            ["currentXp"] = Number(character, CurrentXp, index),
                            ["totalXp"] = Number(character, TotalXp, index),
                        })).ToArray();
                    nodes.Add(new StatsNode([new Stat("Active characters", party.GetArrayLength().ToString(CultureInfo.InvariantCulture))]));
                    nodes.Add(new TableNode([new("origin", "Character origin"), new("level", "Level"),
                        new("currentXp", "XP (current level)"), new("totalXp", "XP (total)")], rows)
                    {
                        Title = "Active party",
                        Description = "SaveInfo.json snapshot; origin strings may be IDs rather than localised character names. At most 128 rows are shown.",
                    });
                }
                else nodes.Add(new TextNode("SaveInfo.json has no supported Active Party / Characters array."));
            }
            catch (JsonException)
            {
                nodes.Add(new TextNode("SaveInfo.json could not be decoded; the original payload is preserved.") { Tone = Tone.Warning });
            }
        }

        nodes.Add(new TextNode("Gold quantity is unavailable: a live inventory quantity mapping has not been verified for this save. Localised companion names are unresolved."));
        return nodes;

        ViewNode Number(JsonElement character, string name, int index)
        {
            var value = Property(character, name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? bindSummary($"party/{index}/{name}", name, FieldValue.Of(number)) : new TextNode("Unavailable");
        }

        ViewNode Text(JsonElement character, string name, int index)
        {
            var value = Property(character, name);
            if (value.ValueKind != JsonValueKind.String) return new TextNode("Unavailable");
            var text = value.GetString()!;
            return bindSummary($"party/{index}/{name}", name, FieldValue.Of(text.Length <= MaxDisplayText ? text : text[..MaxDisplayText] + "…"));
        }
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}
