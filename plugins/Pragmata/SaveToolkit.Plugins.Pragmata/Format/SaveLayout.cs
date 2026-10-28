using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Format;

internal sealed class SaveLayout
{
    public IReadOnlyList<SaveNode> Roots { get; }
    public SaveNode? Header { get; }
    public SaveNode Detail { get; }
    public SaveNode? Inventory { get; }
    public SaveNode? Clock { get; }
    public IReadOnlyList<SaveNode> Items { get; } = [];
    public IReadOnlyList<SaveNode> Timers { get; } = [];

    public SaveLayout(byte[] plaintext)
    {
        var reader = new TypedReader(plaintext);
        Roots = reader.Parse();
        Header = Roots.FirstOrDefault(n => n.Hash == KnownHashes.HeaderRoot);
        Detail = Roots.FirstOrDefault(n => n.Hash == KnownHashes.DetailRoot) ??
            throw new SaveFormatException("Missing Pragmata save detail root.");
        Require(Detail, KnownHashes.Slot, FieldTags.Int32);
        if (Header is null)
        {
            if (!Roots.Any(n => n.Hash == KnownHashes.SystemRoot))
                throw new SaveFormatException("Missing Pragmata gameplay or system root.");
            return;
        }

        Require(Header, KnownHashes.SaveDate, FieldTags.Int64);
        Require(Header, KnownHashes.Playtime, FieldTags.UInt64);
        Require(Header, KnownHashes.Cleared, FieldTags.Boolean);
        Require(Header, KnownHashes.Difficulty, FieldTags.UInt32);
        Inventory = UniqueClass(reader.Classes, KnownHashes.InventoryClass);
        Clock = UniqueClass(reader.Classes, KnownHashes.ClockClass);
        Timers = [Require(Clock, KnownHashes.Elapsed, FieldTags.UInt64), Require(Clock, KnownHashes.Demo, FieldTags.UInt64), Require(Clock, KnownHashes.Pause, FieldTags.UInt64)];
        Items = Require(Inventory, KnownHashes.Items, FieldTags.Array).Children;
        Require(Inventory, KnownHashes.EquippedItem, FieldTags.UInt32);
        foreach (var item in Items)
        {
            Require(item, KnownHashes.ItemId, FieldTags.UInt32);
            Require(item, KnownHashes.Quantity, FieldTags.Int32);
        }
    }

    public static SaveNode Require(SaveNode parent, uint hash, int type)
    {
        var field = parent.Find(hash);
        if (field is null || field.Type != type)
            throw new SaveFormatException($"Missing or mistyped Pragmata field {parent.Path}/{hash:x8}.");
        return field;
    }

    private static SaveNode UniqueClass(IEnumerable<SaveNode> classes, uint hash)
    {
        var matches = classes.Where(n => n.ClassHash == hash).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new SaveFormatException($"Expected exactly one Pragmata class {hash:x8}.");
    }
}
