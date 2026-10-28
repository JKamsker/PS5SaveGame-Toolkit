using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Replace one existing index, never counts, bit widths, balances or unknown tails.</summary>
internal static class SerialParts
{
    public const string Warning = "Experimental existing-slot replacement. Category and database-width checks establish binary compatibility only; " +
        "they do not establish allowed part pools, DLC ownership, exclusivity or legal gear. " + SerialWrites.Warning;

    public static SerialAsset Slot(SerialInfo info, int slot)
    {
        if (!info.Complete || slot < 0 || slot >= info.Parts.Count + info.GenericParts.Count)
            throw new SaveFormatException("Part editing requires a fully mapped serial with a known tail and an existing slot.");
        return slot < info.Parts.Count ? info.Parts[slot] : info.GenericParts[slot - info.Parts.Count];
    }

    private static SerialAsset Candidate(SerialDatabase database, SerialAsset current, string key)
    {
        // The immutable binary database is authoritative; host label overrides cannot grant eligibility.
        var record = database.Find(ItemCatalogs.Indices, key);
        if (record?.Category != current.Category || !int.TryParse(record.Attributes.GetValueOrDefault("index"),
                NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1 || index >= 1 << current.Width)
            throw new SaveFormatException("Choose an existing index in this part category that fits the original database bit width.");
        return database.Asset(current.Category, index);
    }

    public static byte[] Set(byte[] serial, bool wonderlands, int slot, string key)
    {
        var reader = new SerialReader(wonderlands);
        var info = reader.Read(serial, out var envelope);
        var current = Slot(info, slot);
        var candidate = Candidate(SerialDatabase.For(wonderlands), current, key);
        var result = envelope!.Patch(current.Offset, current.Width, candidate.Index);
        var check = reader.Read(result);
        if (!check.Complete || Slot(check, slot).Key != key)
            throw new SaveFormatException("Edited part failed serial validation.");
        return result;
    }

    public static FieldNode Field(GearboxDocument document, string itemId, int slot, string source)
    {
        var database = SerialDatabase.For(document.Root.Schema.Wonderlands);
        return document.PartReference(itemId + "/part/" + slot,
            () => Slot(document.Inventory.Info(itemId), slot).Key,
            key => document.Inventory.SetSerial(itemId, Set(document.Inventory.Find(itemId).Serial, document.Root.Schema.Wonderlands, slot, key)),
            record =>
            {
                try { _ = Candidate(database, Slot(document.Inventory.Info(itemId), slot), record.Key); return new(); }
                catch (SaveFormatException ex) { return new() { Eligible = false, Reasons = [ex.Message] }; }
            }, source);
    }
}
