using System.Buffers.Binary;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

internal static class InventoryInspectionFixture
{
    public static InventoryInspection Create(int count, Action? onFieldBound = null)
    {
        const int itemSize = 29;
        var payload = new byte[count * itemSize];
        var nodes = new FixtureNode[count];
        var items = new InventoryItem[count];
        for (var id = 0; id < count; id++)
        {
            var offset = id * itemSize;
            CsavCorruptionFixture.SetWord(payload, offset, id);
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset + 4), InventoryReader.MoneyTweakId);
            CsavCorruptionFixture.SetWord(payload, offset + 12, 2); // Quantity-only item body.
            nodes[id] = new("itemData", id + 1 < count ? id + 1 : -1, -1, offset, itemSize);
            // This presentation fixture deliberately supplies no quantity binding.
            items[id] = new(id, 1, InventoryReader.MoneyTweakId, null, 0);
        }

        var container = CsavContainer.Parse(CsavCorruptionFixture.Create(payload, nodes));
        var tree = CsavTree.Parse(container);
        return new InventoryInspection(items, container, tree, new CyberpunkReferences(IReferenceContext.Empty, 2210),
            new ItemNames(), payload, _ => throw new InvalidOperationException("No quantity binding in presentation fixture."),
            (id, label, spec, value) =>
            {
                onFieldBound?.Invoke();
                return new FieldNode(id, label, spec with { ReadOnly = true }, FieldValue.Of(value));
            });
    }
}
