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
    private const int MateriaInventoryOffset = 0x60A6C;
    private const int MateriaDetailOffset = 0xBFA68;
    private const int PageSize = 64;
    private const int WeaponCount = 128;
    private const int MateriaCount = 1000;

    public CollectionQuery Collection(string kind)
    {
        var count = kind == "materia" ? MateriaCount : WeaponCount;
        var offset = kind == "materia" ? MateriaInventoryOffset : WeaponInventoryOffset;
        var occupied = Enumerable.Range(0, count).Count(slot => U32(offset + slot * 0x20) != uint.MaxValue &&
            U32(offset + slot * 0x20 + 4) != uint.MaxValue);
        return new(new($"owned-{kind}", $"Owned {kind} relationships",
            [new("slot", "Slot", IntegerSpec.UInt32), new("instance", "Instance"), new("type", "Type"),
             new("name", "Name"), new("quantity", "Count", IntegerSpec.UInt32), new("detail", "Detail type"),
             new("owner", "Referenced by"), new("source", "Source")], occupied)
        {
            Description = "Read-only instance/detail joins; type IDs use browse-only attributed catalogues; equipment legality remains unverified. " + EmbeddedCatalogs.Attribution,
        }, () => Rows(kind, 0, count, bindType: false)
            .Select(row =>
            {
                var values = row.Cells.ToDictionary(pair => pair.Key, pair => ((TextNode)pair.Value).Text, StringComparer.Ordinal);
                return new CollectionEntry(row.Key, values);
            }));
    }

    public IReadOnlyList<ViewNode> Sections() =>
    [
        SectionNode.Lazy("Owned weapons (instance → detail)", "owned/weapon/0", $"{WeaponCount} slots"),
        SectionNode.Lazy("Owned materia (instance → detail → owner)", "owned/materia/0", $"{MateriaCount} slots"),
    ];

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
             new("source", "Source")], Rows(kind, page * PageSize, PageSize, bindType: true))
        {
            Title = materia ? "Owned materia relationships" : "Owned weapon relationships",
            Description = "Instance/type domains are kept separate; unresolved and duplicate details are not repaired. Type IDs use browse-only attributed catalogues. Demo mappings remain Inferred. " + EmbeddedCatalogs.Attribution,
        };
    }

    private IReadOnlyList<TableRow> Rows(string kind, int start, int length, bool bindType)
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
        var rows = new List<TableRow>();
        for (var slot = start; slot < Math.Min(count, start + length); slot++)
        {
            var offset = (materia ? MateriaInventoryOffset : WeaponInventoryOffset) + slot * 0x20;
            var instance = U32(offset);
            var type = U32(offset + 4);
            if (instance == uint.MaxValue || type == uint.MaxValue)
            {
                continue;
            }

            var offsets = detailOffsets.GetValueOrDefault(instance);
            var uniqueDetail = offsets is { Length: 1 };
            uint? detail = uniqueDetail ? U32(offsets![0] + sizeof(uint)) : null;
            var ownerId = materia && uniqueDetail ? bytes[offsets![0] + 0x11] : int.MaxValue;
            var owner = materia && offsets is { Length: > 1 } ? "Ambiguous detail instance"
                : materia && ownerId < Ff7Schema.CharacterCount ? references.Character(ownerId)
                : !materia && characterRefs.TryGetValue(instance, out var names)
                    ? string.Join(", ", names)
                    : "Unassigned / unresolved";
            var name = materia ? references.Materia(type) : references.Weapon(type);
            var key = $"owned/{kind}/{instance:X8}/{slot}";
            var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["slot"] = new TextNode(slot.ToString()),
                ["instance"] = new TextNode($"0x{instance:X8}") { Monospace = true },
                ["type"] = bindType ? bind(new ScalarField($"owned/{kind}/{instance:X8}/{slot}/type", "Type ID", offset + 4,
                    new ReferenceSpec(materia ? "ff7r-materia" : "ff7r-weapons") { ReadOnly = true }, Confidence.Inferred,
                    Warning: "Read-only raw type ID; browse the attributed catalogue for known names. " + EmbeddedCatalogs.Attribution)) :
                    new TextNode($"0x{type:X8}") { Monospace = true },
                ["name"] = new TextNode(name),
                ["quantity"] = new TextNode(U32(offset + 8).ToString()),
                ["detail"] = new TextNode(detail is { } value ? $"0x{value:X8}" : offsets is { Length: > 1 } ? "ambiguous" : "unresolved") { Monospace = true },
                ["owner"] = new TextNode(owner),
                ["source"] = new TextNode(references.Source(materia ? "ff7r-materia" : "ff7r-weapons", type) +
                    " Character labels: " + characterSource),
            };
            rows.Add(new TableRow(key, cells));
        }

        return rows;
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
