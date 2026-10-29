using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class OwnedProjectionTests
{
    [Theory]
    [InlineData("weapon", 0x5FA6C, 0xB3A68, 1001)]
    [InlineData("materia", 0x60A6C, 0xBFA68, 10020)]
    public void QueryAndLegacyPagePreserveRelationshipTextWithoutBindingOffPageFields(
        string kind, int inventoryOffset, int detailOffset, uint type)
    {
        const string title = "PPSA08664";
        const uint instance = 123456;
        var bytes = SyntheticGameplay.Create(title).Bytes;
        Write(inventoryOffset, instance);
        Write(inventoryOffset + sizeof(uint), type);
        Write(inventoryOffset + 2 * sizeof(uint), 1);
        Write(detailOffset, instance);
        Write(detailOffset + sizeof(uint), type);
        Write(CharacterLayout.WeaponOffset(0), instance);
        bytes[detailOffset + 0x11] = 0;
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile("ff7rebirth000", bytes, title));
        var queryable = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["slot"] = "0" } };
        var row = Assert.Single(queryable.Query("owned-" + kind, query).Rows);
        Assert.Equal($"owned/{kind}/{instance:X8}/0", row.Key);
        Assert.Equal("Cloud Strife", ((TextNode)row.Cells["owner"]).Text);
        Assert.False(row.IsEmpty);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(row.Key + "/type"));
        var legacy = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode($"owned/{kind}/0"))).Rows[0];
        foreach (var cell in row.Cells.Where(cell => cell.Key != "type"))
            Assert.Equal(((TextNode)cell.Value).Text, ((TextNode)legacy.Cells[cell.Key]).Text);
        Assert.Equal(type.ToString(System.Globalization.CultureInfo.InvariantCulture), ((FieldNode)legacy.Cells["type"]).Value.AsString());
        Assert.True(((FieldNode)legacy.Cells["type"]).Spec.ReadOnly);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());

        // Relationship views must read current weapon owners, even after a previous query.
        if (kind == "weapon")
        {
            var references = new ReferenceData(SaveToolkit.Sdk.References.IReferenceContext.Empty, Ff7Schema.ForTitle(title)!);
            var relationships = new OwnedRelationsView(bytes, references, _ => throw new InvalidOperationException("Unexpected binding."));
            var collection = relationships.Collection(kind);
            Assert.Equal("Cloud Strife", ((TextNode)Assert.Single(collection.Query(query).Rows).Cells["owner"]).Text);
            Write(CharacterLayout.WeaponOffset(0), instance + 1);
            Write(CharacterLayout.WeaponOffset(1), instance);
            Assert.Equal("Barret Wallace", ((TextNode)Assert.Single(collection.Query(query).Rows).Cells["owner"]).Text);
        }

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
