using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class RagnarokNestedView
{
    public static TableNode Build(RagnarokRecordIndex index, RagnarokReferences references) => new([
        new("container", "Container role (inferred)"), new("id", "Literal ID bytes"), new("offset", "Offset"),
        new("length", "Bytes"), new("resources", "Resources"), new("ids", "Nested IDs"), new("words", "Index-like words"),
    ], index.Nested.Wallets.Select(wallet => new TableRow($"container/{wallet.Id:X16}", new Dictionary<string, ViewNode>
    {
        ["container"] = new TextNode(references.Container(wallet.Id)),
        ["id"] = new TextNode(RagnarokReferences.Key(wallet.Id)),
        ["offset"] = new TextNode($"0x{wallet.Offset:X}"),
        ["length"] = new TextNode((wallet.End - wallet.Offset).ToString()),
        ["resources"] = new TextNode(wallet.ResourceCount.ToString()),
        ["ids"] = new TextNode(wallet.IdCount.ToString()),
        ["words"] = new TextNode(wallet.IndexCount.ToString()),
    })).ToArray())
    {
        Title = "Length-bounded wallet block",
        Description = RagnarokReferences.ContainerSources + " " +
            "Other container roles remain Unknown. Exact framing is observed in the PS5 corpus; role transfers are inferred.",
    };
}
