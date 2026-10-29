using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Read-only joins for Rebirth's measured instance, detail and owner arrays.</summary>
internal sealed class OwnedRelationsView(byte[] bytes, ReferenceData references, Func<ScalarField, FieldNode> bind)
{
    private const int WeaponInventoryOffset = 0x5FA6C;
    private const int WeaponDetailOffset = 0xB3A68;
    private const int MateriaInventoryOffset = OwnedMateriaTargets.InventoryOffset;
    private const int MateriaDetailOffset = OwnedMateriaTargets.DetailOffset;
    private const int PageSize = 64;
    private const int WeaponCount = 128;
    private const int MateriaCount = 1000;
    private const int ColumnCount = 8;

    public CollectionQuery Collection(string kind)
    {
        var count = kind == "materia" ? MateriaCount : WeaponCount;
        return new(new($"owned-{kind}", $"Owned {kind} relationships",
            [new("slot", "Slot", IntegerSpec.UInt32), new("instance", "Instance"), new("type", "Type"),
             new("name", "Name"), new("quantity", "Count", IntegerSpec.UInt32), new("detail", "Detail type"),
             new("owner", "Referenced by"), new("source", "Source")], count)
        {
            EmptyRows = EmptySlots.Presentation,
            Description = "Read-only instance/detail joins; type IDs use browse-only attributed catalogues; equipment legality remains unverified. " + EmbeddedCatalogs.Attribution,
        }, () => Entries(kind, 0, count));
    }

    // Compatibility endpoint for existing sessions; new views use the complete F8 collection.
    public TableNode Page(string kind, int page)
    {
        var materia = kind == "materia";
        var count = materia ? MateriaCount : WeaponCount;
        var pages = (count + PageSize - 1) / PageSize;
        if ((kind is not ("weapon" or "materia")) || page < 0 || page >= pages)
        {
            throw new KeyNotFoundException("Unknown owned-item page.");
        }

        return new TableNode(
            [new("slot", "Slot"), new("instance", "Instance"), new("type", "Type"), new("name", "Name"),
             new("quantity", materia ? "Quantity" : "Count"), new("detail", "Detail type"), new("owner", materia ? "Equipped by" : "Referenced by"),
             new("source", "Source")], Rows(kind, page * PageSize, PageSize))
        {
            Title = materia ? "Owned materia relationships" : "Owned weapon relationships",
            EmptyRows = EmptySlots.Presentation,
            Description = "Instance/type domains are kept separate; unresolved and duplicate details are not repaired. Type IDs use browse-only attributed catalogues. Demo mappings remain Inferred. " + EmbeddedCatalogs.Attribution,
        };
    }

    private IReadOnlyList<TableRow> Rows(string kind, int start, int length) =>
        Entries(kind, start, length).Select((entry, index) =>
        {
            var cells = entry.Values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal);
            cells["instance"] = new TextNode(entry.Values["instance"]) { Monospace = true };
            cells["detail"] = new TextNode(entry.Values["detail"]) { Monospace = true };
            var offset = (kind == "materia" ? MateriaInventoryOffset : WeaponInventoryOffset) + (start + index) * 0x20;
            cells["type"] = bind(new ScalarField(entry.Key + "/type", "Type ID", offset + 4,
                new ReferenceSpec(kind == "materia" ? "ff7r-materia" : "ff7r-weapons") { ReadOnly = true }, Confidence.Inferred,
                Warning: "Read-only raw type ID; browse the attributed catalogue for known names. " + EmbeddedCatalogs.Attribution));
            return new TableRow(entry.Key, cells) { IsEmpty = entry.IsEmpty };
        }).ToArray();

    private IEnumerable<CollectionEntry> Entries(string kind, int start, int length)
    {
        var materia = kind == "materia";
        var count = materia ? MateriaCount : WeaponCount;
        var characterRefs = materia ? [] : CharacterWeaponRefs();
        var detailStart = materia ? MateriaDetailOffset : WeaponDetailOffset;
        var detailStride = materia ? 0x20 : 0x60;
        var detailOffsets = Enumerable.Range(0, count).Select(slot => detailStart + slot * detailStride)
            .GroupBy(U32).ToDictionary(group => group.Key, group => group.ToArray());
        var characterSource = string.Join("; ", Enumerable.Range(0, Ff7Schema.CharacterCount)
            .Select(references.CharacterSource).Distinct(StringComparer.Ordinal));
        var sources = new Dictionary<uint, string>();
        for (var slot = start; slot < Math.Min(count, start + length); slot++)
        {
            var offset = (materia ? MateriaInventoryOffset : WeaponInventoryOffset) + slot * 0x20;
            var instance = U32(offset);
            var type = U32(offset + 4);
            var empty = instance == uint.MaxValue || type == uint.MaxValue;
            var offsets = empty ? null : detailOffsets.GetValueOrDefault(instance);
            var uniqueDetail = offsets is { Length: 1 };
            uint? detail = uniqueDetail ? U32(offsets![0] + sizeof(uint)) : null;
            var ownerId = materia && uniqueDetail ? bytes[offsets![0] + 0x11] : int.MaxValue;
            var owner = materia && offsets is { Length: > 1 } ? "Ambiguous detail instance"
                : materia && ownerId < Ff7Schema.CharacterCount ? references.Character(ownerId)
                : !materia && characterRefs.TryGetValue(instance, out var names)
                    ? string.Join(", ", names)
                    : "Unassigned / unresolved";
            var name = empty ? EmptySlots.Label : materia ? references.Materia(type) : references.Weapon(type);
            var key = $"owned/{kind}/{instance:X8}/{slot}";
            if (!sources.TryGetValue(type, out var source))
            {
                source = references.Source(materia ? "ff7r-materia" : "ff7r-weapons", type) + " Character labels: " + characterSource;
                sources.Add(type, source);
            }
            var values = new Dictionary<string, string>(ColumnCount, StringComparer.Ordinal)
            {
                ["slot"] = slot.ToString(), ["instance"] = $"0x{instance:X8}", ["type"] = $"0x{type:X8}",
                ["name"] = name, ["quantity"] = U32(offset + 8).ToString(),
                ["detail"] = detail is { } value ? $"0x{value:X8}" : offsets is { Length: > 1 } ? "ambiguous" : "unresolved",
                ["owner"] = owner, ["source"] = source,
            };
            yield return new(key, values) { IsEmpty = empty };
        }

    }

    private Dictionary<uint, List<string>> CharacterWeaponRefs()
    {
        var values = new Dictionary<uint, List<string>>();
        for (var i = 0; i < Ff7Schema.CharacterCount; i++)
        {
            var reference = U32(0xCC + i * 0x100);
            if (!values.TryGetValue(reference, out var names))
            {
                names = [];
                values.Add(reference, names);
            }

            names.Add(references.Character(i));
        }

        return values;
    }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
