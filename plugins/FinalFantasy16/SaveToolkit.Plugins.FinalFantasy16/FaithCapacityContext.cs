using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithCapacityContext(byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
{
    private static readonly int[] UpgradeIds = [506, 507, 508, 509];
    internal static bool IsUpgrade(int id) => UpgradeIds.Contains(id);

    public SectionNode Build(FaithContextField field) => new("Potion upgrade evidence — read-only", [
        new TextNode("The MIT UseItem annotations name these upgrade IDs. Allocated records and their quantities are shown " +
            "without treating allocation, a zero stack or a bank as proof of an active upgrade. " +
            "ItemCap can be overridden by UseItem; current capacities, mode/build matching and stage/bank activation are unknown. " +
            "Contextual caps remain unknown. F6 explicit-quantity edits do not activate upgrades or calculate a legal refill." ) { Tone = Tone.Warning },
        new TableNode([new TableColumn("item", "Upgrade reference"), new TableColumn("main", "Main allocated records"),
            new TableColumn("secondary", "Secondary allocated records (unconfirmed role)")],
            UpgradeIds.Select(id => new TableRow($"capacity/{id}", new Dictionary<string, ViewNode>
            {
                ["item"] = field($"capacity/{id}/item", catalog.Name(id),
                    new ReferenceSpec(FaithCatalog.ItemsId) { AllowCustom = true }, () => FieldValue.Of(FaithCatalog.Key(id)),
                    Confidence.Inferred, catalog.Provenance(id)),
                ["main"] = Bank(field, inventory.Items, inventory.IsSupported, "main", id),
                ["secondary"] = Bank(field, inventory.SecondaryItems, inventory.IsSecondarySupported, "secondary", id),
            })).ToArray()),
    ]);

    private ViewNode Bank(FaithContextField field, IReadOnlyList<FaithItem> items, bool supported, string bank, int id)
    {
        if (!supported) return new TextNode("Unsupported/ambiguous bank; no ownership conclusion.");
        var matching = items.Where(item => item.Id == id).ToArray();
        if (matching.Length == 0) return new TextNode("No allocated record; activation is unknown.");
        return new SectionNode("Stored quantities", matching.Select(item => (ViewNode)field(
            $"capacity/{id}/{bank}/{item.Slot}", $"{bank} slot {item.Slot}", IntegerSpec.Int32,
            () => FieldValue.Of(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset + sizeof(int)))),
            Confidence.Verified, "Raw stored quantity only; does not establish whether the game activates the upgrade. " + catalog.Provenance(id))).ToArray());
    }
}
