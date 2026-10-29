using System.Globalization;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Builds collection projections in native order, without binding off-page cells.</summary>
internal static class RagnarokCollectionRows
{
    public static (List<RagnarokCollectionRow> Resources, List<RagnarokCollectionRow> Ids, List<RagnarokCollectionRow> Words)
        Wallets(RagnarokDocument document, RagnarokRecordIndex index, RagnarokReferences references)
    {
        var resources = new List<RagnarokCollectionRow>();
        var ids = new List<RagnarokCollectionRow>();
        var words = new List<RagnarokCollectionRow>();
        foreach (var wallet in index.GetResourceWallets())
        {
            AddResources(document, references, wallet, resources);
            for (var row = 0; row < wallet.IdCount; row++)
            {
                var offset = wallet.IdsOffset + row * sizeof(ulong);
                var id = document.ReadId(offset);
                ids.Add(new($"wallet/{wallet.Id:X16}/ids/{row}", offset, Cells(
                    ("container", references.Container(wallet.Id)), ("id", RagnarokReferences.Key(id)),
                    ("name", references.Name(id)), ("category", references.Find(id)?.Category ?? "Unknown"),
                    ("source", references.Attribution(id)), ("offset", Hex(offset)))));
            }

            for (var row = 0; row < wallet.IndexCount; row++)
            {
                var offset = wallet.IndicesOffset + row * sizeof(uint);
                words.Add(new($"wallet/{wallet.Id:X16}/index-like/{row}", offset, Cells(
                    ("container", references.Container(wallet.Id)), ("word", document.ReadWord(offset).ToString(CultureInfo.InvariantCulture)),
                    ("offset", Hex(offset)))));
            }
        }

        return (resources, ids, words);
    }

    public static IReadOnlyList<RagnarokCollectionRow> Flags(RagnarokDocument document, RagnarokRecordIndex index,
        RagnarokReferences references)
    {
        var flags = new List<RagnarokCollectionRow>();
        foreach (var group in index.Nested.FlagGroups)
        {
            for (var row = 0; row < group.Count; row++)
            {
                var offset = group.RowsOffset + row * RagnarokFlagGroup.RowSize;
                var id = document.ReadId(offset);
                flags.Add(new($"flags/{group.Id:X16}/{id:X16}/{row}", offset, Cells(
                    ("group", RagnarokReferences.Key(group.Id)), ("id", RagnarokReferences.Key(id)),
                    ("group-name", references.Community.Tree(group.Id)?.Name ?? "Unknown group"),
                    ("group-source", references.Community.Tree(group.Id)?.AttributionText ?? "No sourced group mapping"),
                    ("name", references.Name(id)), ("source", references.Attribution(id)),
                    ("flags", $"0x{document.ReadWord(offset + sizeof(ulong)):X8}"), ("offset", Hex(offset)))));
            }
        }

        return flags;
    }

    public static IReadOnlyList<RagnarokCollectionRow> Directory(RagnarokDocument document, RagnarokRecordIndex index,
        RagnarokReferences references)
    {
        return Enumerable.Range(0, index.DirectoryCount).Select(row =>
        {
            var offset = RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong);
            var id = document.ReadId(offset);
            return new RagnarokCollectionRow($"directory/{id:X16}", offset, Cells(
                ("id", RagnarokReferences.Key(id)), ("name", references.Name(id)),
                ("source", references.Attribution(id)), ("word", document.ReadWord(index.FlagsOffset + row * sizeof(uint)).ToString(CultureInfo.InvariantCulture)),
                ("offset", Hex(offset))));
        }).ToArray();
    }

    private static void AddResources(RagnarokDocument document, RagnarokReferences references, RagnarokWallet wallet, List<RagnarokCollectionRow> rows)
    {
        var occurrences = new Dictionary<ulong, int>();
        for (var row = 0; row < wallet.ResourceCount; row++)
        {
            var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
            var id = document.ReadId(offset);
            var occurrence = occurrences.GetValueOrDefault(id);
            occurrences[id] = occurrence + 1;
            rows.Add(new($"wallet/{wallet.Id:X16}/resource/{id:X16}/{occurrence}", offset, Cells(
                ("container", references.Container(wallet.Id)), ("name", references.Name(id)), ("id", RagnarokReferences.Key(id)),
                ("category", references.Category(id)), ("quantity", document.ReadWord(offset + sizeof(ulong)).ToString(CultureInfo.InvariantCulture)),
                ("metadata", document.ReadWord(offset + RagnarokFormat.WalletRowMetadataOffset).ToString(CultureInfo.InvariantCulture)),
                ("source", references.Attribution(id)), ("offset", Hex(offset)), ("icon", string.Empty)),
                "quantity", offset + sizeof(ulong)));
        }
    }

    private static string Hex(int offset) => $"0x{offset:X8}";
    private static Dictionary<string, string> Cells(params (string Key, string Value)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => cell.Value, StringComparer.Ordinal);
}
