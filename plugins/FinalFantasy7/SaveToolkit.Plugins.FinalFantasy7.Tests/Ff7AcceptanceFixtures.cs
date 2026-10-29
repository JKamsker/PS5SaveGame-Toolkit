using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

/// <summary>Pure authored FFVII bytes and offsets for API/browser acceptance fixtures.</summary>
public sealed record Ff7AcceptanceFixture(
    string TitleId,
    string GameplayName,
    string CommonName,
    ReadOnlyMemory<byte> Gameplay,
    ReadOnlyMemory<byte> Common,
    uint ItemType,
    uint MateriaType,
    uint MateriaTargetAp,
    uint MateriaSecondTierAp,
    IReadOnlyDictionary<string, int> FieldOffsets);

/// <summary>Builds parser-valid authored bytes without private samples or production I/O.</summary>
public static class Ff7AcceptanceFixtures
{
    private const uint RemakeMateriaType = 10004;
    private const uint RebirthMateriaType = 10020;
    private const uint UnknownType = 0xDEADBEEF;
    private const int OwnedCountDelta = 8;
    private const int DetailTypeDelta = 4;
    private const int DetailOwnerDelta = 0x11;

    public static Ff7AcceptanceFixture Create(string titleId)
    {
        var schema = Ff7Schema.ForTitle(titleId) ?? throw new ArgumentException("Unsupported FFVII title.", nameof(titleId));
        var layout = GameplayLayout.For(schema);
        var gameplay = SyntheticGameplay.Create(titleId).Bytes;
        var itemType = CatalogType(schema, "Items");
        var materiaType = schema.IsRemake ? RemakeMateriaType : RebirthMateriaType;
        var materia = EmbeddedCatalogs.Records(schema, "ff7r-materia").Single(record => record.Key == materiaType.ToString(CultureInfo.InvariantCulture));
        var thresholds = materia.Attributes["apThresholds"].Split(',').Select(uint.Parse).ToArray();
        ClearInventory(gameplay, layout);
        ClearMateria(gameplay, layout, schema.IsRemake ? 0U : uint.MaxValue);
        StageInventory(gameplay, layout, schema, itemType);
        StageMateria(gameplay, layout, schema, materiaType, thresholds[1]);
        StageCharacters(gameplay, schema);
        if (!schema.IsRemake) StageOwnedMateria(gameplay, materiaType);

        return new(titleId, schema.Prefix + "000", schema.CommonName, gameplay, ContainerTests.Common(titleId),
            itemType, materiaType, thresholds[1], thresholds.Length > 2 ? thresholds[2] : thresholds[1],
            new ReadOnlyDictionary<string, int>(Offsets(layout, schema)));
    }

