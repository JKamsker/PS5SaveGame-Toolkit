using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokPresentationCharacterizationTests
{
    [Theory]
    // Complete protocol fingerprints with invariant formatting; gameplay attribution corrected in #40 docs accuracy.
    [InlineData("legacy", true, "BD8583A6FF856D5524AEE811D67C19A5AD5F5478F88D3DAD9D4335B495B31F46")]
    [InlineData("nested", true, "D695E36067E75727590C9532CFDB63756FC1F80FAD9B386D30F5CC37D6A82717")]
    [InlineData("preferences", true, "3156507C9278A56220BE0198021362CE9C2B922B319919E98A219AE1B5DB0960")]
    [InlineData("legacy", false, "52EFE51C79AC31AE8FAC0954BA89D4CE401911D138D2AEF20CB7269240F5FB0D")]
    [InlineData("nested", false, "F6C2EC4CB717D4E2D6040A85BF1C7EDE0F32C2EE5236CE75A9D465D4551478CB")]
    [InlineData("preferences", false, "3156507C9278A56220BE0198021362CE9C2B922B319919E98A219AE1B5DB0960")]
    public void ViewsCollectionsAndInspectionRetainTheirCompleteProtocol(string kind, bool hosted, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var plugin = new GodOfWarRagnarokPlugin();
            var context = RagnarokTestSupport.PinnedReferences(plugin);
            var bytes = Bytes(kind);
            var name = kind == "preferences" ? "userpreferences" : "GOWRSAVE0";
            var file = MemorySaveSet.SingleFile(name, bytes, "PPSA08330");
            using var document = (RagnarokDocument)(hosted ? plugin.Open(file, context) : plugin.Open(file));
            var protocol = Capture(document, kind);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protocol)));
            Assert.True(expected == actual, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string Capture(RagnarokDocument document, string kind)
    {
        var protocol = new StringBuilder();
        Append<ViewNode>(protocol, document.BuildView());
        foreach (var id in new[] { "header", "raw", "raw-group/0", "raw-page/0", "raw-page/1" })
            Append(protocol, document.ExpandNode(id));
        if (kind == "preferences") return protocol.ToString();

        foreach (var id in new[] { "grammar-evidence", "directory", "directory/0", "secondary", "secondary/0",
            "wallet", "wallet/0", "anchors", "anchors/0", "realm-details", "skill-tree/axe", "skill-details/axe",
            "skill-tree/blades", "skill-details/blades", "skill-tree/spear", "skill-details/spear" })
            Append(protocol, document.ExpandNode(id));
        Append(protocol, document.Collections);
        foreach (var collection in document.Collections)
        {
            var page = document.Query(collection.Id, new() { Limit = 200 });
            Append(protocol, page);
            if (collection.Id != "resources") continue;
            foreach (var row in page.Rows)
            {
                var action = Assert.IsType<ActionNode>(row.Cells["inspect"]);
                Append(protocol, document.InvokeAction(action.ActionId, new Dictionary<string, SaveToolkit.Sdk.Values.FieldValue>()));
            }
        }
        Append(protocol, document.PlanAction(RagnarokWrites.RestoreActionId,
            new Dictionary<string, SaveToolkit.Sdk.Values.FieldValue>(), new()));
        return protocol.ToString();
    }

    private static void Append<T>(StringBuilder protocol, T value) => protocol.Append(JsonSerializer.Serialize(value)).Append('\n');

    private static byte[] Bytes(string kind)
    {
        if (kind == "legacy") return RagnarokTestSupport.LegacyGameplay();
        if (kind == "nested") return new RagnarokNestedFixture().Create();
        var bytes = new byte[696];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 31);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x00513040);
        return bytes;
    }
}
