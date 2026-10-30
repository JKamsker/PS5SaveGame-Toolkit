using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Stable reference authority delegates that always resolve the document's current model.</summary>
internal sealed class GearboxPartBindings(GearboxDocument document)
{
    private readonly Dictionary<(string Item, int Slot), Func<ReferenceRecord, ReferenceEligibility>> _assessors = [];

    internal Func<ReferenceRecord, ReferenceEligibility> Assessor(string item, int slot)
    {
        if (_assessors.TryGetValue((item, slot), out var assess)) return assess;
        assess = record =>
        {
            try
            {
                _ = SerialParts.Candidate(SerialDatabase.For(document.Root.Schema.Wonderlands),
                    SerialParts.Slot(document.Inventory.Info(item), slot), record.Key);
                return new();
            }
            catch (SaveFormatException ex) { return new() { Eligible = false, Reasons = [ex.Message] }; }
        };
        _assessors.Add((item, slot), assess);
        return assess;
    }
}
