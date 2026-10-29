using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>A reviewed scalar batch over existing ordinary collection pairs; never a deck or acquisition transaction.</summary>
internal sealed class GwentPlanning(IReadOnlyList<GwentCopies> copies, IReferenceCatalog? catalog, bool canEdit)
{
    public const string ActionId = "gwent/set-ordinary-copies";
    public const string CopiesParameter = "copies";
    public const string ActionColumn = "batch";
    public const string ActionLabel = "Set ordinary Gwent counts (review)";
    public const string Warning = WriteFields.GwentWarning +
        " Inferred, Experimental batch: leaders and unrecognised card identities are skipped. No missing card indices are created.";
    private int Maximum { get; } = copies.Where(c => !c.Card.Leader).Select(c => c.Maximum)
        .DefaultIfEmpty(GwentCopies.ObservedOrdinaryMaximum).Max();
    public ActionNode Action => new(ActionId, ActionLabel) {
        SupportsPlanning = true, Description = Warning,
        Parameters = [new(CopiesParameter, "Saved copies per selected ordinary card", new IntegerSpec(0, Maximum), FieldValue.Of(1)) {
            Confidence = Confidence.Inferred, Warning = Warning,
            Description = "Each existing field keeps its own imported bound: ordinarily 0..3. A larger opening count only widens that record's bound.",
        }],
    };

    public ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var errors = new List<FieldError>();
        if (!canEdit) { errors.Add(new(ActionId, "No supported editable Gwent collection is available.")); }
        var validTarget = parameters.Count == 1 && parameters.TryGetValue(CopiesParameter, out var value) &&
            value.Kind == FieldValueKind.Integer && value.AsInteger() >= 0 && value.AsInteger() <= Maximum;
        if (!validTarget) { errors.Add(new(CopiesParameter, $"Choose a whole copy count in 0..{Maximum}, with no extra parameters.")); }
        if (scope.QueryScope is { } query && (query.CollectionId != GwentCollection.CollectionId || scope.RowKeys is null))
        {
            errors.Add(new(ActionId, "A query scope must resolve to saved Gwent row keys before planning."));
        }
        var byKey = copies.ToDictionary(c => GwentCollection.Key(c.Card), StringComparer.Ordinal);
        var keys = scope.RowKeys ?? copies.Select(c => GwentCollection.Key(c.Card)).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(k => !byKey.ContainsKey(k)))
        {
            errors.Add(new(ActionId, "Selection contains duplicate or unknown saved Gwent indices."));
        }
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        if (errors.Count == 0)
        {
            var target = parameters[CopiesParameter].AsInt32();
            foreach (var key in keys)
            {
                var copy = byKey[key];
                var reference = catalog?.Find(key);
                if (copy.Card.Leader || reference?.Attributes.GetValueOrDefault("kind") == "Leader")
                {
                    skipped.Add(new(key, "Leader card; this batch only changes ordinary collection counts."));
                }
                else if (reference?.Attributes.GetValueOrDefault("kind") is not ("Unit" or "Hero" or "Special"))
                {
                    skipped.Add(new(key, "Unrecognised ordinary card identity or unavailable reference pack; record is preserved."));
                }
                else if (target > copy.Maximum)
                {
                    skipped.Add(new(key, $"Target exceeds this existing field's 0..{copy.Maximum} bound; record is preserved."));
                }
                else if (target == copy.Value)
                {
                    skipped.Add(new(key, "Already at the requested count."));
                }
                else
                {
                    changes.Add(new(copy.ValueId, $"Card {key} · Saved copies (Inferred)",
                        FieldValue.Of(copy.Value), FieldValue.Of(target)));
                }
            }
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Errors = errors, Warnings = [Warning] };
    }
}
