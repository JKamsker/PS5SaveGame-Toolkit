using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftCollectibleView(DdlSections sections, IReferenceCatalog? catalog,
    RiftCollections collections, Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
{
    private const int MaskBits = 64;

    internal IReadOnlyList<ViewNode> Build()
    {
        if (!sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign)) return [];
        var result = new List<ViewNode>
        {
            new TextNode("Location and spybot joins use CC0 schema ordinals, cross-checked against the supplied PS5 arrays/masks. They remain inferred without controlled pickup pairs. Gold bolt identities and Lorb/Zurpstone bits are not guide coordinates.") { Tone = Tone.Warning },
            Locations(campaign),
            Awards(),
        };
        foreach (var (field, title, prefix) in new[]
        {
            ("RYNOPlansCollected", "Spybot checklist", "spybot"),
            ("RuinTokensCollected", "Ruin token / Lorb bits", "lorb"),
            ("ZerpStonesCollected", "Zurpstone bits", "zurpstone"),
        })
        {
            if (!campaign.TryGetValue(field, out var mask) || mask.Values.Count != 1) continue;
            var bits = mask.Values[0].AsUInt64();
            result.Add(collections.Add($"collectibles/{prefix}", title,
                [new("name", "Identity candidate"), new("bit", "Bit", IntegerSpec.UInt8), new("state", "Stored bit")],
                Enumerable.Range(0, MaskBits).Select(bit => BitRow(prefix, field, bit, bits)),
                $"All 64 bits of {field} are preserved, including unresolved/reserved positions. Clear bits do not establish that a pickup exists. No lifetime/current-run distinction or exact location is assumed. " + DdlSections.Attribution));
        }
        return result;
    }

    private QueryableTableNode Awards()
    {
        var awards = sections.Roots.GetValueOrDefault(DdlSections.AwardsTag)?.GetValueOrDefault("Awards");
        return collections.Add("collectibles/awards", "Collectible award progress (including CraiggerBears)",
            [new("code", "Stored award code"), new("value", "CurrentValue"), new("prize", "PrizeGiven")],
            (awards?.Objects ?? []).Select((award, index) => (award, index)).Where(pair =>
            {
                var code = pair.award.Fields.GetValueOrDefault("CodeName")?.Values.Single().AsString() ?? "";
                return code.Contains("Collect", StringComparison.OrdinalIgnoreCase) ||
                    code.Contains("Bear", StringComparison.OrdinalIgnoreCase) || code.Contains("RYNO", StringComparison.OrdinalIgnoreCase) ||
                    code.Contains("Zerp", StringComparison.OrdinalIgnoreCase) || code.Contains("Lorb", StringComparison.OrdinalIgnoreCase);
            }).Select(pair =>
            {
                var code = pair.award.Fields["CodeName"].Values.Single().AsString();
                var state = pair.award.Fields.GetValueOrDefault("AwardSave")?.Objects.SingleOrDefault()?.Fields
                    .GetValueOrDefault("Obj")?.Objects.SingleOrDefault()?.Fields;
                var id = $"collectibles/award/{code}/{pair.index}";
                return new RiftRow(id, new Dictionary<string, RiftCell>
                {
                    ["code"] = RiftCell.TextOnly(code),
                    ["value"] = Cell("CurrentValue"), ["prize"] = Cell("PrizeGiven"),
                });
                RiftCell Cell(string name) => state?.GetValueOrDefault(name) is { Values.Count: 1 } field
                    ? RiftCollections.Field(field.Values[0].ToString(), () => bind($"{id}/{name}", name, field.Spec, field.Values[0]) with
                    {
                        Confidence = Confidence.Verified,
                        Description = "Stored award field, not an individual pickup count or PSN unlock. " + DdlSections.Attribution,
                    }) : RiftCell.TextOnly("Not stored / unresolved");
            }), "CurrentValue and PrizeGiven are separate stored fields. All_CraigBears can have CurrentValue=1 with PrizeGiven=0; neither its title nor this progress number proves collection completion or platform unlocks. No individual bear locations are known. " + DdlSections.Attribution);
    }

    private QueryableTableNode Locations(IReadOnlyDictionary<string, DdlField> campaign)
    {
        string[] fields = ["LevelGoldBolts", "LevelRYNOPlans", "LevelVanityPickups", "LevelRaritanium"];
        var count = fields.Select(name => campaign.GetValueOrDefault(name)?.Values.Count ?? 0).Max();
        return collections.Add("world/location-counters", "Collectibles by stored location",
            [new("name", "Location candidate"), new("index", "Stored index", IntegerSpec.UInt8),
             .. fields.Select(field => new SaveToolkit.Sdk.Collections.QueryableColumn(field, field, IntegerSpec.UInt32))],
            Enumerable.Range(0, count).Select(index =>
            {
                var reference = catalog?.Find($"location/{index}");
                var id = $"world/location/{index}";
                var cells = new Dictionary<string, RiftCell>
                {
                    ["name"] = Name(id, reference, $"Unresolved location {index}"),
                    ["index"] = RiftCollections.Number(index),
                };
                foreach (var name in fields)
                {
                    var field = campaign.GetValueOrDefault(name);
                    cells[name] = field is not null && index < field.Values.Count
                        ? RiftCollections.Field(field.Values[index].ToString(), () => bind($"{id}/{name}", name, field.Spec, field.Values[index]) with
                        {
                            Confidence = Confidence.Verified,
                            Description = $"Stored {name}[{index}]; location label is inferred. " + DdlSections.Attribution,
                        }, (decimal)field.Values[index].AsUInt64()) : RiftCell.TextOnly("Not stored");
                }
                return new RiftRow(index.ToString(CultureInfo.InvariantCulture), cells);
            }), "Stored per-location counters, not a guide-numbered pickup map. Location 0 remains unresolved; ordinal joins are inferred.");
    }

    private RiftRow BitRow(string prefix, string field, int bit, ulong mask)
    {
        var id = $"collectibles/{prefix}/{bit}";
        var reference = prefix == "spybot" ? catalog?.Find($"spybot/{bit}") : null;
        var value = (mask & (1UL << bit)) != 0;
        return new(bit.ToString(CultureInfo.InvariantCulture), new Dictionary<string, RiftCell>
        {
            ["name"] = Name(id, reference, $"Unresolved {prefix} bit {bit}"),
            ["bit"] = RiftCollections.Number(bit),
            ["state"] = RiftCollections.Field(value ? "Set" : "Not set", () => bind(id + "/set", "Stored bit", new BoolSpec(), FieldValue.Of(value)) with
            {
                Confidence = Confidence.Verified,
                Description = $"Exact bit {bit} of {field}. Pickup identity and completion semantics remain inferred. " + DdlSections.Attribution,
            }),
        });
    }

    private RiftCell Name(string id, ReferenceRecord? reference, string fallback) =>
        RiftCollections.Field(reference?.Name ?? fallback, () => bind(id + "/name", "Identity candidate", new TextSpec(), FieldValue.Of(reference?.Name ?? fallback)) with
        {
            Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
            Description = reference is null ? "No established reference join; raw identity retained." :
                reference.Description + " " + reference.AttributionText,
        });
}
