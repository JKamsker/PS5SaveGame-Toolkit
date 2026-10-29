using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class HonestLabelTests
{
    [Theory]
    [InlineData(1, Confidence.Inferred)]
    [InlineData(99999, Confidence.Unknown)]
    public void NamedMainStack_SeparatesStorageEvidenceFromItemMeaning(int itemId, Confidence expected)
    {
        var payload = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(payload, itemId);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        using var document = new FinalFantasy16Plugin().Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).ToArray();
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query(FaithInventoryView.CollectionId, new() { Limit = 1 });
        var queried = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["quantity"]);
        var aliases = fields.Where(node => node.ValueId == "inventory/0/quantity").Append(queried).ToArray();
        Assert.True(aliases.Length >= 2);
        foreach (var alias in aliases)
        {
            Assert.Equal(expected, alias.Confidence);
            Assert.Contains("Verified storage layout", alias.Description);
            Assert.Contains("item meaning", alias.Description);
            Assert.False(alias.Spec.ReadOnly);
            Assert.NotEmpty(alias.Warning!);
        }
        var field = aliases[0];
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1233))]).Succeeded);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1234))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CrcField_DescribesVerifiedAlgorithmWithoutClaimingConsoleAcceptance()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        using var document = new FinalFantasy16Plugin().Open(FaithTestData.File(bytes));
        var crc = Assert.Single(FaithViewFields.All(document.BuildView()), field => field.ValueId == "integrity/crc32");

        Assert.Equal(Confidence.Verified, crc.Confidence);
        Assert.True(crc.Spec.ReadOnly);
        Assert.Contains("CRC32/IEEE", crc.Description);
        Assert.Contains("72", crc.Description);
        Assert.Contains("console acceptance", crc.Description);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
