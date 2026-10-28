using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroWorldPlanning
{
    public const string ActionId = "collectibles/collect-world";
    private const string WorldParameter = "world";

    public static IReadOnlyList<ActionNode> Views(AstroSchema schema, AstroLocations locations)
    {
        var worlds = schema.Records.Select(locations.World).OfType<ReferenceRecord>()
            .DistinctBy(world => world.Key, StringComparer.Ordinal).OrderBy(world => world.Key, StringComparer.Ordinal)
            .ToArray();
        if (worlds.Length == 0) return [];
        var spec = new ReferenceSpec(AstroReferences.Worlds) { Assess = locations.AssessWorld };
        return [new ActionNode(ActionId, "Collect existing records in candidate world")
        {
            SupportsPlanning = true,
            Tone = Tone.Warning,
            Description = "Uses schema-qualified candidate world joins, intersected with selected rows when invoked in a table. " +
                "Only existing IsGot flags change. This does not clear a world or grant rewards; unmapped/outside-world records are visibly skipped. " +
                AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Collection + " " + AstroWriteWarnings.Integrity,
            Parameters = [new FieldNode(WorldParameter, "Candidate world", spec, FieldValue.Of(worlds[0].Key))
            { Confidence = Confidence.Inferred, Description = "Candidate aliases, not physical pickup mappings. " + worlds[0].AttributionText }]
        }];
    }

    public static ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        AstroSchema schema, AstroLocations locations, Func<AstroField, FieldValue> current)
    {
        if (parameters.Count != 1 || !parameters.TryGetValue(WorldParameter, out var value) ||
            value.Kind != FieldValueKind.Text || !schema.Records.Any(record => locations.World(record)?.Key == value.AsString()))
            return new ActionPlan { Errors = [new FieldError(WorldParameter, "Choose an existing schema-qualified candidate world.")] };

        // First validate the complete supplied scope: foreign/duplicate keys must not disappear
        // silently when it is intersected with the requested world.
        var all = AstroPlanning.PlanCollectAll(scope, schema, current);
        if (all.Errors.Count > 0) return all;
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        var included = schema.Records.Where(record => selected is null || selected.Contains(record.Element.Id)).ToArray();
        var matching = included.Where(record => locations.World(record)?.Key == value.AsString()).ToArray();
        var plan = AstroPlanning.PlanCollectAll(new ActionScope(matching.Select(record => record.Element.Id).ToArray()), schema, current);
        return plan with
        {
            SkippedRows = [.. plan.SkippedRows, .. included.Except(matching).Select(record =>
                new SkippedActionRow(record.Element.Id, "Outside the requested candidate world or no unambiguous schema-qualified world mapping."))],
            Warnings = [.. plan.Warnings, "Candidate world joins are Inferred. No clear, cinematic, notification, reward, UDS or trophy state is derived."]
        };
    }
}
