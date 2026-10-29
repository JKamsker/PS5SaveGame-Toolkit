using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

/// <summary>Authored framing and relationships only; no real save content or gameplay claims.</summary>
internal sealed class SemanticPresetFixture
{
    public const uint ItemType = 101;
    public const uint MateriaType = 10020;
    public const uint Instance = 123456;
    public const string Rebirth = "PPSA08664";
    public SemanticPresetFixture(string title = Rebirth)
    {
        Title = title;
        Schema = Ff7Schema.ForTitle(title)!;
        Layout = GameplayLayout.For(Schema);
        Bytes = SyntheticGameplay.Create(title).Bytes;
        for (var slot = 0; slot < GameplayFields.InventoryCount; slot++)
            Write(Layout.InventoryOffset + slot * Layout.InventoryStride, uint.MaxValue);
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
        {
            Write(Layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, Schema.IsRemake ? 0U : uint.MaxValue);
            if (!Schema.IsRemake)
            {
                Write(OwnedMateriaTargets.InventoryOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
                Write(OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
            }
        }
    }

    public string Title { get; }
    public Ff7Schema Schema { get; }
    public GameplayLayout Layout { get; }
    public byte[] Bytes { get; }
    public static Dictionary<string, FieldValue> RestockParameters => new() { ["quantity"] = FieldValue.Of(99L) };
    public static Dictionary<string, FieldValue> StarParameters => new()
        { ["type"] = FieldValue.Of("10020"), ["stars"] = FieldValue.Of(2L) };

    public FinalFantasy7Document Open() => (FinalFantasy7Document)new FinalFantasy7Plugin().Open(
        MemorySaveSet.SingleFile(Schema.Prefix + "000", Bytes, Title));

    public void Item(int slot, uint type = ItemType, uint quantity = 1)
    {
        var offset = Layout.InventoryOffset + slot * Layout.InventoryStride;
        Write(offset, type);
        Write(offset + GameplayLayout.QuantityDelta, quantity);
    }

    public void Materia(int ownedSlot, int detailSlot, uint instance = Instance, uint type = MateriaType,
        byte stars = 1, uint ap = 0)
    {
        var ownedAt = OwnedMateriaTargets.InventoryOffset + ownedSlot * GameplayLayout.MateriaStride;
        Write(ownedAt, instance);
        Write(ownedAt + sizeof(uint), type);
        Write(ownedAt + 2 * sizeof(uint), 1);
        Detail(detailSlot, instance, type, stars, ap);
    }

    public void Detail(int slot, uint instance = Instance, uint type = MateriaType, byte stars = 1, uint ap = 0)
    {
        Write(OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride, instance);
        Write(Layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, type);
        Write(Layout.MateriaApOffset + slot * GameplayLayout.MateriaStride, ap);
        Bytes[Layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride] = stars;
    }

    public void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);
    public static void Unchanged(FinalFantasy7Document document, byte[] expected) =>
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
}
