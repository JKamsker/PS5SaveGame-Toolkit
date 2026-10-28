using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Rebirth's source reference positions, without assuming usable capacity or linked sockets.</summary>
internal sealed class SocketGraphs(byte[] bytes, ReferenceData references, Func<ScalarField, FieldNode> bind)
{
    public const string TabTitle = "Equipment and folios";
    private const int CharacterStride = 0x100;
    private const int WeaponReference = 0xCC;
    private const int WeaponMateria = 0xF4;
    private const int ArmorMateria = 0xAC;
    private const int ReferenceSlots = 8;
    private const int MateriaDetails = 0xBFA68;
    private const int MateriaStride = 0x20;

    public IReadOnlyList<ViewNode> Sections() => [.. Enumerable.Range(0, Ff7Schema.CharacterCount)
        .Select(index => SectionNode.Lazy(references.Character(index) + " equipment references", $"spatial/sockets/{index}")
            with { Description = references.CharacterSource(index) + " " + EmbeddedCatalogs.Attribution })];

    public IReadOnlyList<ViewNode> Expand(int character)
    {
        if (character < 0 || character >= Ff7Schema.CharacterCount) throw new KeyNotFoundException("Unknown FFVII socket group.");
        var types = Enumerable.Range(0, GameplayFields.MateriaCount)
            .GroupBy(slot => U32(MateriaDetails + slot * MateriaStride))
            .ToDictionary(group => group.Key, group => group.Select(slot => U32(MateriaDetails + slot * MateriaStride + sizeof(uint))).ToArray());
        var fields = new List<FieldNode>();
        var items = new List<GraphItem>();
        var shift = character * CharacterStride;
        var path = new[] { $"tab:{TabTitle}", $"section:spatial/sockets/{character}" };
        var source = "Rebirth template.ts equipment reference offsets; " + EmbeddedCatalogs.Attribution + " " + references.CharacterSource(character);
        Add("weapon", "Equipped weapon reference", WeaponReference + shift, "Equipment", null);
        foreach (var (name, start) in new[] { ("Weapon", WeaponMateria), ("Armor", ArmorMateria) })
        {
            for (var slot = 0; slot < ReferenceSlots; slot++)
            {
                var offset = start + shift + slot * sizeof(uint);
                var instance = U32(offset);
                var nameText = instance == uint.MaxValue ? "Empty sentinel" :
                    types.TryGetValue(instance, out var matches) && matches.Length == 1
                        ? references.Materia(matches[0]) : "Unresolved / ambiguous instance";
                var provenance = types.TryGetValue(instance, out var candidates) && candidates.Length == 1
                    ? references.Source("ff7r-materia", candidates[0]) : source;
                Add($"{name.ToLowerInvariant()}/{slot}", $"{name} reference {slot + 1}: {nameText}", offset, name, provenance);
            }
        }

        return [new GraphNode(references.Character(character) + " equipment reference positions", items, [],
            "Eight source reference positions per weapon and armor; these are stored references, not eight proven usable sockets. " +
            "All states are Unknown. No socket links, capacity, prerequisite edges, moves, swaps or weapon-upgrade purchases are inferred."),
            new SectionNode("Stored references", [.. fields])];

        void Add(string suffix, string label, int offset, string branch, string? provenance)
        {
            var id = $"socket/{character}/{suffix}";
            fields.Add(bind(new(id, label, offset, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }, Confidence.Inferred,
                Warning: source + " " + provenance + " Read-only; capacity, links, owner consistency and console identity are unverified.")));
            items.Add(new(id, label, GraphState.Unknown)
            {
                Branch = branch, Description = $"Stored instance 0x{U32(offset):X8} at 0x{offset:X}. {source} {provenance}",
                Target = new(ViewTargetKind.Field, id) { Path = path },
            });
        }
    }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
