using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class EditPolicyTests
{
    [Theory]
    [InlineData(299, 1, 0, 299)]
    [InlineData(300, 2, 300, 749)]
    [InlineData(301, 2, 300, 749)]
    [InlineData(749, 2, 300, 749)]
    [InlineData(750, 3, 750, 1199)]
    public void HealingApCannotCrossEitherBoundaryOfItsStoredTier(uint ap, byte level, uint minimum, uint maximum)
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var bytes = file.ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBFA6C), 10001); // Healing: pinned 0/300/750/1200 curve.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBFA70), ap);
        bytes[0xBFA78] = level;
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08664"));
        _ = document.ExpandNode("materia/0");
        Assert.True(document.ApplyEdits([new("materia/0/ap", FieldValue.Of((long)minimum))]).Succeeded);
        Assert.True(document.ApplyEdits([new("materia/0/ap", FieldValue.Of((long)maximum))]).Succeeded);
        Assert.False(document.ApplyEdits([new("materia/0/ap", FieldValue.Of((long)maximum + 1))]).Succeeded);
        if (minimum > 0)
        {
            Assert.False(document.ApplyEdits([new("materia/0/ap", FieldValue.Of((long)minimum - 1))]).Succeeded);
        }

        Assert.Equal(level, document.GetValue("materia/0/level").AsUInt32());
        Assert.True(document.ApplyEdits([new("materia/0/ap", FieldValue.Of((long)ap))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MismatchedApAndNegativeOrAmbiguousQuantitiesStayReadOnly()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var bytes = file.ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBFA6C), 10001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBFA70), 299);
        bytes[0xBFA78] = 2; // Incoherent source curve.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x4FA70), 100); // Negative Potion sentinel.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x4FA74), -1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x4FA90), 1); // Duplicate gil, alongside slot 10.
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08664"));
        _ = document.BuildView();
        var inventory = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("inventory/0")));
        var materia = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("materia/0")));
        Assert.True(((FieldNode)inventory.Rows[0].Cells["quantity"]).Spec.ReadOnly);
        Assert.True(((FieldNode)inventory.Rows[1].Cells["quantity"]).Spec.ReadOnly);
        Assert.True(((FieldNode)inventory.Rows[10].Cells["quantity"]).Spec.ReadOnly);
        Assert.True(((FieldNode)materia.Rows[0].Cells["quantity"]).Spec.ReadOnly);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void OriginalValuesAboveEditorCapsCanBeRestoredAfterRefreshingBindings()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var bytes = file.ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x4FA70), 100); // Potion.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x4FA74), 15000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xBFA70), 2000000); // Ordinary materia already at final tier.
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA08664"));
        _ = document.ExpandNode("inventory/0");
        _ = document.ExpandNode("materia/0");
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(10L)), new("materia/0/ap", FieldValue.Of(300L))]).Succeeded);
        _ = document.ExpandNode("inventory/0");
        _ = document.ExpandNode("materia/0");
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(15000L)), new("materia/0/ap", FieldValue.Of(2000000L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01911", 465)]
    [InlineData("PPSA08664", 530)]
    public void EverySuppliedOrdinaryMateriaAgreesWithThePinnedCurve(string title, int expectedRows)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var count = 0;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            schema.TryGetRole(file.FileName, out var common) && !common))
        {
            var bytes = file.ReadAllBytes();
            var policy = new MateriaEditPolicy(bytes, schema);
            for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
            {
                var offset = (schema.IsRemake ? 0x1844 : 0xBFA6C) + slot * 32;
                var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                var record = EmbeddedCatalogs.Records(schema, "ff7r-materia").FirstOrDefault(record => record.Key == id.ToString());
                if (record is not null && record.Category != "Summon Materia")
                {
                    Assert.NotNull(policy.Range(slot));
                    count++;
                }
            }
        }

        Assert.Equal(expectedRows, count);
    }
}
