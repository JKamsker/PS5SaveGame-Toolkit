using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Read-only joins for Rebirth's measured instance, detail and owner arrays.</summary>
internal sealed class OwnedRelationsView(byte[] bytes, ReferenceData references)
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
            Description = "Read-only instance/detail joins; equipment legality remains unverified. " + EmbeddedCatalogs.Attribution,
        }, () => Enumerable.Range(0, (count + PageSize - 1) / PageSize).SelectMany(page => Page(kind, page).Rows)
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

        Dictionary<uint, List<string>> characterRefs = materia ? [] : CharacterWeaponRefs();
        var detailTypes = materia ? MateriaDetails() : WeaponDetails();
        var rows = new List<TableRow>();
        var start = page * PageSize;
        for (var slot = start; slot < Math.Min(count, start + PageSize); slot++)
        {
            var offset = (materia ? MateriaInventoryOffset : WeaponInventoryOffset) + slot * 0x20;
            var instance = U32(offset);
            var type = U32(offset + 4);
            if (instance == uint.MaxValue || type == uint.MaxValue)
            {
                continue;
            }

            uint? detail = detailTypes.TryGetValue(instance, out var detailType) ? detailType : null;
            var owner = materia && DetailOwner(instance) is { } ownerId
                ? references.Character(ownerId)
                : !materia && characterRefs.TryGetValue(instance, out var names)
                    ? string.Join(", ", names)
                    : "Unassigned / unresolved";
            var name = materia ? references.Materia(type) : references.Weapon(type);
            var key = $"owned/{kind}/{instance:X8}/{slot}";
            var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["slot"] = new TextNode(slot.ToString()),
                ["instance"] = new TextNode($"0x{instance:X8}") { Monospace = true },
                ["type"] = new TextNode($"0x{type:X8}") { Monospace = true },
                ["name"] = new TextNode(name),
                ["quantity"] = new TextNode(U32(offset + 8).ToString()),
                ["detail"] = new TextNode(detail is { } value ? $"0x{value:X8}" : "unresolved") { Monospace = true },
                ["owner"] = new TextNode(owner),
                ["source"] = new TextNode(references.Source(materia ? "ff7r-materia" : "ff7r-weapons", type) +
                    " Character labels: " + string.Join("; ", Enumerable.Range(0, Ff7Schema.CharacterCount)
                        .Select(references.CharacterSource).Distinct(StringComparer.Ordinal))),
            };
            rows.Add(new TableRow(key, cells));
        }

        return new TableNode(
            [new("slot", "Slot"), new("instance", "Instance"), new("type", "Type"), new("name", "Name"),
             new("quantity", materia ? "Quantity" : "Count"), new("detail", "Detail type"), new("owner", materia ? "Equipped by" : "Referenced by"),
             new("source", "Source")], rows)
        {
            Title = materia ? "Owned materia relationships" : "Owned weapon relationships",
            Description = "Instance/type domains are kept separate. Joins are sample-proven for the supplied retail Rebirth files; demo values remain Inferred and all rows are read-only. " + EmbeddedCatalogs.Attribution,
        };
    }

    private Dictionary<uint, uint> WeaponDetails()
    {
        var values = new Dictionary<uint, uint>();
        for (var i = 0; i < WeaponCount; i++)
        {
            var offset = WeaponDetailOffset + i * 0x60;
            values[U32(offset)] = U32(offset + 4);
        }

        return values;
    }

    private Dictionary<uint, uint> MateriaDetails()
    {
        var values = new Dictionary<uint, uint>();
        for (var i = 0; i < MateriaCount; i++)
        {
            var offset = MateriaDetailOffset + i * 0x20;
            values[U32(offset)] = U32(offset + 4);
        }

        return values;
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

    private int? DetailOwner(uint instance)
    {
        for (var i = 0; i < MateriaCount; i++)
        {
            var offset = MateriaDetailOffset + i * 0x20;
            if (U32(offset) == instance)
            {
                var owner = bytes[offset + 0x11];
                return owner < Ff7Schema.CharacterCount ? owner : null;
            }
        }

        return null;
    }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
