using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroRestorePlanning
{
    public const string ActionId = "collectibles/restore-imported";
    private const string RestoreWarning =
        "Restore uses this document's immutable opening IsGot values. Only staged ownership reductions are restored; " +
        "flags already above the opening baseline stay unchanged. Reimporting establishes a new baseline. " +
        "This is not a complete-world reversal; independent ownership reversal and linked effects still need console validation.";

    public static ActionNode View() => new(ActionId, "Restore imported ownership (review before apply)")
    {
        SupportsPlanning = true,
        Tone = Tone.Warning,
        Description = "Outside a table, review all existing collectibles. In a table, review its selected rows (including hidden selections). " +
            AstroWriteWarnings.Experimental + " " + RestoreWarning + " " + AstroWriteWarnings.Collection + " " + AstroWriteWarnings.Integrity
    };

    public static ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        AstroSchema schema, Func<AstroField, FieldValue> current)
    {
        if (parameters.Count != 0)
        {
            return new ActionPlan { Errors = [new FieldError(ActionId, "Restore imported ownership takes no parameters.")] };
        }
        if (!schema.TrySelectRecords(scope.RowKeys, out var records))
        {
            return new ActionPlan { Errors = [new FieldError(ActionId, "Selection must contain distinct existing collectible row keys only.")] };
        }

        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var record in records)
        {
            if (record.Ownership is not { Editable: true } ownership)
            {
                skipped.Add(new(record.Element.Id, "No unique editable boolean IsGot field; unknown structure is preserved."));
                continue;
            }

            var value = current(ownership);
            // Initial belongs to the parsed allocation, never to edit history or a preview.
            if (value == ownership.Initial)
            {
                skipped.Add(new(record.Element.Id, "Already equals the opening ownership baseline; no change proposed."));
            }
            else if (value.AsBool())
            {
                skipped.Add(new(record.Element.Id, "Ownership is above the opening baseline; staged grant preserved."));
            }
            else
            {
                changes.Add(new(ownership.Token.Id, $"{record.Group}/{record.Element.Name} IsGot (Inferred; restore imported)",
                    value, ownership.Initial));
            }
        }

        return new ActionPlan
        {
            Changes = changes,
            SkippedRows = skipped,
            Warnings = [AstroWriteWarnings.Experimental, RestoreWarning, AstroWriteWarnings.Collection, AstroWriteWarnings.Integrity]
        };
    }
}
