using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Convenience actions compose existing format policies without constructing new records.</summary>
internal sealed class GearboxCoverageActions(GearboxDocument document, GearboxAmmoRestore ammo)
{
    internal const string MatchLevel = "items/match-level";
    internal const string Mark = "items/mark";
    private const string LevelWarning = SerialWrites.Warning +
        " Inferred: match explicitly selected existing gear to this character's current XP-derived level, clipped to the title's editor limit. " +
        "Unsupported serials are skipped. Item legality, stats, parts, rarity and account gear are not inferred.";
    private const string MarkWarning = SerialWrites.Warning +
        " Inferred: batch marks affect only selected supported existing records. Conflicting favourite+junk and unsupported rows are skipped. " +
        "Unknown flag bits and serial bytes are preserved. Validate UI marks and sale protection on console.";

    internal static bool Handles(string actionId) => actionId is MatchLevel or Mark or GearboxAmmoRestore.ActionId;
    internal static ActionNode AmmoAction() => Node(GearboxAmmoRestore.ActionId, "Restore imported ammo amounts", "Verified pool schema; Experimental action. " + GearboxAmmoRestore.Warning);
    internal static IReadOnlyList<ViewNode> ItemActions(bool profile)
    {
        var actions = new List<ViewNode>();
        if (!profile) actions.Add(Node(MatchLevel, "Match selected gear to character level", LevelWarning));
        actions.Add(Node(Mark, "Mark selected items", MarkWarning) with
        {
            Parameters = [new("mark", "Mark (0 ordinary, 1 favourite, 2 junk)", new IntegerSpec(0, 2), FieldValue.Of(1))
            { Confidence = Confidence.Inferred, Warning = MarkWarning }],
        });
        return actions;
    }

    private static ActionNode Node(string id, string label, string description) => new(id, label)
    { SupportsPlanning = true, Description = description };

    internal ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var warnings = new[] { actionId == MatchLevel ? LevelWarning : actionId == Mark ? MarkWarning : GearboxAmmoRestore.Warning };
        try
        {
            var changes = new List<PlannedChange>();
            var skipped = new List<SkippedActionRow>();
            if (actionId == GearboxAmmoRestore.ActionId) ammo.Plan(document, scope, changes, skipped);
            else
            {
                if (actionId == MatchLevel && document.Profile)
                    throw new SaveFormatException("Matching gear requires this character's own level; profiles have no character level.");
                var target = actionId == MatchLevel ? CharacterLevel() : TargetMark(parameters);
                foreach (var entry in new GearboxItemActions(document).Select(scope, requireSelection: true))
                {
                    try
                    {
                        if (actionId == MatchLevel)
                        {
                            var old = document.Inventory.Info(entry.Id).Level;
                            if (old is null || old < 1 || old > SerialWrites.Maximum(document.Root.Schema.Wonderlands, "level"))
                                throw new SaveFormatException("Unknown or out-of-policy current item level; original serial is preserved.");
                            _ = SerialWrites.Set(entry.Serial, document.Root.Schema.Wonderlands, "level", target);
                            if (old != target) changes.Add(new(entry.Id + "/level", entry.Id + " level (Inferred)", FieldValue.Of(old.Value), FieldValue.Of(target)));
                        }
                        else
                        {
                            var message = entry.Field.Message;
                            if (message is null || !document.Inventory.Info(entry.Id).Complete || !GearboxItemMarks.Supported(message))
                                throw new SaveFormatException("Unsupported item record/serial or conflicting favourite+junk flags; original bytes are preserved.");
                            var old = message.Integer("flags");
                            var updated = GearboxItemMarks.Flags(old, target);
                            if (old != updated) changes.Add(new(entry.Id + "/flags", entry.Id + " flags (Inferred; 2 favourite, 4 junk)", FieldValue.Of(old), FieldValue.Of(updated)));
                        }
                    }
                    catch (SaveFormatException ex) { skipped.Add(new(entry.Id, ex.Message)); }
                }
            }
            return new() { Changes = changes, SkippedRows = skipped, Warnings = warnings };
        }
        catch (Exception ex) when (ex is SaveFormatException or OverflowException)
        { return new() { Errors = [new("", ex.Message)], Warnings = warnings }; }
    }

    internal void Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionPlan plan)
    {
        foreach (var change in plan.Changes)
        {
            var separator = change.ValueId.LastIndexOf('/');
            if (actionId == GearboxAmmoRestore.ActionId)
            {
                var index = int.Parse(change.ValueId[(separator + 1)..], CultureInfo.InvariantCulture);
                document.Root.Children("resource_pools").ElementAt(index).SetFloat("amount", (float)change.NewValue.AsDouble());
            }
            else
            {
                var entry = document.Inventory.Find(change.ValueId[..separator]);
                if (actionId == MatchLevel)
                    document.Inventory.SetSerial(entry.Id, SerialWrites.Set(entry.Serial, document.Root.Schema.Wonderlands, "level", change.NewValue.AsInt32()));
                else GearboxItemMarks.Set(entry.Field.Message!, TargetMark(parameters));
            }
        }
        document.InvalidateItems();
        _ = document.Serialize();
    }

    private int CharacterLevel()
    {
        var experience = document.Root.Integer("experience_points");
        if (experience < 0 || experience > int.MaxValue) throw new SaveFormatException("Unsupported character XP; no item level can be derived.");
        return Math.Min(document.Root.Schema.Level((int)experience), SerialWrites.Maximum(document.Root.Schema.Wonderlands, "level"));
    }

    private static int TargetMark(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("mark", out var value) || value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > 2)
            throw new SaveFormatException("Choose ordinary (0), favourite (1) or junk (2).");
        return value.AsInt32();
    }
}
