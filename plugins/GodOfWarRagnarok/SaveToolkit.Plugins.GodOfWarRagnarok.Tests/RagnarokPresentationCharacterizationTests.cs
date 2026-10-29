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
    // Retain the complete baseline protocol apart from the separately asserted main restore binding below.
    [InlineData("legacy", true, "99360F76ECCCF1B40C4EAEC674E0890614B84BF3FC5100BB3C9ED79EA697C6C8")]
    [InlineData("nested", true, "30E5118AECD1815132AB466D2F159E94D1768245D9B664999DB1AEC13A60AE5C")]
    [InlineData("preferences", true, "3156507C9278A56220BE0198021362CE9C2B922B319919E98A219AE1B5DB0960")]
    [InlineData("legacy", false, "B638B8CE11FB8618B00BAEDA2DE4CBFD7497DFC7D266AB1051A39A1895A4D3B0")]
    [InlineData("nested", false, "000891004F3D9E4B5C30BC49555C9BE72AD29862F1F335A50E77FD1E7DD42CDE")]
    [InlineData("preferences", false, "3156507C9278A56220BE0198021362CE9C2B922B319919E98A219AE1B5DB0960")]
    public void ViewsCollectionsAndInspectionRetainTheirProtocolApartFromRestoreBinding(string kind, bool hosted, string expected)
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
        var root = Assert.IsType<TabsNode>(document.BuildView());
        if (kind != "preferences")
        {
            var tab = Assert.Single(root.Tabs, tab => tab.Title == "Resources & XP");
            var action = Assert.Single(tab.Children.OfType<ActionNode>());
            Assert.Equal("resources", action.SelectionTableId);
            // Assert the intended one-property delta, then compare every other protocol byte with the baseline.
            var baselineAction = action with { SelectionTableId = null };
            var baselineTab = tab with { Children = tab.Children.Select(node => node == action ? baselineAction : node).ToArray() };
            root = root with { Tabs = root.Tabs.Select(candidate => candidate == tab ? baselineTab : candidate).ToArray() };
        }
        Append<ViewNode>(protocol, root);
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