    private static uint CatalogType(Ff7Schema schema, string category) => uint.Parse(
        EmbeddedCatalogs.Records(schema, "ff7r-items").First(record => record.Category == category &&
            !record.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)).Key, CultureInfo.InvariantCulture);

    private static void ClearInventory(byte[] bytes, GameplayLayout layout)
    {
        for (var slot = 0; slot < GameplayFields.InventoryCount; slot++)
        {
            Write(bytes, layout.InventoryOffset + slot * layout.InventoryStride, uint.MaxValue);
            Write(bytes, layout.InventoryOffset + slot * layout.InventoryStride + GameplayLayout.QuantityDelta, 0);
        }
    }

    private static void ClearMateria(byte[] bytes, GameplayLayout layout, uint emptyType)
    {
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
        {
            Write(bytes, layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, emptyType);
            Write(bytes, layout.MateriaApOffset + slot * GameplayLayout.MateriaStride, 0);
            bytes[layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride] = 0;
        }
    }

    private static void StageInventory(byte[] bytes, GameplayLayout layout, Ff7Schema schema, uint itemType)
    {
        foreach (var slot in new[] { 0, 1, 200 }) WriteInventory(bytes, layout, slot, itemType, 1);
        WriteInventory(bytes, layout, 201, itemType, 99);
        WriteInventory(bytes, layout, 202, itemType, 0);
        WriteInventory(bytes, layout, 203, layout.GilType, 10);
        WriteInventory(bytes, layout, 204, UnknownType, 1);
        var weapon = CatalogType(schema, "Weapons");
        WriteInventory(bytes, layout, 205, weapon, 1);
    }

    private static void WriteInventory(byte[] bytes, GameplayLayout layout, int slot, uint type, uint quantity)
    {
        var offset = layout.InventoryOffset + slot * layout.InventoryStride;
        Write(bytes, offset, type);
        Write(bytes, offset + GameplayLayout.QuantityDelta, quantity);
    }

    private static void StageMateria(byte[] bytes, GameplayLayout layout, Ff7Schema schema, uint type, uint targetAp)
    {
        foreach (var slot in new[] { 0, 1, 200 }) WriteMateria(bytes, layout, slot, type, targetAp: 0, stars: 1);
        WriteMateria(bytes, layout, 201, type, targetAp: 0, stars: 0);
        var summon = uint.Parse(EmbeddedCatalogs.Records(schema, "ff7r-materia")
            .First(record => record.Category == "Summon Materia").Key, CultureInfo.InvariantCulture);
        WriteMateria(bytes, layout, 202, summon, targetAp: 0, stars: 1);
        var foreign = uint.Parse(EmbeddedCatalogs.Records(schema, "ff7r-materia")
            .First(record => record.Category != "Summon Materia" && record.Key != type.ToString(CultureInfo.InvariantCulture)).Key,
            CultureInfo.InvariantCulture);
        WriteMateria(bytes, layout, 203, foreign, targetAp: 0, stars: 1);
        if (!schema.IsRemake) WriteMateria(bytes, layout, 5, type, targetAp, stars: 1);
    }

    private static void WriteMateria(byte[] bytes, GameplayLayout layout, int slot, uint type, uint targetAp, byte stars)
    {
        var offset = layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride;
        Write(bytes, offset, type);
        Write(bytes, offset + GameplayLayout.MateriaApDelta, targetAp);
        bytes[layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride] = stars;
    }

    private static void StageCharacters(byte[] bytes, Ff7Schema schema)
    {
        for (var character = 0; character < Ff7Schema.CharacterCount; character++)
        {
            var offset = CharacterLayout.Offset(schema, character);
            bytes[offset] = 40;
            Write(bytes, offset + CharacterLayout.HpDelta, (uint)(100 + character));
            Write(bytes, offset + CharacterLayout.HpDelta + CharacterLayout.MaximumVitalDelta, (uint)(500 + character));
            Write(bytes, offset + CharacterLayout.MpDelta, (uint)(10 + character));
            Write(bytes, offset + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta, (uint)(50 + character));
            Write(bytes, offset + CharacterLayout.ExperienceDelta, 0);
        }
    }

    private static void StageOwnedMateria(byte[] bytes, uint type)
    {
        var instance = SemanticPresetFixture.Instance;
        Write(bytes, OwnedMateriaTargets.InventoryOffset, instance);
        Write(bytes, OwnedMateriaTargets.InventoryOffset + DetailTypeDelta, type);
        Write(bytes, OwnedMateriaTargets.InventoryOffset + OwnedCountDelta, 1);
        Write(bytes, OwnedMateriaTargets.DetailOffset, instance);
        Write(bytes, OwnedMateriaTargets.DetailOffset + DetailTypeDelta, type);
        bytes[OwnedMateriaTargets.DetailOffset + DetailOwnerDelta] = 0;
        Write(bytes, OwnedMateriaTargets.DetailOffset + 5 * GameplayLayout.MateriaStride, instance + 1);
        Write(bytes, OwnedMateriaTargets.DetailOffset + 5 * GameplayLayout.MateriaStride + DetailTypeDelta, type);
        bytes[OwnedMateriaTargets.DetailOffset + 5 * GameplayLayout.MateriaStride + DetailOwnerDelta] = 1;
    }

    private static Dictionary<string, int> Offsets(GameplayLayout layout, Ff7Schema schema)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in new[] { 0, 1, 7, 200, 201, 202, 203, 204, 205 })
        {
            var at = layout.InventoryOffset + slot * layout.InventoryStride;
            offsets[$"inventory/{slot}/type"] = at;
            offsets[$"inventory/{slot}/quantity"] = at + GameplayLayout.QuantityDelta;
        }
        foreach (var slot in new[] { 0, 1, 5, 7, 200 })
        {
            var at = layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride;
            offsets[$"materia/{slot}/type"] = at;
            offsets[$"materia/{slot}/ap"] = at + GameplayLayout.MateriaApDelta;
            offsets[$"materia/{slot}/level"] = layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride;
        }
        for (var character = 0; character < Ff7Schema.CharacterCount; character++)
        {
            var at = CharacterLayout.Offset(schema, character);
            offsets[$"character/{character}/level"] = at;
            offsets[$"character/{character}/hp"] = at + CharacterLayout.HpDelta;
            offsets[$"character/{character}/max-hp"] = at + CharacterLayout.HpDelta + CharacterLayout.MaximumVitalDelta;
            offsets[$"character/{character}/mp"] = at + CharacterLayout.MpDelta;
            offsets[$"character/{character}/max-mp"] = at + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta;
            offsets[$"character/{character}/experience"] = at + CharacterLayout.ExperienceDelta;
        }
        foreach (var slot in new[] { 0, 1, 7 })
        {
            var owned = OwnedMateriaTargets.InventoryOffset + slot * GameplayLayout.MateriaStride;
            var detail = OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride;
            offsets[$"owned/{slot}/instance"] = owned;
            offsets[$"owned/{slot}/type"] = owned + DetailTypeDelta;
            offsets[$"owned/{slot}/count"] = owned + OwnedCountDelta;
            offsets[$"detail/{slot}/instance"] = detail;
            offsets[$"detail/{slot}/owner"] = detail + DetailOwnerDelta;
        }
        foreach (var slot in new[] { 5 })
        {
            var detail = OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride;
            offsets[$"detail/{slot}/instance"] = detail;
            offsets[$"detail/{slot}/owner"] = detail + DetailOwnerDelta;
        }
        return offsets;
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
