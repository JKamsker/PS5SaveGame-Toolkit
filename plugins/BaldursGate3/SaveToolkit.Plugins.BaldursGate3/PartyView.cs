using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>SaveInfo's sourced summary is deliberately separate from live LSMF character state.</summary>
internal static class PartyView
{
    private const int MaxDisplayText = 2048;

    internal static IReadOnlyList<ViewNode> Build(IReadOnlyList<DocumentEntry> entries,
        Func<string, string, ResourceAttribute, FieldNode> bindAttribute,
        Func<string, string, FieldValue, FieldNode> bindSummary, Bg3References? references = null)
    {
        var nodes = new List<ViewNode>
        {
            new TextNode("Experimental saved-party metadata edits. Level and XP change only this summary; live character stats are stored separately in NewAge. Not validated on console; edited saves may be rejected.") { Tone = Tone.Warning },
        };
        AddLeaders(entries, nodes, bindAttribute);
        AddSummary(entries, nodes, bindSummary, references);
        nodes.Add(new TextNode("A player-owned gold total is unavailable: Live NewAge exposes explicit stack-member quantities across all owners without a party ownership join. Unlisted origins retain their raw IDs."));
        return nodes;
    }

    private static void AddLeaders(IReadOnlyList<DocumentEntry> entries, List<ViewNode> nodes,
        Func<string, string, ResourceAttribute, FieldNode> bindAttribute)
    {
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
    }

    private static void AddSummary(IReadOnlyList<DocumentEntry> entries, List<ViewNode> nodes,
        Func<string, string, FieldValue, FieldNode> bindSummary, Bg3References? references)
    {
        var summary = entries.FirstOrDefault(e => e.Name.Equals("SaveInfo.json", StringComparison.OrdinalIgnoreCase));
        if (summary is null || summary.Bytes.Length > PartySummaryEdits.MaxSummaryBytes)
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
                    var rows = party.EnumerateArray().Take(PartySummaryEdits.MaxCharacters).Select((character, index) => new TableRow(
                        index.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
                        {
                            ["name"] = OriginLabel(character, index, bindSummary, references),
                            ["origin"] = SummaryText(character, "Origin", index, bindSummary),
                            ["level"] = SummaryNumber(character, "Level", index, bindSummary),
                            ["currentXp"] = SummaryNumber(character, PartySummaryEdits.CurrentXp, index, bindSummary),
                            ["totalXp"] = SummaryNumber(character, PartySummaryEdits.TotalXp, index, bindSummary),
                        })).ToArray();
                    nodes.Add(new StatsNode([new Stat("Active characters", party.GetArrayLength().ToString(CultureInfo.InvariantCulture))]));
                    nodes.Add(new TableNode([new("name", "Origin label"), new("origin", "Raw origin"), new("level", "Level"),
                        new("currentXp", "XP (current level)"), new("totalXp", "XP (total)")], rows)
                    {
                        Title = "Active party",
                        Description = "SaveInfo.json metadata only. Existing level/XP tokens are editable in v18 packages within conservative limits. Sourced origin labels carry attribution. At most 128 rows are shown.",
                    });
                }
                else nodes.Add(new TextNode("SaveInfo.json has no supported Active Party / Characters array."));
            }
            catch (JsonException)
            {
                nodes.Add(new TextNode("SaveInfo.json could not be decoded; the original payload is preserved.") { Tone = Tone.Warning });
            }
        }
    }

    private static ViewNode OriginLabel(JsonElement character, int index, Func<string, string, FieldValue, FieldNode> bindSummary, Bg3References? references)
    {
        var value = Property(character, "Origin");
        if (value.ValueKind != JsonValueKind.String) return new TextNode("Unavailable");
        var raw = value.GetString()!;
        if (raw.Length > MaxDisplayText) return new TextNode("Origin exceeds the name lookup limit; inspect its raw payload.");
        var record = references?.Origin(raw);
        return Bg3References.Describe(bindSummary($"party/{index}/origin-label", "Origin label",
            FieldValue.Of(record?.Name ?? raw)), record, raw);
    }

    private static ViewNode SummaryNumber(JsonElement character, string name, int index, Func<string, string, FieldValue, FieldNode> bindSummary)
    {
        var value = Property(character, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? bindSummary($"party/{index}/{name}", name, FieldValue.Of(number)) : new TextNode("Unavailable");
    }

    private static ViewNode SummaryText(JsonElement character, string name, int index, Func<string, string, FieldValue, FieldNode> bindSummary)
    {
        var value = Property(character, name);
        if (value.ValueKind != JsonValueKind.String) return new TextNode("Unavailable");
        var text = value.GetString()!;
        var field = bindSummary($"party/{index}/{name}", name, FieldValue.Of(text.Length <= MaxDisplayText ? text : text[..MaxDisplayText] + "…"));
        return name == "Origin" ? field with { Spec = new ReferenceSpec(Bg3References.Origins) { ReadOnly = true, AllowCustom = true } } : field;
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}
