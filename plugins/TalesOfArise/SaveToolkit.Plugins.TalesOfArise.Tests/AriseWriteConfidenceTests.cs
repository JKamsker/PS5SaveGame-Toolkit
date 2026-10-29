using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseWriteConfidenceTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void UnmappedCharacterScalars_HaveUnknownMeaningsAndRemainReadOnly(int index)
    {
        var file = AriseSampleTests.GameplayFiles().First();
        using var document = new TalesOfArisePlugin().Open(file);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var characters = Assert.Single(root.Tabs.Single(tab => tab.Title == "Party").Children.OfType<TableNode>());
        var row = characters.Rows.Single(row => row.Key == $"character/{index}");
        foreach (var field in row.Cells.Values.OfType<FieldNode>())
        {
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.True(field.Spec.ReadOnly);
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        }

        var knownLevel = Assert.IsType<FieldNode>(characters.Rows[0].Cells["level"]);
        Assert.Equal(Confidence.Inferred, knownLevel.Confidence);
        Assert.False(knownLevel.Spec.ReadOnly);
        Assert.Contains("Experimental", Assert.IsType<FieldNode>(characters.Rows[0].Cells["hp"]).Warning!);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(PluginMaturity.Experimental, new TalesOfArisePlugin().Info.Maturity);
    }

    [Theory]
    [InlineData(0, Confidence.Unknown, true)]
    [InlineData(65535, Confidence.Unknown, true)]
    [InlineData(1, Confidence.Inferred, false)]
    public void InventoryQuantityConfidence_DoesNotInferUnknownIdentity(int id, Confidence confidence, bool readOnly)
    {
        var file = AriseSampleTests.GameplayFiles().First();
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var offset = save.Sections.Single(section => section.Name == "SaveData_ItemManager").Offset;
        BinaryPrimitives.WriteUInt16LittleEndian(save.Payload.AsSpan(offset + 2), checked((ushort)id));
        save.Payload[offset] = 1;
        var bytes = AriseFormat.Encode(save);
        using var document = new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA02745"));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = queryable.Query("inventory", new()
        {
            IncludeEmpty = true,
            Filters = new Dictionary<string, string> { ["id"] = id.ToString() },
        });
        var field = Assert.IsType<FieldNode>(page.Rows.Single(row => row.Key == "inventory/0/quantity").Cells["quantity"]);
        Assert.Equal(confidence, field.Confidence);
        Assert.Equal(readOnly, field.Spec.ReadOnly);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
