using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Existing Rebirth weapon references only; never allocates or rewrites socket arrays.</summary>
internal sealed class WeaponEquipment(byte[] bytes, Ff7Schema schema, ReferenceData references)
{
    private const int InventoryOffset = 0x5FA6C;
    private const int DetailOffset = 0xB3A68;
    private const int WeaponCount = 128;
    private const int InventoryStride = 0x20;
    private const int DetailStride = 0x60;
    // Inferred character families in the pinned source list; guest types 1050/1051 are excluded.
    private static readonly (uint First, uint Last)[] TypeFamilies =
        [(1001, 1007), (1008, 1014), (1015, 1021), (1022, 1028), (1029, 1035), (1036, 1042), (1043, 1049)];

    public ScalarField Field(int character)
    {
        var offset = CharacterLayout.WeaponOffset(character);
        var original = U32(offset);
        var owned = Owned();
        var originalType = owned.GetValueOrDefault(original);
        var family = TypeFamilies.FirstOrDefault(range => originalType >= range.First && originalType <= range.Last);
        var choices = owned.Where(pair => family.First != 0 && pair.Value >= family.First && pair.Value <= family.Last).ToDictionary();
        return new($"character/{character}/weapon", "Weapon instance", offset, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
            Confidence.Inferred, Editable: choices.ContainsKey(original) && !Enumerable.Range(0, Ff7Schema.CharacterCount)
                .Any(index => index != character && U32(CharacterLayout.WeaponOffset(index)) == original),
            Warning: WriteWarnings.Experimental + "Based on matching inventory/detail instances and the original weapon type family. " +
                "Socket capacity, proficiency and folio effects are unverified; existing sockets are preserved. Owned choices: " +
                string.Join("; ", choices.Select(pair => $"0x{pair.Key:X}: {references.Weapon(pair.Value)}")) + ". " + EmbeddedCatalogs.Attribution,
            Validate: value => value < 0 || value > uint.MaxValue || !choices.ContainsKey((uint)value)
                ? "Choose an owned, unambiguous weapon instance in the original type family."
                : Enumerable.Range(0, Ff7Schema.CharacterCount).Any(index => index != character &&
                    U32(CharacterLayout.WeaponOffset(index)) == (uint)value)
                    ? "That weapon is referenced by another character." : null);
    }

    private Dictionary<uint, uint> Owned()
    {
        var inventory = Enumerable.Range(0, WeaponCount).Select(index => InventoryOffset + index * InventoryStride)
            .Where(offset => U32(offset) != uint.MaxValue && U32(offset + 8) == 1)
            .GroupBy(U32).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => U32(group.Single() + 4));
        var details = Enumerable.Range(0, WeaponCount).Select(index => DetailOffset + index * DetailStride)
            .GroupBy(U32).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => U32(group.Single() + 4));
        var known = EmbeddedCatalogs.Records(schema, "ff7r-weapons").Select(record => uint.Parse(record.Key,
            System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        return inventory.Where(pair => known.Contains(pair.Value) && details.TryGetValue(pair.Key, out var type) && type == pair.Value).ToDictionary();
    }

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
